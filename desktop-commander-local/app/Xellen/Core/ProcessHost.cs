using System.Diagnostics;
using System.Net.Sockets;

namespace McpXellen.Core;

public enum ProcState { Starting, Running, Stopping, Stopped, Crashed, RestartPending, Failed }

/// <summary>Activity contract for session children. P1.x NEVER produces <see cref="Idle"/>:
/// there is no mechanism that can prove a child is idle from outside the gateway
/// (gateway-owned TCP sessions are not attributable to a specific child, and the absence of
/// stdout/stderr output does NOT imply idle). Every child is therefore Unknown → treated as
/// ACTIVE/UNKNOWN and never auto-stopped on that basis.</summary>
public enum ChildActivityState { Unknown, Active, Idle }

public sealed class ChildRecord
{
    public uint Pid;
    public uint Ppid;
    public string Cmd = "";
    public string Name = "";
    public DateTime? StartedUtc;
    public long WsBytes;
}

public sealed class TrackedServer
{
    public ServerConfig Config = null!;
    public Process? Proc;
    public ProcState State = ProcState.Stopped;
    public DateTime? StartUtc;
    public int BackoffAttempt;
    public DateTime? NextRestartUtc;
    public int RestartCountTotal;
    public int? LastExitCode;
    public string? LastError;
    public bool ManualStop;
    /// <summary>Last known session children (direct + grandchildren) keyed by PID.</summary>
    public Dictionary<uint, ChildRecord> Children = new();
    /// <summary>True when the last child-snapshot enumeration FAILED: Children holds the previous
    /// (possibly outdated) data and must NOT be treated as a zero count.</summary>
    public bool ChildrenStale;
}

public sealed class OrphanFinding
{
    public uint Pid;
    public uint Ppid;
    public string Cmd = "";
    public string Reason = "";
    public string Action = ""; // killed | reported | skipped-protected | skipped-ambiguous
}

public sealed class SweepReport
{
    public DateTime RunUtc = DateTime.Now;
    public string Mode = "";
    /// <summary>"completed" (enumeration succeeded) or "failed" (enumeration failed — findings empty, no kills, no success stats).</summary>
    public string Status = "completed";
    public string? Error;
    public List<OrphanFinding> Findings = new();
    public override string ToString() => $"[{RunUtc:HH:mm:ss}] status={Status} mode={Mode}: {Findings.Count} finding(s)";
}

/// <summary>
/// Central process lifecycle manager. Every process Xellen starts is tracked with PID, config
/// identity, parent-child tree and exit handling. Kills are only performed after re-verifying
/// the process cmdline against ownership patterns; protected patterns are re-checked immediately
/// before every kill. Wildcard killing by image name (node.exe / chrome.exe) is forbidden.
/// </summary>
public sealed class ProcessHost : IDisposable
{
    public static readonly ProcessHost Instance = new();

    private readonly object _gate = new();
    private readonly Dictionary<string, TrackedServer> _servers = new();
    private System.Threading.Timer? _monitor;
    private System.Threading.Timer? _sweeper;
    private XellenConfig _config = null!;
    private RuntimeState _state = new();
    private int _tick;
    private bool _monitorBusy;
    private DateTime _lastLifetimeWarnUtc = DateTime.MinValue;
    private DateTime _lastCapWarnUtc = DateTime.MinValue;
    private DateTime _lastAutoCullWarnUtc = DateTime.MinValue;
    private DateTime _lastEnumFailLogUtc = DateTime.MinValue;

    public event Action<TrackedServer>? ServerChanged;
    public event Action<SweepReport>? Swept;
    public event Action<string>? Notification; // user-facing warnings/errors (UI shows these)

    internal ProcessHost() { }

    public void Initialize(XellenConfig config, RuntimeState state)
    {
        lock (_gate)
        {
            _config = config;
            _state = state;
            ProtectedPatterns = config.ProtectedProcessPatterns;
            foreach (var old in _servers.Values) { try { old.Proc?.Dispose(); } catch { } }
            _servers.Clear(); // fresh lifecycle per Initialize
            _lastLifetimeWarnUtc = _lastCapWarnUtc = _lastAutoCullWarnUtc = DateTime.MinValue; // per-lifecycle warn throttles
        }
        _monitor?.Dispose();
        _sweeper?.Dispose();
        _monitor = new System.Threading.Timer(_ => MonitorTick(), null,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        if (config.OrphanSweep.Mode != "off" && config.OrphanSweep.IntervalSeconds > 0)
        {
            var due = TimeSpan.FromSeconds(Math.Max(10, config.OrphanSweep.StartupDelaySeconds));
            var period = TimeSpan.FromSeconds(Math.Max(60, config.OrphanSweep.IntervalSeconds));
            _sweeper = new System.Threading.Timer(_ => RunSweep(), null, due, period);
        }
        LogService.Info("processhost", null, $"ProcessHost initialized: servers={config.Servers.Count}, sweepMode={config.OrphanSweep.Mode}");
    }

    public List<TrackedServer> Servers { get { lock (_gate) return _servers.Values.ToList(); } }

    public TrackedServer? Get(string id) { lock (_gate) return _servers.TryGetValue(id, out var t) ? t : null; }

    // ---------------------------------------------------------------- start

    public bool StartServer(string id, out string error)
    {
        error = "";
        TrackedServer t;
        lock (_gate)
        {
            var cfg = _config.Servers.FirstOrDefault(s => s.Id == id);
            if (cfg == null) { error = $"Server '{id}' tidak ada di registry."; return false; }
            if (_servers.TryGetValue(id, out var existing) &&
                existing.Proc is { HasExited: false }) { error = "Sudah berjalan."; return false; }
            t = new TrackedServer { Config = cfg, State = ProcState.Starting };
            if (existing != null)
            {
                // Preserve counters across crash-restarts so exponential backoff survives replacement.
                t.BackoffAttempt = existing.BackoffAttempt;
                t.RestartCountTotal = existing.RestartCountTotal;
                try { existing.Proc?.Dispose(); } catch { }
            }
            _servers[id] = t;
        }

        // Port conflict guard: never fight an already-listening endpoint (side-by-side safety).
        if (t.Config.Port is int port && PortInUse(port))
        {
            t.State = ProcState.Failed;
            t.LastError = $"Port {port} sudah dipakai proses lain (gateway mungkin dijalankan aplikasi lama).";
            LogService.Warn("processhost", id, t.LastError);
            ServerChanged?.Invoke(t);
            error = t.LastError;
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = t.Config.Command,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                WorkingDirectory = string.IsNullOrEmpty(t.Config.WorkingDirectory) ? @"D:\MCP-Bridge" : t.Config.WorkingDirectory,
            };
            foreach (var a in t.Config.Arguments) psi.ArgumentList.Add(a);
            foreach (var kv in t.Config.Environment) psi.Environment[kv.Key] = kv.Value;

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var pidHolder = new uint[1];
            proc.OutputDataReceived += (_, e) => LogService.Debug($"server:{t.Config.Id}", t.Config.Id, RedactLine(pidHolder, e.Data));
            proc.ErrorDataReceived += (_, e) => LogService.Debug($"server:{t.Config.Id}", t.Config.Id, RedactLine(pidHolder, e.Data));
            proc.Exited += (_, _) => OnProcessExited(t);
            if (!proc.Start()) throw new InvalidOperationException("Process.Start returned false");
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            pidHolder[0] = (uint)proc.Id;

            lock (_gate)
            {
                t.Proc = proc;
                t.State = ProcState.Starting;
                t.StartUtc = DateTime.UtcNow;
                t.ManualStop = false;
                t.LastError = null;
            }
            UpdateRuntime(t);
            LogService.Info("processhost", id, $"Started PID={proc.Id} ({t.Config.Name})");
            ServerChanged?.Invoke(t);
            return true;
        }
        catch (Exception ex)
        {
            lock (_gate) { t.State = ProcState.Failed; t.LastError = ex.Message; }
            LogService.Error("processhost", id, "Start failed", ex.Message);
            ServerChanged?.Invoke(t);
            error = ex.Message;
            return false;
        }
    }

    private static string RedactLine(uint[] pidHolder, string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return "";
        var pid = pidHolder[0];
        return pid != 0 ? $"[pid {pid}] {line}" : line;
    }

    private static bool PortInUse(int port)
    {
        try
        {
            using var c = new TcpClient();
            var ar = c.BeginConnect("127.0.0.1", port, null, null);
            if (ar.AsyncWaitHandle.WaitOne(300)) { c.EndConnect(ar); return true; }
            return false;
        }
        catch { return false; }
    }

    // ---------------------------------------------------------------- stop / restart

    /// <summary>Graceful-first stop: (1) close the child's stdin — supergateway's own designed
    /// shutdown path (its onSignals handler reaps its whole child tree); (2) wait; (3) escalate
    /// to whole-tree kill; (4) sweep any residual session children we can prove we own.</summary>
    public bool StopServer(string id, bool manual = true)
    {
        TrackedServer? t;
        lock (_gate) { _servers.TryGetValue(id, out t); }
        if (t == null) return false;
        if (manual) t.ManualStop = true;
        t.State = ProcState.Stopping;
        ServerChanged?.Invoke(t);
        LogService.Info("processhost", id, $"Stopping (graceful stdin close first)…");

        var children = SnapshotChildren(t, fromProcess: t.Proc);
        try
        {
            var proc = t.Proc;
            if (proc is { HasExited: false })
            {
                try { proc.StandardInput?.Close(); } catch { }
                if (!proc.WaitForExit(Math.Max(1000, t.Config.StopTimeoutSeconds * 1000)))
                {
                    LogService.Warn("processhost", id, "Graceful timeout reached — escalating to whole-tree kill.");
                    try { proc.Kill(entireProcessTree: true); } catch { }
                }
            }
        }
        catch (Exception ex) { LogService.Warn("processhost", id, $"Stop exception: {ex.Message}"); }

        // Residual session children: prove ownership pattern + not protected before each kill.
        // Only direct pattern matches are targeted; other descendants die via tree kill.
        var residualPattern = t.Config.ChildProcessPatterns.FirstOrDefault() ?? t.Config.OwnershipPatterns.FirstOrDefault() ?? "";
        foreach (var c in children.Values)
        {
            if (residualPattern != "" && MatchesAny(c.Cmd, new[] { residualPattern }))
                TryKillOwned(c.Pid, residualPattern, $"residual child of stopped server {id}", id);
            else
                TryKillRecordedChild(c, $"residual descendant of stopped server {id}", id);
        }

        FinalizeExit(t, natural: false);
        t.State = ProcState.Stopped;
        LogService.Info("processhost", id, "Stopped.");
        ServerChanged?.Invoke(t);
        return true;
    }

    public bool RestartServer(string id, out string error)
    {
        var t = Get(id);
        if (t != null && t.Proc is { HasExited: false }) StopServer(id);
        lock (_gate) { if (_servers.TryGetValue(id, out var x)) x.BackoffAttempt = 0; }
        var ok = StartServer(id, out error);
        if (ok && Get(id) is { } nt) nt.RestartCountTotal++;
        return ok;
    }

    private void OnProcessExited(TrackedServer t)
    {
        // Runs on a threadpool thread (Process.EnableRaisingEvents).
        lock (_gate)
        {
            if (t.State == ProcState.Stopping || t.State == ProcState.Stopped) return; // expected path
        }
        var exitCode = -1;
        try { if (t.Proc != null) exitCode = t.Proc.ExitCode; } catch { }
        LogService.Error("processhost", t.Config.Id, $"CRASH detected: process exited unexpectedly, exitCode={exitCode}");
        Notification?.Invoke($"Server '{t.Config.Name}' berhenti tak terduga (exit {exitCode}).");
        // Kill residual session children of the dead gateway (provable ownership).
        var residualPattern = t.Config.ChildProcessPatterns.FirstOrDefault() ?? t.Config.OwnershipPatterns.FirstOrDefault() ?? "";
        var residual = SnapshotChildren(t, fromProcess: null, deadPid: t.Proc == null ? null : (uint)t.Proc.Id);
        foreach (var c in residual.Values)
        {
            if (residualPattern != "" && MatchesAny(c.Cmd, new[] { residualPattern }))
                TryKillOwned(c.Pid, residualPattern, $"residual child after crash of {t.Config.Id}", t.Config.Id);
            else
                TryKillRecordedChild(c, $"orphaned descendant after crash of {t.Config.Id}", t.Config.Id);
        }
        FinalizeExit(t, natural: false);

        // Schedule restart with exponential backoff.
        var rp = t.Config.RestartPolicy;
        if (rp.Enabled && !t.ManualStop)
        {
            lock (_gate)
            {
                if (t.BackoffAttempt < rp.MaxAttempts)
                {
                    var secs = Math.Min(rp.BackoffMaxSeconds, rp.BackoffBaseSeconds * (int)Math.Pow(2, t.BackoffAttempt));
                    t.BackoffAttempt++;
                    t.NextRestartUtc = DateTime.UtcNow.AddSeconds(secs);
                    t.State = ProcState.RestartPending;
                    LogService.Warn("processhost", t.Config.Id, $"Restart #{t.BackoffAttempt} scheduled in {secs}s (exponential backoff).");
                }
                else
                {
                    t.State = ProcState.Failed;
                    t.LastError = $"Gagal {rp.MaxAttempts}x berturut-turut — restart dihentikan (manual action required).";
                    Notification?.Invoke($"Server '{t.Config.Name}' GAGAL {rp.MaxAttempts}x — perlu intervensi manual.");
                    LogService.Error("processhost", t.Config.Id, t.LastError!);
                }
            }
        }
        else
        {
            t.State = ProcState.Crashed;
        }
        ServerChanged?.Invoke(t);
    }

    private void FinalizeExit(TrackedServer t, bool natural)
    {
        lock (_gate)
        {
            try { t.LastExitCode = t.Proc?.ExitCode; } catch { }
            t.Children = new Dictionary<uint, ChildRecord>(); // swap, never mutate (UI enumerates)
        }
        UpdateRuntime(t, exited: true);
    }

    // ---------------------------------------------------------------- monitor

    private void MonitorTick()
    {
        if (_monitorBusy) return;
        _monitorBusy = true;
        try
        {
            _tick++;
            List<TrackedServer> list;
            lock (_gate) list = _servers.Values.ToList();
            foreach (var t in list)
            {
                if (t.State == ProcState.RestartPending && t.NextRestartUtc <= DateTime.UtcNow)
                {
                    LogService.Info("processhost", t.Config.Id, "Executing scheduled restart…");
                    StartServer(t.Config.Id, out _);
                    if (Get(t.Config.Id) is { } nt) nt.RestartCountTotal++;
                }
                if (t.State is ProcState.Starting or ProcState.Running)
                {
                    var alive = t.Proc is { HasExited: false };
                    if (alive)
                    {
                        if (t.State == ProcState.Starting) { t.State = ProcState.Running; ServerChanged?.Invoke(t); }
                        var rp = t.Config.RestartPolicy;
                        if (t.StartUtc != null && (DateTime.UtcNow - t.StartUtc.Value).TotalSeconds > rp.StableResetSeconds && t.BackoffAttempt > 0)
                        {
                            t.BackoffAttempt = 0;
                            LogService.Info("processhost", t.Config.Id, $"Stable > {rp.StableResetSeconds}s — backoff counter reset.");
                        }
                    }
                }
            }
            // Heavier work (WMI snapshots + child policy) every 3rd tick (15 s).
            if (_tick % 3 == 0)
            {
                foreach (var t in list.Where(x => x.State == ProcState.Running))
                {
                    RefreshChildren(t);
                    EnforceChildPolicy(t);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Error("processhost", null, "Monitor tick failed", ex.Message);
        }
        finally { _monitorBusy = false; }
    }

    // ---------------------------------------------------------------- children & policy

    public Dictionary<uint, ChildRecord> SnapshotChildren(TrackedServer t, Process? fromProcess, uint? deadPid = null)
    {
        var result = new Dictionary<uint, ChildRecord>();
        uint? rootPid = fromProcess != null ? (uint)fromProcess.Id : deadPid;
        if (rootPid == null) return result;
        var snapRes = ProcessEnumerator.Snapshot();
        if (!snapRes.Success)
        {
            // Fail-safe: enumeration failed → child inventory is UNKNOWN. Return the LAST KNOWN
            // recorded children so callers can proceed; every actual kill re-verifies state via
            // TryGet and REFUSES when the process state is unknown, so no kill can occur on
            // unknown data.
            LogService.Warn("processhost", t.Config.Id,
                $"Child snapshot FAILED ({snapRes.Error}) — returning last-known children ({t.Children.Count}); kills on unknown state will be refused.");
            lock (_gate) return new Dictionary<uint, ChildRecord>(t.Children);
        }
        CollectDescendants(result, snapRes.Processes, rootPid.Value, t.Config.ChildProcessPatterns);
        return result;
    }

    private static void CollectDescendants(Dictionary<uint, ChildRecord> into, List<ProcInfo> snap, uint parentPid, List<string> childPatterns)
    {
        foreach (var p in snap)
        {
            if (p.Ppid != parentPid || into.ContainsKey(p.Pid)) continue;
            into[p.Pid] = new ChildRecord { Pid = p.Pid, Ppid = p.Ppid, Cmd = p.CmdLine ?? "", Name = p.Name, StartedUtc = p.StartedUtc, WsBytes = p.WsBytes };
            CollectDescendants(into, snap, p.Pid, childPatterns); // grandchildren (cmd→node chains)
        }
    }

    internal void RefreshChildren(TrackedServer t)
    {
        try
        {
            if (t.Proc is not { HasExited: false }) return;
            var snapRes = ProcessEnumerator.Snapshot();
            if (!snapRes.Success)
            {
                // Failure is NOT an empty inventory: keep the previous children, mark stale.
                t.ChildrenStale = true;
                if ((DateTime.UtcNow - _lastEnumFailLogUtc).TotalMinutes >= 5)
                {
                    _lastEnumFailLogUtc = DateTime.UtcNow;
                    LogService.Warn("processhost", t.Config.Id,
                        $"Child tracking DEGRADED: enumeration failed ({snapRes.Error}) — previous child data kept (count NOT reset to zero).");
                }
                return;
            }
            var kids = new Dictionary<uint, ChildRecord>();
            CollectDescendants(kids, snapRes.Processes, (uint)t.Proc.Id, t.Config.ChildProcessPatterns);
            lock (_gate)
            {
                t.Children = kids;
                t.ChildrenStale = false;
            }
        }
        catch (Exception ex) { LogService.Warn("processhost", t.Config.Id, "Child snapshot failed", ex.Message); }
    }

    /// <summary>
    /// Child-count and lifetime limits are observational only until Xellen has reliable
    /// per-session idle evidence. A child's age or the number of children cannot prove that
    /// an MCP request is inactive. Never terminate a child from this policy without that proof.
    /// </summary>
    internal void EnforceChildPolicy(TrackedServer t)
    {
        var cp = _config.ChildPolicy;
        if (!cp.Enabled) return;
        var now = DateTime.UtcNow;
        var direct = t.Children.Values
            .Where(c => c.Ppid == (uint?)t.Proc?.Id)
            .Where(c => MatchesAny(c.Cmd, t.Config.ChildProcessPatterns))
            .ToList();

        // ACTIVITY CONTRACT: every session child is Unknown (= ACTIVE/UNKNOWN). No mechanism in
        // P1.x can prove idle (see ChildActivityState). Therefore lifetime/concurrency limits
        // are WARNING thresholds only — they never terminate anything.

        // Liveness evidence: on enumeration failure the child set is UNKNOWN — never zero.
        // Keep previous data (t.Children untouched), skip liveness-based evaluation this cycle.
        var snapRes = ProcessEnumerator.Snapshot();
        HashSet<uint> alivePids;
        if (!snapRes.Success)
        {
            if ((now - _lastEnumFailLogUtc).TotalMinutes >= 5)
            {
                _lastEnumFailLogUtc = now;
                LogService.Warn("processhost", t.Config.Id,
                    $"CHILD-POLICY: enumeration FAILED ({snapRes.Error}) — liveness UNKNOWN, previous child data kept ({direct.Count} candidates), no action taken this cycle.");
            }
            return;
        }
        alivePids = snapRes.Processes.Select(p => p.Pid).ToHashSet();

        foreach (var c in direct)
        {
            if (c.StartedUtc == null) continue;
            var age = now - c.StartedUtc.Value;
            if (age.TotalMinutes > cp.MaxChildLifetimeMinutes && (now - _lastLifetimeWarnUtc).TotalMinutes >= 10)
            {
                _lastLifetimeWarnUtc = now;
                LogService.Warn("processhost", t.Config.Id,
                    $"CHILD-LIFETIME warning (report-only): PID={c.Pid} age={age.TotalMinutes:F0}min > {cp.MaxChildLifetimeMinutes}min. NOT terminated: session-idle state is UNKNOWN and cannot be proven from outside the gateway.");
                Notification?.Invoke($"Server '{t.Config.Name}': child melewati batas umur ({age.TotalMinutes:F0} mtk) — peringatan saja, tidak dihentikan.");
            }
        }

        // One snapshot for all liveness checks (WMI per-PID queries would be too heavy here).
        var alive = direct.Where(c => c.StartedUtc != null && alivePids.Contains(c.Pid)).ToList();
        if (alive.Count > cp.MaxConcurrentChildren && (now - _lastCapWarnUtc).TotalMinutes >= 10)
        {
            _lastCapWarnUtc = now;
            LogService.Warn("processhost", t.Config.Id,
                $"CHILD-CONCURRENCY warning (report-only): {alive.Count} children > {cp.MaxConcurrentChildren}. NOT terminated: session-idle state is UNKNOWN and cannot be proven from outside the gateway.");
            Notification?.Invoke($"Server '{t.Config.Name}': {alive.Count} sesi child melebihi batas {cp.MaxConcurrentChildren} — peringatan saja, tidak dihentikan.");
        }

        // Automatic culling is structurally disabled in P1.x: it would require BOTH
        // AutoCullEnabled AND a real idle-proof provider (IdleProofProvider), which is null.
        // Surface a visible warning if someone enables the flag, so the non-operation is explicit.
        if (cp.AutoCullEnabled && (now - _lastAutoCullWarnUtc).TotalMinutes >= 60)
        {
            _lastAutoCullWarnUtc = now;
            LogService.Warn("processhost", t.Config.Id,
                "autoCullEnabled=true is IGNORED: no idle-proof mechanism exists (IdleProofProvider == null). Automatic culling does NOT run; children are never terminated by this policy.");
        }
    }

    /// <summary>P1.x: ALWAYS null — deliberately. Until a version can attribute gateway sessions
    /// to a specific child (candidate: P2 native transport), there is no honest way to prove a
    /// child is idle, so automatic culling cannot run. Do NOT substitute heuristics (CPU, output
    /// silence, age) — those are fake activity signals and are forbidden.</summary>
    public static Func<ChildRecord, bool>? IdleProofProvider = null;

    /// <summary>Activity contract accessor: P1.x treats every session child as Unknown
    /// (= ACTIVE/UNKNOWN). <see cref="ChildActivityState.Idle"/> is never produced in P1.x.</summary>
    public static ChildActivityState GetActivityState(ChildRecord c) => ChildActivityState.Unknown;

    // ---------------------------------------------------------------- orphan sweep

    public SweepReport RunSweep()
    {
        var report = new SweepReport { Mode = _config.OrphanSweep.Mode };
        try
        {
            var snapRes = ProcessEnumerator.Snapshot();
            if (!snapRes.Success)
            {
                // ABORT-SAFE: a failed inventory must never masquerade as a clean sweep.
                // No findings, no kills, no success statistics, LastRunUtc NOT updated.
                report.Status = "failed";
                report.Error = snapRes.Error;
                LogService.Error("orphansweep", null,
                    $"Sweep ABORTED: process enumeration FAILED ({snapRes.Error}). No findings recorded, no kills performed, success statistics NOT updated (LastRunUtc unchanged). Sweep status = UNKNOWN, not CLEAN.");
                Notification?.Invoke("Orphan sweep GAGAL: enumerasi proses tidak dapat dibaca — status UNKNOWN, tidak ada kill.");
                lock (_gate)
                {
                    _state.OrphanSweep.ConsecutiveFailures++;
                    _state.OrphanSweep.LastFailureUtc = DateTime.UtcNow;
                    _state.OrphanSweep.LastFailureError = snapRes.Error;
                }
                PersistState();
                Swept?.Invoke(report);
                return report;
            }

            var snap = snapRes.Processes;

            // TEST-ONLY hook (null in production): lets tests inject an exception AFTER the
            // inventory succeeded but BEFORE any findings are processed.
            SweepHookAfterSnapshot?.Invoke();

            var byPid = snap.GroupBy(p => p.Pid).ToDictionary(g => g.Key, g => g.First());
            var trackedPids = new HashSet<uint>();
            lock (_gate)
                foreach (var t in _servers.Values)
                    if (t.Proc is { HasExited: false }) { trackedPids.Add((uint)t.Proc.Id); foreach (var k in t.Children.Keys) trackedPids.Add(k); }

            foreach (var p in snap)
            {
                if (string.IsNullOrEmpty(p.CmdLine)) continue;
                var owner = _config.Servers.FirstOrDefault(s => MatchesAny(p.CmdLine!, s.OwnershipPatterns));
                if (owner == null) continue;                       // not provably ours → never touched
                if (trackedPids.Contains(p.Pid)) continue;         // managed by us right now
                if (IsProtected(p.CmdLine!, out var whyProt)) { report.Findings.Add(new OrphanFinding { Pid = p.Pid, Ppid = p.Ppid, Cmd = p.CmdLine!, Reason = whyProt, Action = "skipped-protected" }); continue; }

                byPid.TryGetValue(p.Ppid, out var parent);
                bool parentAlive = parent != null;
                bool parentIsOurs = parentAlive && MatchesAny(parent!.CmdLine ?? "", owner.OwnershipPatterns);
                if (parentIsOurs) continue;                        // live chain (gateway/wrapper) → not an orphan
                if (parentAlive)
                {
                    // Parent alive but not ours: ownership cannot be fully proven (could be another
                    // launcher we don't know). Policy: report only, never kill.
                    report.Findings.Add(new OrphanFinding { Pid = p.Pid, Ppid = p.Ppid, Cmd = p.CmdLine!, Reason = $"parent PID {p.Ppid} alive but not an Xellen process — ambiguous", Action = "skipped-ambiguous" });
                    continue;
                }

                // Parent dead + cmdline matches our exact patterns → provably Xellen-owned orphan.
                report.Findings.Add(new OrphanFinding { Pid = p.Pid, Ppid = p.Ppid, Cmd = p.CmdLine!, Reason = $"cmdline matches '{owner.Id}' ownership AND parent PID {p.Ppid} is dead", Action = _config.OrphanSweep.Mode == "auto" ? "killed" : "reported" });
                if (_config.OrphanSweep.Mode == "auto")
                    TryKillOwned(p.Pid, FirstMatch(p.CmdLine!, owner.OwnershipPatterns), $"orphan of {owner.Id}", owner.Id);
            }
        }
        catch (Exception ex)
        {
            // An exception at ANY stage (including after a successful snapshot) means the sweep
            // FAILED. Partial findings stay in the report for visibility but are NOT counted as
            // success: LastRunUtc/TotalDetected/TotalKilled are NOT updated.
            report.Status = "failed";
            report.Error = ex.Message;
            LogService.Error("orphansweep", null,
                $"Sweep FAILED mid-run: {ex.Message}. Partial findings={report.Findings.Count} retained for visibility but NOT counted; success statistics NOT updated. (Any kills already logged in the kill log remain real.)");
            Notification?.Invoke("Orphan sweep GAGAL di tengah proses — hasil parsial tidak dihitung sebagai sukses.");
            lock (_gate)
            {
                _state.OrphanSweep.ConsecutiveFailures++;
                _state.OrphanSweep.LastFailureUtc = DateTime.UtcNow;
                _state.OrphanSweep.LastFailureError = $"mid-sweep exception: {ex.Message}";
            }
            PersistState();
            Swept?.Invoke(report);
            return report; // success path below must NOT run
        }

        LogService.Info("orphansweep", null, $"Sweep done: {report.Findings.Count} finding(s): " +
            string.Join("; ", report.Findings.GroupBy(f => f.Action).Select(g => $"{g.Key}={g.Count()}")));
        lock (_gate)
        {
            _state.OrphanSweep.LastRunUtc = DateTime.UtcNow;
            _state.OrphanSweep.ConsecutiveFailures = 0; // success resets the failure streak
            _state.OrphanSweep.TotalDetected += report.Findings.Count;
            _state.OrphanSweep.TotalKilled += report.Findings.Count(f => f.Action == "killed");
            _state.OrphanSweep.LastFindings = report.Findings.Select(f => $"PID={f.Pid} {f.Action}: {Trunc(f.Cmd, 120)}").Take(20).ToList();
        }
        PersistState();
        Swept?.Invoke(report);
        return report;
    }

    /// <summary>TEST-ONLY injection point: invoked in RunSweep right after a successful snapshot.
    /// Must remain null in production. Used to prove that a mid-sweep exception (after the
    /// inventory succeeded) yields a FAILED sweep with untouched success statistics.</summary>
    public static Action? SweepHookAfterSnapshot;

    // ---------------------------------------------------------------- kill safety

    /// <summary>The ONLY kill path in Xellen. Re-verifies, at kill time, that the PID still
    /// exists, its cmdline still matches an expected ownership pattern, and it is not protected.
    /// Then kills the whole tree and verifies the result.</summary>
    public static bool TryKillOwned(uint pid, string expectedPattern, string reason, string? serverId)
    {
        if (pid == 0) return false;
        var pre = ProcessEnumerator.QueryPid(pid);
        if (pre.Status is ProcQueryStatus.Failed or ProcQueryStatus.Timeout)
        {
            // UNKNOWN state (query failed/timed out) is NOT "already gone" — fail-safe: refuse.
            LogService.Error("kill", serverId, $"REFUSED kill PID={pid}: process state UNKNOWN ({pre.Status}: {pre.Error}). Fail-safe: no kill.");
            return false;
        }
        if (pre.Status == ProcQueryStatus.NotFound) return false; // genuinely gone — nothing to do
        var now = pre.Info!;
        if (string.IsNullOrEmpty(now.CmdLine))
        {
            LogService.Warn("kill", serverId, $"REFUSED kill PID={pid}: cmdline unreadable (ownership unprovable).");
            return false;
        }
        if (IsProtected(now.CmdLine, out var why))
        {
            LogService.Error("kill", serverId, $"REFUSED kill PID={pid}: protected ({why}). Cmd={Trunc(now.CmdLine, 160)}");
            return false;
        }
        if (expectedPattern != "" && !now.CmdLine.Contains(expectedPattern, StringComparison.OrdinalIgnoreCase))
        {
            LogService.Error("kill", serverId, $"REFUSED kill PID={pid}: cmdline no longer matches '{expectedPattern}' (possible PID reuse).");
            return false;
        }
        LogService.Warn("kill", serverId, $"Killing tree PID={pid} reason='{reason}' cmd={Trunc(now.CmdLine, 160)}");
        try
        {
            using var killer = Process.Start(new ProcessStartInfo("taskkill", $"/PID {pid} /T /F")
            { UseShellExecute = false, CreateNoWindow = true });
            killer?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            LogService.Error("kill", serverId, $"taskkill failed for PID={pid}", ex.Message);
            try { Process.GetProcessById((int)pid).Kill(entireProcessTree: true); } catch { }
        }
        System.Threading.Thread.Sleep(300);
        var verify = ProcessEnumerator.QueryPid(pid);
        if (verify.Status is ProcQueryStatus.Failed or ProcQueryStatus.Timeout)
        {
            LogService.Warn("kill", serverId, $"PID={pid} kill issued but verification UNKNOWN ({verify.Status}: {verify.Error}) — outcome UNKNOWN, NOT counted as verified termination.");
            return false;
        }
        bool gone = verify.Status == ProcQueryStatus.NotFound;
        LogService.Info("kill", serverId, gone ? $"PID={pid} terminated (verified)." : $"PID={pid} still alive after kill attempt!");
        return gone;
    }

    /// <summary>Patterns that must never be terminated by any Xellen kill path. Loaded from config at Initialize.</summary>
    public static List<string> ProtectedPatterns { get; set; } = XellenConfig.DefaultProtectedPatterns();

    /// <summary>Kills a previously recorded descendant of our tree whose parent is now gone.
    /// Safety: (1) cmdline must be byte-identical to the recorded snapshot (rules out PID reuse),
    /// (2) the recorded parent must be dead (true orphan — live parents are handled via their
    /// own tree), (3) protected patterns re-checked.</summary>
    public static bool TryKillRecordedChild(ChildRecord c, string reason, string? serverId)
    {
        if (c.Pid == 0) return false;
        var pre = ProcessEnumerator.QueryPid(c.Pid);
        if (pre.Status is ProcQueryStatus.Failed or ProcQueryStatus.Timeout)
        {
            LogService.Error("kill", serverId, $"REFUSED kill PID={c.Pid}: process state UNKNOWN ({pre.Status}: {pre.Error}). Fail-safe: no kill.");
            return false;
        }
        if (pre.Status == ProcQueryStatus.NotFound) return false; // already gone
        var now = pre.Info!;
        if (!string.Equals(now.CmdLine, c.Cmd, StringComparison.Ordinal))
        {
            LogService.Warn("kill", serverId, $"REFUSED kill PID={c.Pid}: cmdline changed since snapshot (possible PID reuse).");
            return false;
        }
        if (IsProtected(now.CmdLine ?? "", out var why))
        {
            LogService.Error("kill", serverId, $"REFUSED kill PID={c.Pid}: protected ({why}).");
            return false;
        }
        // Parent must be PROVEN absent. Unknown (Failed/Timeout) counts as alive — never kill on it.
        var pq = ProcessEnumerator.QueryPid(c.Ppid);
        if (pq.Status != ProcQueryStatus.NotFound) return false;
        LogService.Warn("kill", serverId, $"Killing recorded-orphan tree PID={c.Pid} reason='{reason}' [owner={serverId ?? "unknown"}] cmd={Trunc(now.CmdLine ?? "", 160)}");
        try
        {
            using var k = Process.Start(new ProcessStartInfo("taskkill", $"/PID {c.Pid} /T /F") { UseShellExecute = false, CreateNoWindow = true });
            k?.WaitForExit(5000);
        }
        catch { }
        Thread.Sleep(200);
        var verify = ProcessEnumerator.QueryPid(c.Pid);
        if (verify.Status is ProcQueryStatus.Failed or ProcQueryStatus.Timeout)
        {
            LogService.Warn("kill", serverId, $"PID={c.Pid} kill issued but verification UNKNOWN ({verify.Status}: {verify.Error}) — outcome UNKNOWN, NOT counted as verified termination.");
            return false;
        }
        return verify.Status == ProcQueryStatus.NotFound;
    }

    public static bool IsProtected(string cmdline, out string why)
    {
        why = "";
        foreach (var pat in ProtectedPatterns)
            if (cmdline.Contains(pat, StringComparison.OrdinalIgnoreCase)) { why = pat; return true; }
        return false;
    }

    private static bool MatchesAny(string? cmd, IEnumerable<string> patterns) =>
        !string.IsNullOrEmpty(cmd) && patterns.Any(p => cmd.Contains(p, StringComparison.OrdinalIgnoreCase));

    private static string FirstMatch(string cmd, IEnumerable<string> patterns) =>
        patterns.FirstOrDefault(p => cmd.Contains(p, StringComparison.OrdinalIgnoreCase)) ?? "";

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    /// <summary>Public helper for UI/log display of (redacted-by-caller) command lines.</summary>
    internal static string TruncCmd(string s) => Trunc(s, 160);

    // ---------------------------------------------------------------- runtime state

    private void UpdateRuntime(TrackedServer t, bool exited = false)
    {
        lock (_gate)
        {
            if (!_state.Servers.TryGetValue(t.Config.Id, out var ri))
                _state.Servers[t.Config.Id] = ri = new ServerRuntimeInfo();
            if (!exited) { ri.LastStartedUtc = DateTime.UtcNow; if (t.Proc != null) ri.LastPid = (uint)t.Proc.Id; ri.RestartCountTotal = t.RestartCountTotal; }
            else { ri.LastExitUtc = DateTime.UtcNow; try { ri.LastExitCode = t.Proc?.ExitCode; } catch { } }
        }
        PersistState();
    }

    private void PersistState() { lock (_gate) ConfigStore.SaveRuntimeState(_state); }

    public RuntimeState StateSnapshot { get { lock (_gate) return _state; } }

    public void ShutdownAll()
    {
        List<TrackedServer> list;
        lock (_gate) list = _servers.Values.ToList();
        foreach (var t in list.Where(x => x.Proc is { HasExited: false }))
            StopServer(t.Config.Id, manual: false);
        LogService.Info("processhost", null, "ShutdownAll complete.");
    }

    public void Dispose()
    {
        _monitor?.Dispose();
        _sweeper?.Dispose();
    }
}
