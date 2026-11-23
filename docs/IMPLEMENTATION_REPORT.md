# MCP Xellen — IMPLEMENTATION REPORT (Master Brief, 13 November 2026)

> Bukti utama: build Release 0 error/0 warning; SelfTest fresh `tests/p1/results.json` = **40 pass / 0 fail / 1 skipped** (T42 UI smoke — headless), exit code 0, 13 Nov 2026 ~03:27 WIB. Production endpoint 8023/8024/8025 tidak disentuh dan tetap 200.

## 1. Yang benar-benar selesai (bukan scaffold)

### A. Core baru (semua teruji lewat SelfTest)
| Komponen | File | Bukti test |
|---|---|---|
| RedactionService terpusat | `Xellen/Core/Redaction.cs` | T34: bearer/token-json/api-key/password/cookie/sk-/PEM masked + truncation |
| ActivityEvent model (PRD §9) | `Xellen/Core/ActivityEvent.cs` | dipakai pipeline nyata (transport tool call) |
| EventBus push tanpa blocking | `Xellen/Core/EventBus.cs` | T35: overflow terhitung jujur (droppedNewest=1, delivered=3) |
| ActivityStore ring+JSONL | `Xellen/Core/ActivityStore.cs` | rotasi 5 MB×2; dipakai transport |
| AuthStore (crypto random, hash-only, scope, expiry, revoke, rotate eksplisit) | `Xellen/Core/AuthStore.cs` | T33: verify/revoke/expiry/rotate |
| Registry v2 (projects + migrasi idempotent v1→v2 + backup) | `Xellen/Core/Registry.cs` | T36: migrasi v1→v2, hash-only auth.json, identity mapping |
| XellenRuntime (wiring) | `Xellen/Core/XellenRuntime.cs` | — |

### B. Native MCP Transport (dual-run; Supergateway 8023 UTUH)
| Komponen | File | Bukti test |
|---|---|---|
| STDIO MCP client bridge (spawn owned child, handshake, tools cache, tools/call forward, timeout, Activity events) | `Xellen/Transport/StdioMcpBridge.cs` | T37: tools/call `synthetic-a__echo` → `echo:hello`; child error JSON-RPC → error terusan (tidak menggantung) |
| Native Streamable HTTP host (Kestrel, bind 127.0.0.1, port 8300-candidate + auto-next-free) | `Xellen/Transport/NativeTransportHost.cs` | T37: healthGET 200, healthPOST 405, noAuth 401, badTok 401, init 200 + Mcp-Session-Id, tools/list 200, noSession 400, bogusSession 404, notif 202, ping 200, badProtoVer 400, unknownTool error JSON-RPC, origin403 403 |
| Rate limit per identitas | sama | T40: [200,200,200,429] |
| Project isolation dua project paralel | sama | T39: token A di endpoint B → 401; revoke A → 401 langsung; tools A tidak bocor ke B |
| Parity vs compatibility mode | sama | T41: initialize + tools/list native vs Supergateway 8023 — envelope/protocolVersion/tools-array sama |

**Keputusan transport (RECOMMENDATION, dijelaskan):** host HTTP + STDIO bridge diimplementasikan manual mengikuti spesifikasi MCP 2025-06-18 (diverifikasi dari docs resmi) karena requirement routing dinamis per server/project (`/srv/{id}/mcp`, `/prj/{id}/mcp`) + auth per identitas + forwarding ke child stdio tidak diekspos bersih oleh API DI SDK C# resmi (model static-tools per-app). SDK resmi tetap direkomendasikan untuk adopsi bertahap ketika dukungan multi-endpoint dinamis tersedia. STDIO JSON-RPC sederhana dipilih juga demi nol risiko dependency di environment ini.

### C. Observability — AI Activity Monitor (jendela TERPISAH)
`Xellen/UI/ActivityMonitorForm.cs` — window independen (resize/pindah monitor, hidup saat dashboard minimize), live stream dari EventBus + ActivityStore (query + filter client/project/server/tool/status), detail panel (correlationId, sanitized in/out, processId), status bar (RAM terukur, published/dropped, sesi native). Palet resmi via `Xellen/UI/DesignSystem.cs`.

### D. Project Manager + AI Connect (real)
`Xellen/UI/MainForm.Projects.cs`: create project (slug validasi, allowedServers divalidasi terhadap registry) → **token crypto-random ditulis SEKALI ke `secrets\project-{slug}-token.txt`** (tidak pernah ditampilkan/dicetak; hash-only di auth.json); delete → revoke identity; Copy Connection Package (endpoint + instruksi, tanpa token).

### E. Remote Access viewer (read-only)
`Xellen/UI/MainForm.Remote.cs`: tailscale status/serve-status + health endpoints + deteksi route basi (report-only). **Tidak ada mutasi Funnel/firewall** — sesuai batasan mutlak.

## 2. Yang BELUM selesai + alasan (jujur)
| Item | Status | Alasan |
|---|---|---|
| Browser Manager (multi-profile per project) | **TIDAK diimplementasi** (P3 PRD) | Prioritas sesi ini = P1 observability + P2 native transport; browser isolation butuh pengujian dua project nyata yang berat — dijadwalkan sesi berikut dengan fondasi yang sudah ada (ProcessHost + AuthStore + Registry) |
| Avalonia UI migration | **Ditunda** (RECOMMENDATION) | Brief melarang rewrite tanpa build proof; shell WinForms dipertahankan + business logic sudah terpisah di Core; evaluasi Avalonia = sesi berikut dengan build proof |
| Z.ai adapter penuh | Parsial: connection package generator real; handshake dilakukan client | Kemampuan platform Z.ai (persistent custom tools) belum diverifikasi — PRD §5.5 melarang klaim sebelum uji |
| DPAPI secret-at-rest | Belum | auth.json menyimpan hash-only; plaintext tidak pernah persist — DPAPI = enhancement berikutnya |
| tools/call parity di Desktop Commander | Deferred | Parity diuji pada initialize/tools/list + tools/call synthetic; parity DC butuh tool call nyata via sesi Z.ai (tidak boleh mengganggu production tanpa izin) |
| UI smoke test | **SKIPPED-BLOCKED** | SelfTest headless; butuh sesi desktop interaktif — T42 |
| Settings/Security/Browser pages | Skeleton navigasi belum dibuat | Tidak ada fake pages — hanya fitur nyata yang ditampilkan |

## 3. File baru / diubah
**Baru:** `Xellen/Core/{Redaction,ActivityEvent,EventBus,ActivityStore,AuthStore,Registry,XellenRuntime}.cs`, `Xellen/Transport/{StdioMcpBridge,NativeTransportHost}.cs`, `Xellen/UI/{DesignSystem,ActivityMonitorForm,MainForm.Projects,MainForm.Remote}.cs`
**Diubah:** `Xellen/Core/XellenConfig.cs` (Projects + NativeTransportSettings), `Program.cs` (wiring init/stop, tombol AI Activity, tab Projects/Remote), `McpXellen.Desktop.csproj` (+FrameworkReference Microsoft.AspNetCore.App)
**Backup:** `backups/pre-master-20261113/` (14 file sumber sebelum perubahan) — rollback = salin balik + rebuild.

## 4. Cara menjalankan / mengaktifkan native transport
1. Production tetap: `publish-direct\MCP-Xellen.exe` (PID 30704 saat baseline) — tak berubah.
2. Build baru: `desktop-commander-local\app\publish-p1\MCP-Xellen.exe`.
3. Aktifkan native (opsional, opt-in): set `"nativeTransport": { "enabled": true, "preferredPort": 8300 }` di `config\xellen.json` → jalankan publish-p1 → listener baru di 127.0.0.1:8300; endpoint /srv & /prj; Supergateway 8023 tetap jalan (dual-run).
4. Create project via tab Projects → token file muncul di `secrets\` → upload manual ke client (workflow Z.ai tetap sama).
