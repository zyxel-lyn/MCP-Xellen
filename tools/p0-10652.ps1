$ErrorActionPreference = "SilentlyContinue"
Write-Output "=== PID 10652 IDENTIFICATION $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ==="
$p = Get-Process -Id 10652 -ErrorAction SilentlyContinue
if ($p) {
    Write-Output ("PID 10652 Path  : " + $p.Path)
    Write-Output ("PID 10652 Start : " + $p.StartTime.ToString("yyyy-MM-dd HH:mm:ss"))
    Write-Output ("PID 10652 RAM   : " + [math]::Round($p.WorkingSet64/1MB,1) + " MB")
} else {
    Write-Output "PID 10652 sudah tidak ada"
}
$par = Get-CimInstance Win32_Process -Filter "ProcessId=10652" -ErrorAction SilentlyContinue
if ($par) {
    Write-Output ("ParentPID       : " + $par.ParentProcessId)
    $pp = Get-CimInstance Win32_Process -Filter ("ProcessId=" + $par.ParentProcessId) -ErrorAction SilentlyContinue
    if ($pp) { Write-Output ("Parent          : " + $pp.Name + " (" + $pp.ProcessId + ")") }
}
Write-Output "--- semua listener terkait Xellen ports ---"
Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object { $_.LocalPort -in @(8023,8024,8025,8089,8300,8399,9010,10000) } | ForEach-Object {
    $pn = (Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName
    $pp = (Get-CimInstance Win32_Process -Filter ("ProcessId=" + $_.OwningProcess) -ErrorAction SilentlyContinue).CommandLine
    if ($pp -and $pp.Length -gt 140) { $pp = $pp.Substring(0,140) }
    Write-Output ("  " + $_.LocalAddress + ":" + $_.LocalPort + "  ownerPID=" + $_.OwningProcess + " [" + $pn + "]  " + $pp)
}
Write-Output "=== SELESAI ==="
