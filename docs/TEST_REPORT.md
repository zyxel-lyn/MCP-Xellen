# TEST REPORT — 13 November 2026

**Environment:** Windows (build 26200-era), dotnet SDK 10.0.401, Node 22.x, project `D:\MCP-Bridge\desktop-commander-local\app`, sandbox test `tests/p1` + `tests/master`.
**Command:** `dotnet build -c Release` → 0 error / 0 warning; `dotnet publish -c Release -o publish-p1`; `publish-p1\MCP-Xellen.exe --selftest`.
**Bukti mentah:** `tests/p1/results.json` (ditulis fresh setiap run), `tests/p1/logs/selftest.log`.

## Hasil run final (13 Nov 2026, exit code 0)

**40 PASS · 0 FAIL · 1 SKIPPED (T42, headless)**

| Kelompok | Test | Inti asersi |
|---|---|---|
| Proses lifecycle (regresi) | T1–T16 | start/stop/restart-backoff/crash-cleanup/orphan-sweep/registry/log-rotation/metrics/graceful/no-cull/proteksi-eksternal |
| WMI failure (regresi) | T17–T32 | Success-list/Success-empty/exception/timeout/cancel-observed/abandoned-jujur/no-pileup/QueryPid 4-status/kill-refused/mid-sweep-failed/recovery |
| AuthStore | T33 | issue→verify; wrong-token→null; revoke→null; TTL 1s→expired; rotate→old dead new works |
| Redaction | T34 | bearer/token-json/sk-/registered-secret/PEM/truncation |
| EventBus | T35 | overflow terhitung jujur (delivered=3, droppedNewest=1) |
| Registry v2 | T36 | migrasi v1→v2 + backup + hash-only auth.json + identity mapping |
| Native transport | T37 | healthGET 200 / healthPOST 405 / noAuth 401 / badTok 401 / init 200+session / tools-list 200 / noSession 400 / bogusSession 404 / notif 202 / ping 200 / badProtoVer 400 / tools/call echo / unknown-tool error / origin 403 |
| Project isolation | T39 | dua project paralel: token cross → 401; tools tidak bocor antar project; revoke langsung 401 |
| Rate limit | T40 | [200,200,200,429] pada limit 3/menit |
| Parity | T41 | initialize+tools/list native vs Supergateway 8023 — shape sama |
| UI smoke | T42 | SKIPPED-BLOCKED (headless) |

## Test berbasis proses sintetis
- `tests/p1/spawner.cmd` / `spawner2.cmd` (cmd+ping ber-marker `xellen-p1-test`).
- `tests/master/synthetic-mcp-server.mjs` — MCP stdio server nyata (initialize/tools/list/tools/call echo+fail_echo).
- Tidak ada test yang menyentuh proses production (T8: 9 kill operations, **0 non-marker kill**).

## Blocked tests
| Test | blocker | bukti yang dibutuhkan |
|---|---|---|
| T42 UI smoke | sesi headless | buka manual "Open AI Activity Monitor"; screenshot dua window |
| tools/call parity di DC | butuh sesi Z.ai nyata dengan izin | correlation ID dari Activity Monitor saat tool call DC |
| Browser isolation 2 project | Browser Manager belum diimplementasi (P3) | — |
