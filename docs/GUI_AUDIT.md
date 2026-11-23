# GUI AUDIT — WinForms Shell MCP Xellen (13 November 2026)

**Metode:** audit source code (`Program.cs`, `Xellen/UI/*.cs`) + perbaikan aman yang langsung diterapkan. Audit VISUAL interaktif (screenshot/resize nyata/DPI scaling nyata) **tetap BLOCKED** pada sesi headless ini — dilaporkan jujur sebagai item untuk verifikasi manual. Label FACT / FIXED / BLOCKED.

## 1. Struktur GUI saat ini (FACT)

| Surface | File | Konten |
|---|---|---|
| MainForm (shell) | `Program.cs` | TabControl 6 tab: Gateway (existing, dipertahankan), Dashboard, **Projects (baru)**, **Remote (baru)**, Proses, Log + tombol **Open AI Activity Monitor** + status strip notifikasi |
| Dashboard | `MainForm.Dashboard.cs` | ListView server + ringkasan metrik + label status |
| Projects | `MainForm.Projects.cs` | CRUD project, token file sekali-tulis, revoke, connection package, **adapter package (baru)** |
| Remote | `MainForm.Remote.cs` | tailscale/serve/health read-only viewer |
| Proses | `MainForm.Processes.cs` | proses tree + child policy + sweep + stop/restart |
| Log | `MainForm.Logs.cs` | JSONL viewer + filter level + follow |
| Activity Monitor | `ActivityMonitorForm.cs` | **jendela TERPISAH** — live stream, filter, detail, status bar |

## 2. Temuan audit + status perbaikan

| # | Temuan | Severity | Status |
|---|---|---|---|
| G-1 | Activity Monitor memakai **polling 1 dtk**, bukan event push (PRD §5.1: push disarankan) | Medium | **FIXED** — subscribe `EventBus` (push → `BeginInvoke` refresh), timer 1 dtk jadi fallback; status bar kini "Live (push+poll)" |
| G-2 | **Seleksi ListView hilang** tiap refresh (1 dtk) — mengganggu saat membaca detail event | Medium | **FIXED** — seleksi dipreservasi via EventId antar refresh |
| G-3 | **DPI mode tidak eksplisit** — csproj tanpa `ApplicationHighDpiMode`; generated file lama tidak memuat set (grep kosong) | Medium | **FIXED** — `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>`; verifikasi build: `build_property.ApplicationHighDpiMode = PerMonitorV2` (obj generated) |
| G-4 | **Clipping tab Projects** — bar tombol fixed width 960 px → terpotong di window lebih sempit | Low | **FIXED** — rows AutoSize + WrapContents; terverifikasi build |
| G-5 | **Kolom terakhir ListView tidak menyesuaikan lebar** → dead space di window lebar (Activity Monitor + Projects) | Low | **FIXED** — `FillLastColumn()` pada ClientChanged/refresh |
| G-6 | Multi-monitor & minimize: Activity Monitor adalah **top-level window tanpa owner** → benar (survive minimize, pindah monitor) | Info | FACT (kode) — uji visual interaktif BLOCKED |
| G-7 | Gateway tab memakai posisi absolut (warisan, tidak diubah) — tidak adaptif saat resize | Low | ACCEPTED (warisan; tidak menyentuh surface existing) |
| G-8 | Keyboard navigation: TabControl + tombol standar dapat dinavigasi; tidak ada shortcut khusus | Low | ACCEPTED — RECOMMENDATION tambah accelerator (Ctrl+1..n) di fase berikut |
| G-9 | Responsivitas thread: semua handler UI async/BeginInvoke; bus → UI via `BeginInvoke`; tidak ada blocking di UI thread | Info | FACT |
| G-10 | Dokumen: SECURITY_REPORT & ROLLBACK_GUIDE memuat salinan COMPATIBILITY_MATRIX/CHANGELOG (duplikasi kecil antar file docs) | Low | CATAT — dibersihkan saat docs final berikutnya |
| G-11 | **Window console terlihat saat SelfTest** (`start` tanpa /B di spawner) | Low | **FIXED** — spawner di disk + template `SelfTestRunner` kini `start /B`; **diverifikasi empiris** (`logs/window-verify2.txt`: anak jalur SelfTest = 0 window; control console nyata = 1 window terdeteksi oleh metode enumerasi) |

## 3. Interaksi & tombol (verifikasi wiring, bukan visual)
**FACT:** seluruh tombol ter-wire ke handler nyata — Gateway: CopyPrompt/LocateToken/Verify/Stop/OpenAIActivity; Projects: Create/Delete+Revoke/ConnectionPackage/GenerateAdapterPackage; Remote: Refresh; Proses: Sweep/Stop/Restart; Log: filter+follow. Tidak ada tombol tanpa fungsi.

## 4. Event integration (verifikasi)
**FACT:** pipeline nyata teruji di SelfTest — transport `tools/call` → `EventBus.Publish` (Running + final) → `ActivityStore` (JSONL) → UI (push). T35 membuktikan overflow terhitung. Event yang tampil berasal dari tool call nyata (T37: `echo:hello`; PoC Avalonia: `echo:avalonia-poc-real-call`), bukan mock.

## 5. BLOCKED (butuh sesi interaktif Anda)
1. Screenshot & verifikasi visual kedua window (dashboard + monitor) pada monitor berbeda.
2. Uji DPI scaling nyata: ubah scale 100%→125%→150% dan amati (PerMonitorV2 sudah diset — pengaruh visual perlu dipastikan).
3. Uji resize nyata: tarik window 1080→700 px dan amati kolom mengisi.

## 6. Rekomendasi berikutnya
1. Aktivator tray icon + minimize-to-tray.
2. Virtual-mode ListView bila event >1000 baris (saat ini capped 300 view + ring 1000).
3. Shortcut keyboard + tema dark penuh untuk seluruh tab (Gateway tab masih gaya warisan).
