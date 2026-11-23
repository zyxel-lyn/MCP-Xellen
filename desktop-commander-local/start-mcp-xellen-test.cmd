@echo off
setlocal
set "ROOT=D:\MCP-Bridge"
set "DC_DIR=%ROOT%\desktop-commander-local\package"
set "SG=%ROOT%\mcp-guard-stack\node_modules\supergateway\dist\index.js"
set "KEY=%ROOT%\secrets\mcp-api-key.txt"
if not exist "%KEY%" (echo MCP Xellen: API key file missing.& pause & exit /b 11)
if not exist "%DC_DIR%\dist\index.js" (echo MCP Xellen: Desktop Commander package missing.& pause & exit /b 12)
echo Starting MCP Xellen TEST on port 8021...
node "%SG%" --stdio "node %DC_DIR%\dist\index.js --no-onboarding" --outputTransport streamableHttp --port 8021 --host 127.0.0.1 --apiKeyFile "%KEY%" --stateful --sessionTimeout 1800000 --healthEndpoint /healthz
echo MCP Xellen stopped.
pause

