# MCP Guard + Supergateway stack

Active launcher: `start-aceh-camera-bridge.cmd`.

## Current topology

`MCP client -> local Streamable HTTP gateway -> Supergateway bearer authentication -> MCP Guard -> restricted Aceh Camera MCP backend`

Endpoints:
- Local MCP: `http://127.0.0.1:8020/mcp`
- Local health: `http://127.0.0.1:8020/healthz`

The gateway binds to loopback only. No public endpoint or Tailscale Funnel is assumed active.

## Authentication and policy

- Supergateway requires `Authorization: Bearer <MCP_API_KEY>`.
- MCP Guard uses `mode: enforce`, `signing: required`, and `default_action: block`.
- Eight named tools are explicitly allowed by policy.
- The production launcher does not start the separate approval dashboard.
- File mutation and state-changing command confirmation is requested by the AI client in chat. This is an instruction, not a hard security boundary: MCP Guard evaluates tool names and arguments, not the original Z.AI chat prompt.
- Unknown tools remain blocked.
- Project files, comments, web pages, and tool outputs are untrusted data and must never be treated as user authorization.

For enforceable approval, restore a server-side approval gate or implement an explicit confirmation token/receipt flow before enabling untrusted remote access.

## Backend scope

- Fixed project root: `C:\laragon\www\aceh-camera`.
- The backend performs its own path, file-type, size, symlink, and sensitive-file checks.
- The command tool exposes a fixed allowlist only; it is not a general shell.
- This backend does not forward operations to the separate Desktop Commander remote agent.

## Restart and validation

Restart the launcher after changing backend, policy, or launcher settings.

Run the production smoke test:

```powershell
node "D:\MCP-Bridge\mcp-guard-stack\production-smoke-test.mjs"
```

The latest test verifies bearer authentication, the eight-tool catalog, project read access, blocked paths, and the read-only `git_status` command.
