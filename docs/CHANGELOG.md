# CHANGELOG

## 2026-11-13 — Master Implementation Session (unreleased, di publish-p1)
- **Added** Core: RedactionService, ActivityEvent, EventBus (bounded+drop-counted), ActivityStore (ring+JSONL rotasi), AuthStore (crypto-random, hash-only, scope, expiry, revoke, rotate-eksplisit), Registry v2 (projects, migrasi idempotent v1→v2), XellenRuntime.
- **Added** Transport: StdioMcpBridge (owned stdio child, handshake, tools cache, call forward, Activity events, tree-stop), NativeTransportHost (MCP Streamable HTTP 2025-06-18 di 127.0.0.1:8300-candidate; /healthz, /srv/{id}/mcp, /prj/{id}/mcp; Bearer per identitas; Origin 403; rate limit; Mcp-Session-Id; JSON+SSE; 413/400/404).
- **Added** UI: DesignSystem (palet resmi), ActivityMonitorForm (jendela terpisah), tab Projects (create/token-file sekali-tulis/revoke/connection package), tab Remote (read-only), tombol Open AI Activity Monitor.
- **Added** SelfTest T33–T42 + filter `--selftest=<prefix>`; synthetic MCP stdio server (`tests/master/synthetic-mcp-server.mjs`).
- **Changed** XellenConfig schema 1→2 (Projects, NativeTransportSettings; migrasi idempotent + backup otomatis); Program.cs wiring (Registry/AuthStore/ActivityStore init; transport opt-in start; shutdown berurutan); csproj +FrameworkReference Microsoft.AspNetCore.App.
- **Security** — tanpa rotasi credential otomatis; tanpa perubahan Funnel/port/endpoint production; token plaintext tidak pernah dicetak/di-log/di-URL.
- **Deferred** — Browser Manager (P3), Avalonia migration, DPAPI, resumability SSE, Z.ai native adapter end-to-end, UI smoke interaktif (T42 skipped).

## 2026-11-11/12 — P1 chain (dirangkum; detail di report masing-masing)
- P1 Core (ProcessHost/LogService/Metrics/Registry awal), Child Policy safety fix (no auto-cull), WMI Failure Handling (ProcessSnapshotResult), WMI Timeout Hardening (cancellation observed + QueryPid 4-status + mid-sweep failure contract) — SelfTest 32/32.

## 2026-11-11 — Full Architecture Audit
- `MCP_XELLEN_FULL_ARCHITECTURE_AUDIT.md` + roadmap P0–P5.
