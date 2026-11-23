@echo off
setlocal
set "APPROVAL_TOKEN_FILE=D:\MCP-Bridge\secrets\mcp-approval-ui-token.txt"
set "API_KEY_FILE=D:\MCP-Bridge\secrets\mcp-api-key.txt"
set "GUARD_DIR=D:\MCP-Bridge\mcp-guard-stack"
for /f "usebackq delims=" %%A in ("%APPROVAL_TOKEN_FILE%") do set "PP_APPROVAL_TOKEN=%%A"
if not defined PP_APPROVAL_TOKEN exit /b 10
cd /d "%GUARD_DIR%"
node "D:\MCP-Bridge\mcp-guard-stack\node_modules\supergateway\dist\index.js" --stdio "node D:\MCP-Bridge\mcp-guard-stack\node_modules\@permission-protocol\mcp-guard\bin\mcp-guard --config D:\MCP-Bridge\mcp-guard-stack\pp.config.test.yaml --agent-id zai-web --operator local-operator --approval-port 3101 -- node D:\MCP-Bridge\mcp-guard-stack\approval-test-server.mjs" --outputTransport streamableHttp --port 8021 --host 127.0.0.1 --apiKeyFile "%API_KEY_FILE%" --stateful --sessionTimeout 1800000 --tools approval_test --healthEndpoint /healthz
endlocal