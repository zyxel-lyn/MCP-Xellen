$ErrorActionPreference = "SilentlyContinue"
Write-Output "=== P0 SECURITY & REMOTE ACCESS ASSESSMENT $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ==="

Write-Output ""
Write-Output "=== [S1] RUNTIME BASELINE ==="
$os = Get-CimInstance Win32_OperatingSystem
Write-Output ("last_boot       : " + $os.LastBootUpTime)
Write-Output ("now             : " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
$tv = & "C:\Program Files\Tailscale\tailscale.exe" version 2>&1 | Select-Object -First 1
Write-Output ("tailscale_ver   : " + $tv)
$ts = Get-Service -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*ailscale*" }
foreach ($s in $ts) { Write-Output ("service         : " + $s.Name + " = " + $s.Status) }
$tsd = Get-CimInstance Win32_Process | Where-Object { $_.Name -like "tailscaled*" }
foreach ($p in $tsd) { Write-Output ("tailscaled PID  : " + $p.ProcessId + "  start=" + $p.CreationDate) }

Write-Output ""
Write-Output "=== [S2] PROCESSES OF INTEREST ==="
$pat = @(
    @{N="launcher-publish-direct"; L="*publish-direct*MCP-Xellen.exe*"},
    @{N="launcher-publish-p1"; L="*publish-p1*MCP-Xellen.exe*"},
    @{N="supergateway-8023"; L="*supergateway*--port 8023*"},
    @{N="supergateway-8025"; L="*supergateway*--port 8025*"},
    @{N="supergateway-8024"; L="*supergateway*--port 8024*"},
    @{N="browsermcp-daemon-9010"; L="*@browsermcp/mcp*"},
    @{N="agent-infra-8089"; L="*@agent-infra/mcp-server-browser*"},
    @{N="dc-child"; L="*desktop-commander-local\package\dist\index.js*"}
)
foreach ($x in $pat) {
    $procs = Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like $x.L }
    Write-Output ($x.N + " : count=" + @($procs).Count + "  pids=" + (($procs | ForEach-Object { $_.ProcessId }) -join ","))
}
$dc = Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like "*desktop-commander-local\package\dist\index.js*" -and $_.CommandLine -notlike "*supergateway*" }
Write-Output ("dc-children (non-sg) : " + @($dc).Count)

Write-Output ""
Write-Output "=== [S3] LISTENING PORTS (binding address + owning process) ==="
foreach ($port in 8023, 8024, 8025, 8089, 8300, 8399, 9010, 8443, 10000) {
    $conns = netstat -ano | Select-String (":" + $port + "\s")
    if ($conns) {
        foreach ($c in $conns) {
            $line = $c.ToString().Trim()
            $parts = $line -split "\s+"
            $ownPid = $parts[-1]
            $procName = ""
            $pp = Get-CimInstance Win32_Process -Filter "ProcessId=$ownPid" -ErrorAction SilentlyContinue
            if ($pp) { $procName = $pp.Name }
            Write-Output ("port " + $port + " : " + $line + "  [" + $procName + "]")
        }
    } else {
        Write-Output ("port " + $port + " : NOT LISTENING")
    }
}

Write-Output ""
Write-Output "=== [S4] HEALTH LADDER (service alive) ==="
foreach ($port in 8023, 8024, 8025, 8089) {
    try {
        $r = Invoke-WebRequest -Uri ("http://127.0.0.1:" + $port + "/healthz") -TimeoutSec 3 -UseBasicParsing
        Write-Output ("port " + $port + " healthz -> " + $r.StatusCode)
    } catch {
        $code = ""
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        Write-Output ("port " + $port + " healthz -> " + $(if ($code) { $code } else { "UNREACHABLE" }))
    }
}

Write-Output ""
Write-Output "=== [S5] MCP LADDER 8023 (production compat DC) ==="
$tokFile = "D:\MCP-Bridge\secrets\mcp-xellen-staging-token.txt"
if (Test-Path $tokFile) {
    $tok = (Get-Content $tokFile -Raw).Trim()
    Write-Output ("token file exists : True  length=" + $tok.Length + " (nilai tidak dicetak)")
    $hdr = @{ Authorization = "Bearer " + $tok; Accept = "application/json, text/event-stream"; "MCP-Protocol-Version" = "2025-06-18" }
    $body = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"p0-audit","version":"0"}}}'
    try {
        $r = Invoke-WebRequest -Uri "http://127.0.0.1:8023/mcp" -Method Post -Headers $hdr -ContentType "application/json" -Body $body -TimeoutSec 15 -UseBasicParsing
        $sid = $r.Headers["Mcp-Session-Id"]
        Write-Output ("initialize -> HTTP " + $r.StatusCode + "  sessionPresent=" + [bool]$sid)
        $res = $r.Content | ConvertFrom-Json
        Write-Output ("protocolVersion  : " + $res.result.protocolVersion)
        Write-Output ("serverInfo.name  : " + $res.result.serverInfo.name)
        Write-Output ("serverInfo.version: " + $res.result.serverInfo.version)
        $hdr2 = $hdr.Clone()
        if ($sid) { $hdr2["Mcp-Session-Id"] = $sid }
        $r2 = Invoke-WebRequest -Uri "http://127.0.0.1:8023/mcp" -Method Post -Headers $hdr2 -ContentType "application/json" -Body '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' -TimeoutSec 15 -UseBasicParsing
        $res2 = $r2.Content | ConvertFrom-Json
        $tools = $res2.result.tools
        Write-Output ("tools/list -> HTTP " + $r2.StatusCode + "  count=" + @($tools).Count)
        Write-Output ("first tools      : " + ((@($tools) | Select-Object -First 8 | ForEach-Object { $_.name }) -join ", "))
        Write-Output ("tools/call       : SKIPPED (butuh tool aman yang disetujui owner; tidak dipanggil pada production)")
    } catch {
        Write-Output ("MCP ladder 8023 ERROR: " + $_.Exception.Message)
    }
    $negH = @{ Accept = "application/json" }
    try {
        Invoke-WebRequest -Uri "http://127.0.0.1:8023/mcp" -Method Post -Headers $negH -ContentType "application/json" -Body '{"jsonrpc":"2.0","id":9,"method":"tools/list"}' -TimeoutSec 5 -UseBasicParsing | Out-Null
        Write-Output ("no-auth probe    : UNEXPECTED SUCCESS")
    } catch {
        $code = ""
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        Write-Output ("no-auth probe    : HTTP " + $code + " (401 = auth aktif)")
    }
} else {
    Write-Output "token file tidak ada: $tokFile"
}

Write-Output ""
Write-Output "=== [S6] MCP LADDER 8025 (browser gateway compat) ==="
$tokFileB = "D:\MCP-Bridge\secrets\mcp-browser-staging-token.txt"
if (Test-Path $tokFileB) {
    $tokB = (Get-Content $tokFileB -Raw).Trim()
    $hdrB = @{ Authorization = "Bearer " + $tokB; Accept = "application/json, text/event-stream" }
    $body = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"p0-audit","version":"0"}}}'
    try {
        $r = Invoke-WebRequest -Uri "http://127.0.0.1:8025/mcp" -Method Post -Headers $hdrB -ContentType "application/json" -Body $body -TimeoutSec 15 -UseBasicParsing
        $sid = $r.Headers["Mcp-Session-Id"]
        Write-Output ("initialize -> HTTP " + $r.StatusCode + "  sessionPresent=" + [bool]$sid)
        $res = $r.Content | ConvertFrom-Json
        Write-Output ("serverInfo.name  : " + $res.result.serverInfo.name)
        $hdr2 = $hdrB.Clone()
        if ($sid) { $hdr2["Mcp-Session-Id"] = $sid }
        $r2 = Invoke-WebRequest -Uri "http://127.0.0.1:8025/mcp" -Method Post -Headers $hdr2 -ContentType "application/json" -Body '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' -TimeoutSec 15 -UseBasicParsing
        $res2 = $r2.Content | ConvertFrom-Json
        $tools = $res2.result.tools
        Write-Output ("tools/list -> HTTP " + $r2.StatusCode + "  count=" + @($tools).Count)
        Write-Output ("first tools      : " + ((@($tools) | Select-Object -First 8 | ForEach-Object { $_.name }) -join ", "))
    } catch {
        Write-Output ("MCP ladder 8025 ERROR: " + $_.Exception.Message)
    }
} else {
    Write-Output "token file browser tidak ada: $tokFileB"
}

Write-Output ""
Write-Output "=== [S7] MCP LADDER 8024 (construct3) ==="
$tokFileC = "D:\MCP-Bridge\secrets\mcp-construct3-shortshift-token.txt"
if (Test-Path $tokFileC) {
    $tokC = (Get-Content $tokFileC -Raw).Trim()
    $hdrC = @{ Authorization = "Bearer " + $tokC; Accept = "application/json, text/event-stream" }
    $body = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"p0-audit","version":"0"}}}'
    try {
        $r = Invoke-WebRequest -Uri "http://127.0.0.1:8024/mcp" -Method Post -Headers $hdrC -ContentType "application/json" -Body $body -TimeoutSec 15 -UseBasicParsing
        $sid = $r.Headers["Mcp-Session-Id"]
        Write-Output ("initialize -> HTTP " + $r.StatusCode + "  sessionPresent=" + [bool]$sid)
        $res = $r.Content | ConvertFrom-Json
        Write-Output ("serverInfo.name  : " + $res.result.serverInfo.name)
        $hdr2 = $hdrC.Clone()
        if ($sid) { $hdr2["Mcp-Session-Id"] = $sid }
        $r2 = Invoke-WebRequest -Uri "http://127.0.0.1:8024/mcp" -Method Post -Headers $hdr2 -ContentType "application/json" -Body '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' -TimeoutSec 15 -UseBasicParsing
        $res2 = $r2.Content | ConvertFrom-Json
        $tools = $res2.result.tools
        Write-Output ("tools/list -> HTTP " + $r2.StatusCode + "  count=" + @($tools).Count)
        Write-Output ("first tools      : " + ((@($tools) | Select-Object -First 8 | ForEach-Object { $_.name }) -join ", "))
    } catch {
        Write-Output ("MCP ladder 8024 ERROR: " + $_.Exception.Message)
    }
} else {
    Write-Output "token construct3 tidak ada: $tokFileC"
}

Write-Output ""
Write-Output "=== [S8] REMOTE ENDPOINTS (dari perangkat ini, read-only) ==="
$hostName = "laptop-see8pp9p.tail943ca4.ts.net"
foreach ($u in @("https://" + $hostName + ":8443/mcp", "https://" + $hostName + ":10000/mcp", "https://" + $hostName + ":443/mcp")) {
    try {
        $r = Invoke-WebRequest -Uri $u -Method Post -ContentType "application/json" -Body '{"jsonrpc":"2.0","id":1,"method":"ping"}' -TimeoutSec 8 -UseBasicParsing
        Write-Output ($u + " -> HTTP " + $r.StatusCode)
    } catch {
        $code = ""
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        Write-Output ($u + " -> " + $(if ($code) { "HTTP " + $code } else { "UNREACHABLE (" + $_.Exception.Message.Substring(0, [Math]::Min(60, $_.Exception.Message.Length)) + ")" }))
    }
}

Write-Output ""
Write-Output "=== [S9] TAILSCALE SERVE/FUNNEL STATUS ==="
Write-Output ("--- funnel status ---")
& "C:\Program Files\Tailscale\tailscale.exe" funnel status 2>&1 | ForEach-Object { Write-Output ("  " + $_) }
Write-Output ("--- serve status ---")
& "C:\Program Files\Tailscale\tailscale.exe" serve status 2>&1 | ForEach-Object { Write-Output ("  " + $_) }
Write-Output ("--- serve status json (raw keys only) ---")
$sv = & "C:\Program Files\Tailscale\tailscale.exe" serve status --json 2>&1 | Out-String
try {
    $j = $sv | ConvertFrom-Json
    if ($j.ServeConfig) { Write-Output ("ServeConfig keys: " + (($j.ServeConfig.PSObject.Properties | ForEach-Object { $_.Name }) -join ", ")) }
} catch { Write-Output ("serve status --json parse: tidak tersedia di versi ini") }

Write-Output ""
Write-Output "=== [S10] TOKEN & SECRET INVENTORY (nama/ukuran saja, tanpa nilai) ==="
Get-ChildItem "D:\MCP-Bridge\secrets" -File | ForEach-Object {
    Write-Output ("  " + $_.Name + "  size=" + $_.Length + "  mtime=" + $_.LastWriteTime.ToString("MM-dd HH:mm"))
}
if (Test-Path "D:\MCP-Bridge\config\auth.json") {
    $a = Get-Content "D:\MCP-Bridge\config\auth.json" -Raw | ConvertFrom-Json
    Write-Output ("config\auth.json identities: " + @($a).Count)
    foreach ($x in $a) {
        $hl = $(if ($x.TokenHash) { $x.TokenHash.Length } else { 0 })
        Write-Output ("  id=" + $x.Id + " kind=" + $x.Kind + " tokenHashLen=" + $hl + " revoked=" + [bool]$x.RevokedUtc + " scopes=[" + ($x.Scopes -join ",") + "]")
    }
} else { Write-Output "config\auth.json: tidak ada" }
if (Test-Path "D:\MCP-Bridge\probe2.json") {
    $p2 = Get-Content "D:\MCP-Bridge\probe2.json" -Raw | ConvertFrom-Json
    $tk = $p2.daemon.files."daemon.json".token
    Write-Output ("probe2.json leaked-token field: present=" + [bool]$tk + " length=" + $(if ($tk) { $tk.Length } else { 0 }) + " (nilai tidak dicetak)")
}
if (Test-Path "C:\Users\user\.browsermcp\daemon.json") {
    $d = Get-Content "C:\Users\user\.browsermcp\daemon.json" -Raw | ConvertFrom-Json
    Write-Output ("browsermcp daemon.json: port=" + $d.port + " pid=" + $d.pid + " version=" + $d.version + " tokenLen=" + $(if ($d.token) { $d.token.Length } else { 0 }))
}
if (Test-Path "C:\Users\user\.browsermcp\identity.json") {
    $idn = Get-Content "C:\Users\user\.browsermcp\identity.json" -Raw | ConvertFrom-Json
    Write-Output ("browsermcp identity.json: publicKeyLen=" + $(if ($idn.publicKey) { $idn.publicKey.Length } else { 0 }) + " privateKeyLen=" + $(if ($idn.privateKey) { $idn.privateKey.Length } else { 0 }))
}

Write-Output ""
Write-Output "=== [S11] CONFIG NATIVE TRANSPORT (production state) ==="
$xc = Get-Content "D:\MCP-Bridge\config\xellen.json" -Raw | ConvertFrom-Json
Write-Output ("schemaVersion       : " + $xc.SchemaVersion)
Write-Output ("nativeTransport.enabled : " + $xc.NativeTransport.enabled)
Write-Output ("nativeTransport.port    : " + $xc.NativeTransport.preferredPort)
Write-Output ("servers in registry     : " + @($xc.Servers).Count + "  ids=" + (($xc.Servers | ForEach-Object { $_.Id }) -join ","))
Write-Output ("projects in registry    : " + @($xc.Projects).Count)

Write-Output ""
Write-Output "=== [S12] BROWSER STACK SECURITY EVIDENCE ==="
$procList = Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like "*browsermcp*" -or $_.CommandLine -like "*agent-infra*" }
foreach ($p in $procList) {
    $cl = $p.CommandLine
    if ($cl.Length -gt 200) { $cl = $cl.Substring(0, 200) }
    Write-Output ("PID=" + $p.ProcessId + " " + $p.Name + " start=" + $p.CreationDate.ToString("MM-dd HH:mm") + " cmd=" + $cl)
}
Write-Output "--- binding 9010/8089 (listen address) ---"
netstat -ano | Select-String ":9010\s|:8089\s" | ForEach-Object { Write-Output ("  " + $_.ToString().Trim()) }

Write-Output ""
Write-Output "=== [S13] EVENT LOG TAILSCALE (3 hari) ==="
$ev = Get-WinEvent -FilterHashtable @{ LogName = "System"; StartTime = (Get-Date).AddDays(-3) } -ErrorAction SilentlyContinue | Where-Object { $_.ProviderName -like "*ailscale*" -or $_.Message -like "*ailscale*" } | Select-Object -First 12
if ($ev) {
    foreach ($e in $ev) { Write-Output ("  " + $e.TimeCreated.ToString("MM-dd HH:mm") + " [" + $e.ProviderName + "] " + $e.Message.Substring(0, [Math]::Min(110, $e.Message.Length))) }
} else {
    Write-Output "  (tidak ada event Tailscale di System log 3 hari terakhir)"
}

Write-Output ""
Write-Output "=== [S14] Z.AI / XELLEN WORKSPACE KEYS (config only) ==="
Write-Output ("zai-work dir ada: " + (Test-Path "D:\MCP-Bridge\.zai-work"))
Write-Output ("edge profile: tidak dibaca (user data, di luar scope)")

Write-Output ""
Write-Output "=== ASSESSMENT DATA COLLECTION SELESAI $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ==="
