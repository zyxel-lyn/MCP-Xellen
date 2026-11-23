$ErrorActionPreference = "Stop"
$BridgeRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Node = "C:\Program Files\nodejs\node.exe"
$GuardCli = Join-Path $BridgeRoot "node_modules\@permission-protocol\mcp-guard\bin\mcp-guard"
$GatewayCli = Join-Path $BridgeRoot "node_modules\supergateway\dist\index.js"
$Config = Join-Path $BridgeRoot "pp.config.yaml"
$Backend = Join-Path $BridgeRoot "approval-test-server.mjs"
$ApiKeyFile = "D:\MCP-Bridge\secrets\mcp-api-key.txt"
$ApprovalTokenFile = "D:\MCP-Bridge\secrets\mcp-approval-ui-token.txt"

if (-not (Test-Path -LiteralPath $ApiKeyFile -PathType Leaf)) { throw "MCP API key is missing." }
if (-not (Test-Path -LiteralPath $ApprovalTokenFile -PathType Leaf)) { throw "Approval UI token is missing." }
$env:PP_APPROVAL_TOKEN = (Get-Content -LiteralPath $ApprovalTokenFile -Raw).Trim()
if ($env:PP_APPROVAL_TOKEN.Length -lt 32) { throw "Approval token is invalid." }

$GuardCommand = '"' + $Node + '" "' + $GuardCli + '" --config "' + $Config + '" --agent-id zai-web --operator local-operator --approval-port 3101 -- "' + $Node + '" "' + $Backend + '"'

Push-Location $BridgeRoot
try {
    Write-Host "Starting approval test on 127.0.0.1:8021/mcp"
    & $Node $GatewayCli --stdio $GuardCommand --outputTransport streamableHttp --port 8021 --host 127.0.0.1 --apiKeyFile $ApiKeyFile --stateful --sessionTimeout 1800000 --tools approval_test --healthEndpoint /healthz
}
finally {
    Pop-Location
}
