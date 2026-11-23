$ErrorActionPreference = 'Stop'

$TokenFile = 'D:\MCP-Bridge\secrets\mcp-approval-ui-token.txt'
$Token = (Get-Content -LiteralPath $TokenFile -Raw).Trim()
if ($Token.Length -lt 32) { throw 'Approval token invalid.' }

$GuardProcesses = Get-CimInstance Win32_Process | Where-Object {
    $_.Name -eq 'node.exe' -and $_.CommandLine -match 'mcp-guard'
}

$Listeners = foreach ($Guard in $GuardProcesses) {
    $Connections = Get-NetTCPConnection -State Listen -OwningProcess $Guard.ProcessId -ErrorAction SilentlyContinue
    foreach ($Connection in $Connections) {
        if ($Connection.LocalAddress -eq '127.0.0.1') {
            [PSCustomObject]@{
                Port = $Connection.LocalPort
                Started = $Guard.CreationDate
            }
        }
    }
}

$Dashboard = $Listeners | Sort-Object Started -Descending | Select-Object -First 1
if (-not $Dashboard) {
    throw 'Approval listener tidak ditemukan. Pastikan start-aceh-camera-bridge.cmd sudah berjalan dan MCP Guard berhasil start.'
}

Write-Host ('Membuka dashboard approval lokal pada port ' + $Dashboard.Port)
Start-Process ('http://127.0.0.1:' + $Dashboard.Port + '/#t=' + $Token)
