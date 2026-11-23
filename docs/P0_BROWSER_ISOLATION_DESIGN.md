# P0 BROWSER ISOLATION DESIGN — MCP Xellen (13 November 2026)

**Status:** Desain + validasi bukti. **TIDAK ADA implementasi/perubahan pada dokumen ini.** Semua eksekusi background, output ke log.
**Bukti mentah:** `logs/p0-browser-evidence.txt`, `logs/p0-slot-evidence.txt`, `logs/window-verify2.txt`, `logs/p0-assessment.txt` (S3/S12), `logs/p0-prep-utf8.txt` ([D]/[F]).

---

## 1. EXECUTIVE SUMMARY

Browser automation saat ini dijalankan oleh **dua stack terpisah** yang keduanya tidak memenuhi isolasi per-client:

1. **browsermcp daemon (9010, v2.5.0)** — HIDUP, diakses Chrome extension + Hermes Agent + chain uji. **SATU browser global bersama** (browser Chrome extension pengguna). **TIDAK memiliki slot/profile/user-data-dir** — diverifikasi grep source (`index.js`/`daemon.js`/`chunk`: 0 match untuk browserId/browser=/slot/slots/userDataDir/profileDir/createBrowser/connectBrowser). Multi-slot native = **TIDAK DIDUKUNG**.
2. **agent-infra 1.2.29 (8089)** — MATI (socket CLOSED, zombie node 2880). Saat hidup: **tanpa auth, satu browser global, flag `--no-sandbox --disable-web-security`**. NAMUN agent-infra **mendukung `--user-data-dir` + `--profile` + `BrowserFinder` + `channel`** (terverifikasi di dist) → mampu isolated-browser-per-process.

**Rekomendasi utama:** **Opsi 3 Hybrid — Browser Manager di Xellen Core yang mengelola agent-infra terisolasi per client** (proses + user-data-dir + token sendiri per client, lazy start, idle stop, auth wrapper). Alasan lengkap di §17. Syarat: implementasi = tahap berikut dengan persetujuan; daemon 9010 dan Hermes TIDAK disentuh selama transisi.

## 2. CURRENT ARCHITECTURE (VERIFIED, fresh 13 Nov)

```
Chrome extension (browser pengguna, PID 39504)
   ⇅ WS 127.0.0.1:9010 /client?token=… & /cdp?token=… (5 koneksi ESTABLISHED)
browsermcp daemon v2.5.0 (node PID 29264, start 11-13 02:04)
   ⇅ spawn thin MCP server per sesi (thin 2.5.0, churn 6+ sesi/menit saat aktif)
Konsumen thin-server: Chrome ext, chain Hermes (10952→38260→20936, start 02:45 via hermes\tools\npm),
                      chain uji Xellen (29288, start 02:04)
```
- daemon.json: port 9010, pid, version 2.5.0, **token 43 char (regen per daemon start; terakhir 12-11 02:14)**; identity.json keypair persisten (sejak 3-11).
- **agent-infra 8089: MATI** — socket CLOSED, zombie node 2880 (7.6 MB) masih hidup.

**Bukti kunci (fresh 13 Nov 05:45):**
- `Get-NetTCPConnection -LocalPort 9010 -State Listen` = kosong pada poll tsb (socket state transient), namun 5 koneksi ESTABLISHED tetap terlihat dan proses daemon 29264 hidup → daemon aktif.
- Grep source browsermcp (index.js 12.9K + daemon.js 196K + chunk 11.6K): **0 match** untuk slot/profile/user-data-dir/browserId → isolasi per-client TIDAK didukung native browsermcp.
- Hermes `config.yaml`: hanya dokumentasi generik MCP (tidak ada entri browsermcp aktif) — koneksi Hermes→9010 berasal dari chain proses Hermes (VERIFIED proses), bukan config.yaml.

## 3. CURRENT BROWSER CONSUMERS (VERIFIED)

| Konsumen | Bukti | Browser yang dipakai |
|---|---|---|
| Chrome extension (Browser MCP) | 5 WS ESTABLISHED ke 9010 dari chrome.exe 39504 + daemon.log "thin" sessions | **Browser pribadi pengguna** (via extension) |
| Hermes Agent | chain 10952→38260→20936 (hermes\tools\npm, start 13-11 02:45); config.yaml REF generic | **Browser yang sama** (via daemon) |
| Chain uji Xellen | node 29288 (start 02:04, cmd 8872→29092) | **Browser yang sama** |
| MCP Xellen production | TIDAK ADA konsumen 9010 (hanya pattern defensif di source) | — |

## 4. SECURITY PROBLEM ANALYSIS
| Masalah | Bukti | Dampak |
|---|---|---|
| Satu browser global = cookie/session/login bersama semua konsumen | arsitektur extension + multi-klien 9010 | Z.ai/Hermes/uji menimpa sesi satu sama lain; data lintas konsumen |
| Tab & navigasi tercampur | single active browser | aksi satu AI menimpa pekerjaan AI lain |
| Tanpa auth per client (token daemon BERSAMA + di query-string) | README `/client?token=`; token regen per start tapi dipakai semua | satu token bocor = kontrol penuh browser pengguna |
| Token daemon pernah bocor di probe2.json (kini stale) | scan 12-11 | bukti pola kebocoran |
| agent-infra 8089 (mati): tanpa auth + `--disable-web-security --no-sandbox` | audit lama + source 1.2.29 | bila hidup: remote control tanpa auth |
| Zombie node 2880 + 10 stray GUI publish-p1 | live census | housekeeping |
| Funnel mati → risiko rendah saat ini | `No serve config` | positif sementara |

## 5. THREAT MODEL
| Threat | Vektor | Mitigasi (desain baru) |
|---|---|---|
| Unauthorized browser access | port browser tanpa auth | Bearer per client di wrapper; bind 127.0.0.1 |
| Session hijacking | token shared/query-string | token per client, tidak di URL, regenerasi |
| Cross-client data leakage | cookie/session bersama | user-data-dir per client |
| Shared cookie leakage | profil browser sama | profil terpisah + tidak pernah memakai profil pribadi user |
| Process ownership confusion | orphan browser | ProcessHost ownership + orphan sweep (teruji T1–T16) |
| Malicious MCP client | tool call berbahaya | scope per client + approval untuk tool sensitif |
| Resource exhaustion | N browser hidup | lazy start + idle stop + cap per client + resource monitor |

## 6. ARCHITECTURE COMPARISON (dengan bukti source)

| Kriteria | Opsi 1: Isolated browser per client (agent-infra) | Opsi 2: Multi-slot browsermcp daemon | Opsi 3: Hybrid (Browser Manager + pool agent-infra) |
|---|---|---|---|
| Dukungan native | **VERIFIED**: `--user-data-dir` ×1, `--profile` ×1, `BrowserFinder` ×2, `channel` ×3 di dist 1.2.29 | **FAILED**: grep 3 file dist = 0 match slot/profile/user-data-dir | sama dengan Opsi 1 + pooling di Browser Manager |
| Session/cookie isolation | ✔ per proses+profil | ✗ tidak ada | ✔ |
| Auth per client | wrapper per client (compat/native) | ✗ satu token daemon shared | ✔ per client |
| Crash isolation | ✔ per proses | ✗ daemon shared | ✔ |
| RAM saat aktif | 1 Chromium per client (ESTIMATE 200–400 MB, belum diukur live) | 1 Chromium shared | sesuai jumlah client aktif + idle-stop |
| RAM saat idle | 0 (idle stop) — mekanisme ProcessHost teruji | selalu ada | 0 (idle stop) |
| Startup latency | Chromium start ±1–3 s (ESTIMATE) | 0 (sudah hidup) | sama Opsi 1 |
| Hermes compatibility | ✔ (chain Hermes tak disentuh; daemon 9010 tetap hidup) | ✔ (status quo) | ✔ |
| Chrome pribadi | ✔ tidak disentuh (BrowserFinder menemukannya tapi user-data-dir terpisah) | ✗ extension memakai browser user | ✔ tidak disentuh |
| Kemudahan pengembangan | Sedang (Browser Manager baru) | Rendah—karena tidak didukung native | Sedang |
| Skalabilitas client | Terbatas RAM (cap per client) | Bagus tapi TIDAK aman | Baik (pool + cap) |

**Catatan jujur Opsi 2:** parameter `/cdp?browser=<slot id>` ada di README, tapi grep source dist menunjukkan tidak ada implementasi slot/profile yang ditemukan — kemampuan slot belum terbukti mengisolasi cookie/session. Opsi 2 hanya dipertimbangkan bila bukti source berlawanan muncul.

## 7. RECOMMENDED ARCHITECTURE — Opsi 3 Hybrid (Browser Manager + isolated agent-infra per client)

```
AI Client (Z.ai/Hermes/lain) → MCP Xellen Gateway (auth per client) → Browser Manager (Xellen Core)
  Browser Manager: Registry slot per client {clientId, user-data-dir, port, token, state}
    → spawn agent-infra per client: --port <p> --user-data-dir profiles\<clientId> --headless(optional)
    → lazy start; idle stop; health; orphan ownership via ProcessHost
  Hermes & Chrome: TETAP di daemon 9010 (tidak disentuh sampai owner menyetujui migrasi)
```
Alasan (6 dimensi): keamanan (profil+token per client; tidak ada browser user yang disentuh), RAM (idle stop; hanya client aktif yang hidup), stabilitas (crash isolation per proses; teruji polanya), pengembangan (ProcessHost/AuthStore/Registry/EventBus sudah ada dan teruji), kompatibilitas (Hermes/Chrome/compat/native tidak berubah), skalabilitas (Registry + cap).

## 8. SESSION & PROFILE ISOLATION
- Profil: `D:\MCP-Bridge\browser-profiles\<clientId>\` — dibuat saat client pertama; TIDAK pernah dihapus kecuali Final Cleanup berpersetujuan.
- Session: `session_id` per client di Registry; token per client; tidak ada session bersama.
- Pemisahan ketat: satu proses agent-infra tidak pernah menerima koneksi client lain (routing per clientId di gateway).

## 9. AUTHENTICATION & AUTHORIZATION
- Setiap client: identity di AuthStore (hash-only) dengan scope `browser:<clientId>`; Bearer wajib di endpoint browser.
- Revoke/rotate/expiry = mekanisme AuthStore yang sudah teruji (T33).
- browsermcp daemon lama (9010) tetap memakai token daemon-nya sampai migrasi — tidak diubah.

## 10. HERMES COMPATIBILITY
- Hermes saat ini terhubung ke daemon 9010 (browser bersama) — **tetap berjalan tanpa gangguan**; tidak dipindahkan tanpa persetujuan.
- Saat Hermes dipindah (opsional): buat slot browser Hermes sendiri + arahkan config Hermes ke endpoint baru — uji paralel sebelum memutus jalur lama.

## 11. CHROME COMPATIBILITY
- Chrome pribadi (157.0.4341.46): **tidak disentuh** — browser baru memakai user-data-dir terpisah; BrowserFinder boleh menemukan executable Chrome yang sama (executable shared ≠ session shared).
- Extension Browser MCP di Chrome user: tetap berfungsi via daemon 9010 sampai migrasi disetujui.

## 12. RAM & PERFORMANCE ANALYSIS
- **TERUKUR (FACT):** RAM Xellen Core 61–68 MB; synthetic child stack 38–59 MB; daemon 9010 tidak diukur terpisah di sesi ini.
- **ESTIMATE (bukan fakta, ditandai):** Chromium headless idle ≈ 150–250 MB; dengan 2–3 tab aktif ≈ 250–400 MB; per client tambahan ≈ +50–100 MB.
- **Skenario (ESTIMATE, ditandai):** 2 client aktif paralel ≈ 500–800 MB tambahan; idle stop → 0. Cap maksimum client aktif default 3 (config), lazy start default on.
- Mitigasi RAM: idle timeout (default 15 menit, config), lazy start, satu browser hanya saat client aktif.

## 13. IMPLEMENTATION ROADMAP (10 tahap; TANPA implementasi sekarang)
| # | Tahap | Target/file | Deps | Acceptance | Test | Risiko | Rollback |
|---|---|---|---|---|---|---|---|
| 1 | Browser Manager Core | `Xellen/Browser/BrowserManager.cs` | ProcessHost, Registry | spawn/stop/health teruji | unit + headless | spawn window (CreateNoWindow) | hapus modul |
| 2 | Client Registry slot | Registry v3 + config | AuthStore | slot per client persist | unit | schema migration | restore config backup |
| 3 | Isolated profiles | `browser-profiles\<clientId>` | Browser Manager | profil terpisah persist | integration | disk growth | hapus slot (persetujuan) |
| 4 | Auth & authz | AuthStore scope `browser:<id>` | AuthStore | 401/403 matrix | T39-style | token leak → redaction | disable endpoint |
| 5 | Lifecycle manager | lazy/idle/cap | ProcessHost | idle stop terbukti | T1-style | idle salah kill → whitelist | disable idle |
| 6 | Resource monitoring | RAM per browser di dashboard | MetricsService | angka terukur | snapshot | — | — |
| 7 | Session cleanup | cleanup hanya slot milik client | ProcessHost | tidak menyentuh milik lain | T14-style | — | — |
| 8 | Hermes compatibility | Hermes tetap 9010; opsi slot terpisah | owner approval | Hermes tidak terganggu | manual | — | — |
| 9 | Chrome compatibility | profil pribadi tidak disentuh | — | verifikasi Chrome user tetap utuh | manual | — | — |
| 10 | Integration test | 2 client paralel end-to-end | semua | tab/cookie/login/stop independen | T39-style extended | — | — |

## 14. ACCEPTANCE CRITERIA
1. Dua client paralel: cookie/login/tab tidak tercampur (bukti screenshot/state).
2. Token lintas project ditolak 401/403.
3. Stop satu client tidak mematikan lain (bukti PID).
4. Idle stop terbukti; lazy start terbukti.
5. Tidak ada window console terlihat (Check A/B — control positif wajib).
6. Chrome pribadi & Hermes tidak berubah (hash profil/user data tidak berubah).
7. Registry/auth.json hash-only; tidak ada plaintext.
8. SelfTest regresi 40/0/1 tetap lulus.

## 15. TEST PLAN
1. Unit: Browser Manager lifecycle, Registry slot, AuthStore scope.
2. Integration: 2 client paralel via native transport (synthetic browser server untuk CI; browser nyata untuk acceptance).
3. Security: 401/403/429/scope/redaction.
4. Empirical window check (Check A/B dengan control positif) tiap run test.
5. Manual (owner): visual, DPI, multi-monitor.

## 16. ROLLBACK STRATEGY
- Fitur browser baru = modul additive + config opt-in; rollback = `enabled=false` + hapus modul dari registry → daemon 9010 lama tetap jalur Hermes/Chrome.
- Profil browser per client: folder terpisah; rollback = arahkan kembali/tutup tanpa menghapus (hapus = Final Cleanup berpersetujuan).

## 17. DECISIONS REQUIRING USER APPROVAL
1. Pilih arsitektur: Opsi 3 hybrid (RECOMMENDED) atau lainnya.
2. Migrasi Hermes ke browser terisolasi (atau tetap di daemon 9010).
3. Aktivasi native transport production (prasyarat auth modern di jalur browser baru).
4. Idle timeout default (usulan 15 menit) & cap client aktif (usulan 3).
5. Headless default (usulan: headless on; headed untuk debugging).
6. Final Cleanup & Repository Hygiene (artefak test, probe2.json, stray, zombie) — ditunda sampai disetujui.

---

## KEPUTUSAN AKHIR (satu rekomendasi)

**Opsi 3 Hybrid — Browser Manager di Xellen Core yang mengelola agent-infra terisolasi per client.**

Alasan berbasis bukti:
- **Keamanan:** browsermcp daemon terbukti TIDAK punya slot/profile (grep source 0 match) dan memakai token bersama di URL — tidak bisa diamankan tanpa redesign upstream; agent-infra terverifikasi mendukung `--user-data-dir`/`--profile` → isolasi nyata.
- **RAM:** hanya client aktif yang hidup (lazy + idle stop via ProcessHost teruji), bukan N browser permanen.
- **Stabilitas:** crash isolation per proses (pola teruji T1–T16); kegagalan Hermes tidak menyentuh Z.ai.
- **Pengembangan:** Core/AuthStore/Registry/ProcessHost sudah ada dan teruji — Browser Manager tinggal modul baru, bukan rewrite.
- **Kompatibilitas:** Hermes & Chrome tidak disentuh; compat 8023 tetap; native = jalur baru.
- **Skalabilitas:** Registry slot + cap + pool memberi jalur tumbuh tanpa mengorbankan isolasi.

Jika bukti berubah (mis. browsermcp merilis dukungan profil/slot multi-browser terverifikasi), keputusan ini diubah kembali — untuk sekarang, bukti mendukung Opsi 3.

**Berhenti di sini. Menunggu persetujuan pengguna sebelum implementasi.**
