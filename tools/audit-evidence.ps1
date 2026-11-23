$ErrorActionPreference = "SilentlyContinue"
Write-Output "=== INDEPENDENT EVIDENCE COLLECTION $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ==="

Write-Output ""
Write-Output "=== [1] SELFTEST RESULTS (freshness + counters) ==="
$rj = "D:\MCP-Bridge\tests\p1\results.json"
$fi = Get-Item $rj
Write-Output ("results.json mtime : " + $fi.LastWriteTime)
Write-Output ("results.json size  : " + $fi.Length)
$j = Get-Content $rj -Raw | ConvertFrom-Json
Write-Output ("passed/failed/skipped: " + $j.passed + "/" + $j.failed + "/" + $j.skipped)
Write-Output ("finishedUtc        : " + $j.finishedUtc)
Write-Output "NOTE startedUtc==finishedUtc (harness menulis keduanya di akhir run) -- timing aktual dari mtime + log"
$names = $j.results | ForEach-Object { $_.Name }
Write-Output ("total test entries : " + @($names).Count)
$t21 = $j.results | Where-Object { $_.Name -eq "T21_SweepAbortsOnFailureNoKill" }
$t22 = $j.results | Where-Object { $_.Name -eq "T22_FailureDoesNotChangeSuccessStats" }
$t23 = $j.results | Where-Object { $_.Name -eq "T23_SweepRecoversAfterWmiRecovers" }
$t24 = $j.results | Where-Object { $_.Name -eq "T24_UiStatusShowsUnknownNotClean" }
Write-Output ("T21 present+pass   : " + $t21.Pass)
Write-Output ("T22 present+pass   : " + $t22.Pass)
Write-Output ("T23 present+pass   : " + $t23.Pass)
Write-Output ("T24 present+pass   : " + $t24.Pass)
$t42 = $j.results | Where-Object { $_.Name -eq "T42_UiSmokeHeadless" }
Write-Output ("T42 skipped        : " + $t42.Skipped + "  reason: " + $t42.Details.Substring(0, [Math]::Min(80, $t42.Details.Length)))
$failedList = $j.results | Where-Object { -not $_.Pass -and -not $_.Skipped }
Write-Output ("failed entries     : " + @($failedList).Count)
$selfProc = Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like "*publish-p1*MCP-Xellen.exe*--selftest*" }
Write-Output ("selftest process still running: " + (@($selfProc).Count -gt 0))

Write-Output ""
Write-Output "=== [2] SELFTEST SOURCE vs RESULTS (jumlah metode T33-T42) ==="
$st = "D:\MCP-Bridge\desktop-commander-local\app\Xellen\SelfTest\SelfTestRunner.cs"
Write-Output ("SelfTestRunner.cs size: " + (Get-Item $st).Length)
$stRaw = Get-Content $st -Raw
Write-Output ("metode test T33-T42 dideklarasikan: " + ([regex]::Matches($stRaw, "private static void T\(33_|private static void T\(34_|private static void T\(35_|private static void T\(36_|private static void T\(37_|private static void T\(39_|private static void T\(40_|private static void T\(41_|private static void T\(42_")).Count)
Write-Output ("Check dipanggil (entries) di source: " + ([regex]::Matches($stRaw, "Check\(")).Count)

Write-Output ""
Write-Output "=== [3] GUI FIX VERIFICATION (G-1..G-5, G-11) ==="
$amf = Get-Content "D:\MCP-Bridge\desktop-commander-local\app\Xellen\UI\ActivityMonitorForm.cs" -Raw
Write-Output ("G-1 EventBus push subscribe : " + ([regex]::Matches($amf, "EventBus\.Instance\.Subscribe")).Count)
Write-Output ("G-1 BeginInvoke refresh     : " + ([regex]::Matches($amf, "BeginInvoke\(new Action\(RefreshView\)\)")).Count)
Write-Output ("G-2 selection preserved     : " + ([regex]::Matches($amf, "Guid\? selected")).Count)
Write-Output ("G-5 FillLastColumn          : " + ([regex]::Matches($amf, "FillLastColumn\(\)")).Count)
$mp = Get-Content "D:\MCP-Bridge\desktop-commander-local\app\Xellen\UI\MainForm.Projects.cs" -Raw
Write-Output ("G-4 Projects rows AutoSize  : " + ([regex]::Matches($mp, "AutoSize = true, AutoSizeMode")).Count)
Write-Output ("G-5 FillLastColumnProjects  : " + ([regex]::Matches($mp, "FillLastColumnProjects")).Count)
$cs = Get-Content "D:\MCP-Bridge\desktop-commander-local\app\McpXellen.Desktop.csproj" -Raw
Write-Output ("G-3 csproj PerMonitorV2     : " + $cs.Contains("ApplicationHighDpiMode"))
$gen = Get-ChildItem "D:\MCP-Bridge\desktop-commander-local\app\obj" -Recurse -Filter "*.editorconfig" | Select-Object -First 2
foreach ($g in $gen) {
    $c = Get-Content $g.FullName -Raw
    if ($c.Contains("ApplicationHighDpiMode")) {
        $line = ($c -split "`n") | Where-Object { $_.Contains("ApplicationHighDpiMode") } | Select-Object -First 1
        Write-Output ("G-3 generated DPI           : " + $line.Trim())
    }
}
$sp1 = Get-Content "D:\MCP-Bridge\tests\p1\spawner.cmd" -Raw
$sp2 = Get-Content "D:\MCP-Bridge\tests\p1\spawner2.cmd" -Raw
$stc2 = Get-Content $st -Raw
Write-Output ("G-11 spawner.cmd start /B   : " + ([regex]::Matches($sp1, "start /B")).Count + " (0 = window terlihat)")
Write-Output ("G-11 spawner2.cmd start /B  : " + ([regex]::Matches($sp2, "start /B")).Count)
Write-Output ("G-11 template start /B      : " + ([regex]::Matches($stc2, "start /B")).Count)

Write-Output ""
Write-Output "=== [4] AVALONIA POC VERIFICATION ==="
$apoc = "D:\MCP-Bridge\desktop-commander-local\avalonia-poc"
Get-ChildItem $apoc -File | ForEach-Object { Write-Output ("  " + $_.Name + " (" + $_.Length + "B)") }
$apc = Get-Content (Join-Path $apoc "McpXellen.AvaloniaPoc.csproj") -Raw
Write-Output ("csproj reuse Core/Transport (Compile Include): " + ([regex]::Matches($apc, "Compile Include")).Count)
Write-Output ("csproj Avalonia package refs : " + ([regex]::Matches($apc, "Avalonia")).Count)
$ht = Get-Content (Join-Path $apoc "HeadlessTest.cs") -Raw
Write-Output ("HeadlessTest: dua window dibuat (MainWindow + ActivityMonitorWindow): " + ([regex]::Matches($ht, "new MainWindow\(|new ActivityMonitorWindow\(")).Count)
Write-Output ("HeadlessTest: real initialize+tools/call POST: " + ([regex]::Matches($ht, "tools/call|initialize")).Count)
$vmf = Get-Content (Join-Path $apoc "ActivityMonitorViewModel.cs") -Raw
Write-Output ("ViewModel: EventBus subscribe + Dispatcher marshal: " + ([regex]::Matches($vmf, "EventBus\.Instance\.Subscribe|Dispatcher\.UIThread\.Post")).Count)
Write-Output "--- bukti run terakhir (logs/avalonia-poc9.txt) ---"
Get-Content "D:\MCP-Bridge\logs\avalonia-poc9.txt" -ErrorAction SilentlyContinue | ForEach-Object { Write-Output ("  " + $_) }
Write-Output "--- bukti run final-rerun3 (PoC setelah fix update-or-add) ---"
$f3 = Get-Content "D:\MCP-Bridge\logs\final-rerun3.txt" -ErrorAction SilentlyContinue | Where-Object { $_ -like "*[poc]*" }
$f3 | ForEach-Object { Write-Output ("  " + $_) }

Write-Output ""
Write-Output "=== [5] Z.AI ADAPTER VERIFICATION ==="
$za = "D:\MCP-Bridge\tools\zai-adapter\zai_mcp_client.py"
Write-Output ("adapter size: " + (Get-Item $za).Length)
$zac = Get-Content $za -Raw
Write-Output ("session capture (Mcp-Session-Id di _post): " + ([regex]::Matches($zac, "Mcp-Session-Id")).Count)
Write-Output ("SSE parse (text/event-stream / data:)    : " + ([regex]::Matches($zac, "text/event-stream|data:")).Count)
Write-Output ("reconnect (call_tool_with_reconnect)     : " + ([regex]::Matches($zac, "call_tool_with_reconnect")).Count)
$tokPrint = ([regex]::Matches($zac, "print\(.{0,40}token\)")).Count
Write-Output ("print token (harus 0)                    : " + $tokPrint)
Write-Output "--- e2e log final ---"
Get-Content "D:\MCP-Bridge\logs\zai-adapter-e2e.txt" -ErrorAction SilentlyContinue | ForEach-Object { Write-Output ("  " + $_) }
Write-Output ("--- INSTRUKSI template exists: " + (Test-Path "D:\MCP-Bridge\tools\zai-adapter\INSTRUKSI_TEMPLATE.md"))

Write-Output ""
Write-Output "=== [6] CREATE_NO_WINDOW COVERAGE (semua jalur spawn) ==="
$srcFiles = Get-ChildItem "D:\MCP-Bridge\desktop-commander-local\app\Xellen" -Recurse -Filter "*.cs" | Where-Object { $_.FullName -notlike "*obj*" }
$totalSpawn = 0
$withNoWindow = 0
foreach ($f in $srcFiles) {
    $c = Get-Content $f.FullName -Raw
    $spawns = [regex]::Matches($c, "Process\.Start\(|ProcessStartInfo\(")
    $nw = [regex]::Matches($c, "CreateNoWindow").Count
    if ($spawns.Count -gt 0) {
        Write-Output ("  " + $f.Name + " : spawn-sites=" + $spawns.Count + " CreateNoWindowRefs=" + $nw)
    }
    $totalSpawn += $spawns.Count
    $withNoWindow += $nw
}
Write-Output ("total spawn-sites di Core/Transport: " + $totalSpawn)
$stm2 = Get-Content $st -Raw
Write-Output ("SelfTestRunner: ProcessStartInfo sites=" + ([regex]::Matches($stm2, "ProcessStartInfo\(")).Count + " CreateNoWindowRefs=" + ([regex]::Matches($stm2, "CreateNoWindow")).Count)

Write-Output ""
Write-Output "=== [7] WINDOW VERIFY EVIDENCE (Check A/B) ==="
Get-Content "D:\MCP-Bridge\logs\window-verify2.txt" -ErrorAction SilentlyContinue | ForEach-Object { Write-Output ("  " + $_) }

Write-Output ""
Write-Output "=== [8] PUBLISH VERIFICATION ==="
$exe = "D:\MCP-Bridge\desktop-commander-local\app\publish-p1\MCP-Xellen.exe"
$ei = Get-Item $exe
Write-Output ("publish-p1 exe mtime : " + $ei.LastWriteTime + "  size=" + $ei.Length)
$srcNewest = Get-ChildItem "D:\MCP-Bridge\desktop-commander-local\app\Xellen" -Recurse -Filter "*.cs" | Where-Object { $_.FullName -notlike "*obj*" } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Output ("newest source .cs    : " + $srcNewest.Name + " @ " + $srcNewest.LastWriteTime)
Write-Output ("binary >= newest src : " + ($ei.LastWriteTime -ge $srcNewest.LastWriteTime))
$ddl = Get-Item "D:\MCP-Bridge\desktop-commander-local\app\publish-p1\McpXellen.Desktop.dll"
Write-Output ("dll version          : " + $ddl.VersionInfo.FileVersion)

Write-Output ""
Write-Output "=== [9] PRODUCTION (read-only) ==="
$p = Get-CimInstance Win32_Process -Filter "ProcessId=30704" -ErrorAction SilentlyContinue
Write-Output ("launcher 30704 alive : " + ($null -ne $p) + "  exe=" + $(if ($p) { $p.CommandLine }))
$launcherNow = Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like "*publish-direct*MCP-Xellen.exe*" }
Write-Output ("launcher publish-direct (semua PID): " + (($launcherNow | ForEach-Object { $_.ProcessId }) -join ","))
$sg = Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like "*supergateway*--port 8023*" } | Select-Object -First 1
Write-Output ("sg8023 alive         : " + ($null -ne $sg) + "  pid=" + $(if ($sg) { $sg.ProcessId }))
foreach ($port in 8023, 8024, 8025) {
    try {
        $r = Invoke-WebRequest -Uri ("http://127.0.0.1:" + $port + "/healthz") -TimeoutSec 2 -UseBasicParsing
        Write-Output ("port " + $port + " healthz -> " + $r.StatusCode)
    } catch {
        Write-Output ("port " + $port + " healthz -> UNREACHABLE")
    }
}
try {
    $r8 = Invoke-WebRequest -Uri "http://127.0.0.1:8089/healthz" -TimeoutSec 2 -UseBasicParsing
    Write-Output ("port 8089 -> " + $r8.StatusCode)
} catch {
    Write-Output "port 8089 -> UNREACHABLE"
}
$l8300 = netstat -ano | Select-String ":8300 "
Write-Output ("port 8300            : " + $(if ($l8300) { "IN USE" } else { "FREE (native off di production)" }))
Write-Output "--- funnel status ---"
& "C:\Program Files\Tailscale\tailscale.exe" serve status 2>&1 | Select-Object -First 4

Write-Output ""
Write-Output "=== [10] DOCS INVENTORY + DUPlikasi ==="
Get-ChildItem "D:\MCP-Bridge\docs" -File | Sort-Object Name | ForEach-Object { Write-Output ("  " + $_.Name + " (" + $_.Length + "B, " + $_.LastWriteTime.ToString("MM-dd HH:mm") + ")") }
$sec = Get-Content "D:\MCP-Bridge\docs\SECURITY_REPORT.md" -Raw
Write-Output ("dup COMPATIBILITY MATRIX di SECURITY_REPORT: " + ([regex]::Matches($sec, "COMPATIBILITY MATRIX")).Count)
$rb = Get-Content "D:\MCP-Bridge\docs\ROLLBACK_GUIDE.md" -Raw
Write-Output ("dup CHANGELOG di ROLLBACK_GUIDE            : " + ([regex]::Matches($rb, "CHANGELOG")).Count)

Write-Output ""
Write-Output "=== SELESAI ==="
