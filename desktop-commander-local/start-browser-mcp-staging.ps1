$ErrorActionPreference = 'Stop'
$root = 'D:\MCP-Bridge'
$node = 'C:\Program Files\nodejs\node.exe'
$gateway = Join-Path $root 'mcp-guard-stack\node_modules\supergateway\dist\index.js'
$token = Join-Path $root 'secrets\mcp-browser-staging-token.txt'
$browserLog = Join-Path $root 'desktop-commander-local\browser-mcp.log'
$gatewayLog = Join-Path $root 'desktop-commander-local\browser-gateway.log'

if (-not (Test-Path $token)) { throw 'Token Browser MCP tidak ditemukan.' }
if (-not (Test-Path $gateway)) { throw 'Supergateway tidak ditemukan.' }

function Test-ListeningPort([int]$port) {
  return [bool](Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue)
}

if (-not (Test-ListeningPort 8089)) {
  $browserCommand = 'npx -y @agent-infra/mcp-server-browser@latest --headless --host 127.0.0.1 --port 8089 > "' + $browserLog + '" 2>&1'
  Start-Process -FilePath 'cmd.exe' -ArgumentList @('/c', $browserCommand) -WindowStyle Hidden
  $ready = $false
  for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    if (Test-ListeningPort 8089) { $ready = $true; break }
  }
  if (-not $ready) { throw 'Browser MCP gagal listen pada port 8089. Periksa browser-mcp.log.' }
}

if (-not (Test-ListeningPort 8025)) {
  $gatewayCommand = '"' + $node + '" "' + $gateway + '" --stdio "npx -y mcp-remote http://127.0.0.1:8089/mcp" --outputTransport streamableHttp --port 8025 --host 127.0.0.1 --apiKeyFile "' + $token + '" --stateful --sessionTimeout 1800000 --healthEndpoint /healthz > "' + $gatewayLog + '" 2>&1'
  Start-Process -FilePath 'cmd.exe' -ArgumentList @('/c', $gatewayCommand) -WorkingDirectory $root -WindowStyle Hidden
  $ready = $false
  for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    try {
      $response = Invoke-WebRequest -Uri 'http://127.0.0.1:8025/healthz' -TimeoutSec 2
      if ($response.StatusCode -eq 200) { $ready = $true; break }
    } catch {}
  }
  if (-not $ready) { throw 'Browser MCP gateway gagal listen pada port 8025. Periksa browser-gateway.log.' }
}

Write-Output 'Browser MCP siap.'
Write-Output 'Local endpoint: http://127.0.0.1:8025/mcp (Bearer token required)'
Write-Output 'Z.ai endpoint: https://laptop-see8pp9p.tail943ca4.ts.net:8443/browser/mcp'
