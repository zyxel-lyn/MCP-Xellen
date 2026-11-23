$ErrorActionPreference = "SilentlyContinue"
Write-Output "=== P0-PREP EVIDENCE $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ==="

Write-Output "--- [A] daemon 9010 owner + tree ---"
$l = Get-NetTCPConnection -LocalPort 9010 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($l) {
    $op = $l.OwningProcess
    Write-Output ("listener PID: " + $op)
    $p = Get-CimInstance Win32_Process -Filter ("ProcessId=" + $op)
    if ($p) {
        $c = $p.CommandLine
        if ($c -and $c.Length -gt 200) { $c = $c.Substring(0, 200) }
        Write-Output ("path  : " + $c)
        Write-Output ("start : " + $p.CreationDate.ToString("yyyy-MM-dd HH:mm:ss"))
        $par = Get-CimInstance Win32_Process -Filter ("ProcessId=" + $p.ParentProcessId) -ErrorAction SilentlyContinue
        if ($par) {
            $pc = $par.CommandLine
            if ($pc -and $pc.Length -gt 160) { $pc = $pc.Substring(0, 160) }
            Write-Output ("parent: " + $par.ProcessId + " " + $par.Name + " :: " + $pc)
            $gp = Get-CimInstance Win32_Process -Filter ("ProcessId=" + $par.ParentProcessId) -ErrorAction SilentlyContinue
            if ($gp) { Write-Output ("grandparent: " + $gp.ProcessId + " " + $gp.Name) }
        }
    }
}

Write-Output "--- [B] konsumen 9010 (ESTABLISHED) ---"
Get-NetTCPConnection -LocalPort 9010 -State Established -ErrorAction SilentlyContinue | ForEach-Object {
    $pp = Get-CimInstance Win32_Process -Filter ("ProcessId=" + $_.OwningProcess) -ErrorAction SilentlyContinue
    $nm = "?"
    if ($pp) { $nm = $pp.Name }
    $pc = ""
    if ($pp -and $pp.CommandLine) { $pc = $pp.CommandLine; if ($pc.Length -gt 120) { $pc = $pc.Substring(0, 120) } }
    Write-Output ("  remote=" + $_.RemoteAddress + ":" + $_.RemotePort + "  ownerPID=" + $_.OwningProcess + " [" + $nm + "] " + $pc)
}

Write-Output "--- [C] browsermcp daemon.log tail (token dimasking) ---"
$dl = "C:\Users\user\.browsermcp\daemon.log"
if (Test-Path $dl) {
    Get-Content $dl -Tail 12 | ForEach-Object {
        $x = $_ -replace "token=[^&\s]+", "token=[MASKED]"
        if ($x.Length -gt 180) { $x = $x.Substring(0, 180) }
        Write-Output ("  " + $x)
    }
}

Write-Output "--- [D] Hermes config merujuk browsermcp/9010 ---"
$cands = New-Object System.Collections.Generic.List[string]
$cands.Add("C:\Users\user\AppData\Local\hermes")
foreach ($sub in @("state", "platforms", "bin")) {
    $d = Join-Path "C:\Users\user\AppData\Local\hermes" $sub
    if (Test-Path $d) { $cands.Add($d) }
}
$hits = 0
$checked = 0
foreach ($f in (Get-ChildItem $cands -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -in @(".json", ".mjs", ".js", ".toml", ".yaml", ".env", ".txt", ".md") -and $_.Length -lt 2000000 } | Select-Object -First 400)) {
    $checked++
    $c = Get-Content $f.FullName -Raw -ErrorAction SilentlyContinue
    if ($c -and ($c.Contains("browsermcp") -or $c.Contains(":9010"))) {
        Write-Output ("  REF: " + $f.FullName)
        $hits++
    }
}
Write-Output ("hermes refs: " + $hits + " (checked " + $checked + " files)")

Write-Output "--- [E] Chrome extensions dir (keberadaan saja) ---"
$extDir = "C:\Users\user\AppData\Local\Google\Chrome\User Data\Default\Extensions"
Write-Output ("chrome extensions dir reachable: " + (Test-Path $extDir) + " (isi TIDAK dibaca)")

Write-Output "--- [F] Xellen source referensi 9010/browsermcp ---"
Get-ChildItem "D:\MCP-Bridge\desktop-commander-local\app" -Recurse -Filter "*.cs" -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notlike "*obj*" } | ForEach-Object {
    $c = Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue
    if ($c -and ($c.Contains("browsermcp") -or $c.Contains(":9010"))) { Write-Output ("  REF: " + $_.FullName) }
}

Write-Output "--- [G] supergateway apiKey mechanism (multi-key?) ---"
$akFiles = Get-ChildItem "D:\MCP-Bridge\mcp-guard-stack\node_modules\supergateway\dist" -Recurse -File -Filter "*.js" -ErrorAction SilentlyContinue | Where-Object { (Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue).Contains("apiKeyFile") }
foreach ($f in $akFiles) { Write-Output ("  file contains apiKeyFile: " + $f.FullName) }
foreach ($f in $akFiles) {
    $c = Get-Content $f.FullName -Raw
    $ms = [regex]::Matches($c, ".{0,80}apiKeyFile.{0,200}")
    $i = 0
    foreach ($x in $ms) {
        if ($i -ge 3) { break }
        Write-Output ("  [" + $f.Name + " @" + $x.Index + "] " + $x.Value)
        $i++
    }
}

Write-Output "--- [H] stray publish-p1 detail ---"
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like "*publish-p1*MCP-Xellen.exe*" } | ForEach-Object {
    $par = Get-CimInstance Win32_Process -Filter ("ProcessId=" + $_.ParentProcessId) -ErrorAction SilentlyContinue
    $pn = "dead"
    if ($par) { $pn = $par.Name }
    $ram = [math]::Round($_.WorkingSetSize/1MB, 1)
    Write-Output ("  PID=" + $_.ProcessId + " start=" + $_.CreationDate.ToString("MM-dd HH:mm") + " parent=" + $_.ParentProcessId + "(" + $pn + ") RAM=" + $ram + "MB")
}

Write-Output "--- [I] PID 2880 detail + children ---"
$z = Get-CimInstance Win32_Process -Filter "ProcessId=2880" -ErrorAction SilentlyContinue
if ($z) {
    Write-Output ("  PID=2880 start=" + $z.CreationDate.ToString("MM-dd HH:mm") + " parent=" + $z.ParentProcessId)
    $zp = Get-CimInstance Win32_Process -Filter ("ProcessId=" + $z.ParentProcessId) -ErrorAction SilentlyContinue
    if ($zp) { Write-Output ("  parent: " + $zp.Name + " (" + $zp.ProcessId + ")") } else { Write-Output "  parent: DEAD" }
    $kids = Get-CimInstance Win32_Process | Where-Object { $_.ParentProcessId -eq 2880 }
    Write-Output ("  children: " + @($kids).Count + "  pids=" + (($kids | ForEach-Object { $_.ProcessId }) -join ","))
} else {
    Write-Output "  PID 2880 GONE"
}

Write-Output "--- [J] launcher + gateway saat ini ---"
Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like "*publish-direct*MCP-Xellen.exe*" -or $_.CommandLine -like "*supergateway*--port 8023*" } | ForEach-Object {
    $c = $_.CommandLine
    if ($c.Length -gt 150) { $c = $c.Substring(0, 150) }
    Write-Output ("  PID=" + $_.ProcessId + " " + $_.Name + " start=" + $_.CreationDate.ToString("MM-dd HH:mm") + " " + $c)
}

Write-Output "--- [K] auth.json + config state ---"
if (Test-Path "D:\MCP-Bridge\config\auth.json") {
    $a = Get-Content "D:\MCP-Bridge\config\auth.json" -Raw | ConvertFrom-Json
    Write-Output ("auth.json identities: " + @($a).Count)
} else {
    Write-Output "auth.json: tidak ada (belum ada identity native diterbitkan)"
}
$xc = Get-Content "D:\MCP-Bridge\config\xellen.json" -Raw | ConvertFrom-Json
Write-Output ("xellen.json schemaVersion=" + $xc.SchemaVersion + " nativeEnabled=" + $xc.NativeTransport.enabled + " servers=" + @($xc.Servers).Count + " projects=" + @($xc.Projects).Count)

Write-Output "=== P0-PREP SELESAI ==="
