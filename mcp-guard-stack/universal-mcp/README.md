# Z.ai Universal Project MCP

Status: staged and locally smoke-tested. This service is not connected to the public Tailscale route and does not replace the existing Aceh Camera MCP.

## Workspace roots

Configured in package.json:

- C:\laragon\www
- D:\Wildanun Mukhallid\Project
- D:\Unity\Projects

Projects must be located inside one of these roots. Add another root only after checking its contents and access scope.

## Tools

Read-only:
- list_workspaces
- inspect_project
- universal_list_project_tree
- universal_read_project_file
- universal_search_project_text

Approval-gated through MCP Guard:
- universal_write_project_file
- universal_edit_project_file
- universal_delete_project_file
- universal_create_project_directory
- universal_run_project_command

The existing Aceh Camera tools remain registered under their original names. Universal tools use the universal_ prefix to avoid name collisions.

## Supported project ecosystems

Framework detection includes Laravel/PHP, Node.js, .NET/C#, Python, Android/Gradle, Flutter/Dart, Go, and Rust.

Command executables are allowlisted: git, php, composer, npm, npx, node, python, py, dotnet, adb, gradle, and gradlew. Commands run with the selected project as the fixed working directory and do not accept a free-form shell string.

## Safety boundaries

- Filesystem access is restricted to configured workspace roots.
- Paths are normalized and traversal, symbolic links, and reparse-point escapes are refused.
- Secret-like filenames, environment files, binary files, dependency folders, build outputs, and common caches are excluded.
- File writes create new files only; edits require exact-match counts; deletion is limited to allowlisted text files.
- Command execution and all universal mutations require MCP Guard approval.
- Command output is capped at 1 MiB and execution timeout at 5 minutes.

## Local test

Run from D:\MCP-Bridge\mcp-guard-stack:

    node .\universal-mcp\smoke-test.mjs
    node .\universal-mcp\smoke-test-guarded.mjs

## Launchers

- start-universal-test.cmd: temporary local-only Supergateway on 127.0.0.1:8022 for isolated verification.
- start-universal-bridge.cmd: prepared production launcher for port 8020 using the same existing API key file and Tailscale route. Do not run it while the current process owns port 8020.

The 8022 test process has been stopped after verification. No Tailscale route was changed.

## Integration status

The production files in the existing stack have deliberately not been modified:
- ..\stdio-server.mjs
- ..\pp.config.yaml
- ..\start-aceh-camera-bridge.cmd

The merged server and matching Guard policy are already staged in this folder. The remaining cutover is to stop the existing Supergateway process on :8020 and start start-universal-bridge.cmd. Tailscale already targets :8020, so its route does not need to change. This briefly interrupts the current Z.ai connection; cutover is intentionally deferred until a maintenance window is approved. The old launcher remains available for rollback.
