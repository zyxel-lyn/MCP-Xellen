# Security Notes

MCP Xellen exposes a local Desktop Commander process through an authenticated HTTP endpoint. This is powerful by design and must be treated as remote access to the Windows account running the application.

## What the token protects

Supergateway requires an Authorization Bearer token for MCP requests. The installer generates 32 cryptographically random bytes and stores the hexadecimal token in the user's local application data directory.

The token is not:
- a per-user identity system;
- a per-tool permission system;
- an approval engine;
- a sandbox;
- a replacement for Windows account permissions.

Anyone who obtains the token can attempt to call the exposed Desktop Commander tools.

## Prompt confirmation is not enforcement

The generated Z.ai prompt asks the agent to seek explicit confirmation before mutations. This is only an instruction to the model. A different MCP client, malicious prompt, prompt injection, or compromised agent can ignore it.

Do not describe this as a hard security boundary.

## Safe handling

- Never commit `secrets/`, `config.json`, logs, or token files.
- Never publish a Funnel hostname that you consider private.
- Never paste a token into public chat, issues, pull requests, or screenshots.
- Upload the token only to the intended Z.ai Agent Mode session.
- Use a dedicated Windows account for remote MCP access where practical.
- Restrict Desktop Commander's own allowed directories if you do not want the agent to reach the entire user environment.
- Keep Windows, Node.js, Tailscale, Desktop Commander, and Supergateway updated.
- Stop MCP Xellen and disable Funnel when remote access is not needed.
- Do not expose port 8023 directly on your router or bind it to 0.0.0.0.

## Token rotation

1. Stop MCP Xellen.
2. Back up nothing containing the old token; revoke it by replacing the token file.
3. Generate a new 32-byte random token and replace `secrets/mcp-xellen-token.txt`.
4. Restart MCP Xellen.
5. Upload the new token file to the intended Z.ai session.
6. Reconnect and verify the handshake.

A previously uploaded token should be considered compromised if it was shared with an unintended party.

## Public Funnel exposure

Tailscale Funnel makes the HTTPS endpoint publicly reachable. Funnel does not replace the application's Bearer authentication. Use the HTTPS endpoint only, keep the token secret, and do not expose a health or debug endpoint that leaks local information.

## Reporting vulnerabilities

Do not publish secrets or exploit details in a public issue. Contact the repository maintainer privately through GitHub.
