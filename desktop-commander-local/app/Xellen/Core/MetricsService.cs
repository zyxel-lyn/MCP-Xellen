using System.Diagnostics;
using System.Net.Http;

namespace McpXellen.Core;

public sealed class ServerMetrics
{
    public string Id = "";
    public string Name = "";
    public ProcState State;
    public int? Pid;
    public int? Port;
    public double UptimeMinutes;
    public int Restarts;
    public int SessionChildren;      // direct wrappers of the server process
    public int DescendantProcesses;  // full tree (cmd + node chains)
    public double ChildRamMb;        // measured WorkingSet of the descendant tree
    public double ProcRamMb;         // measured WorkingSet of the server process itself
    public double CpuPercent;        // measured over the poll interval
    public string? Health;           // endpoint probe result
    public string? LastError;
}

public sealed class MetricsSnapshot
{
    public DateTime Utc = DateTime.UtcNow;
    public int ManagedServers;
    public int ActiveProcesses;
    public int FailedServers;
    public double CoreRamMb;
    public double TotalChildRamMb;
    public int OrphansDetectedTotal;
    public int OrphansKilledTotal;
    /// <summary>Human-readable sweep status: "CLEAN (…)" only after a SUCCESSFUL sweep;
    /// "ERROR/UNKNOWN — …" while the last sweep failed (never shows CLEAN on failure).</summary>
    public string SweepStatus = "";
    public DateTime? LastSweepUtc;
    public List<ServerMetrics> Servers = new();
}

/// <summary>
/// Actual measurements only — WorkingSet64 / TotalProcessorTime deltas / HTTP health probes.
/// No estimates presented as measurements.
/// </summary>
public static class MetricsService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2.5) };
    private static readonly Dictionary<uint, (TimeSpan Cpu, DateTime Wall)> LastCpu = new();
    private static readonly Dictionary<string, string> LastHealth = new();
    private static readonly object Gate = new();
    private static System.Threading.Timer? _healthTimer;
    private static XellenConfig _config = null!;

    public static void Start(XellenConfig config)
    {
        _config = config;
        _healthTimer?.Dispose();
        _healthTimer = new System.Threading.Timer(_ => PollHealth(), null,
            TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(15));
    }

    private static void PollHealth()
    {
        foreach (var s in _config.Servers)
        {
            if (s.Port is not int port || string.IsNullOrEmpty(s.HealthPath)) continue;
            var code = Probe($"http://127.0.0.1:{port}{s.HealthPath}");
            lock (Gate) LastHealth[s.Id] = code;
        }
    }

    private static string Probe(string url)
    {
        try
        {
            using var resp = Http.GetAsync(url).GetAwaiter().GetResult();
            return $"{(int)resp.StatusCode}";
        }
        catch (Exception ex)
        {
            return ex.Message.Length > 40 ? "unreachable" : ex.Message;
        }
    }

    public static string? HealthOf(string serverId)
    {
        lock (Gate) return LastHealth.TryGetValue(serverId, out var v) ? v : null;
    }

    /// <summary>Sweep status label for the UI. ERROR/UNKNOWN while the last sweep failed —
    /// a failed enumeration must never be displayed as CLEAN.</summary>
    public static string SweepStatusLabel(RuntimeState st)
    {
        var s = st.OrphanSweep;
        if (s.ConsecutiveFailures > 0)
            return $"ERROR/UNKNOWN — enumerasi gagal {s.ConsecutiveFailures}x (terakhir {s.LastFailureUtc?.ToLocalTime():HH:mm:ss}: {TruncErr(s.LastFailureError)})";
        if (s.LastRunUtc != null)
            return $"CLEAN ({s.LastRunUtc.Value.ToLocalTime():HH:mm:ss})";
        return "BELUM PERNAH SWEEP";
    }

    private static string TruncErr(string? e) => string.IsNullOrEmpty(e) ? "?" : (e.Length <= 60 ? e : e[..60] + "…");

    private static double CpuPercentOf(uint pid)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            var cpu = p.TotalProcessorTime;
            var now = DateTime.UtcNow;
            double result;
            lock (Gate)
            {
                if (LastCpu.TryGetValue(pid, out var prev))
                {
                    var wall = (now - prev.Wall).TotalSeconds;
                    var used = (cpu - prev.Cpu).TotalSeconds;
                    result = wall > 0.5 ? Math.Max(0, used / wall / Environment.ProcessorCount * 100.0) : -1;
                }
                else result = -1; // first sample — no delta yet
                LastCpu[pid] = (cpu, now);
            }
            return result;
        }
        catch { lock (Gate) LastCpu.Remove(pid); return -1; }
    }

    public static MetricsSnapshot Snapshot(RuntimeState state)
    {
        var snap = new MetricsSnapshot();
        snap.CoreRamMb = Math.Round(Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0, 1);
        snap.OrphansDetectedTotal = state.OrphanSweep.TotalDetected;
        snap.OrphansKilledTotal = state.OrphanSweep.TotalKilled;
        snap.LastSweepUtc = state.OrphanSweep.LastRunUtc?.ToLocalTime();
        snap.SweepStatus = SweepStatusLabel(state);

        foreach (var t in ProcessHost.Instance.Servers)
        {
            var m = new ServerMetrics
            {
                Id = t.Config.Id,
                Name = t.Config.Name,
                State = t.State,
                Port = t.Config.Port,
                Restarts = t.RestartCountTotal,
                Health = HealthOf(t.Config.Id),
                LastError = t.LastError,
            };
            var proc = t.Proc;
            if (proc is { HasExited: false })
            {
                m.Pid = proc.Id;
                if (t.StartUtc != null) m.UptimeMinutes = Math.Round((DateTime.UtcNow - t.StartUtc.Value).TotalMinutes, 1);
                try { m.ProcRamMb = Math.Round(proc.WorkingSet64 / 1024.0 / 1024.0, 1); } catch { }
                m.CpuPercent = Math.Round(CpuPercentOf((uint)proc.Id), 1);
                snap.ActiveProcesses++;
            }
            var kids = t.Children.Values.ToList(); // safe: dictionary is swapped atomically, never mutated in place
            m.SessionChildren = kids.Count(k => k.Ppid == (uint?)proc?.Id);
            m.DescendantProcesses = kids.Count;
            m.ChildRamMb = Math.Round(kids.Sum(k => k.WsBytes) / 1024.0 / 1024.0, 1);
            snap.TotalChildRamMb += m.ChildRamMb;
            foreach (var k in kids) CpuPercentOf(k.Pid); // keep CPU samples fresh for children
            if (t.State == ProcState.Failed) snap.FailedServers++;
            snap.Servers.Add(m);
        }
        snap.ManagedServers = snap.Servers.Count;
        return snap;
    }
}
