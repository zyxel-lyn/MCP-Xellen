# P1 — XELLEN CORE v1: IMPLEMENTATION REPORT

| | |
|---|---|
| **Tanggal** | 11 November 2026 |
| **Scope** | P1 saja: ProcessHost, stabilisasi RAM (F-01), orphan cleanup, Registry, LogService, MetricsService, Dashboard UI |
| **Prinsip** | Berjalan berdampingan dengan sistem lama. Tidak ada endpoint/port/token/Funnel yang diubah. Tidak ada cleanup di luar kepemilikan terbukti. |
| **Hasil akhir** | Build sukses (0 error/0 warning) • Automated tests **10/10 PASS** (exit 0) • Endpoint lama terverifikasi utuh • UI baru terverifikasi mode berdampingan |

---

## 1. TEMUAN AKAR MASALAH F-01 (investigasi berbasis bukti, bukan asumsi)

Temuan audit awal ("95 child DC = orphan, 1,2 GB") **terbukti salah mekanismenya**. Investigasi ulang dengan census PPID + analisis log memberikan akar penyebab sebenarnya:

**Fakta yang terukur (11 Nov 2026, live):**

| Bukti | Nilai | Sumber |
|---|---|---|
| Struktur per sesi | `supergateway → cmd.exe /d /s /c "node DC"` → `node DC` = **2 proses OS per sesi MCP** (supergateway spawn via shell; conhost kadang menambah 1) | census PPID: 43 cmd (PPID=gateway) + 43 node (PPID=cmd) |
| Orphan (parent mati) | **0 proses** | klasifikasi PPID seluruh child DC |
| Reaper idle timeout | **Bekerja**: 1.493 event `"Session … timed out, cleaning up"` di log gateway; 810+ sesi dibuat | grep `mcp-xellen-app.log` (69,5 MB) |
| Churn sesi | 95 → 76 → 90 child antar-pengukuran (Z.ai aktif; proyek Topiora terlihat menulis file via DC saat audit) | 3 census berjarak menit |
| Sesi berumur panjang | Child tertua ±6,5 jam; hanya 6 koneksi ESTABLISHED ke 8023 (2 milik daemon `@browsermcp` yang persisten, 1 milik health-probe app) | netstat + log |
| RAM per pasangan sesi | cmd ≈ 2–5 MB + node DC ≈ 5–103 MB → puluhan sesi aktif ≈ >1 GB | census WorkingSet |

**Kesimpulan akar masalah (3 lapisan):**
1. **Biaya per sesi = 2–3 proses** (cmd wrapper + node DC + conhost) akibat spawn-via-shell supergateway.
2. **Lifetime sesi tak terbatas selama client masih terhubung** — timeout 30 menit adalah *idle* timeout; sesi yang dipegang koneksi persisten (mis. daemon browsermcp 6,5 jam) tidak pernah di-reap. Inilah sumber child berumur jam-jaman.
3. **Churn tinggi dari Z.ai Agent Mode** (810+ sesi) × biaya 2–3 proses = puluhan proses konkuren selama dan setelah penggunaan aktif.

Orphan *tetap* menjadi risiko pada jalur abnormal (crash gateway/app, kill -9, power loss) — jalur ini yang diantisipasi mekanisme orphan cleanup P1 (saat investigasi: 0 orphan karena sistem lama kebetulan selalu mati lewat jalur graceful).

**Konsekuensi desain P1:** bukan "taskkill lebih keras", melainkan **pembatasan yang belum ada**: (a) batas umur per child, (b) batas konkurensi child, (c) pembersihan recorded-orphan untuk jalur abnormal, (d) visibilitas metrik agar kebocoran terlihat harian.

---

## 2. ARSITEKTUR PROCESSHOST

`Xellen/Core/ProcessHost.cs` — singleton `ProcessHost.Instance`, timer monitor 5 dtk, snapshot WMI berat tiap 15 dtk.

```
Program.Main
  └─ ConfigStore.LoadOrCreate()        → migrasi konfigurasi hardcoded → config/xellen.json
  └─ LogService.Init(cfg.Log, tokens)  → redaksi nilai token dari AuthTokenFile
  └─ ProcessHost.Instance.Initialize(cfg, state)
        ├─ MonitorTimer 5 dtk    : exited? crash? restart-due? stable-reset? (backoff counter reset
        │                          setelah stabil > 120 dtk); tiap 3 tick: RefreshChildren + ChildPolicy
        ├─ SweeperTimer          : startup delay 45 dtk, interval 300 dtk (configurable)
        └─ Events → UI: ServerChanged, Swept, Notification
```

**Kemampuan sesuai spesifikasi Anda:**

| Kemampuan | Implementasi |
|---|---|
| Start | `StartServer(id)` — guard konflik port dulu (TcpClient probe 300 ms); ArgumentList identik byte-per-byte dengan cmdline lama |
| Stop | **Graceful-first**: tutup stdin child → supergateway menjalankan shutdown path-nya sendiri (reap seluruh tree anaknya) → tunggu `StopTimeoutSeconds` → eskalasi `Kill(entireProcessTree)` → residual sweep |
| Restart | Manual (reset backoff) + otomatis pasca-crash dengan **exponential backoff** (base 5 dtk × 2^n, cap 300 dtk, max 5 percobaan → `Failed` + notifikasi) |
| Monitor | Tick 5 dtk: alive check, health via MetricsService, stable-reset |
| Track PID & parent-child | `TrackedServer{Proc, State, StartUtc, BackoffAttempt…}` + `Children` (direct + grandchildren) dari snapshot `Win32_Process` (PPID walk), di-swap atomik (tidak pernah dimutasi in-place → aman untuk UI) |
| Ownership | Setiap kill wajib melewati `TryKillOwned` / `TryKillRecordedChild` (Bagian 3) |
| Crash detection | `Process.EnableRaisingEvents` + `Exited` + monitor tick; exit code dicatat |
| Timeout management | Start (port guard), Stop (graceful → eskalasi), child lifetime, taskkill 5 dtk |
| Tree cleanup | `taskkill /PID x /T /F` + verifikasi pasca-kill via WMI |

---

## 3. MEKANISME ORPHAN CLEANUP (dan aturan keamanannya)

**Tiga jalur kill, semuanya dengan bukti kepemilikan wajib:**

1. **`TryKillOwned(pid, expectedPattern, …)`** — jalur umum. Saat eksekusi (bukan saat snapshot): re-fetch cmdline; tolak jika (a) proses sudah mati, (b) cmdline tidak lagi match pattern (deteksi PID-reuse), (c) cmdline match **protected pattern**. Log WARN sebelum + verifikasi sesudah.
2. **`TryKillRecordedChild(record, …)`** — untuk cucu proses tanpa marker di cmdline (kasus nyata yang ditemukan test: `ping`/`conhost` yatim setelah tree-kill). Syarat: cmdline **identik byte-per-byte** dengan snapshot yang direkam + **parent tercatat sudah mati** + tidak protected. Ini yang memperbaiki bug tree-kill yang ditemukan saat pengujian (Bagian 10).
3. **`RunSweep()` (startup + periodik + tombol UI)** — kandidat = cmdline match `OwnershipPatterns` server terdaftar; klasifikasi:
   - parent hidup & cmdline-nya juga match pattern kami → **bagian rantai hidup → tidak disentuh** (termasuk seluruh child gateway lama yang sedang berjalan);
   - parent hidup tapi bukan proses kami → **`skipped-ambiguous`** (kepemilikan tak terbukti penuh → hanya dilaporkan, sesuai aturan Anda);
   - match protected pattern → **`skipped-protected`**;
   - parent mati + cmdline match → **orphan milik Xellen terbukti** → mode `auto`: kill; mode `report`: catat saja.

**Protected patterns default** (tidak pernah dibunuh oleh jalur mana pun): `construct3-mcp`, `_bmcp`, `@browsermcp`, `reactbits`, `shadcn`, `@ant-design`, `\Google\Chrome\Application`, `ZCode`. **Tidak ada wildcard kill by image name** — `node.exe`/`chrome.exe` global tidak pernah disentuh. Selama self-test, pola production bahkan dimasukkan ke daftar protected sebagai pengaman ganda.

---

## 4. REGISTRY (CONFIG)

- Lokasi: `D:\MCP-Bridge\config\xellen.json` (+ `runtime-state.json`, + `backups\`). **Dibuat saat first run** (migrasi otomatis dari konstanta `Program.cs` lama; `migratedFrom: "hardcoded-Program.cs-2026-11-11"`).
- Schema versioning (`schemaVersion: 1`), **backup otomatis sebelum setiap save** (retensi 10 backup).
- **Tidak ada token di JSON** — hanya `authTokenFile` (PATH referensi). Terverifikasi: file config berisi path `secrets\mcp-xellen-staging-token.txt`, bukan nilainya.
- Field per server sesuai spesifikasi Anda: id, name, command, arguments, workingDirectory, environment, transport, port, healthPath, authTokenFile, autoStart, restartPolicy (enabled/maxAttempts/backoff/backoffMax/stableReset), stopTimeoutSeconds, ownershipPatterns, childProcessPatterns. `processState` dipisah ke `runtime-state.json` (lastPid, lastExit, restart total, statistik sweep) agar config tidak berubah tiap event runtime.
- Command gateway 8023 di config **diverifikasi identik** dengan cmdline live supergateway PID 8332 (termasuk `--stateful --sessionTimeout 1800000 --healthEndpoint /healthz`).
- Config lama tidak dihapus/diubah; `.cmd` launcher lama tetap utuh.

## 5. LOGSERVICE

- JSONL terstruktur: `{ts, level, component, serverId, pid, msg, data}` di `D:\MCP-Bridge\logs\xellen-core.log`.
- Rotasi ukuran: default **10 MB × 5 backup** (`xellen-core.1.log`…); floor 64 KB untuk mencegah misconfig produksi.
- **Redaksi otomatis**: nilai token dari semua `AuthTokenFile` dimuat saat init dan di-scrub dari setiap baris log (terverifikasi: `redactionEntries=1`).
- Log stdout/stderr child server ditulis level **Debug** (tidak tampil pada level Info default) agar percakapan JSON-RPC supergateway tidak membanjiri disk — kebalikan perilaku log lama yang menumbuhkan 69,5 MB.
- **Log lama `mcp-xellen-app.log` (69,5 MB saat ini) tidak disentuh sama sekali** — tetap sebagai bukti di path aslinya (app lama masih menulis ke sana).

## 6. METRICSSERVICE

- **Semua angka = pengukuran aktual**: RAM dari `WorkingSet64`, CPU dari delta `TotalProcessorTime` (sampel pertama = `-1`, jujur ditampilkan `—`), uptime dari `StartUtc`, health endpoint via HTTP probe 15 dtk.
- Snapshot per server: status, PID, port, uptime, RAM proses, CPU%, jumlah sesi child, RAM child, health, restart count. Ringkasan: managed/active/failed, orphan kumulatif (detected/killed), RAM Core, RAM child total, sweep terakhir.
- Counter orphan bersumber dari `runtime-state.json` (persisten antar run).

## 7. DAFTAR FILE BARU

| File | Isi |
|---|---|
| `desktop-commander-local\app\Xellen\Core\XellenConfig.cs` | Model config + ConfigStore (migrasi, backup, sandbox override) + RuntimeState |
| `desktop-commander-local\app\Xellen\Core\LogService.cs` | Logging JSONL + rotasi + redaksi + ring buffer UI |
| `desktop-commander-local\app\Xellen\Core\ProcessEnumerator.cs` | Snapshot `Win32_Process` via WMI (System.Management 11.0.0) |
| `desktop-commander-local\app\Xellen\Core\ProcessHost.cs` | ProcessHost + TryKillOwned + TryKillRecordedChild + OrphanSweeper + ChildPolicy |
| `desktop-commander-local\app\Xellen\Core\MetricsService.cs` | Metrik terukur + health probe |
| `desktop-commander-local\app\Xellen\UI\MainForm.Dashboard.cs` | Tab Dashboard (ListView server + ringkasan metrik) |
| `desktop-commander-local\app\Xellen\UI\MainForm.Processes.cs` | Tab Proses (tree proses + sweep + stop/restart + policy label) |
| `desktop-commander-local\app\Xellen\UI\MainForm.Logs.cs` | Tab Log (viewer + filter level + follow) |
| `desktop-commander-local\app\Xellen\SelfTest\SelfTestRunner.cs` | 10 automated tests headless (`--selftest`) |
| `desktop-commander-local\app\publish-p1\` | Output publish baru (side-by-side; `publish-direct` tidak disentuh) |
| `D:\MCP-Bridge\config\xellen.json` + `runtime-state.json` | Registry produksi (dibuat first-run UI, terverifikasi) |
| `D:\MCP-Bridge\logs\xellen-core.log` | Log baru |
| `D:\MCP-Bridge\tests\p1\` | Sandbox test (config, log, spawner.cmd, results.json) |

## 8. DAFTAR FILE YANG DIUBAH

| File | Perubahan |
|---|---|
| `app\Program.cs` | MainForm → partial; kontrol existing pindah utuh ke tab "Gateway (Existing)" (koordinat & handler sama); Start/Stop/Health diarahkan ke ProcessHost dengan **perilaku sama**; tambah mode berdampingan; `--selftest` entry |
| `app\McpXellen.Desktop.csproj` | + `System.Management` 11.0.0 (untuk WMI) |
| `app\Xellen\UI\MainForm.Logs.cs` | (perbaikan crash: `SelectedIndex` dipindah ke init setelah AddRange) |

Tidak ada file sistem lama yang diubah/dihapus selain itu.

## 9. HASIL BUILD

```
dotnet build -c Release  → Build succeeded. 0 Warning(s). 0 Error(s).
dotnet publish -c Release -o publish-p1 → OK (framework-dependent net10.0-windows)
```
Perbaikan kompilasi selama iterasi: 5 error (nullable/cast/ctor Timer) + 1 crash runtime UI — semuanya terdokumentasi di log percakapan, tidak ada yang disembunyikan.

## 10. HASIL AUTOMATED TEST — 10/10 PASS (exit code 0)

Run final (`publish-p1\MCP-Xellen.exe --selftest`, hasil lengkap: `tests\p1\results.json`):

| Test | Skenario (sesuai daftar wajib Anda) | Hasil & bukti |
|---|---|---|
| T1 | Start/Stop + child cleanup | start=True, childrenBefore=2, allGone=True, stopped=True |
| T2 | Restart + backoff | 2 auto-restart (backoff 2→4 dtk) lalu `Failed`, restarts=2 |
| T3 | Parent crash → residual cleanup | crash disimulasikan via taskkill /F pada proses uji; residual dibersihkan; state=`Crashed` |
| T4 | Orphan detection + proteksi | 2 orphan (parent mati) → `killed=2`; decoy protected → `skipped-protected` & tetap hidup; decoy parent-hidup → `skipped-ambiguous` & tetap hidup |
| T5 | Registry persistence | save→load round-trip; backup otomatis terbentuk (backups=3 kumulatif) |
| T6 | Log rotation | 4 rotasi → 3 file, base 19.107 B ≤ cap; rotation fired=True |
| T7 | Metrics accuracy | managed=1, active=1, ramProc=7,4 MB, sessionChildren=4, descendants=8, ramChildren=52,2 MB, coreRam=63,6 MB (semua WorkingSet terukur) |
| T9 | Graceful shutdown (ShutdownAll) | treeGone=True, state=`Stopped` |
| T10 | Startup recovery / stale state | PID stale 999999 ditangani tanpa kill; state persisten |
| T8 | **Proteksi proses eksternal** | 6 operasi kill oleh Core selama seluruh test → **0 non-marker kill**; census proses production (DC/supergateway/construct3/_bmcp/Chrome user) before→after = informasional (churn alami Z.ai terlihat, bukan kill kami) |

**Test menggunakan proses uji khusus** (`cmd`/`ping` ber-marker `xellen-p1-test` di `tests\p1\`) — **bukan** endpoint production. Config test hanya memuat marker test sebagai ownership pattern, dan pola production masuk daftar protected selama test.

**Bug nyata yang ditemukan & diperbaiki berkat test ini** (bukti test bekerja):
1. Tree-kill .NET meninggalkan **cucu proses** (ping/conhost) yatim → lahir mekanisme `TryKillRecordedChild`.
2. `StopServer` tidak pernah men-set `State=Stopped` → diperbaiki.
3. Crash UI (`ComboBox.SelectedIndex` pada Items kosong) → diperbaiki.
4. Tiga race/test-design issue (state race T3, decoy pollution T4→T9, audit regex) → diperbaiki.

## 11. BUKTI PROSES UJI SEBELUM / SESUDAH CLEANUP

Contoh dari run final (log `tests\p1\logs\selftest.log`, JSONL):
- Sebelum cleanup T4: `orphansBefore=2` (2 cmd ber-marker, parent mati) + decoy protected + decoy ambiguous hidup.
- Sweep: `killed=2, skipped-protected=1, skipped-ambiguous=1`; setelahnya `orphansAfter=0`, decoy protected **tetap hidup** (terverifikasi), decoy ambiguous **tetap hidup**.
- Akhir sesi test: `cleanup: marker processes remaining = 0` (semua milik test dibersihkan; tidak ada yang lain).

## 12. PENGGUNAAN RAM AKTUAL (terukur, bukan estimasi)

| Proses | RAM (WorkingSet, terukur) |
|---|---|
| App baru (publish-p1, mode berdampingan) | **67,7 MB** |
| App lama (PID 37928, masih berjalan) | 22 MB |
| Xellen Core saat selftest (headless) | 63,6 MB |
| Gateway supergateway 8023 (lama) | 39 MB |
| Child DC sesi (per pasangan cmd+node) | 7–108 MB (terukur bervariasi) |

**Kejujuran penting:** P1 **belum** menurunkan angka 1,2 GB child DC yang berjalan sekarang, karena gateway 8023 masih dimiliki app lama (kami tidak menyentuhnya, sesuai aturan Anda). Penurunan terjadi **setelah Anda berpindah** ke app baru: saat itu child policy aktif — batas konkurensi 24 sesi (cull hanya child berumur ≥30 mtk) + batas umum lifetime 240 mtk + orphan sweep — sehingga akumulasi jadi **terbatas dan terlihat** di Dashboard. Klaim penurunan spesifik (angka X MB) baru boleh dibuat setelah pengukuran pasca-perpindahan.

## 13. VERIFIKASI ENDPOINT EXISTING

Sebelum selftest → sesudah selftest → selama & sesudah smoke test UI:

| Endpoint | Sebelum | Sesudah test | Sesudah smoke UI |
|---|---|---|---|
| 8023 `/healthz` | 200 | 200 | 200 |
| 8023 unauth `POST /mcp` | 401 | **401** ✓ | (tidak diprobe ulang; health 200) |
| 8025 `/healthz` | 200 | 200 | — |
| 8089 (agent-infra) | 404* | 404* | — |
| 8024 `/healthz` | 200 | 200 | — |
| App lama PID 37928 | hidup | hidup | **hidup** |
| Gateway PID 8332 | hidup | hidup | hidup |
| Funnel | tidak disentuh | tidak disentuh | tidak disentuh |

\* 404 = server hidup tanpa route healthz (perilaku bawaan agent-infra), bukan kegagalan.

Smoke test UI (PID 4784, 14 detik): app hidup stabil, log mencatat *"Gateway 8023 sudah berjalan oleh proses lain. Mode berdampingan aktif; endpoint tidak disentuh."*, config produksi ter-migrasi, lalu graceful close (WM_CLOSE) → keluar bersih; app lama tetap hidup.

## 14. KELEBIHAN IMPLEMENTASI

- Kepemilikan proses terbukti sebelum setiap kill (cmdline re-verify + protected list + anti-PID-reuse + syarat orphan) — nol wildcard.
- Graceful stop memakai jalur shutdown bawaan supergateway (stdin close) sebelum eskalasi.
- Dual-mode transport-ready: config per server; side-by-side guard mencegah konflik port dengan app lama.
- Semua metrik aktual; kebocoran proses kini terlihat harian di Dashboard.
- Test suite otomatis yang benar-benar menemukan 2 bug Core — bukan test kosmetik.
- Config + token + log lama + endpoint lama: nol perubahan destruktif.

## 15. KEKURANGAN IMPLEMENTASI

- Child policy hanya bisa membatasi (bound), tidak menghilangkan biaya 2–3 proses per sesi — perbaikan struktural butuh P2 (native transport, mode shared-child).
- Deteksi idle per child tidak mungkin dari luar (koneksi terminasi di gateway, bukan di child) → policy berbasis umur/konkurensi, bukan idle per sesi.
- Cull saat cap terlampaui dapat memotong sesi yang kebetulan sedang mengeksekusi request (probabilitas rendah: hanya child ≥30 mtk yang di-cull; request sub-detik) — risiko diambil dan di-dokumentasikan.
- WMI menjadi dependensi enumerator (fallback tidak tersedia di P1; kegagalan WMI di-log ERROR dan fitur sweep/tracking menurun).
- "Graceful" untuk child selain supergateway tetap hard-kill (node stdio tanpa sinyal yang andal di Windows) — sama seperti perilaku upstream.
- Orphan sweep `auto` hanya membunuh kasus parent-mati; kasus parent-hidup-tapi-bukan-kami selalu report-only (bisa berarti orphan lama tidak pernah bersih bila parent-nya ber-PID-reuse — dicatat, bukan dibunuh).
- UI WinForms fungsional namun masih dasar (belum tray icon, belum notifikasi toast).

## 16. RISIKO YANG TERSISA

1. **Perpindahan app** belum dilakukan — selama app lama masih memegang 8023, semua policy P1 belum aktif di production. Perpindahan = tutup app lama (tree-kill children-nya), buka `publish-p1\MCP-Xellen.exe`. Keputusan waktu perpindahan milik Anda.
2. Sweep startup pertama (+45 dtk) di app baru dapat membersihkan orphan historis (bila ada) — akan ter-log dengan bukti cmdline; perilaku ini memang tujuan P1, tapi perlu Anda ketahui.
3. Cull concurrency (24) dan lifetime (240 mtk) adalah default yang bisa diubah di `config\xellen.json` — nilai terlalu rendah akan memotong sesi aktif.
4. `conhost.exe` ikut terhitung sebagai descendant — angka descendant di UI sedikit lebih besar dari pasangan cmd+node murni (kosmetik).
5. Log child pada level Debug bisa membesar bila `minLevel` diturunkan ke Debug untuk diagnostik — rotasi menahan, tapi tetap pantau.

## 17. CATATAN UNTUK CHATGPT

**Apa yang sebenarnya berhasil dibuat.** Xellen kini punya Core proses yang nyata: registry ber-schema + backup otomatis, ProcessHost dengan ownership-proof triple-check sebelum kill, graceful-first stop lewat jalur shutdown bawaan supergateway, crash detection + restart backoff eksponensial, orphan sweeper dengan klasifikasi (killed/reported/skipped-protected/skipped-ambiguous), LogService JSONL ber-rotasi dengan redaksi token, MetricsService yang hanya menampilkan angka terukur, dan Dashboard WinForms 4-tab yang mempertahankan seluruh fungsi lama. Automated test 10/10 lulus dan — yang lebih penting — **test menemukan dua bug Core sungguhan** (cucu proses lolos tree-kill; state Stopping tidak pernah menjadi Stopped) yang keduanya sudah diperbaiki dan ter-cover test.

**Apa yang belum.** Root cause F-01 tidak "dihilangkan", melainkan **dibatasi**: biaya 2–3 proses per sesi tetap ada (struktur supergateway), sesi berumur panjang kini punya ceiling 240 mtk, akumulasi kini punya ceiling 24 sesi. Perbaikan struktural (1 proses shared per server) butuh P2 native transport. Policy P1 juga belum aktif terhadap gateway yang berjalan karena gateway masih dimiliki app lama — ini disengaja (aturan berdampingan Anda). Angka penurunan RAM belum bisa dijanjikan; yang bisa dijanjikan adalah *bounded* dan *terlihat*.

**Kejujuran arsitektur.** Investigasi membuktikan diagnosis awal audit ("95 orphan") keliru mekanismenya — tidak ada orphan; yang ada adalah churn sesi × biaya proses × sesi berumur tak terbatas. Laporan ini mengoreksi audit. Justru ini nilai penting P1: keputusan dibangun di atas census PPID + 1.493 bukti event timeout, bukan pada narasi awal.

**Yang perlu diperiksa/diperbaiki berikutnya (urut prioritas).** (1) Perpindahan app lama → baru (manual, kapan pun Anda siap); (2) P2 native transport untuk memangkas 1–2 proses per sesi secara struktural; (3) fallback enumerator tanpa WMI; (4) tray icon + notifikasi; (5) pengukuran pasca-perpindahan untuk angka RAM riil.

## 18. REKOMENDASI FASE BERIKUTNYA

1. **Perpindahan (manual, 5 menit, milik Anda):** tutup app lama → jalankan `publish-p1\MCP-Xellen.exe` → amati Dashboard 1× sesi Z.ai aktif → cek `config\runtime-state.json` + log sweep. Buat shortcut desktop baru ke `publish-p1\MCP-Xellen.exe` (shortcut lama tidak diubah).
2. **P2 — Native Transport:** host Streamable HTTP native (SDK C# resmi MCP) di port BARU (usulan 8300) + StdioBridge dengan mode `shared-child` → memangkas biaya per sesi dari 2–3 proses menjadi 0 proses tambahan (child hanya untuk server itu sendiri), plus rate limit + audit log.
3. **P3 — Browser Manager** (desain sudah tervalidasi audit penuh sebelumnya) di atas Core yang kini sehat.
4. **P0 hygiene yang tertunda keputusan Anda:** rotasi token daemon yang bocor di `probe2.json`, pembersihan route Funnel basi `/oc`, `/wcu`.

---
*Laporan ini hanya mengklaim yang terukur. Bukti mentah: `tests\p1\results.json`, `tests\p1\logs\selftest.log`, `logs\xellen-core.log`, output Event Log (crash UI awal), dan census proses live yang dikutip per bagian.*
