# Troubleshooting

## Application does not start

- Confirm Node.js 22.12.0+ is installed and available in PATH.
- Confirm the runtime exists in `%LOCALAPPDATA%\MCP Xellen\backend\node_modules`.
- Confirm the token file exists under `secrets\mcp-xellen-token.txt`.
- Re-run `scripts/install.ps1` from the cloned repository if dependencies or the app build are incomplete.

## Port 8023 is already in use

Find the process:

```powershell
Get-NetTCPConnection -LocalPort 8023 -State Listen
```

Do not terminate an unknown process. Identify the owning PID first. Stop the other application or change the port consistently in the app source, installer configuration, and Funnel target.

## Health endpoint returns an error

```powershell
Invoke-WebRequest http://127.0.0.1:8023/healthz
```

Inspect the local log at `%LOCALAPPDATA%\MCP Xellen\logs\mcp-xellen.log`.

## Unauthenticated request is not rejected

The expected behavior is HTTP 401 for an MCP request without a Bearer token. If it is not, stop the gateway and inspect the Supergateway command and token-file path before exposing the endpoint.

## Funnel cannot be enabled

Check:
- Tailscale is signed in and Connected.
- MagicDNS is enabled.
- Funnel is allowed by the tailnet policy.
- The selected port is one of 443, 8443, or 10000.
- Another Serve/Funnel configuration is not occupying the same port.

Commands:

```powershell
tailscale status
tailscale funnel status
```

## Z.ai returns HTTP 401

The token uploaded to Agent Mode must match the token file used by the running gateway. Do not upload an old token from a previous installation. Rotate the token if it may have been exposed.

## Z.ai cannot connect

- Ensure the computer is awake and connected to the internet.
- Confirm MCP Xellen is running.
- Confirm Funnel is active.
- Check that the endpoint ends with `:8443/mcp`.
- Test `https://YOUR-HOSTNAME:8443/healthz` in a browser. A successful health check does not prove MCP authentication.

## MCP session expired

The current gateway is configured for stateful sessions with a 30-minute idle timeout. Re-run initialize, obtain a new session ID, send `notifications/initialized`, and call `tools/list` again.

## Wi-Fi changed or laptop resumed from Hibernate

No Funnel reconfiguration is normally required. Wait for Wi-Fi and Tailscale to reconnect, then check the app and Funnel status. During Hibernate, the endpoint is unavailable because the laptop is suspended.
