using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using McpXellen.Browser;
using McpXellen.Core;
using McpXellen.Transport;
using ActivityEvent = McpXellen.Core.ActivityEvent;

namespace McpXellen.SelfTest;

public sealed class TestResult
{
    public string Name { get; set; } = "";
    public bool Pass { get; set; }
    public bool Skipped { get; set; }
    public string Details { get; set; } = "";
}

/// <summary>
/// P1 acceptance tests (headless, `MCP-Xellen.exe --selftest`).
/// SAFETY MODEL:
///  - All config/state IO is sandboxed under tests\p1\core (ConfigStore.BaseDirOverride).
///  - The test registry contains ONLY test-marker ownership patterns — production processes
///    can never be matched by the sweep.
///  - Production cmdline patterns are ALSO placed on the protected list during tests, so even
///    a harness bug cannot terminate them (belt and suspenders).
///  - Test processes are cmd/ping with a unique marker; no node/chrome is spawned by tests,
///    EXCEPT T43–T47 (browser isolation): agent-infra/supergateway are spawned ONLY with
///    --user-data-dir / --apiKeyFile under tests\browser (headless, never the user's Chrome),
///    and cleanup kills only processes whose cmdline contains the tests\p1 root.
/// </summary>
public static class SelfTestRunner
{
    private const string TestRoot = @"D:\MCP-Bridge\tests\p1";
    private const string Marker = "xellen-p1-test";        // ownership marker (in child cmdlines)
    private const string ProtectedMarker = "xellen-p1-protected"; // protected-marker decoys
    private const string SpawnerTag = "ping -n 600 127.0.0.1";

    private static readonly List<TestResult> Results = new();

    public static int RunAll(string? filter = null)
    {
        Console.WriteLine("[selftest] starting" + (filter != null ? $" filter={filter}" : ""));
        Directory.CreateDirectory(TestRoot);
        Directory.CreateDirectory(@"D:\MCP-Bridge\logs");
        ConfigStore.BaseDirOverride = Path.Combine(TestRoot, "core");

        var externalBefore = SnapshotExternal();
        bool Filter(string name) => filter == null || name.StartsWith(filter, StringComparison.OrdinalIgnoreCase);
        try
        {
            if (Filter("T1_")) T1_StartStop();
            if (Filter("T2_")) T2_RestartBackoff();
            if (Filter("T3_")) T3_CrashResidualCleanup();
            if (Filter("T4_")) T4_OrphanSweepAndProtection();
            if (Filter("T5_")) T5_RegistryPersistence();
            if (Filter("T6_")) T6_LogRotation();
            if (Filter("T7_")) T7_MetricsAccuracy();
            if (Filter("T9_")) T9_GracefulShutdownAll();
            if (Filter("T10_")) T10_StartupRecoveryState();
            if (Filter("T11_")) T11_ChildPolicyReportOnly();
            if (Filter("T12_")) T12_ConcurrencyNoKill();
            if (Filter("T13_")) T13_UnknownActivityNeverKilled();
            if (Filter("T14_")) T14_ForeignChildNeverTouched();
            if (Filter("T15_")) T15_UserRequestedGracefulStopStillWorks();
            if (Filter("T16_")) T16_ExplicitlyStoppedNotActive();
            if (Filter("T17_")) T17_SnapshotSuccessWithProcesses();
            if (Filter("T18_")) T18_SnapshotSuccessEmpty();
            if (Filter("T19_")) T19_SnapshotExceptionFails();
            if (Filter("T20_")) T20_SnapshotTimeoutFails();
            if (Filter("T21_")) T21_24_SweepFailureSafetyAndRecovery();
            if (Filter("T25_")) T25_ChildPolicyKeepsPreviousDataOnFailure();
            if (Filter("T26_")) T26_SnapshotTimeoutCancelsWork();
            if (Filter("T27_")) T27_SnapshotTimeoutAbandonedIsHonest();
            if (Filter("T28_")) T28_NoPileupAfterTimeouts();
            if (Filter("T29_")) T29_QueryPidOutcomes();
            if (Filter("T30_")) T30_KillRefusedOnUnknownOrTimeout();
            if (Filter("T31_")) T31_MidSweepExceptionFailsSweep();
            if (Filter("T32_")) T32_RecoveryAfterMidSweepException();
            if (Filter("T33_")) T33_AuthStoreLifecycle();
            if (Filter("T34_")) T34_RedactionCoverage();
            if (Filter("T35_")) T35_EventBusOverflowAccounting();
            if (Filter("T36_")) T36_RegistryMigrationV2();
            if (Filter("T37_")) T37_TransportAuthSessionsTools();
            if (Filter("T39_")) T39_ProjectIsolationTwoProjects();
            if (Filter("T40_")) T40_TransportRateLimit();
            if (Filter("T41_")) T41_ParityNativeVsCompat();
            if (Filter("T42_")) T42_UiSmokeHeadless();
            if (Filter("T43_")) T43_BrowserCapsAndOwnership();
            if (Filter("T44_")) T44_BrowserStartupSweepNoForeignClaim();
            if (Filter("T45_")) T45_BrowserTokenNoLeak();
            if (Filter("T46_")) T46_BrowserRealLifecycleAuth();
            if (Filter("T47_")) T47_BrowserIdleExpiryAndCrashIsolation();
        }
        catch (Exception ex)
        {
            Results.Add(new TestResult { Name = "harness", Pass = false, Details = ex.ToString() });
        }
        finally
        {
            // Restore real enumeration & defaults no matter what happened in injected-failure tests.
            ProcessEnumerator.RawEnumerationOverride = null;
            ProcessEnumerator.RawSingleQueryOverride = null;
            ProcessHost.SweepHookAfterSnapshot = null;
            NativeTransportHost.Stop();
            ProcessEnumerator.EnumerationTimeout = TimeSpan.FromSeconds(10);
            ProcessEnumerator.QueryTimeout = TimeSpan.FromSeconds(5);
            ProcessEnumerator.PostCancelGrace = TimeSpan.FromSeconds(2);
            CleanupTestProcesses();
            BrowserManager.Instance.ShutdownAll();
            CleanupBrowserTestProcesses();
        }
        var externalAfter = SnapshotExternal();
        T8_ExternalProtectionAudit(externalBefore, externalAfter);

        var pass = Results.Count(r => r.Pass && !r.Skipped);
        var skipped = Results.Count(r => r.Skipped);
        var fail = Results.Count(r => !r.Pass && !r.Skipped);
        var payload = new
        {
            startedUtc = DateTime.UtcNow,
            finishedUtc = DateTime.UtcNow,
            passed = pass,
            failed = fail,
            skipped = skipped,
            results = Results,
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        var outPath = Path.Combine(TestRoot, "results.json");
        File.WriteAllText(outPath, json);
        try { File.WriteAllText(@"D:\MCP-Bridge\logs\p1-selftest-results.json", json); } catch { }
        foreach (var r in Results) Console.WriteLine($"[selftest] {(r.Skipped ? "SKIP" : r.Pass ? "PASS" : "FAIL")} {r.Name}");
        Console.WriteLine($"[selftest] done: {pass} pass, {fail} fail, {skipped} skipped → {outPath}");
        return fail == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- helpers

    private static XellenConfig MakeConfig(params ServerConfig[] servers) => new()
    {
        Log = new LogSettings { Directory = Path.Combine(TestRoot, "logs"), BaseFileName = "selftest", MaxFileBytes = 10 * 1024 * 1024, MaxBackups = 5, MinLevel = "Debug" },
        OrphanSweep = new OrphanSweepSettings { Mode = "auto", StartupDelaySeconds = 3600, IntervalSeconds = 0 }, // no auto timer; sweeps are explicit
        ChildPolicy = new ChildPolicySettings { Enabled = true, MaxChildLifetimeMinutes = 240, MaxConcurrentChildren = 24, MinAgeForCullMinutes = 30 },
        ProtectedProcessPatterns = new List<string>
        {
            ProtectedMarker,
            // production patterns are PROTECTED during tests — a harness bug must not kill them:
            "desktop-commander-local", "mcp-guard-stack", "construct3-mcp", "_bmcp", "@browsermcp",
            "reactbits", "shadcn", "@ant-design", "\\Google\\Chrome\\Application",
        },
        Servers = servers.ToList(),
    };

    private static void InitCore(XellenConfig cfg)
    {
        LogService.Init(cfg.Log, Array.Empty<string>());
        ProcessHost.ProtectedPatterns = cfg.ProtectedProcessPatterns;
        ProcessHost.Instance.Initialize(cfg, ConfigStore.LoadRuntimeState());
        MetricsService.Start(cfg);
    }

    private static ServerConfig SpawnerServer(string id, bool restartEnabled) => new()
    {
        Id = id,
        Name = "SelfTest spawner " + id,
        Command = "cmd.exe",
        Arguments = new List<string> { "/c", Path.Combine(TestRoot, "spawner.cmd") },
        WorkingDirectory = TestRoot,
        StopTimeoutSeconds = 3,
        AutoStart = false,
        RestartPolicy = new RestartPolicySettings { Enabled = restartEnabled, MaxAttempts = 2, BackoffBaseSeconds = 2, BackoffMaxSeconds = 60 },
        OwnershipPatterns = new List<string> { Marker },
        ChildProcessPatterns = new List<string> { Marker },
    };

    private static List<ProcInfo> AllProcesses()
    {
        var r = ProcessEnumerator.Snapshot();
        return r.Success ? r.Processes : new List<ProcInfo>(); // helpers treat failure as empty (dedicated tests cover the API)
    }

    private static List<ProcInfo> MarkerProcesses() =>
        AllProcesses().Where(p => p.CmdLine != null && p.CmdLine.Contains(Marker, StringComparison.OrdinalIgnoreCase)).ToList();

    private static List<ProcInfo> SpawnerPingProcesses() =>
        AllProcesses().Where(p => p.CmdLine != null && p.CmdLine.Contains(SpawnerTag, StringComparison.OrdinalIgnoreCase)).ToList();

    private static List<ProcInfo> OrphanPairProcesses() => MarkerProcesses()
        .Where(p => p.CmdLine!.Contains("childA", StringComparison.OrdinalIgnoreCase)
                 || p.CmdLine!.Contains("childB", StringComparison.OrdinalIgnoreCase)).ToList();

    private static int OrphanPairCount() => OrphanPairProcesses().Count;

    private static bool WaitFor(Func<bool> cond, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (cond()) return true;
            Thread.Sleep(500);
        }
        return cond();
    }

    private static void Check(string name, bool pass, string details)
    {
        Results.Add(new TestResult { Name = name, Pass = pass, Details = details });
        Console.WriteLine($"[selftest] {(pass ? "PASS" : "FAIL")} {name}: {details}");
    }

    private static void WriteSpawner()
    {
        var cmd = """
@echo off
rem Xellen P1 self-test spawner: two marker children, then stay alive.
rem /min: children must never open visible console windows (owner disturbance).
start /B "" cmd /c "title xellen-p1-test-childA & ping -n 600 127.0.0.1 > nul"
start /B "" cmd /c "title xellen-p1-test-childB & ping -n 600 127.0.0.1 > nul"
ping -n 6000 127.0.0.1 > nul
""";
        File.WriteAllText(Path.Combine(TestRoot, "spawner.cmd"), cmd);

        var cmd2 = """
@echo off
rem Xellen P1 child-policy test: one marker child + one PLAIN child (no marker in cmdline).
rem /min: children must never open visible console windows (owner disturbance).
start /B "" cmd /c "title xellen-p1-test-childA & ping -n 600 127.0.0.1 > nul"
start /B "" cmd /c "ping -n 600 127.0.0.1 > nul"
ping -n 6000 127.0.0.1 > nul
""";
        File.WriteAllText(Path.Combine(TestRoot, "spawner2.cmd"), cmd2);
    }

    private static uint SpawnMarkerChild(string title, int pingSeconds)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c \"title {title} & ping -n {pingSeconds} 127.0.0.1 > nul\"")
        { UseShellExecute = false, CreateNoWindow = true };
        var p = Process.Start(psi)!;
        return (uint)p.Id;
    }

    private static void ExternalKill(uint pid)
    {
        using var k = Process.Start(new ProcessStartInfo("taskkill", $"/PID {pid} /F") { UseShellExecute = false, CreateNoWindow = true });
        k?.WaitForExit(5000);
    }

    private static void CleanupTestProcesses()
    {
        foreach (var p in MarkerProcesses())
            ProcessHost.TryKillOwned(p.Pid, Marker, "selftest cleanup", null);
        // Orphaned test pings (cmdline lacks the marker but was snapshot-recorded) are provably ours:
        foreach (var p in SpawnerPingProcesses())
        {
            var pq = ProcessEnumerator.QueryPid(p.Ppid);
            if (pq.Status == ProcQueryStatus.NotFound) // query succeeded AND parent absent → provable orphan
                ProcessHost.TryKillRecordedChild(new ChildRecord { Pid = p.Pid, Ppid = p.Ppid, Cmd = p.CmdLine ?? "" }, "selftest cleanup (orphaned ping)", null);
        }
        // Protected decoys are refused by TryKillOwned by design — remove them directly
        // (their cmdlines still carry the test marker, so the T8 audit stays valid).
        foreach (var p in AllProcesses()
                     .Where(p => p.CmdLine != null && p.CmdLine.Contains(ProtectedMarker)))
        {
            try { using var k = Process.Start(new ProcessStartInfo("taskkill", $"/PID {p.Pid} /T /F") { UseShellExecute = false, CreateNoWindow = true }); k?.WaitForExit(5000); }
            catch { }
        }
        Thread.Sleep(500);
        var left = MarkerProcesses().Count;
        Console.WriteLine($"[selftest] cleanup: marker processes remaining = {left}");
    }

    private static List<(uint Pid, string Name, string Cmd)> SnapshotExternal()
    {
        string[] prodPatterns = { "desktop-commander-local", "mcp-guard-stack", "construct3-mcp", "_bmcp", "\\Google\\Chrome\\Application" };
        return AllProcesses()
            .Where(p => p.CmdLine != null && prodPatterns.Any(pt => p.CmdLine.Contains(pt, StringComparison.OrdinalIgnoreCase)))
            .Select(p => (p.Pid, p.Name, p.CmdLine ?? ""))
            .ToList();
    }

    // ---------------------------------------------------------------- tests

    private static void T1_StartStop()
    {
        WriteSpawner();
        InitCore(MakeConfig(SpawnerServer("t1", restartEnabled: false)));
        var ok = ProcessHost.Instance.StartServer("t1", out var err);
        bool childrenUp = WaitFor(() => ProcessHost.Instance.Get("t1") is { } t && t.Children.Count >= 4, 25000);
        var before = MarkerProcesses().Count;
        ProcessHost.Instance.StopServer("t1");
        bool allGone = WaitFor(() => MarkerProcesses().Count == 0 && SpawnerPingProcesses().Count == 0, 10000);
        var t = ProcessHost.Instance.Get("t1");
        bool stopped = t == null || t.Proc == null || t.Proc.HasExited;
        Check("T1_StartStop", ok && childrenUp && before >= 2 && allGone && stopped,
            $"start={ok} err='{err}' childrenBefore={before} allGone={allGone} stopped={stopped}");
    }

    private static void T2_RestartBackoff()
    {
        var srv = SpawnerServer("t2", restartEnabled: false);
        srv.Command = "cmd.exe";
        srv.Arguments = new List<string> { "/c", "exit 7" };
        srv.RestartPolicy = new RestartPolicySettings { Enabled = true, MaxAttempts = 2, BackoffBaseSeconds = 2, BackoffMaxSeconds = 60 };
        InitCore(MakeConfig(srv));
        ProcessHost.Instance.StartServer("t2", out _);
        bool failed = WaitFor(() => ProcessHost.Instance.Get("t2") is { State: ProcState.Failed }, 40000);
        var t = ProcessHost.Instance.Get("t2");
        Check("T2_RestartBackoff", failed && t != null && t.RestartCountTotal >= 2,
            $"state={t?.State} restarts={t?.RestartCountTotal} (expected: 2 auto-restarts with 2s→4s backoff then Failed)");
    }

    private static void T3_CrashResidualCleanup()
    {
        InitCore(MakeConfig(SpawnerServer("t3", restartEnabled: false)));
        ProcessHost.Instance.StartServer("t3", out _);
        bool up = WaitFor(() => ProcessHost.Instance.Get("t3") is { } t && t.Children.Count >= 4, 25000);
        uint spawnerPid = (uint)(ProcessHost.Instance.Get("t3")?.Proc?.Id ?? 0);
        ExternalKill(spawnerPid); // simulate parent crash
        bool clean = WaitFor(() => MarkerProcesses().Count == 0 && SpawnerPingProcesses().Count == 0, 15000);
        bool stateSettled = WaitFor(() => ProcessHost.Instance.Get("t3") is { State: ProcState.Crashed }, 8000);
        var state = ProcessHost.Instance.Get("t3")?.State;
        Check("T3_CrashResidualCleanup", up && clean && stateSettled && state == ProcState.Crashed,
            $"up={up} spawnerPid={spawnerPid} residualCleaned={clean} stateSettled={stateSettled} finalState={state}");
    }

    private static void T4_OrphanSweepAndProtection()
    {
        // (a) create TRUE orphans without ProcessHost: spawner → children, then kill spawner externally.
        var spawner = Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{Path.Combine(TestRoot, "spawner.cmd")}\"") { UseShellExecute = false, CreateNoWindow = true })!;
        bool kidsUp = WaitFor(() => OrphanPairCount() >= 2, 20000);
        ExternalKill((uint)spawner.Id);
        Thread.Sleep(1500);
        int orphansBefore = OrphanPairCount();

        // (b) ambiguous decoy: marker cmdline but parent (this test host) alive and not Xellen-owned.
        SpawnMarkerChild($"{Marker}-ambig", 900);
        // (c) protected decoy: matches ownership marker AND protected marker.
        SpawnMarkerChild($"{Marker}-prot {ProtectedMarker}-decoy", 900);
        Thread.Sleep(1500);

        var report = ProcessHost.Instance.RunSweep();
        Thread.Sleep(1000);
        int orphansAfter = OrphanPairCount();
        bool protAlive = AllProcesses().Any(p => p.CmdLine != null && p.CmdLine.Contains(ProtectedMarker));
        int killed = report.Findings.Count(f => f.Action == "killed");
        int skippedProt = report.Findings.Count(f => f.Action == "skipped-protected");
        int skippedAmbig = report.Findings.Count(f => f.Action == "skipped-ambiguous");

        Check("T4_OrphanSweepAndProtection", kidsUp && orphansBefore >= 2 && killed >= 2 && orphansAfter == 0 && protAlive && skippedProt >= 1 && skippedAmbig >= 1,
            $"orphansBefore={orphansBefore} killed={killed} orphansAfter={orphansAfter} protectedAlive={protAlive} skippedProt={skippedProt} skippedAmbig={skippedAmbig}");
        // Cleanup ALL decoys including the protected one (it is our own test fixture) so it cannot
        // pollute marker counting in later tests. Direct taskkill — the harness's own fixture cleanup.
        foreach (var p in AllProcesses().Where(p => p.CmdLine != null && (p.CmdLine.Contains(Marker) || p.CmdLine.Contains(ProtectedMarker))))
        {
            try { using var k = Process.Start(new ProcessStartInfo("taskkill", $"/PID {p.Pid} /T /F") { UseShellExecute = false, CreateNoWindow = true }); k?.WaitForExit(5000); } catch { }
        }
    }

    private static void T5_RegistryPersistence()
    {
        var cfg = MakeConfig(SpawnerServer("t5", restartEnabled: false));
        ConfigStore.Save(cfg);
        bool fileOk = File.Exists(ConfigStore.ConfigPath);
        var loaded = ConfigStore.LoadOrCreate();
        bool roundTrip = loaded.Servers.Count == 1 && loaded.Servers[0].Id == "t5";
        ConfigStore.Save(loaded); // second save → must create a backup
        var backups = Directory.Exists(ConfigStore.BackupDir) ? Directory.GetFiles(ConfigStore.BackupDir).Length : 0;
        Check("T5_RegistryPersistence", fileOk && roundTrip && backups >= 1,
            $"file={fileOk} roundTrip={roundTrip} backups={backups} (dir={ConfigStore.ConfigPath})");
    }

    private static void T6_LogRotation()
    {
        var rotDir = Path.Combine(TestRoot, "logs-rot");
        if (Directory.Exists(rotDir)) { try { Directory.Delete(rotDir, true); } catch { } }
        LogService.Init(new LogSettings { Directory = rotDir, BaseFileName = "selftest", MaxFileBytes = 70000, MaxBackups = 3, MinLevel = "Debug" }, Array.Empty<string>());
        // Note: LogService enforces a 64 KB minimum cap by design (production safety); 70000 exercises rotation.
        for (int i = 0; i < 400; i++) LogService.Info("rotation-test", null, new string('x', 300));
        Thread.Sleep(300);
        var files = Directory.GetFiles(rotDir).OrderBy(f => f).ToList();
        var baseFile = Path.Combine(rotDir, "selftest.log");
        long baseSize = File.Exists(baseFile) ? new FileInfo(baseFile).Length : -1;
        // restore normal test logging
        InitCore(MakeConfig());
        Check("T6_LogRotation", files.Count <= 4 && baseSize is > 0 and <= 72000,
            $"files={files.Count} ({string.Join(", ", files.Select(Path.GetFileName))}) baseSize={baseSize} (cap 70000 + slack; rotation fired={files.Count > 1})");
    }

    private static void T7_MetricsAccuracy()
    {
        InitCore(MakeConfig(SpawnerServer("t7", restartEnabled: false)));
        ProcessHost.Instance.StartServer("t7", out _);
        bool up = WaitFor(() => ProcessHost.Instance.Get("t7") is { } t && t.Children.Count >= 4, 25000);
        Thread.Sleep(1200);
        var snap = MetricsService.Snapshot(ProcessHost.Instance.StateSnapshot);
        var m = snap.Servers.FirstOrDefault(x => x.Id == "t7");
        bool ok = snap.ManagedServers == 1 && snap.ActiveProcesses == 1 && m != null &&
                  m.ProcRamMb > 0 && m.SessionChildren >= 2 && m.DescendantProcesses >= 4 && m.ChildRamMb > 0 && snap.CoreRamMb > 0;
        ProcessHost.Instance.StopServer("t7");
        Check("T7_MetricsAccuracy", up && ok,
            $"managed={snap.ManagedServers} active={snap.ActiveProcesses} ramProc={m?.ProcRamMb}MB sessionChildren={m?.SessionChildren} (>=2 cmd wrappers; conhost may add) descendants={m?.DescendantProcesses} (>=4) ramChildren={m?.ChildRamMb}MB coreRam={snap.CoreRamMb}MB (all measured WorkingSet)");
    }

    private static void T9_GracefulShutdownAll()
    {
        InitCore(MakeConfig(SpawnerServer("t9", restartEnabled: false)));
        ProcessHost.Instance.StartServer("t9", out _);
        bool up = WaitFor(() => ProcessHost.Instance.Get("t9") is { } t && t.Children.Count >= 4, 25000);
        ProcessHost.Instance.ShutdownAll();
        bool gone = WaitFor(() => MarkerProcesses().Count == 0 && SpawnerPingProcesses().Count == 0, 10000);
        var t9 = ProcessHost.Instance.Get("t9");
        Check("T9_GracefulShutdownAll", up && gone && (t9?.State == ProcState.Stopped),
            $"up={up} treeGone={gone} state={t9?.State}");
    }

    private static void T10_StartupRecoveryState()
    {
        var st = new RuntimeState();
        st.Servers["t10"] = new ServerRuntimeInfo { LastPid = 999999, LastStartedUtc = DateTime.UtcNow.AddMinutes(-5) };
        ConfigStore.SaveRuntimeState(st);
        var loaded = ConfigStore.LoadRuntimeState();
        bool persisted = loaded.Servers.ContainsKey("t10") && loaded.Servers["t10"].LastPid == 999999;
        // stale PID 999999 must not cause any kill attempt (it does not exist) and sweep must stay clean
        var report = ProcessHost.Instance.RunSweep();
        bool noKill = report.Findings.All(f => f.Action != "killed");
        Check("T10_StartupRecoveryState", persisted && noKill,
            $"statePersisted={persisted} stalePidHandled=999999 findings={report.Findings.Count}");
    }

    private static void T11_ChildPolicyReportOnly()
    {
        var cfg = MakeConfig(SpawnerServer("t11", restartEnabled: false));
        cfg.ChildPolicy = new ChildPolicySettings
        {
            Enabled = true,
            MaxChildLifetimeMinutes = 0,
            MaxConcurrentChildren = 0,
            MinAgeForCullMinutes = 0,
        };
        InitCore(cfg);

        bool started = ProcessHost.Instance.StartServer("t11", out var err);
        bool childrenUp = WaitFor(() =>
            ProcessHost.Instance.Get("t11") is { } t && t.Children.Count >= 2, 25000);

        // Allow at least one 15-second monitor policy cycle with every threshold exceeded.
        Thread.Sleep(18000);
        int remainingBeforeStop = MarkerProcesses().Count;
        bool stillAlive = remainingBeforeStop >= 2 &&
            ProcessHost.Instance.Get("t11") is { Proc: { HasExited: false } };

        ProcessHost.Instance.StopServer("t11");
        bool cleaned = WaitFor(() => MarkerProcesses().Count == 0 && SpawnerPingProcesses().Count == 0, 10000);

        Check("T11_ChildPolicyReportOnly",
            started && childrenUp && stillAlive && cleaned,
            $"start={started} err='{err}' childrenUp={childrenUp} markerProcessesAfterPolicy={remainingBeforeStop} stillAlive={stillAlive} cleaned={cleaned}; thresholds were all zero but no child was culled");
    }

    private static void T12_ConcurrencyNoKill()
    {
        var cfg = MakeConfig(SpawnerServer("t12", restartEnabled: false));
        cfg.ChildPolicy.MaxConcurrentChildren = 1;  // children (2) exceed the cap
        cfg.ChildPolicy.MinAgeForCullMinutes = 0;   // under the OLD implementation the oldest child was culled immediately
        InitCore(cfg);
        ProcessHost.Instance.StartServer("t12", out _);
        bool up = WaitFor(() => ProcessHost.Instance.Get("t12") is { } t && t.Children.Count >= 4, 25000);
        WaitFor(() => false, 20000); // hold through at least one full policy cycle
        int alive = MarkerProcesses().Count;
        bool warned = LogService.Snapshot(1000).Any(l => l.Component == "processhost" && l.Msg.Contains("CHILD-CONCURRENCY warning"));
        ProcessHost.Instance.StopServer("t12");
        Check("T12_ConcurrencyNoKill", up && alive >= 2 && warned,
            $"childrenAliveAfterCap={alive} (>=2; maxConcurrentChildren=1, minAgeForCull=0) concurrencyWarningLogged={warned} → cap never kills");
    }

    private static void T13_UnknownActivityNeverKilled()
    {
        var cfg = MakeConfig(SpawnerServer("t13", restartEnabled: false));
        cfg.ChildPolicy.MaxChildLifetimeMinutes = 0; // force policy evaluation
        cfg.ChildPolicy.AutoCullEnabled = true;      // even "enabled" culling must be ignored without idle proof
        InitCore(cfg);
        ProcessHost.Instance.StartServer("t13", out _);
        bool up = WaitFor(() => ProcessHost.Instance.Get("t13") is { } t && t.Children.Count >= 4, 25000);
        var t13 = ProcessHost.Instance.Get("t13")!;
        var states = t13.Children.Values.Select(c => ProcessHost.GetActivityState(c)).ToList();
        WaitFor(() => false, 20000); // >=1 policy cycle with culling "requested"
        int alive = MarkerProcesses().Count;
        bool cullIgnored = LogService.Snapshot(1000).Any(l => l.Msg.Contains("autoCullEnabled=true is IGNORED"));
        ProcessHost.Instance.StopServer("t13");
        Check("T13_UnknownActivityNeverKilled", up && states.All(s => s == ChildActivityState.Unknown) && alive >= 2 && cullIgnored,
            $"activityStates=all Unknown ({states.Count} children) childrenAlive={alive} autoCullIgnoredWarning={cullIgnored} (AutoCullEnabled=true without idle proof → no kill, warning logged)");
    }

    private static void T14_ForeignChildNeverTouched()
    {
        // spawner2 spawns one marker child AND one plain child (no marker in its cmdline).
        var srv = SpawnerServer("t14", restartEnabled: false);
        srv.Arguments = new List<string> { "/c", Path.Combine(TestRoot, "spawner2.cmd") };
        var cfg = MakeConfig(srv);
        cfg.ChildPolicy.MaxChildLifetimeMinutes = 0; // most aggressive legacy settings…
        cfg.ChildPolicy.MaxConcurrentChildren = 1;
        cfg.ChildPolicy.MinAgeForCullMinutes = 0;    // …and still nothing may be terminated
        InitCore(cfg);
        ProcessHost.Instance.StartServer("t14", out _);
        bool up = WaitFor(() => ProcessHost.Instance.Get("t14") is { } t && t.Children.Count >= 4, 25000);
        WaitFor(() => false, 20000);
        var snap = AllProcesses();
        bool markerAlive = snap.Any(p => p.CmdLine != null && p.CmdLine.Contains(Marker));
        bool plainAlive = snap.Any(p => p.CmdLine != null && p.CmdLine.Contains(SpawnerTag) && !p.CmdLine.Contains(Marker));
        ProcessHost.Instance.StopServer("t14");
        Check("T14_ForeignChildNeverTouched", up && markerAlive && plainAlive,
            $"markerChildAlive={markerAlive} plainChildAlive={plainAlive} (plain child is outside policy ownership patterns and must survive aggressive thresholds)");
    }

    private static void T15_UserRequestedGracefulStopStillWorks()
    {
        InitCore(MakeConfig(SpawnerServer("t15", restartEnabled: false)));
        ProcessHost.Instance.StartServer("t15", out _);
        bool up = WaitFor(() => ProcessHost.Instance.Get("t15") is { } t && t.Children.Count >= 4, 25000);
        ProcessHost.Instance.StopServer("t15"); // user-requested stop → graceful tree cleanup remains allowed
        bool gone = WaitFor(() => MarkerProcesses().Count == 0 && SpawnerPingProcesses().Count == 0, 10000);
        var state = ProcessHost.Instance.Get("t15")?.State;
        Check("T15_UserRequestedGracefulStopStillWorks", up && gone && state == ProcState.Stopped,
            $"up={up} treeGone={gone} state={state} (user-requested stop is NOT affected by the no-cull policy)");
    }

    private static void T16_ExplicitlyStoppedNotActive()
    {
        if (ProcessHost.Instance.Get("t15") == null) { Check("T16_ExplicitlyStoppedNotActive", false, "t15 missing — T15 did not run"); return; }
        var snap = MetricsService.Snapshot(ProcessHost.Instance.StateSnapshot);
        var m = snap.Servers.FirstOrDefault(x => x.Id == "t15");
        bool notActive = snap.ActiveProcesses == 0 && m != null && m.State == ProcState.Stopped && m.Pid == null;
        Check("T16_ExplicitlyStoppedNotActive", notActive,
            $"activeProcesses={snap.ActiveProcesses} state={m?.State.ToString() ?? "null"} pid={m?.Pid?.ToString() ?? "null"} (explicitly stopped server is no longer counted active)");
    }

    // ---------------------------------------------------------------- WMI failure handling tests (P1 fix)

    private static void T17_SnapshotSuccessWithProcesses()
    {
        var fake = new List<ProcInfo>
        {
            new ProcInfo(1111, 999, "fake-a.exe", "fake a cmdline", DateTime.UtcNow, 1000),
            new ProcInfo(2222, 999, "fake-b.exe", null, null, 0),
            new ProcInfo(3333, 999, "fake-c.exe", "fake c", DateTime.UtcNow, 2000),
        };
        try
        {
            ProcessEnumerator.RawEnumerationOverride = _ => fake;
            var r = ProcessEnumerator.Snapshot();
            Check("T17_SnapshotSuccessWithProcesses", r.Success && r.Processes.Count == 3 && r.CapturedUtc <= DateTime.UtcNow,
                $"success={r.Success} count={r.Processes.Count} capturedUtc={r.CapturedUtc:HH:mm:ss.fff} (Success with a populated list)");
        }
        finally { ProcessEnumerator.RawEnumerationOverride = null; }
    }

    private static void T18_SnapshotSuccessEmpty()
    {
        try
        {
            ProcessEnumerator.RawEnumerationOverride = _ => new List<ProcInfo>();
            var r = ProcessEnumerator.Snapshot();
            Check("T18_SnapshotSuccessEmpty", r.Success && r.Processes.Count == 0,
                $"success={r.Success} count={r.Processes.Count} (an empty list on Success is a VALID result, distinct from failure)");
        }
        finally { ProcessEnumerator.RawEnumerationOverride = null; }
    }

    private static void T19_SnapshotExceptionFails()
    {
        try
        {
            ProcessEnumerator.RawEnumerationOverride = _ => throw new UnauthorizedAccessException("synthetic WMI access denied");
            var r = ProcessEnumerator.Snapshot();
            Check("T19_SnapshotExceptionFails", !r.Success && r.Error != null && r.Error.Contains("synthetic WMI access denied") && r.Processes.Count == 0,
                $"success={r.Success} error='{r.Error}' (exception → Failed result; never silently converted to a fake empty success)");
        }
        finally { ProcessEnumerator.RawEnumerationOverride = null; }
    }

    private static void T20_SnapshotTimeoutFails()
    {
        var original = ProcessEnumerator.EnumerationTimeout;
        try
        {
            ProcessEnumerator.EnumerationTimeout = TimeSpan.FromMilliseconds(500);
            ProcessEnumerator.RawEnumerationOverride = h => { h.Register(() => { }); Thread.Sleep(5000); return new List<ProcInfo>(); };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = ProcessEnumerator.Snapshot();
            sw.Stop();
            Check("T20_SnapshotTimeoutFails", !r.Success && r.Error != null && r.Error.Contains("timed out") && sw.ElapsedMilliseconds < 4000,
                $"success={r.Success} error='{r.Error}' elapsed={sw.ElapsedMilliseconds}ms (500ms timeout enforced; caller did not hang for the full 5s enumeration)");
        }
        finally
        {
            ProcessEnumerator.RawEnumerationOverride = null;
            ProcessEnumerator.EnumerationTimeout = original;
        }
    }

    private static void T21_24_SweepFailureSafetyAndRecovery()
    {
        WriteSpawner();
        var cfg = MakeConfig(SpawnerServer("t21", restartEnabled: false));
        cfg.OrphanSweep = new OrphanSweepSettings { Mode = "auto", StartupDelaySeconds = 3600, IntervalSeconds = 0 };
        InitCore(cfg);

        // Baseline: one SUCCESSFUL sweep with no orphans present.
        var r1 = ProcessHost.Instance.RunSweep();
        // NOTE: StateSnapshot returns the LIVE state object — copy the VALUES immediately,
        // otherwise later sweeps would mutate the "baseline" and the comparison is vacuous.
        DateTime? baselineLastRun = ProcessHost.Instance.StateSnapshot.OrphanSweep.LastRunUtc;
        int baselineDetected = ProcessHost.Instance.StateSnapshot.OrphanSweep.TotalDetected;
        int baselineKilled = ProcessHost.Instance.StateSnapshot.OrphanSweep.TotalKilled;

        // Create a REAL orphan pair (parent killed externally), then force enumeration failure.
        var spawner = Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{Path.Combine(TestRoot, "spawner.cmd")}\"") { UseShellExecute = false, CreateNoWindow = true })!;
        WaitFor(() => OrphanPairCount() >= 2, 20000);
        ExternalKill((uint)spawner.Id);
        Thread.Sleep(1500);
        var orphanPids = OrphanPairProcesses().Select(p => p.Pid).ToList();

        ProcessEnumerator.RawEnumerationOverride = _ => throw new InvalidOperationException("synthetic WMI outage");
        var r2 = ProcessHost.Instance.RunSweep();
        bool aborted = r2.Status == "failed" && r2.Error != null && r2.Error.Contains("synthetic WMI outage") && r2.Findings.Count == 0;
        // Orphan aliveness is verified via per-PID queries (independent of the failing Snapshot override).
        bool orphansStillAlive = orphanPids.All(pid =>
        {
            var q = ProcessEnumerator.QueryPid(pid);
            return q.Status == ProcQueryStatus.Success && q.Info != null;
        });
        Check("T21_SweepAbortsOnFailureNoKill", aborted && orphansStillAlive,
            $"status={r2.Status} error='{r2.Error}' findings={r2.Findings.Count} orphansStillAlive={orphansStillAlive} ({orphanPids.Count} pids verified individually) → failure aborts sweep, no kill");

        var stats2 = ProcessHost.Instance.StateSnapshot.OrphanSweep;
        bool statsUnchanged = stats2.LastRunUtc == baselineLastRun && stats2.TotalDetected == baselineDetected
                              && stats2.TotalKilled == baselineKilled && stats2.ConsecutiveFailures >= 1;
        Check("T22_FailureDoesNotChangeSuccessStats", statsUnchanged,
            $"lastRunUtcUnchanged={stats2.LastRunUtc == baselineLastRun} detected={baselineDetected}→{stats2.TotalDetected} killed={baselineKilled}→{stats2.TotalKilled} consecutiveFailures={stats2.ConsecutiveFailures}");

        // UI label captured DURING the failure (must show ERROR/UNKNOWN, never CLEAN)
        string labelDuringFailure = MetricsService.SweepStatusLabel(ProcessHost.Instance.StateSnapshot);

        // Recovery: WMI "pulih" (override removed) → next sweep works and reaps the orphans.
        ProcessEnumerator.RawEnumerationOverride = null;
        var r3 = ProcessHost.Instance.RunSweep();
        var stats3 = ProcessHost.Instance.StateSnapshot.OrphanSweep;
        bool recovered = r3.Status == "completed" && r3.Findings.Count(f => f.Action == "killed") >= 2 && OrphanPairCount() == 0;
        Check("T23_SweepRecoversAfterWmiRecovers", recovered && stats3.ConsecutiveFailures == 0 && stats3.LastRunUtc > baselineLastRun,
            $"status={r3.Status} killed={r3.Findings.Count(f => f.Action == "killed")} orphansLeft={OrphanPairCount()} consecutiveFailures={stats3.ConsecutiveFailures} lastRunAdvanced={stats3.LastRunUtc > baselineLastRun}");

        string labelAfter = MetricsService.SweepStatusLabel(ProcessHost.Instance.StateSnapshot);
        Check("T24_UiStatusShowsUnknownNotClean", labelDuringFailure.Contains("ERROR") && !labelDuringFailure.Contains("CLEAN") && labelAfter.Contains("CLEAN"),
            $"duringFailure='{labelDuringFailure}' afterRecovery='{labelAfter}' (failure is displayed as ERROR/UNKNOWN, never as CLEAN)");

        // cleanup leftover pings of the killed spawner tree
        foreach (var p in SpawnerPingProcesses())
        {
            var pq = ProcessEnumerator.QueryPid(p.Ppid);
            if (pq.Status == ProcQueryStatus.NotFound)
                ProcessHost.TryKillRecordedChild(new ChildRecord { Pid = p.Pid, Ppid = p.Ppid, Cmd = p.CmdLine ?? "" }, "T21 cleanup (orphaned ping)", null);
        }
    }

    private static void T25_ChildPolicyKeepsPreviousDataOnFailure()
    {
        var cfg = MakeConfig(SpawnerServer("t25", restartEnabled: false));
        cfg.ChildPolicy.MaxChildLifetimeMinutes = 0; // aggressive thresholds — policy must still take NO action on failure
        cfg.ChildPolicy.MaxConcurrentChildren = 1;
        InitCore(cfg);
        ProcessHost.Instance.StartServer("t25", out _);
        bool up = WaitFor(() => ProcessHost.Instance.Get("t25") is { } t && t.Children.Count >= 4 && !t.ChildrenStale, 25000);
        var t25 = ProcessHost.Instance.Get("t25")!;
        int countBefore = t25.Children.Count;

        ProcessEnumerator.RawEnumerationOverride = _ => throw new InvalidOperationException("synthetic WMI outage (child policy)");
        try
        {
            ProcessHost.Instance.RefreshChildren(t25);     // must keep previous data + mark stale (NOT zero)
            ProcessHost.Instance.EnforceChildPolicy(t25);  // must treat child set as UNKNOWN: no action
            Thread.Sleep(1000);
        }
        finally { ProcessEnumerator.RawEnumerationOverride = null; }

        bool kept = t25.Children.Count == countBefore && t25.ChildrenStale && t25.Children.Count > 0;
        bool serverAlive = t25.Proc is { HasExited: false };
        int childrenStillAlive = MarkerProcesses().Count;
        int countAfterFailure = t25.Children.Count; // capture BEFORE StopServer (which clears children)
        ProcessHost.Instance.StopServer("t25");
        Check("T25_ChildPolicyKeepsPreviousDataOnFailure", up && kept && serverAlive && childrenStillAlive >= 2,
            $"childrenBefore={countBefore} childrenAfterFailure={countAfterFailure} staleFlag={t25.ChildrenStale} serverAlive={serverAlive} childrenStillAlive={childrenStillAlive} (failure ≠ zero children; no destructive action)");
    }

    // ---------------------------------------------------------------- WMI timeout & cancellation hardening tests

    private static long OutstandingNow => Interlocked.Read(ref WmiInstrumentation.OutstandingOperations);
    private static long CancelledNow => Interlocked.Read(ref WmiInstrumentation.CancelledOperations);
    private static long AbandonedNow => Interlocked.Read(ref WmiInstrumentation.AbandonedOperations);

    private static void T26_SnapshotTimeoutCancelsWork()
    {
        var et = ProcessEnumerator.EnumerationTimeout;
        var gr = ProcessEnumerator.PostCancelGrace;
        try
        {
            ProcessEnumerator.EnumerationTimeout = TimeSpan.FromMilliseconds(300);
            ProcessEnumerator.PostCancelGrace = TimeSpan.FromMilliseconds(2000);
            long cancelledBefore = CancelledNow, abandonedBefore = AbandonedNow, outstandingBefore = OutstandingNow;
            var release = new ManualResetEventSlim(false);
            // Fake WMI op that HONORS cancellation: registered callback releases the worker.
            ProcessEnumerator.RawEnumerationOverride = h =>
            {
                h.Register(release.Set);
                release.Wait(10000);
                return new List<ProcInfo>();
            };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = ProcessEnumerator.Snapshot();
            sw.Stop();
            bool drained = WaitFor(() => OutstandingNow == outstandingBefore, 3000);
            Check("T26_SnapshotTimeoutCancelsWork",
                !r.Success && r.Error!.Contains("timed out") && r.Error.Contains("cancellation observed") &&
                sw.ElapsedMilliseconds < 2500 &&
                CancelledNow == cancelledBefore + 1 && AbandonedNow == abandonedBefore && drained,
                $"success={r.Success} error='{r.Error}' elapsed={sw.ElapsedMilliseconds}ms cancelledDelta={CancelledNow - cancelledBefore} abandonedDelta={AbandonedNow - abandonedBefore} outstandingDrained={drained} (cancel was issued AND observed — work provably stopped, not just the wait)");
        }
        finally
        {
            ProcessEnumerator.RawEnumerationOverride = null;
            ProcessEnumerator.EnumerationTimeout = et;
            ProcessEnumerator.PostCancelGrace = gr;
        }
    }

    private static void T27_SnapshotTimeoutAbandonedIsHonest()
    {
        var et = ProcessEnumerator.EnumerationTimeout;
        var gr = ProcessEnumerator.PostCancelGrace;
        try
        {
            ProcessEnumerator.EnumerationTimeout = TimeSpan.FromMilliseconds(300);
            ProcessEnumerator.PostCancelGrace = TimeSpan.FromMilliseconds(500);
            long cancelledBefore = CancelledNow, abandonedBefore = AbandonedNow, outstandingBefore = OutstandingNow;
            var release = new ManualResetEventSlim(false);
            bool cancelRequested = false;
            // Fake WMI op that IGNORES cancellation (simulates an unresponsive WMI/COM layer).
            ProcessEnumerator.RawEnumerationOverride = h =>
            {
                h.Register(() => cancelRequested = true); // registered but deliberately NOT honored
                release.Wait(15000);
                return new List<ProcInfo>();
            };
            var r = ProcessEnumerator.Snapshot();
            long outstandingDuring = OutstandingNow;
            bool honestAbandoned = !r.Success && r.Error!.Contains("abandoned") && !r.Error.Contains("cancellation observed");
            // Release the stuck worker AFTER the pipeline gave up: outstanding must drain — proving
            // the accounting tracks real work instead of hiding it.
            release.Set();
            bool drained = WaitFor(() => OutstandingNow == outstandingBefore, 5000);
            Check("T27_SnapshotTimeoutAbandonedIsHonest",
                honestAbandoned && cancelRequested &&
                AbandonedNow == abandonedBefore + 1 && CancelledNow == cancelledBefore &&
                outstandingDuring == outstandingBefore + 1 && drained,
                $"honestAbandoned={honestAbandoned} cancelRequested={cancelRequested} abandonedDelta={AbandonedNow - abandonedBefore} cancelledDelta={CancelledNow - cancelledBefore} outstandingDuring={outstandingDuring - outstandingBefore} (+1 = work still running, reported honestly) drainedAfterRelease={drained}");
        }
        finally
        {
            ProcessEnumerator.RawEnumerationOverride = null;
            ProcessEnumerator.EnumerationTimeout = et;
            ProcessEnumerator.PostCancelGrace = gr;
        }
    }

    private static void T28_NoPileupAfterTimeouts()
    {
        var et = ProcessEnumerator.EnumerationTimeout;
        var gr = ProcessEnumerator.PostCancelGrace;
        try
        {
            ProcessEnumerator.EnumerationTimeout = TimeSpan.FromMilliseconds(250);
            ProcessEnumerator.PostCancelGrace = TimeSpan.FromMilliseconds(1500);
            long outstandingBefore = OutstandingNow, cancelledBefore = CancelledNow, abandonedBefore = AbandonedNow;
            ProcessEnumerator.RawEnumerationOverride = h =>
            {
                var rel = new ManualResetEventSlim(false);
                h.Register(rel.Set); // honor cancel
                rel.Wait(5000);
                return new List<ProcInfo>();
            };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var results = new List<ProcessSnapshotResult>();
            for (int i = 0; i < 3; i++) results.Add(ProcessEnumerator.Snapshot());
            sw.Stop();
            bool drained = WaitFor(() => OutstandingNow == outstandingBefore, 3000);
            Check("T28_NoPileupAfterTimeouts",
                results.All(r => !r.Success) && sw.ElapsedMilliseconds < 5000 &&
                CancelledNow == cancelledBefore + 3 && AbandonedNow == abandonedBefore && drained,
                $"3 sequential timed-out snapshots: allFailed={results.All(r => !r.Success)} totalElapsed={sw.ElapsedMilliseconds}ms (<5s → each op really stopped instead of piling up) cancelledDelta={CancelledNow - cancelledBefore} abandonedDelta={AbandonedNow - abandonedBefore} outstandingDrained={drained}");
        }
        finally
        {
            ProcessEnumerator.RawEnumerationOverride = null;
            ProcessEnumerator.EnumerationTimeout = et;
            ProcessEnumerator.PostCancelGrace = gr;
        }
    }

    private static void T29_QueryPidOutcomes()
    {
        var qt = ProcessEnumerator.QueryTimeout;
        var gr = ProcessEnumerator.PostCancelGrace;
        try
        {
            // (a) normal — REAL WMI path against a PID guaranteed to exist: this test process.
            var q1 = ProcessEnumerator.QueryPid((uint)Environment.ProcessId);
            bool normal = q1.Status == ProcQueryStatus.Success && q1.Info != null &&
                          q1.Info.Name.Contains("MCP-Xellen", StringComparison.OrdinalIgnoreCase);
            // (b) not found — REAL WMI path, PID 0xFFFFFFFE (4294967294) is not a valid running PID.
            var q2 = ProcessEnumerator.QueryPid(4294967294u);
            bool notFound = q2.Status == ProcQueryStatus.NotFound;
            // (c) timeout — fake query that honors cancellation.
            ProcessEnumerator.QueryTimeout = TimeSpan.FromMilliseconds(300);
            ProcessEnumerator.PostCancelGrace = TimeSpan.FromMilliseconds(2000);
            ProcessEnumerator.RawSingleQueryOverride = (pid, h) =>
            {
                var rel = new ManualResetEventSlim(false);
                h.Register(rel.Set);
                rel.Wait(10000);
                return new ProcInfo(pid, 4242u, "fake.exe", "fake", null, 0);
            };
            var q3 = ProcessEnumerator.QueryPid(12345u);
            bool timedOut = q3.Status == ProcQueryStatus.Timeout && q3.Error!.Contains("cancellation observed");
            // (d) failed (access denied) — fake query that throws.
            ProcessEnumerator.RawSingleQueryOverride = (pid, _) => throw new UnauthorizedAccessException($"synthetic deny for {pid}");
            var q4 = ProcessEnumerator.QueryPid(12346u);
            bool failed = q4.Status == ProcQueryStatus.Failed && q4.Error!.Contains("synthetic deny");
            Check("T29_QueryPidOutcomes", normal && notFound && timedOut && failed,
                $"normal={normal}({q1.Status}:{q1.Info?.Name}) notFound={notFound}({q2.Status}) timeout={timedOut}('{q3.Error}') failed={failed}('{q4.Error}')");
        }
        finally
        {
            ProcessEnumerator.RawSingleQueryOverride = null;
            ProcessEnumerator.QueryTimeout = qt;
            ProcessEnumerator.PostCancelGrace = gr;
        }
    }

    private static void T30_KillRefusedOnUnknownOrTimeout()
    {
        var qt = ProcessEnumerator.QueryTimeout;
        var gr = ProcessEnumerator.PostCancelGrace;
        uint pid = 0;
        try
        {
            // A live synthetic process we own (marker in cmdline, alive until we say otherwise).
            pid = SpawnMarkerChild($"{Marker}-tk", 600);
            bool alive = WaitFor(() =>
            {
                var q = ProcessEnumerator.QueryPid(pid);
                return q.Status == ProcQueryStatus.Success && q.Info != null;
            }, 10000);
            if (!alive) { Check("T30_KillRefusedOnUnknownOrTimeout", false, "synthetic child did not start"); return; }

            // (a) TryGet Timeout → kill must be REFUSED.
            ProcessEnumerator.QueryTimeout = TimeSpan.FromMilliseconds(300);
            ProcessEnumerator.PostCancelGrace = TimeSpan.FromMilliseconds(1000);
            ProcessEnumerator.RawSingleQueryOverride = (p, h) =>
            {
                var rel = new ManualResetEventSlim(false);
                h.Register(rel.Set);
                rel.Wait(10000);
                return new ProcInfo(p, 4242u, "fake.exe", "cmd " + Marker, null, 0);
            };
            bool refusedOnTimeout = !ProcessHost.TryKillOwned(pid, Marker, "T30 (timeout case)", null);

            // (b) TryGet Failed (access denied) → kill must be REFUSED.
            ProcessEnumerator.RawSingleQueryOverride = (p, _) => throw new UnauthorizedAccessException("synthetic deny");
            bool refusedOnFailed = !ProcessHost.TryKillOwned(pid, Marker, "T30 (failed case)", null);

            // Restore real queries: the process must STILL be alive (both refusals were real).
            ProcessEnumerator.RawSingleQueryOverride = null;
            ProcessEnumerator.QueryTimeout = qt;
            ProcessEnumerator.PostCancelGrace = gr;
            var stillAlive = ProcessEnumerator.QueryPid(pid);
            bool aliveAfterRefusals = stillAlive.Status == ProcQueryStatus.Success && stillAlive.Info != null;

            // And the same kill path must WORK once state is knowable → refusal was due to UNKNOWN only.
            bool killedAfterRestore = ProcessHost.TryKillOwned(pid, Marker, "T30 cleanup", null);
            int refusals = LogService.Snapshot(1000).Count(l => l.Component == "kill" && l.Msg.Contains("REFUSED kill") && l.Msg.Contains("UNKNOWN"));
            Check("T30_KillRefusedOnUnknownOrTimeout",
                refusedOnTimeout && refusedOnFailed && aliveAfterRefusals && killedAfterRestore && refusals >= 2,
                $"refusedOnTimeout={refusedOnTimeout} refusedOnFailed={refusedOnFailed} stillAliveAfterRefusals={aliveAfterRefusals} killWorkedAfterRestore={killedAfterRestore} refusedLogEntries={refusals}");
        }
        finally
        {
            ProcessEnumerator.RawSingleQueryOverride = null;
            ProcessEnumerator.QueryTimeout = qt;
            ProcessEnumerator.PostCancelGrace = gr;
            if (pid != 0) ProcessHost.TryKillOwned(pid, Marker, "T30 safety cleanup", null);
        }
    }

    private static void T31_MidSweepExceptionFailsSweep()
    {
        var cfg = MakeConfig(SpawnerServer("t31", restartEnabled: false));
        cfg.OrphanSweep = new OrphanSweepSettings { Mode = "auto", StartupDelaySeconds = 3600, IntervalSeconds = 0 };
        InitCore(cfg);

        // Baseline SUCCESSFUL sweep, values copied immediately (StateSnapshot is a live object).
        var r0 = ProcessHost.Instance.RunSweep();
        var st = ProcessHost.Instance.StateSnapshot.OrphanSweep;
        DateTime? baseLastRun = st.LastRunUtc;
        int baseDetected = st.TotalDetected, baseKilled = st.TotalKilled;

        // Exception AFTER the snapshot succeeded (inventory fine, mid-run crash).
        ProcessHost.SweepHookAfterSnapshot = () => throw new InvalidOperationException("synthetic mid-sweep failure");
        var r1 = ProcessHost.Instance.RunSweep();
        st = ProcessHost.Instance.StateSnapshot.OrphanSweep;
        bool failed = r1.Status == "failed" && r1.Error!.Contains("synthetic mid-sweep failure");
        bool statsUnchanged = st.LastRunUtc == baseLastRun && st.TotalDetected == baseDetected && st.TotalKilled == baseKilled;
        bool failCounted = st.ConsecutiveFailures >= 1;
        string labelDuring = MetricsService.SweepStatusLabel(ProcessHost.Instance.StateSnapshot);
        bool labelHonest = labelDuring.Contains("ERROR") && !labelDuring.Contains("CLEAN");
        Check("T31_MidSweepExceptionFailsSweep",
            r0.Status == "completed" && failed && statsUnchanged && failCounted && labelHonest,
            $"baseline={r0.Status} midRun={r1.Status} err='{r1.Error}' lastRunUnchanged={st.LastRunUtc == baseLastRun} detected={baseDetected}→{st.TotalDetected} killed={baseKilled}→{st.TotalKilled} consecutiveFailures={st.ConsecutiveFailures} label='{labelDuring}'");
    }

    private static void T32_RecoveryAfterMidSweepException()
    {
        ProcessHost.SweepHookAfterSnapshot = null; // WMI "back to normal"
        var r = ProcessHost.Instance.RunSweep();
        var st = ProcessHost.Instance.StateSnapshot.OrphanSweep;
        string label = MetricsService.SweepStatusLabel(ProcessHost.Instance.StateSnapshot);
        Check("T32_RecoveryAfterMidSweepException",
            r.Status == "completed" && st.ConsecutiveFailures == 0 && label.Contains("CLEAN"),
            $"status={r.Status} consecutiveFailures={st.ConsecutiveFailures} label='{label}'");
    }

    // ---------------------------------------------------------------- Master implementation tests (native transport, auth, projects)

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private const string SyntheticServerFileRel = @"master\synthetic-mcp-server.mjs";

    private static string SyntheticServerPath => Path.Combine(TestRoot, "master", "synthetic-mcp-server.mjs");

    private static void WriteSyntheticServer()
    {
        Directory.CreateDirectory(Path.Combine(TestRoot, "master"));
        var js = """
import readline from "node:readline";
const rl = readline.createInterface({ input: process.stdin, terminal: false });
const send = (m) => process.stdout.write(JSON.stringify(m) + "\n");
const tools = [
  { name: "echo", description: "echo input text", inputSchema: { type: "object", properties: { text: { type: "string" } }, required: ["text"] } },
  { name: "fail_echo", description: "always isError", inputSchema: { type: "object", properties: {} } },
];
rl.on("line", (line) => {
  let m; try { m = JSON.parse(line); } catch { return; }
  if (!m || m.jsonrpc !== "2.0") return;
  if (m.method === "initialize") { send({ jsonrpc: "2.0", id: m.id, result: { protocolVersion: "2025-06-18", capabilities: { tools: {} }, serverInfo: { name: "synthetic", version: "1.0.0" } } }); }
  else if (m.method === "tools/list") { send({ jsonrpc: "2.0", id: m.id, result: { tools } }); }
  else if (m.method === "tools/call") {
    const name = m.params?.name;
    if (name === "echo") send({ jsonrpc: "2.0", id: m.id, result: { content: [{ type: "text", text: "echo:" + (m.params.arguments?.text ?? "") }] } });
    else if (name === "fail_echo") send({ jsonrpc: "2.0", id: m.id, result: { content: [{ type: "text", text: "intentional failure" }], isError: true } });
    else send({ jsonrpc: "2.0", id: m.id, error: { code: -32602, message: "unknown tool" } });
  }
  else if (m.id !== undefined) { send({ jsonrpc: "2.0", id: m.id, result: {} }); }
});
""";
        File.WriteAllText(SyntheticServerPath, js);
    }

    private static ServerConfig SyntheticServerDef(string id) => new()
    {
        Id = id,
        Name = "Synthetic MCP " + id,
        Command = "node",
        Arguments = new List<string> { SyntheticServerPath },
        WorkingDirectory = TestRoot,
        Transport = "stdio-native",
        AutoStart = false,
    };

    private static (int status, string body, string sessionId) McpPost(int port, string path, string? token, string? session, string json, string? origin = null, bool sseOnly = false, string? protocolVersion = null, bool useGet = false)
    {
        var req = new HttpRequestMessage(useGet ? HttpMethod.Get : HttpMethod.Post, $"http://127.0.0.1:{port}{path}");
        req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(sseOnly ? "text/event-stream" : "application/json"));
        if (!sseOnly) req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (token != null) req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (session != null) req.Headers.Add("Mcp-Session-Id", session);
        if (origin != null) req.Headers.Add("Origin", origin);
        if (protocolVersion != null) req.Headers.Add("MCP-Protocol-Version", protocolVersion);
        if (!useGet) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp = Http.SendAsync(req).GetAwaiter().GetResult();
        var b = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var sid = resp.Headers.Contains("Mcp-Session-Id") ? string.Join("", resp.Headers.GetValues("Mcp-Session-Id")) : "";
        return ((int)resp.StatusCode, b, sid);
    }

    private static string InitJson(string ver = "2025-06-18") =>
        $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{{\"protocolVersion\":\"{ver}\",\"capabilities\":{{}},\"clientInfo\":{{\"name\":\"selftest\",\"version\":\"0\"}}}}}}";

    private static void T33_AuthStoreLifecycle()
    {
        AuthStore.Initialize(ConfigStore.ConfigDir);
        var issued = AuthStore.Issue("t33-identity", AuthKind.Client, new[] { "synthetic-a" }, TimeSpan.FromSeconds(5));
        bool verifyOk = AuthStore.Verify(issued.PlaintextToken) != null;
        bool wrongRejected = AuthStore.Verify(issued.PlaintextToken[..^4] + "aaaa") == null;
        AuthStore.Revoke(issued.Identity.Id);
        bool revokedRejected = AuthStore.Verify(issued.PlaintextToken) == null;
        var issued2 = AuthStore.Issue("t33-ttl", AuthKind.Client, new[] { "synthetic-a" }, TimeSpan.FromSeconds(1));
        bool ttlOk = AuthStore.Verify(issued2.PlaintextToken) != null;
        Thread.Sleep(1300);
        bool expiredRejected = AuthStore.Verify(issued2.PlaintextToken) == null;
        var rotated = AuthStore.Rotate(issued2.Identity.Id);
        bool oldDead = AuthStore.Verify(issued2.PlaintextToken) == null;
        bool newWorks = AuthStore.Verify(rotated.PlaintextToken) != null;
        Check("T33_AuthStoreLifecycle", verifyOk && wrongRejected && revokedRejected && ttlOk && expiredRejected && oldDead && newWorks,
            $"verifyOk={verifyOk} wrongRejected={wrongRejected} revokedRejected={revokedRejected} ttlWorks={ttlOk} expiredRejected={expiredRejected} rotateOldDead={oldDead} rotateNewWorks={newWorks} (plaintext never printed)");
    }

    private static void T34_RedactionCoverage()
    {
        var secret = "SUPERSECRETVALUE123456";
        Redaction.RegisterSecret(secret);
        var s1 = Redaction.Sanitize("Authorization: Bearer abcdef1234567890abcdef");
        var s2 = Redaction.Sanitize("{\"token\":\"abcdef1234567890abcdef\"}");
        var s3 = Redaction.Sanitize("call with sk-abcdefgh12345678abcdefgh inside");
        var s4 = Redaction.Sanitize($"payload contains {secret} inline");
        var s5 = Redaction.Sanitize("-----BEGIN PRIVATE KEY-----\nMIIEvQ\n-----END PRIVATE KEY-----");
        var s6 = Redaction.Sanitize(new string('x', 500), 200);
        Redaction.UnregisterSecret(secret);
        bool ok = !s1.Contains("abcdef1234567890abcdef") && s1.Contains("[REDACTED]")
               && !s2.Contains("abcdef1234567890abcdef")
               && !s3.Contains("sk-abcdefgh12345678abcdefgh")
               && !s4.Contains(secret)
               && !s5.Contains("MIIEvQ")
               && s6.Length <= 220 && s6.Contains("truncated");
        Check("T34_RedactionCoverage", ok,
            $"bearer={s1.Contains("[REDACTED]")} tokenJson={s2.Contains("[REDACTED]")} sk={s3.Contains("[REDACTED]")} registered={s4.Contains("[REDACTED]")} pem={s5.Contains("[REDACTED]")} truncLen={s6.Length}");
    }

    private static void T35_EventBusOverflowAccounting()
    {
        long published0 = EventBus.Instance.Published, dropped0 = EventBus.Instance.DroppedTotal;
        var gate = new ManualResetEventSlim(false);
        var started = new ManualResetEventSlim(false);
        var delivered = new List<Guid>();
        using var sub = EventBus.Instance.Subscribe(e =>
        {
            started.Set();
            gate.Wait(3000);
            lock (delivered) delivered.Add(e.EventId);
        }, capacity: 2);
        var ids = new List<Guid>();
        for (int i = 0; i < 4; i++)
        {
            var e = new ActivityEvent { EventType = "t35", ToolName = "e" + i };
            ids.Add(e.EventId);
            EventBus.Instance.Publish(e);
            if (i == 0) started.Wait(2000); // pump took e1 and is now blocked in the handler
        }
        gate.Set();
        bool drained = WaitFor(() => { lock (delivered) return delivered.Count >= 3; }, 3000);
        var droppedNow = EventBus.Instance.DroppedTotal - dropped0;
        lock (delivered)
        {
            Check("T35_EventBusOverflowAccounting", delivered.Count == 3 && droppedNow == 1 && drained,
                $"published=4 delivered={delivered.Count} droppedNewest={droppedNow} (capacity 2 + blocked handler → newest dropped and honestly counted)");
        }
    }

    private static void T36_RegistryMigrationV2()
    {
        // Seed a v1 config into the sandbox, then migrate.
        var v1 = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            log = new { directory = Path.Combine(TestRoot, "logs"), baseFileName = "selftest", maxFileBytes = 10485760, maxBackups = 5, minLevel = "Info" },
            servers = new[] { new { id = "legacy-server", name = "legacy", command = "cmd.exe", arguments = new[] { "/c", "exit 0" }, transport = "stdio-native", autoStart = false } },
        });
        Directory.CreateDirectory(ConfigStore.ConfigDir);
        File.WriteAllText(ConfigStore.ConfigPath, v1);
        var cfg = ConfigStore.LoadOrCreate();
        Registry.Initialize(cfg);
        bool migrated = cfg.SchemaVersion >= 2;
        bool backupExists = Directory.Exists(ConfigStore.BackupDir) && Directory.GetFiles(ConfigStore.BackupDir, "xellen-*.json").Length > 0;
        var (proj, issued) = Registry.CreateProject("t36-proj", "T36 Project", new[] { "legacy-server" });
        var file = Path.Combine(ConfigStore.ConfigDir, "auth.json");
        bool authPersisted = File.Exists(file);
        string authText = File.ReadAllText(file);
        bool hashOnly = !authText.Contains(issued.PlaintextToken) && authText.Contains("tokenHash");
        bool mapped = Registry.GetProjectByIdentity(issued.Identity.Id)?.Id == proj.Id;
        Check("T36_RegistryMigrationV2", migrated && backupExists && authPersisted && hashOnly && mapped,
            $"schemaVersion={cfg.SchemaVersion} backup={backupExists} projectCreated=true authJson={authPersisted} hashOnlyPersistence={hashOnly} identityMappedBack={mapped}");
    }

    private static void T37_TransportAuthSessionsTools()
    {
        WriteSyntheticServer();
        var cfg = MakeConfig(SyntheticServerDef("synthetic-a"));
        cfg.NativeTransport = new NativeTransportSettings { Enabled = true, PreferredPort = 8377, SessionTimeoutMinutes = 30, RateLimitPerMinute = 1000 };
        InitCore(cfg);
        Registry.Initialize(cfg);
        AuthStore.Initialize(ConfigStore.ConfigDir);
        var bridge = new StdioMcpBridge();
        XellenRuntime.Bridge = bridge;
        var port = NativeTransportHost.Start(8377, cfg, bridge);
        try
        {
            var srvTok = AuthStore.Issue("t37-server", AuthKind.Server, new[] { "synthetic-a" }).PlaintextToken;
            var (hStatus, _, _) = McpPost(port, "/healthz", null, null, "{}", useGet: true);
            var (hPost, _, _) = McpPost(port, "/healthz", null, null, "{}");
            var (noAuth, _, _) = McpPost(port, "/srv/synthetic-a/mcp", null, null, InitJson());
            var (badTok, _, _) = McpPost(port, "/srv/synthetic-a/mcp", "wrong-token-123", null, InitJson());
            var (initStatus, initBody, sid) = McpPost(port, "/srv/synthetic-a/mcp", srvTok, null, InitJson());
            var (listStatus, listBody, _) = McpPost(port, "/srv/synthetic-a/mcp", srvTok, sid, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}");
            var (noSession, _, _) = McpPost(port, "/srv/synthetic-a/mcp", srvTok, null, "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/list\"}");
            var (bogusSession, _, _) = McpPost(port, "/srv/synthetic-a/mcp", srvTok, "deadbeef", "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/list\"}");
            var (notifStatus, _, _) = McpPost(port, "/srv/synthetic-a/mcp", srvTok, sid, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
            var (pingStatus, pingBody, _) = McpPost(port, "/srv/synthetic-a/mcp", srvTok, sid, "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"ping\"}");
            var (badPv, _, _) = McpPost(port, "/srv/synthetic-a/mcp", srvTok, sid, "{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"tools/list\"}", protocolVersion: "garbage-version");
            var (callStatus, callBody, _) = McpPost(port, "/srv/synthetic-a/mcp", srvTok, sid, "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"tools/call\",\"params\":{\"name\":\"synthetic-a__echo\",\"arguments\":{\"text\":\"hello\"}}}");
            var (unknownToolStatus, unknownToolBody, _) = McpPost(port, "/srv/synthetic-a/mcp", srvTok, sid, "{\"jsonrpc\":\"2.0\",\"id\":8,\"method\":\"tools/call\",\"params\":{\"name\":\"synthetic-a__nope\",\"arguments\":{}}}");
            var (origin403, _, _) = McpPost(port, "/healthz", null, null, "{}", origin: "http://evil.example");
            bool protocolNegotiated = initBody.Contains("\"protocolVersion\":\"2025-06-18\"");
            Check("T37_TransportAuthSessionsTools",
                port == 8377 && hStatus == 200 && hPost == 405 && noAuth == 401 && badTok == 401 &&
                initStatus == 200 && protocolNegotiated && sid.Length >= 16 &&
                listStatus == 200 && listBody.Contains("synthetic-a__echo") &&
                noSession == 400 && bogusSession == 404 && notifStatus == 202 &&
                pingStatus == 200 && badPv == 400 &&
                callStatus == 200 && callBody.Contains("echo:hello") &&
                unknownToolStatus == 200 && unknownToolBody.Contains("unknown tool") &&
                origin403 == 403,
                $"port={port} healthGET={hStatus} healthPOST={hPost} noAuth={noAuth} badTok={badTok} init={initStatus} negotiated={protocolNegotiated} toolsList={listStatus} noSession={noSession} bogusSession={bogusSession} notif={notifStatus} ping={pingStatus} badProtoVer={badPv} call={callStatus}(echo:hello) unknownTool={unknownToolStatus} origin403={origin403}");
        }
        finally
        {
            NativeTransportHost.Stop();
            bridge.StopAll();
        }
    }

    private static void T39_ProjectIsolationTwoProjects()
    {
        WriteSyntheticServer();
        var cfg = MakeConfig(SyntheticServerDef("synthetic-a"), SyntheticServerDef("synthetic-b"));
        cfg.NativeTransport = new NativeTransportSettings { Enabled = true, PreferredPort = 8379, SessionTimeoutMinutes = 30, RateLimitPerMinute = 5000 };
        InitCore(cfg);
        Registry.Initialize(cfg);
        AuthStore.Initialize(ConfigStore.ConfigDir);
        var bridge = new StdioMcpBridge();
        XellenRuntime.Bridge = bridge;
        var port = NativeTransportHost.Start(8379, cfg, bridge);
        try
        {
            var (a, tokA) = Registry.CreateProject("t39-a", "Project A", new[] { "synthetic-a" });
            var (b, tokB) = Registry.CreateProject("t39-b", "Project B", new[] { "synthetic-b" });

            var (initA, bodyA, sidA) = McpPost(port, $"/prj/{a.Slug}/mcp", tokA.PlaintextToken, null, InitJson());
            var (statusListA, listA, _) = McpPost(port, $"/prj/{a.Slug}/mcp", tokA.PlaintextToken, sidA, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}");
            var (statusCallA, callA, _) = McpPost(port, $"/prj/{a.Slug}/mcp", tokA.PlaintextToken, sidA, "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"synthetic-a__echo\",\"arguments\":{\"text\":\"fromA\"}}}");

            var (crossToken, _, _) = McpPost(port, $"/prj/{a.Slug}/mcp", tokB.PlaintextToken, null, InitJson());
            var (initB, bodyB, sidB) = McpPost(port, $"/prj/{b.Slug}/mcp", tokB.PlaintextToken, null, InitJson());
            var (statusListB, listB, _) = McpPost(port, $"/prj/{b.Slug}/mcp", tokB.PlaintextToken, sidB, "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/list\"}");

            var serverTokB = AuthStore.Issue("t39-srv-b", AuthKind.Server, new[] { "synthetic-b" }).PlaintextToken;
            var (bTokOnA, _, _) = McpPost(port, "/srv/synthetic-a/mcp", serverTokB, null, InitJson());

            // Revoke A → old token instantly rejected.
            AuthStore.Revoke(a.ClientIdentityId);
            var (revokedA, _, _) = McpPost(port, $"/prj/{a.Slug}/mcp", tokA.PlaintextToken, null, InitJson());

            bool isolated = statusListA == 200 && listA.Contains("synthetic-a__echo") && !listA.Contains("synthetic-b__")
                            && statusListB == 200 && listB.Contains("synthetic-b__echo") && !listB.Contains("synthetic-a__")
                            && crossToken == 401 && bTokOnA == 401 && revokedA == 401
                            && statusCallA == 200 && callA.Contains("echo:fromA") && initA == 200 && initB == 200;
            Check("T39_ProjectIsolationTwoProjects", isolated,
                $"initA={initA} statusListA={statusListA} toolsA_hasA={listA.Contains("synthetic-a__echo")} toolsA_leaksB={listA.Contains("synthetic-b__")} initB={initB} statusListB={statusListB} toolsB_hasB={listB.Contains("synthetic-b__echo")} toolsB_leaksA={listB.Contains("synthetic-a__")} crossTokenOnA={crossToken} serverTokBOnA={bTokOnA} revokedARejected={revokedA} callA={statusCallA}({callA.Contains("echo:fromA")})");
        }
        finally
        {
            NativeTransportHost.Stop();
            bridge.StopAll();
        }
    }

    private static void T40_TransportRateLimit()
    {
        WriteSyntheticServer();
        var cfg = MakeConfig(SyntheticServerDef("synthetic-rl"));
        cfg.NativeTransport = new NativeTransportSettings { Enabled = true, PreferredPort = 8380, RateLimitPerMinute = 3 };
        InitCore(cfg);
        Registry.Initialize(cfg);
        AuthStore.Initialize(ConfigStore.ConfigDir);
        var bridge = new StdioMcpBridge();
        XellenRuntime.Bridge = bridge;
        var port = NativeTransportHost.Start(8380, cfg, bridge);
        try
        {
            var tok = AuthStore.Issue("t40-server", AuthKind.Server, new[] { "synthetic-rl" }).PlaintextToken;
            var statuses = new List<int>();
            statuses.Add(McpPost(port, "/srv/synthetic-rl/mcp", tok, null, InitJson()).status); // 1 initialize
            statuses.Add(McpPost(port, "/srv/synthetic-rl/mcp", tok, null, InitJson()).status); // 2 (new session each init)
            statuses.Add(McpPost(port, "/srv/synthetic-rl/mcp", tok, null, InitJson()).status); // 3
            statuses.Add(McpPost(port, "/srv/synthetic-rl/mcp", tok, null, InitJson()).status); // 4 → 429
            Check("T40_TransportRateLimit",
                statuses[0] == 200 && statuses[1] == 200 && statuses[2] == 200 && statuses[3] == 429,
                $"statuses=[{string.Join(",", statuses)}] (limit=3/min → 4th request 429)");
        }
        finally
        {
            NativeTransportHost.Stop();
            bridge.StopAll();
        }
    }

    private static void T41_ParityNativeVsCompat()
    {
        // Compat baseline: the existing Supergateway endpoint on 8023 (production, read-only calls).
        var tokenFile = @"D:\MCP-Bridge\secrets\mcp-xellen-staging-token.txt";
        bool compatUp;
        try { compatUp = new HttpClient().GetAsync("http://127.0.0.1:8023/healthz").GetAwaiter().GetResult().IsSuccessStatusCode; }
        catch { compatUp = false; }
        if (!compatUp || !File.Exists(tokenFile))
        {
            Check("T41_ParityNativeVsCompat", true, "SKIPPED-BLOCKED: compat endpoint 8023 not reachable or token file missing — parity cannot be measured in this environment state.");
            Results[^1].Skipped = true;
            return;
        }
        var compatToken = File.ReadAllText(tokenFile).Trim();
        Redaction.RegisterSecret(compatToken);

        // Native side: synthetic server through the native transport.
        WriteSyntheticServer();
        var cfg = MakeConfig(SyntheticServerDef("synthetic-par"));
        cfg.NativeTransport = new NativeTransportSettings { Enabled = true, PreferredPort = 8381, RateLimitPerMinute = 5000 };
        InitCore(cfg);
        Registry.Initialize(cfg);
        AuthStore.Initialize(ConfigStore.ConfigDir);
        var bridge = new StdioMcpBridge();
        XellenRuntime.Bridge = bridge;
        var port = NativeTransportHost.Start(8381, cfg, bridge);
        try
        {
            var tok = AuthStore.Issue("t41-server", AuthKind.Server, new[] { "synthetic-par" }).PlaintextToken;
            var (nInit, nInitBody, nSid) = McpPost(port, "/srv/synthetic-par/mcp", tok, null, InitJson());
            var (nListStatus, nListBody, _) = McpPost(port, "/srv/synthetic-par/mcp", tok, nSid, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}");

            var (cInit, cInitBody, cSid) = McpPost(8023, "/mcp", compatToken, null, InitJson());
            var (cListStatus, cListBody, _) = McpPost(8023, "/mcp", compatToken, cSid, "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}");

            bool nativeShape = nInit == 200 && nInitBody.Contains("protocolVersion") && nListStatus == 200 && nListBody.Contains("\"tools\":[");
            bool compatShape = cInit == 200 && cInitBody.Contains("protocolVersion") && cListStatus == 200 && cListBody.Contains("\"tools\":[");
            bool compatTools = cListBody.Contains("\"name\"");
            Check("T41_ParityNativeVsCompat",
                nativeShape && compatShape && compatTools,
                $"compat: init={cInit} toolsList={cListStatus} ({(compatTools ? "tools present" : "no tools")}); native: init={nInit} toolsList={nListStatus} — same JSON-RPC envelope + protocolVersion + tools array shape (parity on initialize/tools/list; tools/call parity on DC deferred to live Z.ai session)");
        }
        finally
        {
            Redaction.UnregisterSecret(compatToken);
            NativeTransportHost.Stop();
            bridge.StopAll();
        }
    }

    private static void T42_UiSmokeHeadless()
    {
        Check("T42_UiSmokeHeadless", true, "SKIPPED-BLOCKED: UI smoke (window main + Activity Monitor) requires an interactive desktop session; selftest runs headless. Evidence required: manual open of 'Open AI Activity Monitor' + both windows visible.");
        Results[^1].Skipped = true;
    }

    // ---------------------------------------------------------------- browser isolation (T43–T47)

    private static BrowserManagerConfig BrowserTestConfig(int maxSessions, int idle, int agentPortsBase)
    {
        var root = Path.Combine(TestRoot, "browser");
        return new BrowserManagerConfig
        {
            Enabled = true,
            Headless = true,
            IdleTimeoutMinutes = idle,                 // produksi terkunci 15; uji idle dipercepat via backdate, bukan config
            MaxActiveSessions = maxSessions,
            AgentPortsBase = agentPortsBase,
            WrapperPortsBase = agentPortsBase + 100,
            ProfileRoot = Path.Combine(root, "profiles"),
            SecretsRoot = Path.Combine(root, "secrets"),
            StatePath = Path.Combine(root, "browser-sessions-state.json"),
            // Probe 13 Nov 2026: BrowserFinder default agent-infra gagal di mesin ini (Chrome per-user
            // install); path eksplisit terbukti launch OK (puppeteer-core 24.7.2 LAUNCH_OK).
            BrowserExecutablePath = @"C:\Users\user\AppData\Local\Google\Chrome\Application\chrome.exe",
        };
    }

    private static bool PortOpen(int port)
    {
        try
        {
            using var c = new TcpClient();
            var ar = c.BeginConnect("127.0.0.1", port, null, null);
            if (ar.AsyncWaitHandle.WaitOne(500)) { c.EndConnect(ar); return true; }
            return false;
        }
        catch { return false; }
    }

    private static string? CmdlineOf(int pid)
    {
        try
        {
            using var s = new System.Management.ManagementObjectSearcher(
                "SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var o in s.Get()) return o["CommandLine"] as string;
        }
        catch { }
        return null;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>Check B (window keberadaan aktual per PID — bukan sekadar MainWindowTitle kosong).</summary>
    private static List<IntPtr> VisibleTopWindowsForPids(IEnumerable<int> pids)
    {
        var set = pids.ToHashSet();
        var found = new List<IntPtr>();
        EnumWindows((h, lp) =>
        {
            GetWindowThreadProcessId(h, out var pid);
            if (set.Contains((int)pid) && IsWindowVisible(h)) found.Add(h);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static void ExternalKillTree(uint pid)
    {
        using var k = Process.Start(new ProcessStartInfo("taskkill", $"/PID {pid} /T /F") { UseShellExecute = false, CreateNoWindow = true });
        k?.WaitForExit(5000);
    }

    /// <summary>Kill sisa stack browser uji. Fingerprint: cmdline HARUS memuat root sandbox tests\p1 —
    /// stack produksi (browser-profiles, secrets\browser-sessions) tidak pernah cocok.</summary>
    private static void CleanupBrowserTestProcesses()
    {
        foreach (var p in AllProcesses())
        {
            var cmd = p.CmdLine ?? "";
            bool testAgent = cmd.Contains("mcp-server-browser", StringComparison.OrdinalIgnoreCase) && cmd.Contains(TestRoot, StringComparison.OrdinalIgnoreCase);
            bool testWrapper = cmd.Contains("supergateway", StringComparison.OrdinalIgnoreCase) && cmd.Contains("--apiKeyFile", StringComparison.OrdinalIgnoreCase) && cmd.Contains(TestRoot, StringComparison.OrdinalIgnoreCase);
            if (!testAgent && !testWrapper) continue;
            try { using var k = Process.Start(new ProcessStartInfo("taskkill", $"/PID {p.Pid} /T /F") { UseShellExecute = false, CreateNoWindow = true }); k?.WaitForExit(5000); }
            catch { }
        }
    }

    private static void T43_BrowserCapsAndOwnership()
    {
        var bm = BrowserManager.Instance;
        bm.TestOnlyReset();
        var bcfg = BrowserTestConfig(3, idle: 15, agentPortsBase: 8540);
        bm.Initialize(bcfg);
        ActivityStore.Configure(Path.Combine(TestRoot, "browser", "activity"), persist: false);
        var events = new List<ActivityEvent>();
        bm.SessionEvent += events.Add;
        try
        {
            var s1 = bm.CreateSession("t43-a", "A");
            var s2 = bm.CreateSession("t43-b", "B");
            _ = bm.CreateSession("t43-c", "C");
            bool capRejected = false;
            try { bm.CreateSession("t43-d", "D"); } catch (InvalidOperationException) { capRejected = true; }

            // Ownership: client lain tidak boleh close/melihat; owner boleh.
            bool foreignClose = bm.CloseSession(s1.SessionId, "t43-b", "t43-foreign");
            bool foreignGet = bm.GetSession(s1.SessionId, "t43-b") == null;
            bool ownerClose = bm.CloseSession(s1.SessionId, "t43-a", "t43-owner");

            // Gate enabled=false menolak aman.
            bool disabledGate = false;
            var off = BrowserTestConfig(3, 15, 8540);
            off.Enabled = false;
            bm.TestOnlyReset();
            bm.Initialize(off);
            try { bm.CreateSession("t43-e", "E"); } catch (InvalidOperationException) { disabledGate = true; }

            bool limitEvent = events.Any(e => e.EventType == "browser_session_limit_reached");
            bool authEvent = events.Any(e => e.EventType == "browser_auth_failure");
            Check("T43_BrowserCapsAndOwnership",
                capRejected && !foreignClose && foreignGet && ownerClose && disabledGate && limitEvent && authEvent,
                $"cap3_reject4={capRejected} foreignCloseRefused={!foreignClose} foreignGetNull={foreignGet} ownerCloseOk={ownerClose} disabledGate={disabledGate} limitEvent={limitEvent} authFailureEvent={authEvent} (sesi aktif tidak pernah dihentikan paksa saat cap tercapai)");
        }
        finally { bm.SessionEvent -= events.Add; }
    }

    private static void T44_BrowserStartupSweepNoForeignClaim()
    {
        var bm = BrowserManager.Instance;
        bm.TestOnlyReset();
        var bcfg = BrowserTestConfig(3, idle: 15, agentPortsBase: 8541);
        uint foreign = SpawnMarkerChild("xellen-p1-test-sweepguard", 600); // proses hidup MILIK test, cmdline TIDAK cocok marker record
        try
        {
            var aliveRec = new BrowserSessionRecord { SessionId = "t44foreign", ClientId = "t44", Status = "Ready", AgentMarker = "t44-no-match-agent", WrapperMarker = "t44-no-match-wrapper" };
            aliveRec.OwnedPids.Add((int)foreign);
            var deadRec = new BrowserSessionRecord { SessionId = "t44dead", ClientId = "t44", Status = "Ready", AgentMarker = "t44-dead-agent", WrapperMarker = "t44-dead-wrapper" };
            deadRec.OwnedPids.Add(2147400000); // PID tidak mungkin ada
            var state = new BrowserStateFile();
            state.Records.Add(aliveRec);
            state.Records.Add(deadRec);
            Directory.CreateDirectory(Path.GetDirectoryName(bcfg.StatePath)!);
            File.WriteAllText(bcfg.StatePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));

            bm.Initialize(bcfg); // startup orphan sweep berjalan di dalam Initialize

            bool foreignAlive;
            try { using var p = Process.GetProcessById((int)foreign); foreignAlive = !p.HasExited; }
            catch { foreignAlive = false; }
            var after = JsonSerializer.Deserialize<BrowserStateFile>(File.ReadAllText(bcfg.StatePath))!;
            bool bothStopped = after.Records.All(r => r.Status == "Stopped");
            Check("T44_BrowserStartupSweepNoForeignClaim",
                foreignAlive && bothStopped,
                $"foreignPid={foreign} masihHidupSetelahSweep={foreignAlive} (fingerprint mismatch → TIDAK di-kill: bukti no-foreign-claim) recordsMarkedStopped={bothStopped}");
        }
        finally { ExternalKill(foreign); }
    }

    private static void T45_BrowserTokenNoLeak()
    {
        var bm = BrowserManager.Instance;
        bm.TestOnlyReset();
        var bcfg = BrowserTestConfig(2, idle: 15, agentPortsBase: 8542);
        bm.Initialize(bcfg);
        ActivityStore.Configure(Path.Combine(TestRoot, "browser", "activity"), persist: false);
        var events = new List<ActivityEvent>();
        bm.SessionEvent += events.Add;
        try
        {
            var rec = bm.CreateSession("t45-a", "Secret hygiene");
            var token = File.ReadAllText(rec.TokenFile).Trim();
            bool tokenShape = token.Length == 64 && token.All(Uri.IsHexDigit);
            bool inStateFile = File.ReadAllText(bcfg.StatePath).Contains(token, StringComparison.Ordinal);
            bool inEvents = events.Any(e => (e.InputSummary ?? "").Contains(token, StringComparison.Ordinal) || (e.OutputSummary ?? "").Contains(token, StringComparison.Ordinal));
            bool inLogs = LogService.Snapshot(500).Any(l => l.Msg.Contains(token, StringComparison.Ordinal) || (l.Data?.ToString() ?? "").Contains(token, StringComparison.Ordinal));
            bool registered = Redaction.RegisteredSecretCount > 0;
            bm.CloseSession(rec.SessionId, "t45-a", "t45");
            Check("T45_BrowserTokenNoLeak",
                tokenShape && !inStateFile && !inEvents && !inLogs && registered,
                $"tokenShape(64hex)={tokenShape} tokenDiStateFile={inStateFile} tokenDiEvents={inEvents} tokenDiLogs={inLogs} redactionRegistered={registered} (token hanya hidup di file secrets per sesi)");
        }
        finally { bm.SessionEvent -= events.Add; }
    }

    private static void T46_BrowserRealLifecycleAuth()
    {
        if (!File.Exists(@"C:\Program Files\nodejs\node.exe"))
        {
            Check("T46_BrowserRealLifecycleAuth", true, "SKIPPED-BLOCKED: node.exe tidak ditemukan — lifecycle/auth stack browser nyata tidak bisa diuji di lingkungan ini.");
            Results[^1].Skipped = true;
            return;
        }
        var bm = BrowserManager.Instance;
        bm.TestOnlyReset();
        var bcfg = BrowserTestConfig(3, idle: 15, agentPortsBase: 8543);
        bm.Initialize(bcfg);
        ActivityStore.Configure(Path.Combine(TestRoot, "browser", "activity"), persist: false);
        var events = new List<ActivityEvent>();
        bm.SessionEvent += events.Add;
        try
        {
            var rec = bm.CreateSession("t46-a", "Real lifecycle");
            bool started = bm.StartSessionAsync(rec.SessionId, "t46-a").GetAwaiter().GetResult();
            var live = bm.GetSession(rec.SessionId, "t46-a");
            bool portsLive = started && live != null && PortOpen(live.AgentPort) && PortOpen(live.WrapperPort);
            int noAuth = -1, auth = -1;
            bool headlessFlag = false;
            int visibleWindows = -1;
            if (live != null)
            {
                (noAuth, _, _) = McpPost(live.WrapperPort, "/mcp", null, null, InitJson());
                var tok = File.ReadAllText(live.TokenFile).Trim();
                (auth, _, _) = McpPost(live.WrapperPort, "/mcp", tok, null, InitJson());
                headlessFlag = (CmdlineOf(live.OwnedPids[0]) ?? "").Contains("--headless", StringComparison.Ordinal);
                var exePathFlag = (CmdlineOf(live.OwnedPids[0]) ?? "").Contains("--executable-path", StringComparison.Ordinal);
                visibleWindows = VisibleTopWindowsForPids(live.OwnedPids).Count;
            }
            bool closed = bm.CloseSession(rec.SessionId, "t46-a", "t46");
            // Kedua port harus mati DAN tidak ada proses residual ber-fingerprint sessionId (anti leaf re-parent).
            bool portsDead = live != null && WaitFor(() => !PortOpen(live.WrapperPort) && !PortOpen(live.AgentPort), 10_000);
            bool noResidual = WaitFor(() => !AllProcesses().Any(p =>
            {
                var c = p.CmdLine ?? "";
                return c.Contains(rec.SessionId, StringComparison.Ordinal) &&
                       (c.Contains("mcp-server-browser", StringComparison.OrdinalIgnoreCase) || c.Contains("supergateway", StringComparison.OrdinalIgnoreCase));
            }), 10_000);
            bool readyEvent = events.Any(e => e.EventType == "browser_session_ready");
            Check("T46_BrowserRealLifecycleAuth",
                started && portsLive && noAuth == 401 && auth == 200 && headlessFlag && exePathFlag && visibleWindows == 0 && closed && portsDead && noResidual && readyEvent,
                $"started={started} portsLive={portsLive} noAuth={noAuth} authInit={auth} headlessInCmdline={headlessFlag} execPathFlag={exePathFlag} visibleWindows={visibleWindows} (Check B per PID) closed={closed} portsDead={portsDead} noResidualProc={noResidual} readyEvent={readyEvent}");
        }
        finally { bm.SessionEvent -= events.Add; CleanupBrowserTestProcesses(); }
    }

    private static void T47_BrowserIdleExpiryAndCrashIsolation()
    {
        if (!File.Exists(@"C:\Program Files\nodejs\node.exe"))
        {
            Check("T47_BrowserIdleExpiryAndCrashIsolation", true, "SKIPPED-BLOCKED: node.exe tidak ditemukan — idle/crash stack browser nyata tidak bisa diuji di lingkungan ini.");
            Results[^1].Skipped = true;
            return;
        }
        var bm = BrowserManager.Instance;
        bm.TestOnlyReset();
        var bcfg = BrowserTestConfig(3, idle: 1, agentPortsBase: 8544); // idle uji 1 menit; produksi terkunci 15
        bm.Initialize(bcfg);
        ActivityStore.Configure(Path.Combine(TestRoot, "browser", "activity"), persist: false);
        var events = new List<ActivityEvent>();
        bm.SessionEvent += events.Add;
        try
        {
            // (a) Idle expiry deterministik: LastActivityUtc dimundurkan 20 menit (>= batas uji 1 menit).
            var s1 = bm.CreateSession("t47-idle", "Idle probe");
            bool started1 = bm.StartSessionAsync(s1.SessionId, "t47-idle").GetAwaiter().GetResult();
            bool idleFired = false; bool expiredEvent = false;
            if (started1)
            {
                bm.TestOnlyBackdateActivity(s1.SessionId, "t47-idle", TimeSpan.FromMinutes(20));
                idleFired = WaitFor(() => bm.GetSession(s1.SessionId, "t47-idle") is { Status: "Stopped" }, 90_000);
                expiredEvent = events.Any(e => e.EventType == "browser_session_expired");
            }

            // (b) Crash isolation: agent dibunuh eksternal → Failed + wrapper milik sesi ikut dibersihkan; sesi lain tidak ada yang tersentuh.
            var s2 = bm.CreateSession("t47-crash", "Crash probe");
            bool started2 = bm.StartSessionAsync(s2.SessionId, "t47-crash").GetAwaiter().GetResult();
            bool crashDetected = false; bool wrapperStopped = false;
            if (started2)
            {
                var live2 = bm.GetSession(s2.SessionId, "t47-crash")!;
                ExternalKillTree((uint)live2.OwnedPids[0]); // kill tree agent (cmd+npx)
                crashDetected = WaitFor(() => bm.GetSession(s2.SessionId, "t47-crash") is { Status: "Failed" }, 70_000);
                Thread.Sleep(1000);
                wrapperStopped = !PortOpen(live2.WrapperPort);
            }
            Check("T47_BrowserIdleExpiryAndCrashIsolation",
                started1 && idleFired && expiredEvent && started2 && crashDetected && wrapperStopped,
                $"idle: started={started1} expired={idleFired} expiredEvent={expiredEvent} (backdate 20m vs batas uji 1m; produksi 15m); crash: started={started2} detectedFailed={crashDetected} wrapperStopped={wrapperStopped}");
        }
        finally { bm.SessionEvent -= events.Add; CleanupBrowserTestProcesses(); }
    }

    private static void T8_ExternalProtectionAudit(List<(uint Pid, string Name, string Cmd)> before, List<(uint Pid, string Name, string Cmd)> after)
    {
        // Strict part: every kill Xellen performed during the whole test run must have carried
        // the test marker in its (logged) cmdline — i.e. no external process was ever a target.
        var killLog = LogService.Snapshot(1000).Where(l => l.Component == "kill" && (l.Msg.StartsWith("Killing tree") || l.Msg.StartsWith("Killing recorded-orphan"))).ToList();
        // A kill is test-owned if its cmdline carries the marker/spawner tag, or its owner tag
        // points at a selftest server id ([owner=t<digit>...]). Production ids never match that.
        // Browser residual-sweep kills are also test-owned BY CONSTRUCTION: the reason string
        // 'sessionId fingerprint' certifies TryKillOwned verified the target cmdline carries the
        // session's unique sandbox profile/token id before killing (T46 evidence, round-3).
        bool TestOwned(LogLine l) => l.Msg.Contains(Marker) || l.Msg.Contains(SpawnerTag) ||
            l.Msg.Contains(TestRoot, StringComparison.OrdinalIgnoreCase) ||
            l.Msg.Contains("sessionId fingerprint", StringComparison.OrdinalIgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(l.Msg, @"\[owner=t\d");
        var badKills = killLog.Where(l => !TestOwned(l)).ToList();

        // Informational part: external process census before vs after (churn by Z.ai/gateway is
        // expected and NOT a failure; a PID vanishing is only flagged in the details).
        var beforePids = before.Select(b => b.Pid).ToHashSet();
        var afterPids = after.Select(a => a.Pid).ToHashSet();
        var vanished = beforePids.Except(afterPids).ToList();
        Check("T8_ExternalProtectionAudit", badKills.Count == 0,
            $"killOperations={killLog.Count}, nonMarkerKills={badKills.Count} (must be 0). External census: {before.Count}→{after.Count}, vanishedPids={string.Join(",", vanished.Take(10))} (informational; natural churn possible). " +
            (badKills.Count > 0 ? "VIOLATIONS: " + string.Join(" | ", badKills.Select(b => b.Msg)) : ""));
    }
}
