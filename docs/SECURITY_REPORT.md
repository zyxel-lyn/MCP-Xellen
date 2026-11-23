# SECURITY REPORT — 13 November 2026

## Threat model singkat
Internet → Tailscale Funnel → endpoint MCP (compat 8023 / native 8300) → server eksekusi (DC). Permukaan kritis: token bocor, blanket process kill, payload sensitif di log, endpoint browser tanpa auth.

## Hasil kontrol keamanan (dengan bukti test)
| Kontrol | Implementasi | Bukti |
|---|---|---|
| Auth per identitas | AuthStore: token crypto-random 64-hex, **hanya hash SHA-256 yang dipersist**, verifikasi konstan | T33 (verify/wrong/revoke/expiry/rotate) |
| Scope per project | Project → client identity → allowedServers; endpoint /prj hanya menerima identity milik project | T39 (cross-token 401, tools tidak bocor) |
| Revoke instan | Revoke → token lama ditolak pada request berikut | T39 (revokedARejected=401) |
| Origin validation | Middleware: Origin ≠ host → 403; non-browser (tanpa Origin) diizinkan | T37 (origin403=403) |
| Rate limit | Fixed-window per identitas, default 120/menit, config-driven | T40 (429 di ke-4) |
| Redaction terpusat | Redaction.Sanitize pada log + activity summaries; registered runtime secrets | T34 + grep activity.jsonl: 0 kecocokan Bearer/token |
| Token tidak di URL/UI/log | Connection package memuat endpoint + PATH file token, bukan nilai | Kode `MainForm.Projects.cs` (CopyConnectionPackage) |
| Rotation eksplisit | AuthStore.Rotate ada TAPI tidak pernah dipanggil otomatis; rotasi credential existing butuh persetujuan owner | Brief §4 dipatuhi; probe2.json token lama TIDAK dirotasi otomatis |
| No blanket kill | Semua kill via TryKillOwned/TryKillRecordedChild (ownership proof); grep source: 0 `taskkill /IM` | audit grep + T8 (0 non-marker kill) |
| Browser flags | Xellen source: **tidak ada** `--disable-web-security`/`--no-sandbox` (flag itu ada di kode agent-infra pihak ketiga — di luar repository) | grep source |
| Health endpoint tanpa auth | /healthz GET → 200; POST → 405 | T37 |

## Temuan secret exposure (dilaporkan, TIDAK dirotasi otomatis)
1. `D:\MCP-Bridge\probe2.json` — token daemon browsermcp generasi lama (43 char) + log tail. **Sudah invalid** (daemon regenerasi token per start — terbukti scan 12 Nov: nilai ≠ token aktif). Pembersihan menunggu persetujuan owner (rencana rotasi final sudah diserahkan, pilihan A/B).
2. Token production Xellen tetap plaintext di `secrets\mcp-xellen-staging-token.txt` (desain existing; tidak diubah tanpa persetujuan).

## Residual risks
1. auth.json masih plaintext JSON (berisi hash) — DPAPI menyusul.
2. Native transport opt-in: selama off, compat 8023 tidak punya rate-limit/origin-control setara (batasan Supergateway existing).
3. Sessions native in-memory — restart host menghapus sesi (by design).
4. `mcp-xellen-app.log` legacy (69 MB) berisi aktivitas historis — tidak disentuh; rotasi hanya berlaku log baru.

# COMPATIBILITY MATRIX

| Client / Transport | Endpoint | Status | Bukti |
|---|---|---|---|
| SelfTest MCP client (JSON-RPC) | native /srv + /prj | **LOLOS** | T37/T39/T40 |
| Supergateway compat (initialize+tools/list) | 8023 | **LOLOS (parity)** | T41 |
| Z.ai Web Agent (Python+Bearer via Funnel) | compat /mcp | **Dipakai production hari ini** (healthz 200; belum diuji ulang end-to-end di sesi ini — butuh sesi Z.ai) | historis + health |
| Z.ai Web Agent → **native** /prj | native | **BELUM diuji** — butuh Funnel route baru yang hanya ditambah dengan persetujuan owner | — |
| Local stdio client (config generator) | stdio | Belum diuji end-to-end | — |
| Browser MCP (extension) | 9010 | Eksternal, tidak berubah | audit 12 Nov |
