# POST-IMPLEMENTATION AUDIT — 13 November 2026

Audit independen dilakukan SETELAH coding, dengan bukti command/grep/run — bukan checklist dari sesi coding. Temuan diberi label FACT / ASSUMPTION / RECOMMENDATION dan severity.

## A. Source & architecture
- **FACT (OK):** pemisahan Core/Transport/UI nyata — `Xellen/Core` tanpa `using System.Windows.Forms`; UI hanya memanggil Core/Transport.
- **FACT (OK):** tidak ada mock/placeholder yang diklaim fitur — Activity Monitor kosong bila belum ada tool call (by design); Projects hanya menampilkan data registry nyata.
- **FACT (minor):** duplikasi kecil `ActivityStore.Add` dua kali di StdioMcpBridge.CallToolAsync (Running + final) — disengaja (timeline), tapi bisa dirapikan.
- **FACT (minor):** `NativeTransportHost._config` static — di-set saat Start; test beruntun aman karena Start/Stop per test.
- **RECOMMENDATION:** pindahkan sesi & rate-limit store ke interface agar unit-testable tanpa Kestrel.

## B. Security
- **FACT (OK):** grep source Xellen: **0** `taskkill /IM`, **0** `disable-web-security`, **0** `no-sandbox`.
- **FACT (OK):** auth.json hash-only (T36 memverifikasi plaintext TIDAK ada di file).
- **FACT (OK):** redaction aktif di LogService (registered secrets) + activity summaries (T34).
- **FACT (temuan, report-only):** `probe2.json` memuat token lama (sudah invalid — bukti scan 12 Nov); menunggu keputusan owner (A/B).
- **FACT (temuan):** route Funnel "No serve config" terdeteksi 13 Nov — kondisi runtime berubah di luar sesi; dilaporkan, tidak diutak-atik.
- **RECOMMENDATION:** rate limit naikkan ke token-bucket sliding window; tambah audit-log event untuk auth failure.

## C. Runtime & resource
- **FACT:** production sepanjang sesi: launcher PID 30704 (publish-direct) + gateway 8023 PID 2020 hidup; 8023/8024/8025 healthz 200 di awal dan akhir sesi.
- **FACT:** test T1–T16 membuktikan lifecycle tanpa orphan (cleanup=0 marker tersisa).
- **FACT:** 8300 bebas sepanjang sesi (transport production OFF — opt-in).
- **FACT (perubahan eksternal):** 8089 agent-infra UNREACHABLE pada verifikasi akhir (hidup saat baseline pagi) — proses pihak ketiga mati sendiri, bukan aksi Xellen (T8: 0 non-marker kill).

## D. MCP protocol
- **FACT:** initialize (negosiasi versi), notifications/initialized (202), tools/list (agregasi prefiks), tools/call (forward + content), ping, session-id/expiry(404)/missing(400), JSON+SSE, 401, Origin 403, 429 — T37/T39/T40.
- **FACT:** parity initialize/tools/list vs compat 8023 — T41.
- **RECOMMENDATION:** resumability (Last-Event-ID) + GET SSE stream = P2 berikutnya.

## E. Activity Monitor
- **FACT:** pipeline event nyata: transport tools/call → EventBus → store; T35 akunting drop jujur; T39/T37 memicu event nyata (echo calls) yang terlihat di store.
- **BLOCKED:** verifikasi visual jendela terpisah (headless) — T42 skipped; bukti yang diminta: buka manual tombol "Open AI Activity Monitor".

## F. UI/UX
- **FACT:** palet resmi dipakai via DesignSystem; Activity Monitor adalah Form terpisah (bisa dipindah monitor lain).
- **BLOCKED:** screenshot/resize/DPI audit — butuh sesi interaktif.

## G. Regression & compatibility
- **FACT:** T1–T32 regresi lulus semua (proses, WMI, child policy).
- **FACT:** endpoint production 8023/8024/8025 healthz 200 di awal dan akhir; Funnel tidak disentuh (status "No serve config" = kondisi eksternal yang dilaporkan).
- **FACT:** Git absent — versioning via folder backups (pre-master-20261113 + config backups).

## Ringkasan severity
- Critical: 0. High: 0. Medium: 2 (Funnel route hilang — kondisi eksternal; T42 UI smoke blocked). Low: 4 (duplikasi kecil, static _config, DPAPI, resumability).
