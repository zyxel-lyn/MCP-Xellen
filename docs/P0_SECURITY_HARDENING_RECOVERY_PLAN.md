# P0 SECURITY HARDENING & RECOVERY PLAN — MCP Xellen (13 November 2026)

**Status:** Dokumen persiapan (assessment + rencana). **TIDAK ADA implementasi production pada dokumen ini.**
**Bukti mentah:** `logs/p0-assessment.txt` (S1–S14), `logs/p0-gapfill.txt`, `logs/p0-gapfill2.txt`, `logs/p0-10652.txt`, `logs/p0-prep-utf8.txt`, `logs/window-verify2.txt`, `tools/p0-assessment.ps1`, `tools/p0-gapfill.ps1`, `tools/p0-gapfill2.ps1`, `tools/p0-10652.ps1`, `tools/p0-prep-evidence.ps1`.
**Klasifikasi:** VERIFIED / PARTIALLY VERIFIED / NOT VERIFIED / FAILED / BLOCKED. Semua eksekusi terminal background, output ke file log, tanpa tab foreground.

---

## 1. EXECUTIVE SUMMARY

Assessment fresh (13 Nov 04:43–04:57) mengonfirmasi: **localhost MCP sehat** (8023/8024/8025 — initialize 200+session, no-auth 401), **remote access MATI** (Funnel/Serve = `No serve config`, debug prefs kosong, 3 endpoint publik UNREACHABLE), **8089 agent-infra MATI** (socket CLOSED, zombie node 2880), **browsermcp daemon 9010 HIDUP** dan kini dipakai bersama Chrome + **Hermes Agent (baru — config.yaml terverifikasi)** + chain uji Xellen, **6 stray GUI publish-p1** hidup, dan **dual-key rotation TERVERIFIKASI dari source SuperGateway** (`apiKey.js`: file token di-split per baris → satu file = banyak kunci; `--apiKeyFile` repeatable; file dibaca saat startup).

Rencana pemulihan berurutan: **(1) rotasi token dual-key 8023 → (2) isolasi browser daemon bersama → (3) regresi auth lokal → (4) pulihkan Funnel** — masing-masing dengan rollback dan persetujuan eksplisit.

## 2. KONDISI AKTUAL (VERIFIED, 13 Nov 04:43–04:57)

| Item | Nilai | Bukti |
|---|---|---|
| Last boot | 11-04 08:27 (tidak ada reboot 13 Nov) | p0-assessment S1 |
| Tailscale | v1.104.4, service Running, tailscaled sejak 11-04 08:28 (2 PID) | S1 |
| Launcher production | PID 30704 `publish-direct\MCP-Xellen.exe` hidup | S2/audit-evidence |
| Gateway 8023 | PID 2020 (start 13-11 02:15); healthz 200; initialize 200+session; tools/list → SSE; no-auth 401 | S5 |
| 8025 browser gateway | healthz 200; initialize 200+session; listener = **node PID 10652, start 11-11 02:21** (cmdline tidak terbaca dari konteks audit) | S4/S6 + p0-10652 |
| 8024 construct3 | healthz 200; initialize 200+session; tools/list → SSE | S7 |
| 8089 agent-infra | socket CLOSED (bukan LISTENING); zombie node **PID 2880** (start 11-11 02:20, RAM 7.6 MB, parent cmd.exe 16788 HIDUP, 1 child 47420) | S3 + p0-gapfill [I] |
| 9010 browsermcp daemon v2.5.0 | node PID 29264; 5 koneksi ESTABLISHED (Chrome 39504, node 29288 chain-1, node 20936 **chain Hermes**, daemon sendiri) | S3/S12 + p0-prep [B] |
| Funnel/Serve | `No serve config` (funnel + serve + debug prefs) | S9 + p0-gapfill |
| Native transport | config `enabled=False`; 8300 FREE; auth.json tidak ada | S3/S11 + prep [K] |
| Stray GUI publish-p1 | **6 proses** (29808, 14148, 34648, 24988, 49964, 8896; start 04:03–04:13; parent chain ZCode→bash; RAM 0–0.3 MB) | prep [H] |
| Token browsermcp lama di probe2.json | masih ada (43 char) — **STALE/invalid** (daemon regen per start, ver 2.5.0) | S10 |

**Temuan baru:** (1) **Hermes Agent desktop berjalan penuh** — 12 proses Hermes.exe + gateway CLI Python + node `C:/Users/user/construct3-mcp/dist/index.js` (dari HOME user, bukan repo) + **chain browsermcp kedua via `hermes\tools\npm`** (start 02:45). (2) **Hermes `config.yaml` terverifikasi merujuk browsermcp/9010** (7 REF di 400 file; termasuk `spawn-ledger.json`). (3) **daemon.log: session churn tinggi** — 6+ thin server per sesi dibuka/ditutup cepat 11-12 21:59–22:03. (4) **Hermes adalah konsumen browser bersama yang baru terkonfirmasi.**

## 3. BROWSER MCP ISOLATION DESIGN (Tahap A)

### 3.1 Kondisi aktual (VERIFIED)
- **SATU browser instance global bersama**: daemon 9010 (browsermcp 2.5.0) menjembatani Chrome extension (satu browser) ke SEMUA client.
- Konsumen terverifikasi: Chrome extension (PID 39504, WS ESTABLISHED), **Hermes Agent** (config.yaml REF + chain proses 10952/38260/20936), chain uji Xellen (29288).
- Session churn: daemon.log menunjukkan 6+ sesi dibuka/ditutup cepat — thin server per sesi (`thin 2.5.0, pid …`).
- Konfigurasi koneksi: **Hermes** → `hermes\config.yaml` (+ spawn-ledger.json); **Chrome** → extension (profile tidak dibaca — privasi); **Xellen** → tidak ada konsumen production (hanya `ProtectedProcessPatterns` defensif di XellenConfig.cs + SelfTest).

### 3.2 Risiko terverifikasi
| Risiko | Tingkat | Bukti |
|---|---|---|
| Cookie/session/tab lintas client (Hermes + Chrome + Z.ai berbagi SATU browser) | P1 High | arsitektur single-browser (README 2.5.0) + multi-klien 9010 |
| Aksi satu client menimpa tab/sesi client lain | P1 High | desain single-browser |
| Token daemon di query-string URL | P2 | README `/client?token=` |
| Daemon mati = semua client putus (single point) | P2 | arsitektur |

### 3.3 Desain isolasi (target; implementasi = tahap berikut dengan persetujuan)
**Opsi 1 (RECOMMENDED):** satu proses browser per client memakai **agent-infra 1.2.29** (sudah ada) dengan `--user-data-dir` per client + wrapper auth per client (compat/native). Setiap client = browser instance + profil + token sendiri. Lifecycle via ProcessHost (lazy start, idle stop, health, orphan protection — komponen teruji T1–T16).
**Opsi 2 (perlu verifikasi kapabilitas):** multi-slot browsermcp (`/cdp?browser=<slot>` ada di README 2.4.0) — verifikasi dulu apakah slot = profil/cookie terpisah; jika hanya tab, TIDAK memenuhi isolasi.
**Autentikasi per koneksi:** semua client melewati wrapper Bearer (compat atau native) — dilarang ekspos port browser langsung.
**Isolasi kegagalan:** child per client (ProcessHost ownership) → crash satu tidak menjatuhkan lain (pola T1–T16).
**Hermes:** browsermcp chain Hermes (10952/38260/20936) diarahkan ke browser instance Hermes sendiri; Chrome extension tetap di daemon bersama ATAU mendapat instance sendiri — keputusan owner.

### 3.4 Yang TIDAK dilakukan tahap ini
Tidak menghentikan daemon 9010, tidak memutus Hermes/Chrome, tidak mengubah konfigurasi Hermes/Chrome/browser, tidak membaca cookie/data login.

## 4. TOKEN ROTATION PLAN A (dual-key — TANPA eksekusi sekarang)

### 4.1 Mekanisme (VERIFIED dari source `supergateway/dist/lib/apiKey.js`)
- `--apiKeyFile` **repeatable** (array, apiKey.js:24–26) → bisa dua file (lama + baru).
- **Isi satu file di-split per baris** (apiKey.js:58–66) → satu file = banyak kunci.
- Union semua sumber; file kosong = error startup (bukan "tanpa auth"); verifikasi timing-safe (digest + timingSafeEqual).
- **File dibaca saat STARTUP** — mengubah isi file = butuh restart gateway (downtime detik). Tidak ada hot-reload.

### 4.2 Inventaris consumer + dampak (VERIFIED)
| Token file | Dipakai | Client | Dampak dicabut |
|---|---|---|---|
| mcp-xellen-staging-token.txt (64B) | SG 8023 → DC | Semua sesi Z.ai compat | Semua sesi Z.ai aktif 401 |
| mcp-browser-staging-token.txt (64B) | gateway 8025 → browser stack | Z.ai browser flow | browser flow 401 |
| mcp-construct3-shortshift-token.txt (64B) | SG 8024 | Construct 3 flow | C3 flow 401 |
| mcp-api-key(-staging).txt, mcp-approval-ui-token.txt | guard/approval stack | sesuai pemakaian | — |
| browsermcp daemon token (43 char, regen per start) | WS /client+/cdp | Chrome ext + Hermes + chain uji | re-handshake otomatis |
| probe2.json (STALE copy, 43 char) | tidak valid | — | tidak berdampak |
| config\auth.json (hash-only) | native transport (OFF) | belum ada client | — |

### 4.3 Prosedur dual-key (per token; contoh 8023)
1. **Backup** file token → `backups\p0-token-rotation\<stamp>\` (salin, bukan hapus).
2. Terbitkan token baru (crypto random 64-hex — pola `install.ps1`/AuthStore; **nilai tidak dicetak**).
3. Ubah file token menjadi **dua baris** (lama + baru).
4. **Restart gateway 8023** (downtime detik; sesi Z.ai aktif terputus sesaat) — dilakukan saat tidak ada sesi aktif.
5. **Uji**: initialize Bearer baru → 200; initialize Bearer lama → 200; no-auth → 401 (bukti ke log).
6. Migrasi client satu per satu (upload token baru ke Z.ai), verifikasi tiap client.
7. Setelah semua client terverifikasi: hapus baris lama (file satu baris) → restart → verifikasi token lama 401.
**Rollback:** restore file backup → restart → kunci lama aktif lagi.

### 4.4 Gap implementasi (jujur)
- Dual-key **didukung source** (VERIFIED — apiKey.js), tetapi **prosedur restart-per-gateway belum diotomasi**. Saat implementasi dibuat `tools/rotate-token.ps1` (edit file token + restart gateway target + verifikasi ladder + rollback flag). Perubahan yang diperlukan: **hanya script tools baru** (tidak menyentuh SuperGateway/production) — risiko rendah, rollback = hapus script.
- Untuk native transport (setelah aktif): `AuthStore.Rotate()` = **zero-downtime hot-rotate** (VERIFIED T33) — motivasi tambahan P2.

## 5. AUTHENTICATION REGRESSION PLAN (lokal, endpoint test)

| # | Skenario | Hasil wajib | Status |
|---|---|---|---|
| C-1 | Token valid → initialize | 200 + session | **VERIFIED** (T37) |
| C-2 | Token salah | 401 | **VERIFIED** (T37) |
| C-3 | Tanpa auth | 401 | **VERIFIED** (T37) |
| C-4 | Token expired (TTL) | 401 | **VERIFIED** (T33) |
| C-5 | Token revoked | 401 | **VERIFIED** (T33/T39) |
| C-6 | Scope membatasi tools | tersaring per allowedServers | **VERIFIED** (T39) |
| C-7 | Session lintas project | ditolak | **VERIFIED** (T39) |
| C-8 | SSE + JSON | keduanya | **VERIFIED** (T37 + adapter e2e) |
| C-9 | Origin invalid → 403 | **VERIFIED** (T37) |
| C-10 | Rate limit → 429 | **VERIFIED** (T40) |
| C-11 | Token dirotasi → lama mati, baru hidup | **VERIFIED** (T33) |

Seluruh matrix **sudah dijalankan dan lulus** pada native transport port test (SelfTest T33–T42) — regresi ulang cukup sekali setelah perubahan apa pun.

## 6. FUNNEL RECOVERY PROCEDURE (rancangan; TANPA eksekusi)

### 6.1 Route lama vs konfigurasi aktual
Route terakhir terverifikasi (11-11 03:36): `:8443 → 8023`, `:8443/browser → 8025`, `:10000 → 8024`; basi: `/oc→8801`, `/wcu→8802`. Kondisi aktual: **ServeConfig kosong** (semua route hilang). Route basi `/oc`,`/wcu` **TIDAK dipulihkan** (layanan 8801/8802 tidak ada).

### 6.2 Prosedur pemulihan (urutan; butuh persetujuan + dieksekusi manual oleh owner)
1. **Pra-syarat keamanan:** selesaikan Tahap B (rotasi token 8023) — **jangan buka Funnel dengan token yang terekspos sejarahnya tanpa rotasi**.
2. Pastikan 8023 sehat (healthz 200 — VERIFIED live).
3. Pulihkan route utama: `tailscale funnel --bg --https=8443 http://127.0.0.1:8023`.
4. (Opsional) route browser: `tailscale funnel --bg --https=8443 http://127.0.0.1:8025` — hanya setelah browser flow diamankan.
5. (Opsional) route construct3: `tailscale funnel --bg --https=10000 http://127.0.0.1:8024`.
6. Verifikasi ladder: healthz 8023 → initialize HTTPS remote + Bearer → 200+session → tools/list → (opsional) tools/call non-mutatif yang disetujui owner.
7. Verifikasi Funnel status menampilkan route + HTTPS sertifikat otomatis (Tailscale/Let's Encrypt).
8. Catat timestamp + bukti ke log.
**Validasi non-mutatif:** tools/call hanya pada tool disetujui owner eksplisit; default = initialize+tools/list cukup untuk membuktikan jalur.

### 6.3 Validasi route lama vs aktual
`tailscale serve status` harus menunjukkan route persis §6.2 — **route `/oc` dan `/wcu` TIDAK dipulihkan** (basi).

## 7. STRAY PROCESS ASSESSMENT (VERIFIED; tanpa menghentikan apa pun)

**Enam GUI publish-p1 (semua FACT):**
| PID | Start | Parent | RAM | Catatan |
|---|---|---|---|---|
| 29808 | 04:03 | 34564 (ZCode.exe) | 0 MB | attempt e2e gagal (binary lama tanpa --transport-demo membuka GUI) |
| 14148 | 04:03 | 29808 (bash.exe) | 0.3 MB | child bash dari 29808 |
| 34648 | 04:12 | 34564 (ZCode.exe) | 0 MB | attempt kedua |
| 24988 | 04:12 | 34648 (bash.exe) | 0.3 MB | idem |
| 49964 | 04:13 | 24988 (bash.exe) | 0.3 MB | idem |
| 8896 | 04:13 | 49964 (bash.exe) | 0 MB | idem |
- **Masih digunakan?** TIDAK — semua idle (RAM ~0; 8023 dipegang production 30704 → mode berdampingan idle; masing-masing menjalankan Core sendiri tanpa identity native).
- **Risiko jika dihentikan:** nihil terhadap production (tidak memegang port/token server); hanya menghilangkan 6 window + 6 Core idle. **Persetujuan owner tetap diminta.**

**PID 2880 (zombie 8089):** node, start **11-11 02:20**, parent cmd.exe 16788 (HIDUP), 1 child (47420), cmdline tidak terbaca dari konteks audit. Socket 8089 CLOSED → tidak melayani. Risiko jika dihentikan: rendah; kemungkinan besar sisa chain agent-infra 11-11 (dipakai saat pengujian browser-mcp 11-11 02:20–02:21 — konsisten dengan mtime token browser). **Persetujuan owner diminta untuk cleanup.**

## 8. RISIKO DAN MITIGASI (keseluruhan rencana)
| Risiko | Mitigasi |
|---|---|
| Restart gateway saat rotasi memutus sesi aktif | Jadwalkan saat tidak ada sesi Z.ai; dual-key mengurangi kebutuhan rotasi mendadak |
| Rotasi salah → semua client 401 | Backup file token + rollback restore (§ Rollback di bawah) |
| Funnel dibuka sebelum token dirotasi | Urutan wajib: rotasi dulu, Funnel kemudian |
| Daemon 9010 dipindah tanpa re-pair | Dokumentasikan prosedur re-pair (extension Allow + six-digit) sebelum migrasi |
| Stray/zombie dihentikan tanpa sengaja | Cleanup hanya dengan daftar PID eksplisit + persetujuan |

## 9. ROLLBACK PROCEDURE
1. **Token:** restore file backup → restart gateway terkait → verifikasi token lama 200 / token baru 401 (bila rotasi dibatalkan).
2. **Funnel:** `tailscale funnel --https=8443 off` (+ `--https=10000 off`) → kembali ke `No serve config` (kondisi saat audit).
3. **Browser isolation:** kembalikan konsumen ke daemon 9010 bersama (config Hermes/extension tidak berubah) → verifikasi koneksi.
4. **Native transport:** `enabled=false` → restart aplikasi → 8300 hilang, compat tetap.
Semua langkah tidak menyentuh data user/browser.

## 10. DEPENDENCY DAN URUTAN IMPLEMENTASI
```
P0-A (browser isolation design sign-off)
  → P0-B (token rotasi Plan A: 8023 → 8025 → 8024, satu per satu)
    → P0-C (regresi auth setiap perubahan)
      → P0-D (Funnel recovery + verifikasi remote ladder)
        → Final Cleanup & Repository Hygiene (tertunda, agenda owner)
```
Paralel aman: Tahap E (stray cleanup) kapan pun dengan persetujuan; tidak bergantung A–D.

## 11. DAFTAR PERSETUHUAN PENGGUNA
1. Persetujuan desain isolasi browser (Opsi 1 vs Opsi 2) sebelum implementasi P3.
2. Persetujuan rotasi token per token (Plan A urutan 8023→8025→8024).
3. Persetujuan pulihkan Funnel + perintah tailscale final.
4. Persetujuan menutup 6 stray GUI publish-p1 + zombie node 2880.
5. Persetujuan scrub probe2.json (Final Cleanup).
6. Persetujuan mengaktifkan native transport production (enabled=true).
7. Keputusan Hermes: apakah browsermcp Hermes dijadwalkan ke isolasi per-client juga (konsumen baru yang terverifikasi).

## 12. REKOMENDASI TAHAP IMPLEMENTASI
**Satu tahap paling masuk akal berdasarkan bukti: tahap A+B gabungan ("Secure the Browser & Rotate")** — karena Funnel baru dibuka setelah token 8023 dirotasi, dan browser bersama adalah risiko P1 yang aktif. Urutan: (1) rotasi token 8023 Plan A → (2) isolasi browser per client (Opsi 1) dengan auth wrapper → (3) pulihkan Funnel → (4) regresi auth + verifikasi remote ladder. Stray/zombie cleanup menyertai sebagai housekeeping berpersetujuan.

---
**Referensi bukti:** `logs/p0-assessment.txt` (S1–S14), `logs/p0-gapfill.txt`, `logs/p0-gapfill2.txt`, `logs/p0-10652.txt`, `logs/p0-prep-utf8.txt`, `logs/window-verify2.txt`, `logs/audit-evidence.txt`. Semua timestamp 13 November 2026.
