# P0 SECURITY & REMOTE ACCESS ASSESSMENT — MCP Xellen (13 November 2026)

**Audiitor:** Senior Security Engineer & Infrastructure Auditor (sesi read-only).
**Bukti mentah:** `logs/p0-assessment.txt`, `logs/p0-gapfill.txt`, `logs/p0-gapfill2.txt`, `logs/p0-10652.txt`, `logs/window-verify2.txt`, `logs/audit-evidence.txt` (semua timestamp 13 Nov 2026 04:34–04:57 WIB).
**Status keseluruhan:** Assessment selesai. TIDAK ADA perubahan konfigurasi/konfigurasi production selama audit.

---

## 1. EXECUTIVE SUMMARY

1. **Production MCP (8023 DC compat) hidup dan sehat**: initialize 200 + session, tools/list dijawab (SSE envelope), no-auth ditolak 401. Launcher 30704 + gateway 2020 hidup.
2. **Remote access via Funnel MATI**: `funnel status` dan `serve status` = `No serve config`; probe ketiga endpoint publik gagal. Ini penyebab utama Z.ai-via-internet tidak berfungsi.
3. **Browser MCP 8089 (agent-infra)**: socket berstatus CLOSED (bukan LISTENING), PID 2880 masih hidup — layanan browser automation ini MATI. Chain browsermcp (9010) HIDUP dan justru kini dipakai juga oleh **Hermes Agent** (temuan baru).
4. **Tidak ditemukan token aktif yang bocor**: satu-satunya salinan token di `probe2.json` sudah INVALID (daemon regenerasi token per start — versi daemon kini 2.5.0).
5. **6 instance GUI publish-p1 (binary master) hidup sebagai stray** dari kegagalan e2e — perlu ditutup dengan persetujuan (bukan production).

**Apakah MCP Xellen aman?** Localhost: **YA dengan catatan** (auth aktif di 8023; 8024/8025 auth Bearer aktif; tidak ada listener publik baru). Internet: **TIDAK BISA DIAKSES** (Funnel mati) — itu justru mengamankan sistem sementara, tapi memutus workflow Z.ai.

---

## 2. KONDISI AKTUAL MCP XELLEN (semua VERIFIED, timestamp 13 Nov 04:43–04:56)

| Item | Nilai aktual | Bukti |
|---|---|---|
| Launcher production | PID 30704, `publish-direct\MCP-Xellen.exe`, hidup | Get-CimInstance |
| Gateway 8023 | PID 2020 (start 13-11 02:15), healthz 200, initialize 200+session, tools/list → SSE, no-auth 401 | p0-assessment S5 |
| 8024 construct3 | healthz 200, initialize 200, tools/list (SSE) | p0-assessment S7 |
| 8025 browser gateway | healthz 200, initialize 200, tools/list (SSE); listener = node PID 10652 (start 11-11 02:21, cmdline tidak terbaca) | S4/S6 + p0-10652.txt |
| 8089 agent-infra | socket CLOSED (bukan LISTENING); PID 2880 masih hidup | S3 + p0-gapfill |
| 9010 browsermcp daemon v2.5.0 | PID 29264, LISTENING 127.0.0.1, 5 koneksi ESTABLISHED (Chrome + chain) | S3/S12 |
| Native transport 8300 | FREE, config `enabled=False` | S3/S11 |
| Funnel/Serve | `No serve config` (funnel + serve status) | S9 |
| Last boot | **11-04 08:27** (tidak ada reboot 13 Nov) | S1 |
| Tailscale | v1.104.4, service Running, tailscaled 2 proses sejak 11-04 | S1 |

**Perubahan lingkungan sejak Independent Review (13 Nov pagi):** 8089 berubah alive→CLOSED-state; Funnel tetap mati; sisanya konsisten.

---

## 3. SECURITY ASSESSMENT (ringkas; detail §4–§8)

| Area | Status | Temuan utama |
|---|---|---|
| Auth 8023 (production DC) | **VERIFIED aktif** | initialize Bearer 200 + session; no-auth 401 |
| Auth 8025/8024 | **VERIFIED aktif** | Bearer wajib; SSE envelope dijawab |
| Token storage | **VERIFIED aman di jalur Xellen baru** | auth.json hash-only; plaintext hanya di `secrets\*.txt` (file terpisah, tidak di config/log) |
| Browser MCP auth | **PARTIALLY VERIFIED** | daemon 9010 token-protected (43 char, regen per start) TAPI multi-client bersama (Chrome + Hermes + test chain) dan agent-infra 8089 lama TANPA auth (mati) |
| Native transport | **VERIFIED OFF di production** | config enabled=False; 8300 free; parity terbukti hanya di test |
| Process hygiene | **VERIFIED dengan 1 temuan** | 6 stray publish-p1 GUI + 1 zombie node 2880 (8089 CLOSED) |
| Secrets di repo | **PARTIALLY** | probe2.json token STALE (invalid); secrets\*.txt plaintext file-based (desain existing) |

---

## 4. AUTHENTICATION & TOKEN AUDIT

**Inventaris (nama/ukuran saja — nilai tidak dibaca):**
`secrets\`: mcp-api-key.txt (64B), mcp-api-key-staging.txt (24B), mcp-approval-ui-token.txt (64B), mcp-browser-staging-token.txt (64B, mtime 11-11 02:21), mcp-construct3-shortshift-token.txt (64B), mcp-xellen-staging-token.txt (64B).
`config\auth.json`: **TIDAK ADA** (tidak ada identity native yang diterbitkan di production — sesuai desain opt-in).
`browsermcp`: daemon.json token (43 char, **regenerated per daemon start** — ver 2.5.0 kini), identity.json keypair (publik 124 / privat 241) **persisten sejak 3 Nov**.

**Mekanisme verifikasi:**
- Compat (8023/8024/8025): Bearer via `--apiKeyFile`, no-auth → 401 (**VERIFIED live**), initialize+session → 200 (**VERIFIED live**).
- Native (AuthStore): hash-only persistence (T33/T36), plaintext tidak pernah persist (**VERIFIED** di selftest).
- browsermcp daemon: token di daemon.json dipakai pada query-string `/client?token=` dan `/cdp?token=` (README 2.4.0/2.5.0) + pairing keypair six-digit.

**Risiko:**
| Risiko | Tingkat | Bukti |
|---|---|---|
| Token staging plaintext file-based di `secrets\` (desain existing) | P2 | nama file + mekanisme (nilai tidak dibaca) |
| probe2.json masih berisi token browsermcp STALE (invalid sejak daemon restart 12-11 02:14) | P3 (sudah invalid; tetap perlu scrub) | scan 12 Nov: nilai ≠ token aktif |
| Token dipakai lintas koneksi: token 8023 sama untuk semua sesi Z.ai (desain single-tenant) | P2 | satu file token, semua client sama |
| Hermes Agent + Xellen test chain + Chrome memakai daemon 9010 yang sama | P2 | 5 koneksi ESTABLISHED; browser session bersama |
| Log legacy 65 MB berisi aktivitas historis (token tidak ditemukan bocor di scan) | P3 | scan 0 kecocokan Bearer |

**Tidak ditemukan:** token plaintext di config/log/UI jalur Xellen baru; token sama dipakai lintas layanan berbeda-port (tiap port punya file token sendiri).

---

## 5. BROWSER MCP SECURITY AUDIT

**Dua stack berbeda (FACT):**
1. **browsermcp daemon (9010, v2.5.0)** — HIDUP. Auth: token per-start (43 char) di WS `/client` & `/cdp` + pairing keypair six-digit. Binding **127.0.0.1** (VERIFIED netstat). Multi-klien: Chrome (39504), node 29288 (chain 1), node 20936 (**chain dari Hermes Agent** — temuan baru), daemon 29264.
2. **agent-infra (8089)** — MATI (socket CLOSED, PID 2880 zombie 7.6 MB sejak 11-11 02:20). Saat hidup: **tanpa auth, tanpa validasi Origin, browser instance global tunggal, flag `--no-sandbox --disable-web-security`** (temuan audit lama, tetap berlaku untuk binary 1.2.29).

**Status isolasi sesi browser:**
- browsermcp daemon: **SATU browser instance global bersama** — semua client (Chrome extension, Hermes, uji Xellen) berbagi tab/cookie/login. Isolasi per-project TIDAK ada di stack ini (**VERIFIED dari arsitektur + README**).
- agent-infra (mati): satu browser global per proses (audit lama).

**Risiko:**
| Risiko | Tingkat |
|---|---|
| Browser session bersama lintas konsumen (Hermes + Chrome + Z.ai) — aksi satu client mengubah tab/cookie client lain | **P1 High** |
| Token daemon di query-string URL (mungkin masuk log proxy/browser history server-side) | P2 |
| Token plaintext di daemon.json (0600) — file-level, acceptable untuk single-user tapi perlu scrub dari probe2.json | P3 |
| agent-infra 8089 saat hidup dulu: tanpa auth + `--disable-web-security` | P1 **sekarang mitigated** (layanan mati; jangan dihidupkan ulang tanpa auth wrapper) |
| Remote control browser pengguna: daemon hanya bind 127.0.0.1 + auth token/pairing — remote tidak mungkin tanpa tunnel | VERIFIED (risiko rendah) |

---

## 6. NATIVE MCP TRANSPORT AUDIT

| Kontrol | Status production | Bukti |
|---|---|---|
| Bearer auth | Implementasi ada (AuthStore + middleware); **production OFF** (config enabled=False) | config + audit-evidence |
| Origin validation (403) | Implementasi ada; **BLOCKED di production** (transport off) | T37 |
| Rate limit (429) | Implementasi ada; **BLOCKED di production** | T40 |
| Session lifecycle (Mcp-Session-Id, expiry 404/400) | Implementasi ada; **BLOCKED di production** | T37 |
| JSON + SSE | Implementasi ada; **VERIFIED di test** (SSE parse adapter) | adapter e2e |
| STDIO bridge | Implementasi ada; **VERIFIED** (T37 echo; PoC P4) | T37 |
| Isolasi server/project | Implementasi ada; **VERIFIED** (T39) | T39 |
| Production status | **OPT-IN BELUM DIAKTIFKAN** — bukan production-ready claim | config + 8300 FREE |

---

## 7. PROCESS & NETWORK SECURITY

**Binding address (VERIFIED via netstat + Get-NetTCPConnection):**
| Port | Bind | Owner | Auth |
|---|---|---|---|
| 8023 | 127.0.0.1 | node PID 2020 (supergateway→DC) | Bearer 401 ✓ |
| 8025 | 127.0.0.1 | node PID 10652 (browser chain, start 11-11 02:21) | Bearer ✓ |
| 8024 | 127.0.0.1 (socket FIN_WAIT_1 terakhir; healthz 200) | node 21624 | Bearer ✓ |
| 8089 | 127.0.0.1 socket CLOSED | node 2880 zombie | — (mati) |
| 9010 | 127.0.0.1 | node 29264 (browsermcp daemon) | token+pairing |
| 8300 | tidak ada | — | (opt-in) |
| 8443/10000 | tailscaled (8443 CLOSED; 10000 IPv6 LISTENING sisa) | tailscaled | Funnel config hilang |

**Parent-child & orphan (VERIFIED):**
- 30704 (launcher) → 2020 (supergateway) → DC child (0 aktif saat audit — churn normal).
- Zombie: node 2880 (8089-era, RAM 7.6 MB) — TIDAK dihentikan selama audit; rekomendasi cleanup butuh persetujuan.
- **6 stray publish-p1 GUI instances** (29808, 14148, 34648, 24988, 49964, 8896) — milik sesi implementasi (bukan production); ditutup butuh persetujuan owner; masing-masing menjalankan Core sendiri (idle, mode berdampingan).
- Tidak ada listener 0.0.0.0 pada port aplikasi (semua 127.0.0.1) — **VERIFIED**.

## 8. TAILSCALE & FUNNEL ASSESSMENT

| Item | Status | Bukti |
|---|---|---|
| tailscaled | Running sejak 11-04 08:28 (2 proses) | S1 |
| Tailscale versi | 1.104.4 | S1 |
| Funnel/serve status | **`No serve config`** (funnel + serve status) | S9 |
| ServeConfig di prefs | tidak ada (konsisten) | p0-gapfill |
| Event log Tailscale (3 hari) | kosong | S13 |
| tailscaled file logs (3 hari) | tidak ada | p0-gapfill |
| Funnel vs Serve | Funnel = publik internet via relay; Serve = tailnet-only. Keduanya kini tanpa config | KB + live |

**Analisis hilangnya route:** route Funnel ada 11-11 03:36 (8443→8023, /browser→8025, 10000→8024) dan hilang sebelum 13-11 03:36. tailscaled tidak pernah restart (sejak 11-04). Tidak ada event log, tidak ada file log 3 hari. **Root cause penghapusan: NOT VERIFIED** (kemungkinan: `tailscale serve reset`/`funnel off` dieksekusi manual, atau admin tailnet menghapus policy — butuh audit admin console tailnet yang di luar cakupan mesin ini). **Yang terverifikasi hanya state akhirnya.**

## 9. SUPERGATEWAY ASSESSMENT

- 8023 (PID 2020): command line terverifikasi — `--stdio "node DC --no-onboarding" --outputTransport streamableHttp --port 8023 --host 127.0.0.1 --apiKeyFile mcp-xellen-staging-token.txt --stateful --sessionTimeout ... --healthEndpoint /healthz`. Initialize 200 + no-auth 401 (**VERIFIED live**).
- **Satu-satunya jalur remote utama tetap Supergateway** (native transport production OFF).
- Session handling: stateful per koneksi; child-per-session DC (akar konsumsi RAM historis — sudah dimitigasi di P1 via child policy report-only + dijadwalkan P2 native).
- Child lifecycle: DC child churn normal (0–2 saat audit; 95 saat beban 11 Nov — historical).

## 10. LOCALHOST vs REMOTE CONNECTIVITY

| Tahap | 8023 (DC) | 8025 (browser gateway) | 8024 (C3) | Remote (Funnel ts.net) |
|---|---|---|---|---|
| Service hidup | ✔ 200 | ✔ 200 | ✔ 200 | — |
| Port listen | ✔ | ✔ (healthz+init 200; netstat gap dicatat) | ✔ | Funnel MATI |
| HTTP respons | ✔ | ✔ | ✔ | **UNREACHABLE** (3 endpoint) |
| MCP handshake | ✔ 200+session | ✔ 200+session | ✔ 200+session | **TIDAK DAPAT DIUJI** (Funnel mati) |
| MCP tools/list | ✔ (SSE envelope; parse skrip dibatasi) | ✔ | ✔ | — |
| MCP tools/call | **SKIPPED** (butuh tool aman disetujui owner) | — | — | — |
| Auth | 401 tanpa token ✓ | 401 ✓ | 401 ✓ | — |
| SSE | ✔ (server memilih SSE untuk tools/list) | ✔ | ✔ | — |

**Kesimpulan: localhost SEPENUHNYA berfungsi; internet path PUTUS di Funnel (bukan di MCP).**

## 11. ROOT CAUSE ANALYSIS

| # | Gejala | Bukti aktual | Root cause terbukti | Dugaan (NOT VERIFIED) | Dampak | Risiko |
|---|---|---|---|---|---|---|
| RC-1 | Remote access internet mati | funnel+serve = `No serve config`; 3 endpoint UNREACHABLE; tailscaled hidup sejak 11-04; tidak ada event/file log | **VERIFIED: ServeConfig kosong di tailscaled prefs** | Siapa/apa yang menghapus: `tailscale serve reset`/`funnel off` manual atau perubahan admin tailnet — **NOT VERIFIED** | Z.ai-via-internet putus | **P0** |
| RC-2 | Browser MCP 8089 tidak aktif | socket CLOSED; PID 2880 zombie hidup; agent-infra proses tidak ada; browser-mcp.log terakhir 11-12 | **VERIFIED: proses server 8089 tidak berjalan**; zombie 2880 = sisa | Penyebab mati: crash vs parent exit vs kill — **NOT VERIFIED** (tidak ada log) | Z.ai kehilangan jalur browser agent-infra (browsermcp 9010 masih hidup sebagai jalur alternatif) | **P1** |
| RC-3 | Route `/oc`,`/wcu` hilang | sama dengan RC-1 (ServeConfig kosong menghapus SEMUA route) | **VERIFIED: bagian dari RC-1** | — | endpoint 8801/8802 memang mati; dampak fungsional minim | P3 |
| RC-4 | Z.ai via internet tidak dapat digunakan | = RC-1 | VERIFIED: sama | — | workflow Z.ai remote putus | **P0** |
| RC-5 | Localhost vs remote beda hasil | ladder S4–S8 | **VERIFIED: localhost sehat, remote putus di Funnel** | — | — | — |

## 12. TOKEN ROTATION A/B PLAN (assessment; TANPA eksekusi)

**Token terdampak + konsumen (VERIFIED dari file/proc):**
| Token file | Dipakai oleh | Client |
|---|---|---|
| mcp-xellen-staging-token.txt | Supergateway 8023 (--apiKeyFile) | Z.ai sessions (compat) |
| mcp-browser-staging-token.txt | gateway 8025 (PID 10652 chain) | Z.ai browser flow |
| mcp-construct3-shortshift-token.txt | supergateway 8024 | Construct 3 flow |
| browsermcp daemon.json token (regen per start) | WS /client+/cdp | Chrome extension + Hermes + test chain |
| probe2.json (STALE copy) | tidak valid | — |
| auth.json (hash-only) | native transport (OFF) | belum ada client |

**Fakta pendukung Plan A:** Supergateway `--apiKeyFile` menerima **banyak kunci satu file** (audit-evidence: apiKeyFile di-parse per-baris — verifikasi mechanism saat implementasi). Native AuthStore punya rotate() bawaan.

**Plan A — Zero/Minimal Downtime (untuk token file-based 8023/8024/8025):**
1. Backup file token (salin ke backups\, bukan hapus).
2. Terbitkan token baru; **tambahkan sebagai baris kedua** di file token yang sama (dual-key overlap; gateway tidak perlu restart karena --apiKeyFile dibaca saat start — **VERIFIKASI saat implementasi**: supergateway membaca file sekali di startup → butuh restart gateway setelah file diubah. Karena itu Plan A versi supergateway = dua file + restart yang dikontrol, atau native AuthStore (hot-rotate) setelah transport aktif).
3. Uji token baru: initialize 200 + tools/list via endpoint terkait.
4. Migrasi client bertahap (upload token baru ke Z.ai).
5. Hapus baris token lama dari file → uji token lama = 401 → selesai.
Rollback: kembalikan file backup → restart gateway terkait (downtime detik).

**Plan B — Emergency (bukti kompromi):**
1. Ganti langsung isi file token dengan token baru (old dead immediate).
2. Restart gateway terkait (downtime ±10 detik per gateway).
3. Regenerasi daemon browsermcp token = restart daemon (Chrome extension perlu re-pair).
4. Rotasi SEMUA token lain di secrets\ sebagai mitigasi berlapis.
Risiko: sesi Z.ai aktif terputus; ekstensi perlu re-pair.

**Catatan khusus probe2.json:** token di dalamnya sudah INVALID (bukan bagian rotasi aktif) — cukup scrub di Final Cleanup.

## 13. RISIKO DAN PRIORITAS

| ID | Risiko | Prioritas |
|---|---|---|
| R-1 | Remote access mati (Funnel kosong) — workflow Z.ai internet putus | **P0** |
| R-2 | 6 stray publish-p1 GUI + zombie node 2880 | P1 (housekeeping) |
| R-3 | Browser session bersama lintas konsumen (Hermes+Chrome+Z.ai) di daemon 9010 | P1 |
| R-4 | Token staging file-based + single-tenant (semua sesi Z.ai satu token) | P1 |
| R-5 | agent-infra 8089 tanpa auth bila dihidupkan ulang tanpa wrapper | P1 (jangan hidupkan mentah-mentah) |
| R-6 | probe2.json stale token di repo root | P3 |
| R-7 | Log legacy 65 MB | P3 |
| R-8 | Native transport production OFF (auth modern belum aktif di jalur baru) | P2 |

## 14. REKOMENDASI PEMULIHAN (urutan aman, semua butuh persetujuan)
1. **Pulihkan Funnel** (RC-1): jalankan ulang route yang sama (8443→8023, 10000→8024) via `tailscale funnel --bg ...` — perintah final diserahkan saat persetujuan; verifikasi healthz+handshake.
2. **P0 hygiene**: scrub probe2.json (setelah token browsermcp diperbarui via restart daemon — Plan A/B rotasi) + tutup 6 stray GUI + zombie 2880 dengan persetujuan.
3. **P2**: aktifkan native transport di production (auth modern di jalur baru) — dual-run.
4. **P3**: Browser Manager (isolasi per project) menggantikan browser bersama.

## 15. ROLLBACK PLAN
- Funnel: `tailscale funnel --https=8443 off` (per route) — mengembalikan ke kondisi "No serve config" sekarang.
- Token: file backup token lama di `backups\p0-token-rotation\` (dibuat saat rotasi) → restore + restart gateway terkait.
- Native transport: `enabled=false` → restart aplikasi.
- Stray GUI: tidak perlu rollback (ditutup, bukan production).

## 16. DAFTAR YANG MEMBUTUHKAN PERSETUJUAN PENGGUNA
1. Rotasi token (Plan A atau B) + restart gateway terkait.
2. Aktifkan/pulihkan Funnel (perintah tailscale final).
3. Hapus route basi /oc + /wcu (opsional, saat Funnel dipulihkan).
4. Tutup 6 stray publish-p1 GUI + zombie node 2880.
5. Scrub probe2.json.
6. Aktifkan native transport production (nativeTransport.enabled=true).
7. Final Cleanup & Repository Hygiene.
