# Architecture

## Components

### 1. Windows Forms launcher

The C# WinForms application is the local control surface. It starts the gateway process, polls the local health endpoint, copies a Z.ai Agent Mode prompt, opens the secrets directory, verifies that unauthenticated requests are rejected, and stops the child process tree.

### 2. Desktop Commander

The backend is the official npm package `@wonderwhy-er/desktop-commander@0.3.1`. MCP Xellen does not reimplement Desktop Commander's file, process, or terminal tools.

The backend communicates over STDIO. The launcher starts it as a child process through Supergateway.

### 3. Supergateway

Supergateway `4.2.1` converts the STDIO MCP process into Streamable HTTP. It listens on `127.0.0.1:8023`, requires a Bearer token file, uses stateful sessions with a 30-minute idle timeout, and exposes `/healthz`.

### 4. Tailscale Funnel

Funnel forwards public HTTPS traffic on port 8443 to the local gateway. The MCP endpoint is `/mcp`. The public hostname belongs to each user's own Tailscale device and tailnet. It must never be hard-coded into the public source.

### 5. Z.ai Website Agent Mode

The generated prompt instructs the website agent to use Python `requests` to perform the MCP handshake and list tools. The token is uploaded separately by the user. This is a prompt-driven client workflow, not a native Z.ai connector.

## Request flow

1. Agent sends HTTPS POST to the user's Funnel hostname.
2. Funnel forwards the request to `127.0.0.1:8023`.
3. Supergateway checks the Bearer token.
4. Supergateway maps Streamable HTTP messages to the Desktop Commander STDIO process.
5. Desktop Commander performs the requested operation with the Windows account's effective permissions.
6. The MCP response returns through the same path.

## Local paths

The installer keeps runtime state under:

```text
%LOCALAPPDATA%\MCP Xellen\
├── app\
├── backend\
├── secrets\
├── logs\
└── config.json
```

The repository contains only source, scripts, documentation, and safe templates.

## Operational characteristics

- Local gateway port: 8023.
- Bind address: 127.0.0.1.
- MCP endpoint: /mcp.
- Health endpoint: /healthz.
- Funnel HTTPS port: 8443.
- Bearer token: 32 random bytes encoded as 64 hexadecimal characters.
- Stateful session idle timeout: 1,800,000 ms (30 minutes).
- Desktop Commander backend: STDIO.
- Transport: MCP Streamable HTTP.

## Trust boundaries

Tailscale Funnel makes the endpoint reachable from the public internet. Authentication is provided by the Bearer token enforced by Supergateway. The token is a bearer credential: anyone who obtains it can use the exposed MCP tools.

The application does not provide per-tool authorization, user identity, a server-side approval workflow, or an operating-system sandbox. Prompt instructions asking the agent to request confirmation are not a substitute for enforcement.
