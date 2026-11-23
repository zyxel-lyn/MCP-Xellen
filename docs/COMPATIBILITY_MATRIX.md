# COMPATIBILITY MATRIX — 13 November 2026

| # | Client | Transport | Endpoint | Server/tool | Status | Bukti |
|---|---|---|---|---|---|---|
| 1 | SelfTest JSON-RPC client (HTTP) | Streamable HTTP (JSON) | native `/srv/synthetic-a/mcp` (8377 test) | synthetic (echo/fail_echo) | **LOLOS** | T37 |
| 2 | SelfTest dua project | Streamable HTTP | native `/prj/{slug}/mcp` | synthetic-a & synthetic-b | **LOLOS** (isolasi+revoke) | T39 |
| 3 | Rate-limited client | Streamable HTTP | native | synthetic | **LOLOS** (429) | T40 |
| 4 | Native client | Streamable HTTP | native 8381 (test) | synthetic-par | **LOLOS** (parity shape) | T41 |
| 5 | Compat client (initialize+tools/list) | Streamable HTTP | Supergateway 8023 | Desktop Commander | **LOLOS** (read-only parity baseline) | T41 |
| 6 | Z.ai Web Agent (Python+Bearer) | Streamable HTTP via Funnel | compat `/mcp` | Desktop Commander | **DIPAKAI PRODUCTION** (healthz 200; end-to-end sesi ini tidak diuji ulang) | historis + health |
| 7 | Z.ai Web Agent | Streamable HTTP via Funnel | **native** `/prj/{slug}/mcp` | per-project | **BELUM** — butuh route Funnel baru (persetujuan owner) | — |
| 8 | Local stdio client | stdio | — | — | Config generator belum diuji end-to-end | — |
| 9 | Browser MCP extension | WS localhost | 9010 | browsermcp daemon | Eksternal — tidak berubah | audit 12 Nov |
| 10 | Claude/Cursor/Continue | stdio/HTTP | — | — | **BELUM diuji** — jangan klaim | — |

Catatan jujur: parity diuji pada initialize/tools/list + tools/call synthetic. tools/call pada Desktop Commander via native menunggu sesi nyata berizin.
