# MCP tools exposed by MCP Xellen

MCP Xellen does not implement its own filesystem or terminal tool catalog. It exposes the tools registered by the pinned upstream Desktop Commander package.

The reference environment used Desktop Commander `0.3.1`; its MCP `tools/list` response contained 25 tools during the initial end-to-end handshake. The actual tool catalog is obtained dynamically from the backend at connection time and may change when the upstream package version changes.

## How to inspect tools

After the Z.ai Agent Mode client completes:
1. `initialize`
2. `notifications/initialized`
3. `tools/list`

the client should display the returned tool names and descriptions. The list from the live server is authoritative.

## Tool access and permissions

The tool catalog can include filesystem, terminal, process, and other Desktop Commander capabilities. Their effective access is determined by Desktop Commander configuration and the Windows account running the backend.

MCP Xellen does not filter the upstream tool list or implement per-tool permission rules. Review the upstream Desktop Commander documentation before exposing the service remotely.

The prompt asks the agent to seek approval before mutating operations, but this instruction is not enforced by the server.
