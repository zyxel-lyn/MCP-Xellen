using System.Text;

namespace McpXellen.Core;

/// <summary>
/// In-memory activity store (bounded ring) + JSONL persistence with rotation for the PRD's
/// activity metadata. Persisted records contain ONLY sanitized summaries (redaction happens at
/// event construction, before this store ever sees them).
/// </summary>
public static class ActivityStore
{
    private static readonly object Gate = new();
    private static readonly List<ActivityEvent> Ring = new();
    private const int RingCapacity = 1000;
    private static string _dir = @"D:\MCP-Bridge\logs";
    private static long _maxBytes = 5 * 1024 * 1024;
    private static int _maxBackups = 2;
    private static bool _persistEnabled = true;

    public static void Configure(string dir, bool persist, long maxBytes = 5 * 1024 * 1024, int maxBackups = 2)
    {
        lock (Gate)
        {
            _dir = dir;
            _persistEnabled = persist;
            _maxBytes = maxBytes;
            _maxBackups = Math.Clamp(maxBackups, 0, 5);
            if (persist) Directory.CreateDirectory(dir);
        }
    }

    public static void Add(ActivityEvent e)
    {
        lock (Gate)
        {
            Ring.Add(e);
            if (Ring.Count > RingCapacity) Ring.RemoveAt(0);
        }
        if (_persistEnabled) AppendJsonl(e);
    }

    private static string CurrentFile => Path.Combine(_dir, "activity.jsonl");

    private static void AppendJsonl(ActivityEvent e)
    {
        try
        {
            lock (Gate)
            {
                var fi = new FileInfo(CurrentFile);
                if (fi.Exists && fi.Length >= _maxBytes)
                {
                    for (int i = _maxBackups - 1; i >= 1; i--)
                    {
                        var src = Path.Combine(_dir, $"activity.{i}.jsonl");
                        var dst = Path.Combine(_dir, $"activity.{i + 1}.jsonl");
                        if (File.Exists(src)) File.Move(src, dst, true);
                    }
                    if (_maxBackups > 0) File.Move(CurrentFile, Path.Combine(_dir, "activity.1.jsonl"), true);
                    else File.Delete(CurrentFile);
                }
                File.AppendAllText(CurrentFile,
                    System.Text.Json.JsonSerializer.Serialize(e) + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { /* observability must not break the pipeline */ }
    }

    /// <summary>Query the ring with optional filters (newest first).</summary>
    public static List<ActivityEvent> Query(string? clientId, string? projectId, string? serverId,
        string? toolContains, ActivityStatus? status, int take = 300)
    {
        lock (Gate)
        {
            IEnumerable<ActivityEvent> q = Ring.AsEnumerable().Reverse();
            if (!string.IsNullOrEmpty(clientId)) q = q.Where(e => e.ClientId == clientId);
            if (!string.IsNullOrEmpty(projectId)) q = q.Where(e => e.ProjectId == projectId);
            if (!string.IsNullOrEmpty(serverId)) q = q.Where(e => e.ServerId == serverId);
            if (!string.IsNullOrEmpty(toolContains)) q = q.Where(e => (e.ToolName ?? "").Contains(toolContains, StringComparison.OrdinalIgnoreCase));
            if (status != null) q = q.Where(e => e.Status == status.Value);
            return q.Take(take).ToList();
        }
    }

    public static (long published, long dropped) Counters()
    {
        lock (Gate) return (EventBus.Instance.Published, EventBus.Instance.DroppedTotal);
    }
}
