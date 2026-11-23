# AVALONIA FEASIBILITY REPORT — Proof of Concept (13 November 2026)

> **Bukti utama:** `avalonia-poc` build Release sukses; headless test **7/7 PASS** (`logs/avalonia-poc9.txt` + `tests/avalonia-poc/logs/poc.log`, POC_EXIT=0). Termasuk **tool call nyata** (native transport → synthetic stdio child → EventBus → ViewModel Avalonia). Verifikasi jendela empiris: `logs/window-verify2.txt`.

## 1. Yang dibangun (PoC terpisah, production tidak tersentuh)
Lokasi: `desktop-commander-local/avalonia-poc/` — project sendiri (`McpXellen.AvaloniaPoc`), **TIDAK menggantikan GUI production**.

**Bukti reuse Core tanpa duplikasi:** csproj PoC memakai `<Compile Include="..\app\Xellen\Core\**\*.cs">` dan `Transport\**\*.cs` — business logic yang sama persis dikompilasi ke shell Avalonia. Bila Core berubah, PoC ikut berubah tanpa sinkronisasi manual.

| Bukti | Asersi |
|---|---|
| P1 | Native transport host started di port test (8391) |
| P2 | **Dua window Avalonia dibuat**: Dashboard + AI Activity Monitor (terpisah) |
| P3 | initialize HTTP nyata → 200 + Mcp-Session-Id |
| P4 | **tools/call nyata** → `echo:avalonia-poc-real-call` |
| P5 | **ViewModel Avalonia menerima 2 event nyata** (Running + Success) via EventBus |
| P6 | Filter status mengubah tampilan (Failed=0, Success=1) |
| P7 | Resize property-level; visual/DPI/multi-monitor = BLOCKED (headless) |

## 2. Perbandingan dengan WinForms existing

| Aspek | WinForms (existing) | Avalonia (PoC) |
|---|---|---|
| Reuse Core | ✔ (satu process, langsung) | ✔ via compile-link sumber (tanpa duplikasi) |
| Activity Monitor window terpisah | ✔ (Form) | ✔ (Window) |
| Event integration | Timer 1s fallback + push baru ditambahkan | Push via `Dispatcher.UIThread.Post` + CollectionChanged |
| DPI | PerMonitorV2 (baru diset eksplisit) | DPI-aware by design (renderer vektor) |
| Multi-monitor | ✔ (top-level window) | ✔ (top-level window) |
| Startup & footprint | Ringan, pustaka OS | Paket Avalonia ±40 MB restore; runtime self-contained lebih besar |
| Maturity di repo | Terbukti berbulan-bulan | **Baru PoC — belum production** |
| Ekosistem kontrol | Terbukti; kontrol lama | Modern, dark theme native Fluent |
| Risiko migrasi | — | XAML/templates, tray icon, accessibility perlu porting |

## 3. Keputusan & syarat migrasi (RECOMMENDATION)
1. PoC **layak dilanjutkan** — bukti integrasi Core nyata sudah ada.
2. Migrasi penuh HANYA setelah: Activity Monitor Avalonia dipakai harian paralel dengan WinForms (shadow-run), tray icon + settings ported, dan perbandingan RAM/UX nyata.
3. Selama migrasi, publish production tetap WinForms; Avalonia = build terpisah di folder sendiri.

## 4. Temuan teknis PoC (jujur)
- `MainWindow`/`ActivityMonitorWindow` code-only (tanpa XAML) untuk memperkecil risiko build PoC — XAML/databinding penuh = langkah berikutnya.
- 1 transitive package warning (NU1903 Tmds.DBus.Protocol — jalur Linux DBus; tidak menyentuh jalur Windows PoC).
- Headless test memakai sandbox config sendiri (`tests/avalonia-poc`), token test di-revoke di akhir; production tidak tersentuh.
- Bug yang ditemukan & diperbaiki saat PoC: synthetic server path lama tidak ada → kini tiap harness menulis server sintetisnya sendiri ke sandbox masing-masing + update-or-add server def (anti def basi tersimpan).

## 5. Cara menjalankan PoC
```
cd desktop-commander-local/avalonia-poc
dotnet run -c Release -- --headless-test   (otomatis, untuk CI)
dotnet run -c Release -- --ui              (jendela nyata untuk uji resize/multi-monitor manual)
```

## 6. Verifikasi jendela (empiris, terkait hardening spawn)
Metode: `tools/verify-windows.ps1` — Check A (MainWindowTitle, sinyal lemah) + **Check B (EnumWindows per PID, termasuk `ConsoleWindowClass`, `IsWindowVisible`)**. Hasil: anak jalur SelfTest (start /B) = **0 window visible** (2 anak hidup); control console nyata = **1 window terdeteksi** → metode terbukti mampu mendeteksi window nyata, sehingga hasil nol adalah bukti sungguhan. Koreksi owner diterapkan: judul jendela kosong BUKAN bukti mutlak — laporan memisahkan Check A dan Check B.
