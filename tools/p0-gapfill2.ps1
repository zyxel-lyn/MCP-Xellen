$ErrorActionPreference = "SilentlyContinue"
Write-Output "=== P0 GAP-FILL 2: SIAPA MENJAWAB 8025 $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ==="

Write-Output "--- healthz 8025 (timestamped) ---"
try {
    $r = Invoke-WebRequest -Uri "http://127.0.0.1:8025/healthz" -TimeoutSec 3 -UseBasicParsing
    Write-Output ("status=" + $r.StatusCode + " server=" + $r.Headers["Server"] + " body=" + $r.Content.Substring(0, [Math]::Min(60, $r.Content.Length)))
} catch { Write-Output ("healthz 8025 gagal: " + $_.Exception.Message) }

Write-Output "--- Get-NetTCPConnection :8025 (semua state + owning process) ---"
$c = Get-NetTCPConnection -LocalPort 8025 -ErrorAction SilentlyContinue
if ($c) {
    foreach ($x in $c) {
        $proc = Get-CimInstance Win32_Process -Filter ("ProcessId=" + $x.OwningProcess) -ErrorAction SilentlyContinue
        $cmd = ""
        if ($proc -and $proc.CommandLine) { $cmd = $proc.CommandLine; if ($cmd.Length -gt 200) { $cmd = $cmd.Substring(0, 200) } }
        Write-Output ("  " + $x.State + " local=" + $x.LocalAddress + ":" + $x.LocalPort + " remote=" + $x.RemoteAddress + ":" + $x.RemotePort + " ownerPID=" + $x.OwningProcess + " [" + $(if ($proc) { $proc.Name } else { "?" }) + "] cmd=" + $cmd)
    }
} else {
    Write-Output "  TIDAK ADA koneksi/socket pada port 8025 saat ini"
}

Write-Output "--- system proxy (dapat menjelaskan healthz 200 palsu) ---"
$px = Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings"
Write-Output ("ProxyEnable=" + $px.ProxyEnable + "  ProxyServer=" + $px.ProxyServer + "  AutoConfigURL=" + $px.AutoConfigURL)

Write-Output "--- supergateway proses (semua, cmdline 260 char) ---"
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like "*supergateway*" } | ForEach-Object {
    $c = $_.CommandLine
    if ($c.Length -gt 260) { $c = $c.Substring(0, 260) }
    Write-Output ("  PID=" + $_.ProcessId + " start=" + $_.CreationDate.ToString("MM-dd HH:mm") + " " + $c)
}

Write-Output "--- healthz 8023 ulang (timestamped) ---"
try {
    $r = Invoke-WebRequest -Uri "http://127.0.0.1:8023/healthz" -TimeoutSec 3 -UseBasicParsing
    Write-Output ("status=" + $r.StatusCode)
} catch { Write-Output ("8023 gagal: " + $_.Exception.Message) }

Write-Output "=== GAP-FILL 2 SELESAI ==="
