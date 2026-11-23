# MCP Xellen

MCP Xellen is a Windows desktop launcher for the official `@wonderwhy-er/desktop-commander` package.

## Active staging application

- Executable: `app\publish-direct\MCP-Xellen.exe`
- Desktop shortcut: MCP Xellen (Staging)
- Backend: official Desktop Commander over STDIO.
- Gateway: Supergateway Streamable HTTP, bound to `127.0.0.1:8023`.
- Authentication: Supergateway `--apiKeyFile`, requiring `Authorization: Bearer <token>`.
- Token file: `D:\MCP-Bridge\secrets\mcp-xellen-staging-token.txt` (64 hex characters; never print or paste its contents into chat).
- Local MCP endpoint: `http://127.0.0.1:8023/mcp`.
- Public staging endpoint: `https://laptop-see8pp9p.tail943ca4.ts.net:8443/mcp`.
- Health: `/healthz`.
- Stateful sessions enabled with 30-minute idle timeout.
- App buttons: Copy Z.ai Magic Prompt, Locate Token File, Verify Gateway, Stop.
- The token must be uploaded separately to the Z.ai Agent Mode sandbox as `upload/mcp-xellen-staging-token.txt`.
- Magic Prompt uses Z.ai Website Agent Mode with Python requests; it does not refer to ZCode or a nonexistent MCP Settings menu.
- The prompt instructs the agent to perform initialize, notifications/initialized, tools/list, and preserve Mcp-Session-Id. It also tells the agent not to print the token and to request confirmation before mutations.
- Verify Gateway confirms local health and rejects requests without a token. It does not prove Z.ai has authenticated or called tools.

## Funnel configuration

- Production: `https://laptop-see8pp9p.tail943ca4.ts.net` port 443 -> `http://127.0.0.1:8020`.
- Staging: `https://laptop-see8pp9p.tail943ca4.ts.net:8443` -> `http://127.0.0.1:8023`.
- These are separate Funnel listeners. Production port 443 remains unchanged.
- Funnel status was checked after staging activation; public staging health returned HTTP 200, unauthenticated MCP returned HTTP 401, and production health returned HTTP 200.

## Legacy local OAuth prototype

- `app\xellen-oauth-proxy.mjs` is retained for reference/rollback and is not used by the active direct-Bearer app.
- Previous local OAuth proxy used port 8021 and Supergateway port 8022.
- Do not route Funnel to 8021 or 8022 in the active staging design.

## Security and limitations

- The static staging token is separate from the existing production API key.
- The token is stored in a local secret file and is not included in the Magic Prompt.
- Upload the token file only to the intended Z.ai Agent Mode sandbox; do not paste its contents into chat or screenshots.
- Supergateway's Bearer token authenticates access, but does not provide per-tool authorization or enforce chat-level confirmation. The Magic Prompt's confirmation rule is behavioral, not a server-side security boundary.
- Desktop Commander tools still inherit the Windows account's filesystem and command permissions, subject to its own configured allowed directories.
- Before granting write/command access to untrusted prompts or documents, add a server-side policy/approval layer or restrict the backend's permissions further.

## Rollback

1. Close MCP Xellen (Staging) to stop its local Supergateway process.
2. Disable only the staging Funnel listener with `tailscale funnel --https=8443 off`.
3. Do not use `tailscale funnel reset`; it would remove the production listener too.
4. Leave production port 443 and its port 8020 backend untouched.


## Browser MCP add-on for Z.ai Web

- Browser engine: `@agent-infra/mcp-server-browser@latest`, headless, bound to `127.0.0.1:8089`.
- Browser MCP is bridged to authenticated Streamable HTTP through Supergateway on `127.0.0.1:8025`.
- Separate Bearer token: `secrets\mcp-browser-staging-token.txt` (never print or paste its contents into chat).
- Public route reuses the existing staging Funnel listener without changing its root or production route: `https://laptop-see8pp9p.tail943ca4.ts.net:8443/browser/mcp` -> local port `8025`.
- Start script: `desktop-commander-local\START_BROWSER_MCP_XELLEN.cmd`.
- Z.ai Web prompt: `desktop-commander-local\ZAI_BROWSER_MCP_PROMPT.md`.
- Browser MCP local service is separate from the existing `@browsermcp/mcp` Chrome-extension workflow in `C:\laragon\www\_bmcp`.
- The Browser MCP endpoint requires the separate Browser staging token. Do not reuse or expose the Xellen Desktop Commander token.
- Verified: local Browser MCP initialize + tools/list returned 21 tools; public Funnel health returned HTTP 200; public unauthenticated MCP request returned HTTP 401; authenticated public initialize succeeded.
