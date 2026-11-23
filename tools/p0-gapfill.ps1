$ErrorActionPreference = "SilentlyContinue"
Write-Output "=== P0 GAP-FILL EVIDENCE $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ==="

Write-Output "--- PID 2880 (pemilik socket 8089) ---"
$p = Get-CimInstance Win32_Process -Filter "ProcessId=2880" -ErrorAction SilentlyContinue
if ($p) {
    $c = $p.CommandLine
    if ($c.Length -gt 220) { $c = $c.Substring(0, 220) }
    Write-Output ("alive: " + $p.Name + " start=" + $p.CreationDate.ToString("MM-dd HH:mm") + " cmd=" + $c)
} else {
    Write-Output "PID 2880 GONE (proses sudah tidak ada)"
}

Write-Output "--- semua socket 8025 ---"
netstat -ano | Select-String ":8025" | ForEach-Object { Write-Output ("  " + $_.ToString().Trim()) }

Write-Output "--- semua proses supergateway (cmdline max 240) ---"
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like "*supergateway*" } | ForEach-Object {
    $c = $_.CommandLine
    if ($c.Length -gt 240) { $c = $c.Substring(0, 240) }
    Write-Output ("  PID=" + $_.ProcessId + " start=" + $_.CreationDate.ToString("MM-dd HH:mm") + " " + $c)
}

Write-Output "--- socket 8024 (semua state) ---"
netstat -ano | Select-String ":8024" | ForEach-Object { Write-Output ("  " + $_.ToString().Trim()) }

Write-Output "--- hermes dir ---"
Get-ChildItem "C:\Users\user\AppData\Local\hermes" -Directory -ErrorAction SilentlyContinue | ForEach-Object { Write-Output ("  " + $_.Name) }
$hm = Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like "*hermes*" }
Write-Output ("hermes processes: " + @($hm).Count)
$hm | Select-Object -First 8 | ForEach-Object {
    $c = $_.CommandLine
    if ($c.Length -gt 160) { $c = $c.Substring(0, 160) }
    Write-Output ("  PID=" + $_.ProcessId + " " + $_.Name + " " + $c)
}

Write-Output "--- tailscale debug prefs (ServeConfig) ---"
$pr = & "C:\Program Files\Tailscale\tailscale.exe" debug prefs 2>&1 | Out-String
$hit = [regex]::Matches($pr, "ServeConfig[^\r\n]{0,200}")
if ($hit.Count -gt 0) {
    foreach ($x in $hit) { Write-Output ("  " + $x.Value) }
} else {
    Write-Output "  ServeConfig tidak ditemukan di prefs (konsisten: No serve config)"
}

Write-Output "--- tailscaled logs dir (3 hari) ---"
$ld = "C:\ProgramData\Tailscale"
if (Test-Path $ld) {
    $lf = Get-ChildItem $ld -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -gt (Get-Date).AddDays(-3) } | Sort-Object LastWriteTime -Descending | Select-Object -First 6
    foreach ($x in $lf) { Write-Output ("  " + $x.FullName + " (" + $x.Length + "B, " + $x.LastWriteTime.ToString("MM-dd HH:mm") + ")") }
    if (-not $lf) { Write-Output "  tidak ada file log 3 hari terakhir" }
} else {
    Write-Output "  tidak ada C:\ProgramData\Tailscale"
}

Write-Output "=== GAP-FILL SELESAI ==="
