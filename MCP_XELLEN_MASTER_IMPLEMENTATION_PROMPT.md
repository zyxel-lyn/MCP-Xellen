# MCP Xellen — Master Implementation Brief

**Tujuan:** Implementasikan pengembangan MCP Xellen secara terpadu dalam satu sesi kerja menggunakan repository existing `D:\MCP-Bridge`, berdasarkan PRD `MCP_XELLEN_PRD.md` dan audit `MCP_XELLEN_ARCHITECTURE.md`. Setelah implementasi selesai, lakukan audit pasca-implementasi secara menyeluruh dan hasilkan laporan bukti.

> Prompt ini mengizinkan implementasi pada repository project, tetapi bukan izin untuk merusak layanan aktif, mengubah jaringan publik, menghapus data, atau merotasi credential secara diam-diam. Batas keselamatan di bagian 4 tetap mutlak.

---

## 1. Peran dan hasil akhir

Bertindak sebagai principal software architect, senior .NET engineer, security engineer, desktop UI/UX engineer, QA engineer, dan technical writer.

Bangun MCP Xellen sebagai Universal AI Gateway & Control Center. Jangan berhenti pada proposal, mockup, atau scaffold kosong. Implementasikan fitur yang dapat diselesaikan dengan bukti nyata, jalankan build/test, perbaiki error yang ditemukan, lalu audit hasil akhir.

Jangan mengklaim fitur selesai jika baru berupa UI statis, placeholder, mock data, atau tombol tanpa fungsi.

## 2. Baca sebelum bekerja

1. Inventarisasi seluruh repository `D:\MCP-Bridge`.
2. Baca source code, project files, configuration, scripts, tests, README, installer, dan dokumentasi.
3. Baca `MCP_XELLEN_PRD.md` dan `MCP_XELLEN_ARCHITECTURE.md` jika tersedia di root. Jika dokumen belum disalin ke root, gunakan dokumen PRD dan audit yang diberikan bersama prompt.
4. Periksa Git status, branch, commit terakhir, untracked files, dan perubahan lokal.
5. Buat baseline report: build, test, proses terkait, port/listener, endpoint, Funnel status, konfigurasi, dan penggunaan resource.
6. Cocokkan baseline dengan audit 11 November 2026. Audit tersebut adalah snapshot historis, bukan bukti kondisi runtime saat ini.
7. Tandai setiap temuan sebagai FACT, ASSUMPTION, atau RECOMMENDATION.

Jangan mengasumsikan file, port, proses, token, atau route masih sama dengan snapshot audit. Verifikasi ulang.

## 3. Sasaran implementasi

### A. Xellen Core
Buat pemisahan yang jelas antara Core, Transport, Modules, Storage, dan Presentation. Core minimal memiliki:
- Registry untuk server, project, client, endpoint, dan policy.
- ProcessHost untuk spawn, monitor, restart, graceful shutdown, orphan detection, dan ownership.
- AuthStore untuk credential generation, scope, expiry, revoke, dan rotation.
- LogService dengan structured logging, redaction, rotation, retention.
- HealthService dan MetricsService.
- Event bus untuk aktivitas AI/tool call.
- Configuration persistence dan backup/restore.

Jangan membuat satu kelas raksasa. Gunakan dependency injection, interface, cancellation token, async I/O, dan error handling yang konsisten.

### B. Process lifecycle dan penggunaan resource
Ini prioritas kritis karena audit sebelumnya mengukur puluhan child Desktop Commander dan lebih dari 1 GB penggunaan RAM gabungan.

Persyaratan:
- Setiap proses child yang dibuat Xellen harus memiliki owner record: PID, parent PID, project/server, command fingerprint, start time, status.
- Cleanup hanya terhadap proses yang terbukti dimiliki Xellen atau cocok dengan fingerprint yang sangat spesifik dan telah diverifikasi.
- Jangan pernah menjalankan `taskkill /IM node.exe`, `taskkill /IM chrome.exe`, atau operasi blanket kill lain.
- Jangan menyentuh proses MCP milik ZCode, VS Code, browser pribadi, Construct 3, atau aplikasi lain.
- Implementasikan timeout, graceful shutdown, process-tree cleanup yang aman, restart backoff, dan orphan detection.
- Startup orphan sweep harus dry-run/report terlebih dahulu; automatic termination hanya untuk process ownership yang dapat dibuktikan.
- Tampilkan RAM/CPU per process dan server di dashboard.

### C. Native MCP Transport
Implementasikan native Streamable HTTP menggunakan SDK C# resmi yang kompatibel dengan framework repository saat ini. Implementasikan STDIO client bridge untuk server existing.

Endpoint native:
- Gunakan port baru yang dipilih setelah pemeriksaan port availability. Port `8300` hanya kandidat dari audit, bukan izin untuk mengambil alih listener yang sudah ada.
- Bind ke `127.0.0.1` secara default.
- Routing per server/project, misalnya `/srv/{serverId}/mcp` dan `/prj/{projectId}/mcp`.
- Auth per identity/project, Origin validation, rate limiting, request limits, timeout, dan session lifecycle.
- Patuhi MCP Streamable HTTP specification versi yang didukung SDK aktual.
- Uji initialize, notifications/initialized, tools/list, tools/call, 401, invalid Origin, rate limit, SSE, session expiry, reconnect, dan error handling.

**Dual-run wajib:** Supergateway existing tetap tersedia sebagai compatibility mode. Jangan mematikan atau mengganti endpoint 8023/8025 sampai native parity test lulus dan ada rollback teruji. Jangan mengubah port existing hanya agar desain baru terlihat rapi.

### D. Dashboard & AI Activity Monitor
Ini fitur inti, bukan dekorasi.

Buat:
1. Dashboard utama untuk status server, project, client, endpoint, remote access, resource, dan ringkasan aktivitas.
2. Jendela terpisah bernama **AI Activity Monitor** untuk memantau lalu lintas pekerjaan AI secara live. Jendela harus dapat dibuka terpisah, di-resize, dipindah ke monitor lain, dan tidak membekukan aplikasi utama.

Activity Monitor harus menampilkan:
- Timestamp.
- AI/client identity.
- Project.
- MCP server.
- Tool/action.
- Status queued/running/success/failed/cancelled/timeout.
- Durasi.
- Sanitized input/output summary.
- Error dan correlation ID.
- Resource/process yang terkait jika ada.

Sediakan filter client, project, server, tool, status, waktu, dan risk level. Klik event membuka detail timeline request dan log terkait. Gunakan event-driven push (SignalR/WebSocket atau event mechanism yang sesuai) dengan reconnect dan fallback yang wajar.

**Privasi:** jangan pernah mencatat token, Authorization header, password, cookie, secret, atau payload sensitif mentah. Terapkan redaction sebelum event mencapai UI dan storage. Sediakan batas ukuran payload dan retention. Jangan gunakan mock activity untuk menyatakan fitur live telah selesai; buktikan dengan tool call nyata.

### E. GUI desktop — jangan terkunci pada WinUI
GUI tidak boleh dirancang dengan asumsi bahwa WinUI adalah satu-satunya pilihan.

Evaluasi repository dan pilih strategi yang paling aman:
- Rekomendasi awal: **Avalonia UI** untuk desktop shell .NET lintas platform, jika kompatibel dengan target framework dan kebutuhan tray/window/packaging.
- Business logic tetap di Core, tidak boleh bergantung pada Avalonia/WinUI/WinForms.
- Jika migrasi UI penuh berisiko, buat presentation abstraction dan migrasi bertahap, tetapi hasil akhir tetap harus memiliki GUI modern dan Activity Monitor window terpisah.
- Jangan mempertahankan WinForms hanya karena sudah ada, dan jangan melakukan rewrite framework tanpa build proof serta alasan teknis.
- Jangan menggunakan UI berbasis screenshot/gambar statis. Seluruh tabel, status, filter, grafik, dan tombol harus berupa kontrol UI nyata.

Navigasi:
Dashboard, AI Activity, MCP Servers, Projects, AI Connections, Browser, Remote Access, Security, Logs, Settings.

### F. Design system
Gunakan warna persis:
- `#5D737E` — slate / secondary surface.
- `#FF8811` — primary action / active highlight.
- `#C0FDFB` — info / connection highlight.
- `#F4D06F` — warning / attention.
- `#020402` — dark foundation.

Buat design tokens terpusat. Perhatikan kontras teks, status success/error, hover/focus/disabled, dark theme, DPI scaling, dan keyboard navigation. Desain harus modern, premium, profesional, dan padat informasi. Hindari dashboard template generik, elemen terpotong, card berlebihan, animasi dekoratif, dan visual yang dibuat dari screenshot AI.

### G. Project Manager
Setiap project mempunyai identitas, endpoint, token/credential, allowed servers, permissions, allowed roots, client access, browser profile, dan activity scope sendiri.

Uji minimal dua project aktif secara paralel. Pastikan credential, tool access, browser session, cookie, log privat, dan stop/restart antar-project terisolasi.

### H. AI Connect dan Z.ai adapter
Buat alur:
1. Pilih project.
2. Pilih jenis client.
3. Pair client dengan one-time pairing code.
4. Terbitkan credential scoped dan expiry.
5. Generate connection package/instruction otomatis.
6. Tampilkan status initialize, tools/list, dan last activity.
7. Revoke atau disconnect client.

Untuk Z.ai Website Agent Mode:
- Audit kembali cara kerja aktual dan kemampuan platform yang tersedia.
- Buat adapter/client helper yang menangani Streamable HTTP handshake, session ID, notifications/initialized, tools/list, tools/call, JSON/SSE, retry, dan session expiry.
- Kurangi prompt manual menjadi bootstrap instruction sekecil mungkin.
- Jangan mengklaim file upload token atau prompt manual dapat dihilangkan sepenuhnya jika Z.ai tidak menyediakan persistent custom tools, MCP configuration, API, atau mekanisme credential exchange yang diperlukan.
- Token tidak boleh ditempel ke prompt, dicetak, disimpan di log, atau dimasukkan ke URL.
- Jangan mengirim credential ke domain selain endpoint Xellen yang sudah dipasangkan.

### I. Browser Manager
- Satu browser profile per project.
- Auth wajib pada jalur browser MCP.
- Lazy start dan idle stop.
- Persist profile saat stop.
- Jangan mengakses browser pribadi.
- Hindari flag `--disable-web-security` dan `--no-sandbox` kecuali ada alasan yang terverifikasi, isolasi alternatif, dan persetujuan eksplisit.
- Uji dua project paralel untuk tab/cookie/login/navigasi/stop independen.
- Jangan membuka atau mengekspos port browser unauthenticated.

### J. Remote Access
Sediakan Local only, Tailnet only, dan Public Tunnel profiles. Tampilkan rekomendasi provider berdasarkan kemampuan yang benar-benar tersedia. Jangan menjanjikan direct connection pada CGNAT.

**Tidak boleh otomatis:**
- Mengubah Tailscale Funnel/Serve.
- Menghapus route Funnel.
- Mengubah firewall, router, DNS, port forwarding, atau service.
- Mengganti endpoint publik.
- Menghentikan Tailscale atau tunnel yang sedang digunakan.
Remote access hanya menyiapkan rencana/perintah dan meminta konfirmasi eksplisit sebelum perubahan sistem/jaringan.

### K. Security, Logs, Backup
- Token per identity/project/server dengan cryptographic random.
- DPAPI untuk secret at rest jika sesuai.
- Expiry, revoke, rotation, rate limiting, Origin validation.
- Redaction terpusat.
- Audit log untuk pairing, tool calls, konfigurasi, remote access, dan security actions.
- Log rotation dan retention.
- Backup sebelum migrasi dan restore test.
- Temuan secret exposure dilaporkan dengan lokasi dan jenisnya tanpa mencetak nilai rahasia. Jangan menghapus atau merotasi secara otomatis.

## 4. Batas keselamatan mutlak

Implementasi boleh mengubah source code dan file project setelah backup dan pemeriksaan Git status, tetapi tindakan berikut **dilarang tanpa persetujuan eksplisit pengguna pada saat tindakan akan dilakukan**:

- Menghapus atau memindahkan data/token/credential/configuration existing.
- Merotasi atau mencabut token existing.
- Mengubah Funnel/Serve route, port publik, firewall, DNS, router, atau network adapter.
- Menghentikan/mengganti endpoint existing yang sedang digunakan.
- Membunuh proses yang bukan milik Xellen.
- Menghapus browser profile, cookie, session, atau user data.
- Mengubah konfigurasi server pihak ketiga di luar repository.
- Commit, push, publish, atau deploy.
- Menginstal/menghapus software global atau mengubah environment sistem.

Jika implementasi memerlukan tindakan terlarang, selesaikan bagian aman terlebih dahulu, lalu catat tindakan yang tertahan dan alasan teknisnya. Jangan menunggu persetujuan untuk seluruh pekerjaan jika bagian lain dapat dikerjakan dengan aman.

Sebelum mengubah file project:
- Periksa Git status dan pertahankan perubahan lokal.
- Jangan overwrite file pengguna yang tidak terkait.
- Buat backup untuk file konfigurasi/data yang akan dimigrasi.
- Gunakan migrasi yang idempotent.
- Simpan laporan perubahan dan rollback steps.

## 5. Strategi implementasi satu sesi

Kerjakan secara dependency-aware, bukan membuat semua fitur sekaligus tanpa urutan.

1. Baseline, inventory, backup, dan architecture decision.
2. Core abstractions, Registry, ProcessHost, logging, metrics, event bus.
3. Activity Monitor event pipeline dan dashboard.
4. Native transport + STDIO bridge + compatibility mode.
5. Project/auth isolation dan AI Connect.
6. Browser Manager isolation.
7. Remote Access UI dan safe command planning.
8. Packaging/docs/tests.

Jika satu bagian tidak dapat diselesaikan karena dependency, platform limitation, atau butuh persetujuan, implementasikan bagian lain yang aman. Jangan membuat fake implementation untuk mengejar checklist.

## 6. Quality gates wajib

- Build Debug dan Release.
- Unit tests untuk Registry, AuthStore, redaction, lifecycle, event pipeline.
- Integration tests untuk native MCP.
- Compatibility parity tests dengan Supergateway.
- Security tests: 401, Origin, rate limit, credential redaction, project isolation.
- Process lifecycle tests tanpa menyentuh proses aplikasi lain.
- Activity Monitor test menggunakan tool call nyata.
- Browser isolation test dua project bila browser module diimplementasikan.
- UI smoke test untuk window utama dan Activity Monitor.
- Backup/restore test.
- Periksa memory/resource usage dan orphan count setelah test.
- Tidak boleh menyatakan lulus jika test belum dijalankan.

Jika test membutuhkan jaringan/credential yang tidak tersedia, tandai BLOCKED dan jelaskan bukti yang diperlukan.

## 7. Audit pasca-implementasi — wajib dilakukan setelah coding

Setelah implementasi dan test selesai, lakukan audit independen terhadap hasil akhir. Jangan hanya membaca checklist yang dibuat saat coding.

Audit minimal:

### A. Source & architecture audit
- Struktur project, dependency boundaries, duplicate code, dead code, error handling.
- Tidak ada business logic tertanam di UI.
- Tidak ada placeholder/mock yang diklaim production feature.
- Konfigurasi dan migration safety.

### B. Security audit
- Secret/token leakage di source, logs, prompts, URLs, diagnostics, build output.
- Authentication, authorization, Origin, rate limit, project isolation.
- Browser endpoint exposure dan profile isolation.
- Command execution boundary dan allowed roots.
- Tidak ada insecure browser flags tanpa justifikasi.

### C. Runtime & resource audit
- Daftar process tree yang dibuat Xellen.
- PID ownership dan orphan count.
- RAM/CPU per server, child, browser, dan Core.
- Lifecycle saat start, crash, restart, shutdown.
- Tidak ada blanket process kill.

### D. MCP protocol audit
- Initialize, notifications/initialized, tools/list, tools/call.
- JSON/SSE.
- Session ID/expiry/reconnect.
- 401/invalid Origin/rate limit.
- Native versus compatibility parity.

### E. Activity Monitor audit
- Event nyata dari tool call.
- Event completeness, ordering, correlation ID, status transition.
- Secret redaction.
- Filter/detail/reconnect.
- UI tidak freeze dan tidak kehilangan event secara diam-diam.

### F. UI/UX audit
- Screenshot hanya sebagai bukti visual audit, bukan sebagai UI produk.
- Semua jendela dan halaman.
- Clipping/overflow, DPI, responsive resize, typography, contrast, empty/error/loading states.
- Tombol benar-benar berfungsi.
- Warna sesuai palet.
- Activity Monitor bisa dibuka sebagai window terpisah.

### G. Regression & compatibility audit
- Existing config dan endpoint.
- Funnel status dan route tidak berubah tanpa persetujuan.
- Desktop Commander, Construct 3, browser workflow, dan tool existing tidak rusak.
- Git diff dan untracked files diperiksa.

## 8. Deliverables wajib

Simpan dokumentasi di folder `docs/` atau struktur dokumentasi existing yang sesuai:

1. `MCP_XELLEN_PRD.md` — PRD final, diperbarui jika implementasi mengungkap perubahan kebutuhan.
2. `MCP_XELLEN_ARCHITECTURE.md` — arsitektur aktual dan target.
3. `IMPLEMENTATION_REPORT.md` — fitur, file berubah, keputusan teknis, status.
4. `POST_IMPLEMENTATION_AUDIT.md` — audit independen, temuan severity, bukti, status.
5. `TEST_REPORT.md` — semua command/test, hasil, blocked tests.
6. `SECURITY_REPORT.md` — threat model, hasil security checks, residual risks.
7. `COMPATIBILITY_MATRIX.md` — client/server/transport yang benar-benar diuji.
8. `ROLLBACK_GUIDE.md` — cara kembali ke compatibility mode dan memulihkan konfigurasi.
9. `CHANGELOG.md` — perubahan terstruktur.

Jangan menulis laporan seolah-olah semua test sukses. Sertakan tanggal, environment, command, hasil, dan bukti.

## 9. Format laporan akhir kepada pengguna

Ringkas namun faktual:
- Apa yang benar-benar selesai.
- Fitur yang belum selesai dan alasannya.
- Build/test yang lulus/gagal/blocked.
- Bukti Activity Monitor menerima tool call nyata.
- Bukti native MCP parity.
- Kondisi process/RAM/orphan.
- Status endpoint/port/Funnel sebelum dan sesudah; tegaskan jika tidak diubah.
- Risiko keamanan tersisa.
- File penting yang berubah.
- Cara menjalankan aplikasi.
- Cara rollback.

**Mulai sekarang:** lakukan inventory dan baseline terlebih dahulu, lalu implementasikan semua bagian yang aman dan dapat dibuktikan. Jangan mengakhiri pekerjaan hanya dengan rencana atau mockup. Setelah implementasi, jalankan audit pasca-implementasi sesuai Bagian 7 sebelum menyatakan hasil akhir.
