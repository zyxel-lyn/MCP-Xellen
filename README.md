# MCP Xellen

**A lightweight Windows launcher that exposes the official Desktop Commander MCP server over authenticated Streamable HTTP.**

MCP Xellen starts the Desktop Commander STDIO server, wraps it with Supergateway, binds the gateway to localhost, and protects the HTTP endpoint with a locally generated Bearer token. Users can optionally expose that endpoint through Tailscale Funnel so a remote agent such as Z.ai Website Agent Mode can reach it.

> **Status: Early public release / Windows source build.** The project is being prepared for broader use. Review the limitations and security notes before exposing your computer to the internet.

## What it does

- One-click Windows launcher for the local MCP gateway.
- Uses the official `@wonderwhy-er/desktop-commander` package as the MCP backend.
- Converts STDIO to MCP Streamable HTTP through Supergateway.
- Binds the gateway to `127.0.0.1:8023`, not all network interfaces.
- Generates a unique 256-bit random Bearer token on each installation.
- Requires authentication for MCP requests.
- Provides a local health check and an unauthenticated-request rejection test.
- Generates a Z.ai Website Agent Mode prompt that uses Python `requests`.
- Keeps each user's endpoint, token, runtime dependencies, and logs outside the Git repository.

## Architecture

```text
Z.ai Website Agent Mode
        |
        | HTTPS + Authorization: Bearer <user token>
        v
Tailscale Funnel :8443
        |
        v
127.0.0.1:8023/mcp
        |
        v
Supergateway (Streamable HTTP)
        |
        | STDIO
        v
Official Desktop Commander MCP server
        |
        v
The Windows user's files, processes, and terminal
```

MCP Xellen is a local bridge, not a hosted cloud service. Your Windows computer must be powered on, awake, connected to the internet, and running MCP Xellen for the remote endpoint to work.

## Requirements

| Component | Requirement |
|---|---|
| Operating system | Windows 10/11 x64 |
| Node.js | 22.12.0 or newer |
| npm | Installed with Node.js |
| .NET SDK | .NET 10 SDK (for building from source) |
| .NET runtime | .NET 10 Desktop Runtime (for framework-dependent app) |
| Tailscale | Current Windows client, signed in |
| Tailscale Funnel | Enabled for your tailnet and permitted by its policy |
| Z.ai | Website Agent Mode with Python/Bash and file-upload capability |

The installer downloads the pinned runtime dependencies:
- `@wonderwhy-er/desktop-commander@0.3.1`
- `supergateway@4.2.1`

## Install from source

1. Install Node.js 22.12.0+ and .NET 10 SDK.
2. Install Tailscale and sign in to your own tailnet.
3. Clone this repository:
   ```powershell
   git clone https://github.com/zyxel-lyn/MCP-Xellen.git
   cd MCP-Xellen
   ```
4. Run PowerShell:
   ```powershell
   Set-ExecutionPolicy -Scope Process Bypass
   .\scripts\install.ps1
   ```
5. The installer places the application and runtime files in:
   ```text
   %LOCALAPPDATA%\MCP Xellen\
   ```
6. Launch **MCP Xellen** from the Desktop shortcut.

The installer creates a local token file at:
```text
%LOCALAPPDATA%\MCP Xellen\secrets\mcp-xellen-token.txt
```

It also creates `config.json`. Do not upload either file to GitHub.

## Configure Tailscale Funnel

MCP Xellen listens locally on port `8023`. After the application reports that the gateway is running, open PowerShell and run:

```powershell
tailscale funnel --bg --https=8443 http://127.0.0.1:8023
tailscale funnel status
```

Tailscale Funnel requires HTTPS, MagicDNS, a Funnel-enabled tailnet policy, and an allowed HTTPS port. Funnel supports ports 443, 8443, and 10000.

Copy the HTTPS hostname shown by `tailscale funnel status`. Your MCP endpoint is:

```text
https://YOUR-DEVICE.YOUR-TAILNET.ts.net:8443/mcp
```

Edit `%LOCALAPPDATA%\MCP Xellen\config.json` and replace the example endpoint:

```json
{
  "publicEndpoint": "https://YOUR-DEVICE.YOUR-TAILNET.ts.net:8443/mcp"
}
```

Restart MCP Xellen after changing the configuration. The app reads the public endpoint when it starts and when it generates the prompt.

To disable only this Funnel listener later:

```powershell
tailscale funnel --https=8443 off
```

Do not use `tailscale funnel reset` if other Funnel services are configured on the same device.

## Connect Z.ai Website Agent Mode

1. Keep MCP Xellen running and confirm its status is healthy.
2. Confirm the public endpoint works through your own Tailscale Funnel.
3. In MCP Xellen, click **Copy Z.ai Magic Prompt**.
4. Open [Z.ai Website](https://chat.z.ai/) and use Agent Mode. This workflow is for the website, not ZCode or a desktop client.
5. Paste the copied prompt.
6. Click **Locate Token File** in MCP Xellen and upload `mcp-xellen-token.txt` to the intended Agent Mode session.
7. Let the agent read the token from the uploaded file without printing it.
8. The agent should perform MCP `initialize`, `notifications/initialized`, and `tools/list`. Confirm that the returned server information and tool list are present before asking it to call a tool.

This is a prompt-driven HTTP-client workflow. It is not a native Z.ai MCP connector integration, and availability depends on Z.ai Agent Mode retaining Python/Bash and file-upload support.

## Local verification

In MCP Xellen, click **Verify Gateway**. A successful local check means:
- `GET http://127.0.0.1:8023/healthz` returns a successful response.
- An MCP POST without a Bearer token is rejected with HTTP 401.

This does **not** prove that the remote Z.ai session authenticated successfully. Only a successful MCP handshake and `tools/list` prove that end-to-end connection.

## Security model

- The gateway binds to loopback (`127.0.0.1`).
- MCP requests require a randomly generated Bearer token.
- The token is stored locally and excluded from Git.
- Tailscale Funnel provides public HTTPS transport; the Bearer token remains the application-level access credential.
- The token grants access to the Desktop Commander tools exposed by the backend.
- Desktop Commander runs with the permissions of the Windows account that launched it.

**Important:** the prompt asks the agent to request confirmation before file changes, deletion, commits, pushes, and mutating commands. That is behavioral guidance, not a server-enforced approval boundary. Do not treat it as a security sandbox. A prompt injection or a different client may call the available tools without following that instruction.

Use a dedicated Windows account or restrict Desktop Commander's own allowed directories if you need stronger isolation. Never publish your token, share it in screenshots, or paste it into a public issue.

See [Security](docs/SECURITY.md), [Installation (Bahasa Indonesia)](docs/INSTALLATION-ID.md), [Architecture](docs/ARCHITECTURE.md), [Z.ai Website integration](docs/ZAI-WEBSITE.md), and [Tool catalog](docs/TOOLS.md).

## Hibernate, sleep, and network changes

Changing from one Wi-Fi network to another does not require changing the MCP configuration. Tailscale reconnects using the new internet connection.

However, Hibernate and Sleep suspend the computer. The local gateway and Funnel cannot serve requests while Windows is suspended. After resume, wait for Wi-Fi and Tailscale to reconnect, then verify MCP Xellen is running. The endpoint and token do not need to be recreated unless their configuration was changed.

## Troubleshooting

| Symptom | Check |
|---|---|
| App says components are missing | Re-run `scripts/install.ps1`; check Node.js and token file |
| Gateway does not start | Check `%LOCALAPPDATA%\MCP Xellen\logs\mcp-xellen.log` |
| Local health check fails | Confirm MCP Xellen is open and port 8023 is not occupied |
| Funnel is unavailable | Run `tailscale status` and `tailscale funnel status`; review tailnet Funnel policy |
| Z.ai gets HTTP 401 | Upload the matching token file; do not use a token from another installation |
| Z.ai cannot reach the endpoint | Confirm the laptop is awake, online, Funnel is active, and the URL ends in `:8443/mcp` |
| Session expired | Re-run the MCP initialize handshake and obtain a new session ID |

## Repository layout

```text
MCP-Xellen/
â”œâ”€â”€ src/MCP-Xellen/       # Windows Forms launcher source
â”œâ”€â”€ scripts/install.ps1   # Install dependencies, create token, publish app
â”œâ”€â”€ docs/                 # Architecture, setup, security, troubleshooting
â”œâ”€â”€ package.json          # Pinned Node.js runtime dependencies
â”œâ”€â”€ config.example.json   # Safe configuration template
â””â”€â”€ README.md
```

## Third-party components

MCP Xellen integrates, but does not replace or claim authorship of:
- [Desktop Commander](https://github.com/wonderwhy-er/DesktopCommanderMCP)
- [Supergateway](https://github.com/supercorp-ai/supergateway)
- [Tailscale](https://tailscale.com/)
- [Model Context Protocol](https://modelcontextprotocol.io/)

See [Third-party notices](THIRD_PARTY_NOTICES.md).

## Contributing

Bug reports and pull requests are welcome. Before submitting a report, remove tokens, private hostnames, local usernames, file paths, and other personal information from logs and screenshots. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

MCP Xellen project code is licensed under the MIT License. Third-party components remain under their respective licenses. See [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
