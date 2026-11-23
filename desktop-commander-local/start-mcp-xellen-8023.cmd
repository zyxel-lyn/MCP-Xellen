@echo off
title MCP Xellen staging 8023
set "ROOT=D:\MCP-Bridge"
set "DC_DIR=%ROOT%\desktop-commander-local\package"
set "SG=%ROOT%\mcp-guard-stack\node_modules\supergateway\dist\index.js"
set "KEY=%ROOT%\secrets\mcp-xellen-staging-token.txt"
netstat -ano | findstr /C:":8023 " | findstr LISTENING >nul
if %ERRORLEVEL%==0 exit /b 0
node "%SG%" --stdio "node %DC_DIR%\dist\index.js --no-onboarding" --outputTransport streamableHttp --port 8023 --host 127.0.0.1 --apiKeyFile "%KEY%" --stateful --sessionTimeout 1800000 --healthEndpoint /healthz
