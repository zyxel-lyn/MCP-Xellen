@echo off
setlocal
set "API_KEY_FILE=D:\MCP-Bridge\secrets\mcp-api-key.txt"
set "APPROVAL_TOKEN_FILE=D:\MCP-Bridge\secrets\mcp-approval-ui-token.txt"
for /f "usebackq delims=" %%A in ("%APPROVAL_TOKEN_FILE%") do set "PP_APPROVAL_TOKEN=%%A"
if not defined PP_APPROVAL_TOKEN exit /b 10
if not exist "%API_KEY_FILE%" exit /b 11
set "GUARD_DIR=D:\MCP-Bridge\mcp-guard-stack"
set "UNIVERSAL_DIR=D:\MCP-Bridge\mcp-guard-stack\universal-mcp"
cd /d "%GUARD_DIR%"
node "%GUARD_DIR%\node_modules\supergateway\dist\index.js" --stdio "node %GUARD_DIR%\node_modules\@permission-protocol\mcp-guard\bin\mcp-guard --config %UNIVERSAL_DIR%\pp.config.yaml --agent-id zai-universal-test --operator local-operator --approval-port 0 -- node %UNIVERSAL_DIR%\stdio-server.mjs" --outputTransport streamableHttp --port 8022 --host 127.0.0.1 --apiKeyFile "%API_KEY_FILE%" --stateful --sessionTimeout 1800000 --tools list_project_tree --tools read_project_file --tools search_project_text --tools write_project_file --tools edit_project_file --tools delete_project_file --tools create_project_directory --tools execute_project_command --tools list_workspaces --tools inspect_project --tools universal_list_project_tree --tools universal_read_project_file --tools universal_search_project_text --tools universal_write_project_file --tools universal_edit_project_file --tools universal_delete_project_file --tools universal_create_project_directory --tools universal_run_project_command --healthEndpoint /healthz
endlocal
