# Z.ai Website Agent Mode integration

This integration targets the **Z.ai website at https://chat.z.ai/**. It does not target ZCode or assume that the website has a native MCP settings/connectors page.

## How it works

The MCP Xellen desktop app copies a prompt containing the user's configured HTTPS MCP endpoint. The user uploads their own token file to the same Agent Mode session. The agent uses Python `requests` to communicate with the MCP Streamable HTTP endpoint.

This is a prompt-driven integration. It depends on the tools and sandbox capabilities currently available in Z.ai Agent Mode. It is not a first-party Z.ai MCP connector.

## User workflow

1. Start MCP Xellen.
2. Confirm the gateway is healthy.
3. Configure Tailscale Funnel and the endpoint in the local `config.json`.
4. Click **Copy Z.ai Magic Prompt**.
5. Open Z.ai Website and select Agent Mode.
6. Paste the prompt.
7. Upload the local `mcp-xellen-token.txt` file into that same session.
8. Ask the agent to connect and list tools only.
9. Check that the agent reports a successful MCP initialize and a tools/list response.

## Expected handshake

The agent is instructed to:

1. Read the token directly from the uploaded file without printing it.
2. Send `initialize` with protocol version `2025-06-18`.
3. Store the returned `Mcp-Session-Id`.
4. Send `notifications/initialized`.
5. Call `tools/list`.
6. Support JSON and Server-Sent Events response bodies.
7. Re-initialize if the session expires.
8. Keep the token out of prompts, logs, and output.

The health endpoint is not proof of an authenticated MCP connection. The handshake and tools/list response are the end-to-end proof.

## Tool calls

The prompt initially asks for connection and tool discovery only. Do not begin with file writes, file deletion, process termination, or terminal commands that mutate the computer.

When the user later wants an operation, the agent should explain the target and expected effect and request approval before it performs that operation. This is a conversational safety instruction only; it is not enforced by the MCP server.

## Troubleshooting

- **401 Unauthorized:** the uploaded token does not match the token file used by the running gateway.
- **Connection timeout:** the computer may be asleep, offline, or Funnel may be inactive.
- **No session ID:** check whether the server accepted the initialize request and whether the client handled the response headers.
- **Session expired:** perform a new initialize handshake and obtain a fresh session ID.
- **Agent cannot read upload:** confirm the token file was uploaded to the same Agent Mode conversation and that file access is available in the current sandbox.
- **Python requests unavailable:** the current Agent Mode sandbox may not support this integration; do not paste the token into ordinary chat as a workaround.

## Privacy

Each user must use their own Funnel hostname and token. Never use the maintainer's test endpoint or token. Never commit a personal `config.json` or token file.
