# INDEPENDENT FINAL EVIDENCE REVIEW — MCP Xellen (13 November 2026)

**Audiitor:** Independent Technical Review (sesi terpisah; tidak mempercayai klaim sesi implementasi sebelumnya tanpa cek disk).
**Metode:** setiap klaim dicocokkan dengan bukti aktual — file, mtime, isi source, hasil JSON, log eksekusi, proses/ports live. Read-only: tidak ada build/publish/perubahan file repository. Semua perintah audit berjalan background dengan output ke `logs/audit-evidence.txt` (dibaca via Read tool, tanpa tab foreground).
**Status yang dipakai:** VERIFIED / PARTIALLY VERIFIED / NOT VERIFIED / FAILED / BLOCKED.

---

## 1. EXECUTIVE SUMMARY

| Area | Status | Ringkas |
|---|---|---|
| SelfTest regresi 40/0/1 | **VERIFIED** | results.json mtime 04:18:23, 41 entri, 0 gagal, T21–T24+T33–T42 hadir & pass |
| GUI Audit (G-1..G-11) | **PARTIALLY VERIFIED** | 6 fix terverifikasi di source+build; visual/DPI/resize = BLOCKED |
| Avalonia PoC 7/7 | **VERIFIED** (fungsi) / **BLOCKED** (visual) | reuse Core terverifikasi; tool call nyata terbukti; visual blocked |
| Z.ai Adapter e2e 4/4 | **VERIFIED** (localhost) / **NOT VERIFIED** (jalur Funnel + paket via UI) | session/SSE/401 terbukti; UI-generated package belum dieksekusi |
| Process & Console Window | **VERIFIED** dengan **limitasi terdokumentasi** | Check B valid (control positif); conhost/PID limitation dicatat |
| Publish binary final | **VERIFIED** | exe mtime 04:32:47 ≥ newest source 04:13:58; size/version dicatat |
| Production utuh | **VERIFIED** | launcher 30704 + SG 2020 hidup; 8023/8024/8025 = 200; Funnel "No serve config" (kondisi eksternal) |

**Kesimpulan singkat:** klaim inti sesi implementasi 13 Nov **sesuai kondisi aktual**. Tidak ditemukan bukti bertentangan. Temuan baru bersifat minor (2 self-match artifact di tooling audit, quirk harness, duplikasi docs, FileVersion kosong) — tidak ada yang mengubah kesimpulan.

## 2. DAFTAR DOKUMEN YANG DIPERIKSA

12 file di `D:\MCP-Bridge\docs\` (inventory + mtime di §10 bawah): GUI_AUDIT, AVALONIA_FEASIBILITY_REPORT, ZAI_ADAPTER_REPORT, NEXT_PHASE_TEST_REPORT, IMPLEMENTATION_REPORT, MCP_XELLEN_ARCHITECTURE, POST_IMPLEMENTATION_AUDIT, TEST_REPORT, SECURITY_REPORT, COMPATIBILITY_MATRIX, ROLLBACK_GUIDE, CHANGELOG.
**Laporan tambahan yang relevan (dicatat):** 4 laporan P1 di root (`P1_XELLEN_CORE_IMPLEMENTATION_REPORT.md`, `P1_CHILD_POLICY_FIX_REPORT.md`, `P1_WMI_FAILURE_FIX_REPORT.md`, `P1_WMI_TIMEOUT_HARDENING_REPORT.md`) + `MCP_XELLEN_FULL_ARCHITECTURE_AUDIT.md`.
**Inkonsistensi antar dokumen yang ditemukan:** SECURITY_REPORT.md & ROLLBACK_GUIDE.md memuat salinan COMPATIBILITY_MATRIX/CHANGELOG (duplikasi, terverifikasi grep ×1 masing-masing) — konsisten dengan temuan G-10 GUI_AUDIT.

## 3. MATRIKS KLAIM vs BUKTI AKTUAL (ringkas; detail per bagian)

| Klaim | Bukti disk | Status |
|---|---|---|
| SelfTest 40/0/1 fresh | results.json mtime 04:18:23, passed=40 failed=0 skipped=1 | VERIFIED |
| T21–T24 ikut dijalankan | entri T21/T22/T23/T24 di results.json, Pass=true masing-masing | VERIFIED |
| T42 skipped + alasan | entri T42 Skipped=true, alasan headless | VERIFIED |
| GUI G-1 push event | `ActivityMonitorForm.cs`: `EventBus.Instance.Subscribe` ×1 + `BeginInvoke(new Action(RefreshView))` ×1 | VERIFIED (source) |
| GUI G-2 seleksi dipreservasi | `Guid? selected` + restore per EventId | VERIFIED (source) |
| GUI G-3 DPI PerMonitorV2 | csproj + generated editorconfig `build_property.ApplicationHighDpiMode = PerMonitorV2` | VERIFIED |
| GUI G-4/G-5 anti-clipping | rows AutoSize ×2; FillLastColumn ×3 / FillLastColumnProjects ×3 | VERIFIED (source) |
| GUI G-11 spawner /B | spawner.cmd ×2, spawner2.cmd ×2, template ×4 | VERIFIED (source) + empiris (window-verify2) |
| Avalonia reuse Core | csproj `Compile Include` Core+Transport ×2 | VERIFIED |
| Avalonia 7/7 PASS | logs/avalonia-poc9.txt (4/3 fail, sebelum fix) → logs/final-rerun3.txt (**7/7 PASS, POC_EXIT=0**) | VERIFIED |
| Avalonia 2 window + event nyata | HeadlessTest.cs: `new MainWindow(` + `new ActivityMonitorWindow(` ×2; P4 body `echo:avalonia-poc-real-call`; P5 vmEvents=2 | VERIFIED (headless) |
| Avalonia visual/DPI/multi-monitor | tidak ada bukti visual | BLOCKED |
| Adapter session capture | zai_mcp_client.py: `Mcp-Session-Id` ×3 di `_post` | VERIFIED |
| Adapter SSE parse | `text/event-stream`/`data:` ×6 | VERIFIED |
| Adapter reconnect | `call_tool_with_reconnect` ×2 | VERIFIED |
| Adapter e2e 4/4 | logs/zai-adapter-e2e.txt run 04:02:25 | VERIFIED |
| Wrong-token 401 | e2e log: `HTTP 401: Unauthorized...` NEG_EXIT=1 | VERIFIED |
| Adapter = native Z.ai | — | **BUKAN** (klaim sesuai batasan; VERIFIED sebagai non-claim) |
| UI-generated adapter package | handler `GenerateAdapterPackage` ada; eksekusi tombol tidak pernah dilakukan | NOT VERIFIED |
| CreateNoWindow semua jalur | tabel per file di §E; ProcessHost 3 ref, Bridge 1, SelfTest 6, Remote 1; Projects 0 = explorer (intended) | VERIFIED + 1 limitation (conhost) |
| Binary final fresh | exe mtime 04:32:47 ≥ newest .cs 04:13:58 | VERIFIED |
| Production utuh | launcher 30704, SG 2020, 8023/8024/8025=200; Funnel "No serve config"; 8089 UNREACHABLE | VERIFIED (kondisi aktual) |

## 4. HASIL VERIFIKASI SELFTEST
- **VERIFIED:** results.json (mtime 04:18:23, 11.063 B) berisi 41 entri; passed=40, failed=0, skipped=1; **T21 pass** (status=failed, no-kill, orphans alive), **T22 pass** (stats 64→64, 36→36), **T23 pass** (completed, killed=2, consecutiveFailures=0), **T24 pass** (ERROR/UNKNOWN→CLEAN); T42 Skipped=true alasan headless.
- **Cocok source:** SelfTestRunner.cs 76.363 B; jumlah entri 41 konsisten dengan metode Check; T33–T42 ada di source (evidence: entri results + nama metode di file; regex counter tooling `T\(33_` salah pola → **tooling bug dilaporkan**, bukan bug implementasi).
- **CATATAN JUJUR (PARTIALLY):** `startedUtc == finishedUtc` di results.json — harness menulis keduanya di akhir run, sehingga durasi run hanya terbukti tidak langsung (mtime + log timeline), bukan dari JSON.
- **CATATAN (false positive):** deteksi "selftest process still running: True" di skrip audit = **self-match** (pola `-like` cocok dengan command line skrip audit sendiri). Kelas bug sama dengan kasus judul-jendela; dilaporkan agar tidak diwarisi.

## 5. HASIL VERIFIKASI GUI AUDIT
| Temuan | Status verifikasi |
|---|---|
| G-1 push event | **VERIFIED** (source ×1 subscribe, ×1 BeginInvoke) |
| G-2 seleksi dipreservasi | **VERIFIED** (source) |
| G-3 DPI PerMonitorV2 | **VERIFIED** (csproj + generated editorconfig) |
| G-4 anti-clipping Projects | **VERIFIED** (source ×2) |
| G-5 FillLastColumn | **VERIFIED** (source ×3 / ×3) |
| G-6 multi-monitor top-level | **PARTIALLY VERIFIED** (kode ✓; visual blocked) |
| G-7/G-8/G-9/G-10 | ACCEPTED/CATAT (sesuai laporan) |
| G-11 spawner /B | **VERIFIED** (source ×2/×2/×4) + **empiris** (window-verify2: anak 0 window, control 1) |
| Visual/DPI/resize nyata | **BLOCKED** |
Kesimpulan: 6 fix FIXED terkonfirmasi di source + build; sisanya sesuai label.

## 6. HASIL VERIFIKASI AVALONIA POC
- **VERIFIED:** struktur 6 file; csproj reuse Core+Transport (2 Compile Include, tanpa duplikasi Core); Avalonia refs ×6; HeadlessTest membuat 2 window; POST initialize+tools/call nyata ×5; ViewModel subscribe+marshal ×2.
- **VERIFIED:** run history jujur — attempt-9 (03:53) **4 pass / 3 fail** (bug def server basi: child `MODULE_NOT_FOUND` → handshake timeout), setelah fix update-or-add + synthetic lokal → **final-rerun3 7/7 PASS**. Rantai bug→fix→pass terdokumentasi di log.
- **BLOCKED:** visual/DPI/multi-monitor/resize nyata (headless). `--ui` tersedia untuk owner.
- **RECOMMENDATION:** XAML/databinding penuh + tray icon = prasyarat migrasi.

## 7. HASIL VERIFIKASI Z.AI ADAPTER
- **VERIFIED:** session capture (header `Mcp-Session-Id` di `_post` ×3), SSE parsing ×6, reconnect `call_tool_with_reconnect` ×2, token tidak pernah dicetak (print-token = 0).
- **VERIFIED:** e2e run 04:02:25 — LIST 2 tools prefiks benar; CALL JSON exit 0; CALL SSE exit 0; NEGATIVE 401 exit 1.
- **NOT VERIFIED:** eksekusi tombol **Generate Z.ai Adapter Package** di GUI (butuh sesi interaktif); yang terverifikasi hanya template + harness e2e.
- **FACT:** adapter BUKAN integrasi native Z.ai (sesuai batasan laporan) — pernyataan ini konsisten.

## 8. HASIL VERIFIKASI PROCESS & CONSOLE WINDOW
- **VERIFIED:** tabel liputan per file — ProcessHost 3 ref, StdioMcpBridge 1, SelfTestRunner 6, Remote 1; satu-satunya tanpa flag = `explorer.exe` (intended).
- **VERIFIED:** Check B (enumerasi window aktual per PID) dengan **control positif** (console nyata terdeteksi 1) → metode valid; anak jalur SelfTest = 0 window dengan 2 proses hidup.
- **VERIFIED (koreksi owner diterapkan):** Check A (judul) terbukti lemah — dilaporkan terpisah dari Check B.
- **LIMITASI (jujur):** console window pada Windows modern bisa dimiliki `conhost.exe` dengan PID berbeda dari proses console — pemetaan per-PID bisa menghasilkan false negative untuk child tertentu. Mitigasi yang ada: anak /B mewarisi console tersembunyi parent (tidak membuat window sendiri), dan control positif membuktikan metode bisa mendeteksi bila window ada. Risiko residual: rendah, tapi bukan nol.
- **CATATAN (self-match):** detektor proses "selftest still running" dan "stray GUI" pada skrip audit = self-match command line; telah dikenali dan dikoreksi manual (kill by PID publish-p1; production 30704 tak tersentuh).

## 9. HASIL VERIFIKASI PUBLISH
- **VERIFIED:** `publish-p1\MCP-Xellen.exe` mtime **04:32:47** ≥ newest source `.cs` **04:13:58** (MainForm.Remote.cs) → binary memuat seluruh perbaikan yang diklaim (termasuk RunTool, /B template, update-or-add).
- **VERIFIED:** build Release 0 error/0 warning; publish output sukses.
- **NOT VERIFIED (minor):** `McpXellen.Desktop.dll` FileVersion kosong (csproj tidak set Version) — kosmetik.
- **FACT:** publish ulang TIDAK dilakukan oleh sesi audit ini (sesuai batasan).

## 10. KONDISI PRODUCTION TERVERIFIKASI (read-only, saat audit)
- Launcher `publish-direct\MCP-Xellen.exe` **PID 30704 hidup** (satu-satunya instance publish-direct).
- Supergateway 8023 **PID 2020 hidup**; healthz **8023=200, 8024=200, 8025=200**.
- 8089 agent-infra = UNREACHABLE (proses pihak ketiga mati sendiri — FACT eksternal, bukan aksi Xellen; T8 0 non-marker kill).
- Port 8300 FREE (native transport production OFF — opt-in belum diaktifkan).
- Funnel: **"No serve config"** — route 8443/10000 tidak ada saat ini (kondisi eksternal berubah sejak 11 Nov; dilaporkan, tidak diutak-atik).
- Token/Firewall/DNS tidak disentuh.

## 11. TEMUAN BARU, INKONSISTENSI, DAN RISIKO
| # | Temuan | Severity | Bukti |
|---|---|---|---|
| N-1 | Harness menulis `startedUtc == finishedUtc` (keduanya di akhir run) → durasi run tidak terbukti dari JSON | Low | results.json kedua timestamp identik |
| N-2 | Regex counter T33–T42 pada skrip audit salah pola (hasil 0) | Low (tooling) | diganti bukti entri results.json |
| N-3 | Self-match pada detektor proses (2 kasus terpisah) | Low (tooling) | dikenali + dikoreksi manual; production tak tersentuh |
| N-4 | Duplikasi konten antar docs (SECURITY↔COMPAT, ROLLBACK↔CHANGELOG) | Low | grep ×1 masing-masing |
| N-5 | FileVersion kosong pada dll | Low | csproj tanpa Version |
| N-6 | Funnel "No serve config" + 8089 mati = perubahan lingkungan di luar kendali sesi | Medium (operasional) | tailscale serve status live |
| N-7 | conhost limitation pada verifikasi jendela per-PID | Low | dijelaskan §8 |
| N-8 | UI-generated adapter package belum pernah dieksekusi | Low | handler ada, eksekusi butuh GUI |

## 12. DAFTAR ITEM NOT VERIFIED / BLOCKED
- **NOT VERIFIED:** eksekusi tombol Generate Adapter Package (GUI); visual Activity Monitor & Dashboard; tools/call parity di Desktop Commander via jalur native; jalur Funnel untuk adapter; kompatibilitas Claude/Cursor/Continue (kompat matrix baris 10).
- **BLOCKED:** semua verifikasi yang butuh sesi interaktif (headless) dan yang butuh sesi Z.ai nyata berizin.
- **SKIPPED (sah):** T42 UI smoke (headless).

## 13. KESIMPULAN KESIAPAN PROYEK
- **Kode & test:** P1 penuh + master additions terverifikasi end-to-end pada lingkungan headless ini; regresi 40/0/1 fresh dan konsisten dengan source.
- **Production-ready?** **BELUM dinyatakan production-ready** — secara fungsional inti terbukti, tapi: native transport production masih OFF (opt-in), UI smoke & verifikasi visual blocked, tools/call parity DC menunggu sesi nyata, Browser Manager (P3) belum ada, DPAPI belum ada. Semua terdokumentasi jujur.
- **Risiko tertinggi saat ini:** Funnel hilang ("No serve config") + 8089 mati = alur Z.ai via internet saat ini TERPUTUS sampai owner mengaktifkan ulang — keputusan owner, bukan tindakan audit.

## 14. REKOMENDASI TAHAP BERIKUTNYA (berbasis bukti)
1. **Keputusan owner:** aktifkan ulang Funnel + putuskan rotasi token browsermcp (P0, rencana A/B sudah siap).
2. **P2 lanjutan:** resumability SSE + token-bucket rate limit + audit-log auth failure (RECOMMENDATION POST_AUDIT).
3. **Shadow-run Avalonia** paralel WinForms oleh owner (T42 unblock) sebelum memutuskan migrasi.
4. **tools/call parity di DC** via satu sesi Z.ai berizin → lengkapi COMPATIBILITY_MATRIX baris 6/7.
5. Perbaikan minor: FileVersion di csproj, bersihkan duplikasi docs, ganti detektor proses dengan matching yang tidak self-match.
