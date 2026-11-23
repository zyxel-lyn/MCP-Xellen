@echo off
setlocal
title MCP Xellen
set "ROOT=D:\MCP-Bridge"
set "DC_DIR=%ROOT%\desktop-commander-local\package"
set "SG=%ROOT%\mcp-guard-stack\node_modules\supergateway\dist\index.js"
set "KEY=%ROOT%\secrets\mcp-api-key.txt"
if not exist "%KEY%" (
  echo [ERROR] MCP Bridge API key file is missing.
  pause
  exit /b 11
)
if not exist "%DC_DIR%\dist\index.js" (
  echo [ERROR] Desktop Commander package is missing.
  pause
  exit /b 12
)
echo.
echo  MCP Xellen
echo  Desktop Commander local backend
echo  Endpoint: http://127.0.0.1:8020/mcp
echo.
node "%SG%" --stdio "node %DC_DIR%\dist\index.js --no-onboarding" --outputTransport streamableHttp --port 8020 --host 127.0.0.1 --apiKeyFile "%KEY%" --stateful --sessionTimeout 1800000 --healthEndpoint /healthz
echo.
echo [INFO] MCP Xellen has stopped.
pause
