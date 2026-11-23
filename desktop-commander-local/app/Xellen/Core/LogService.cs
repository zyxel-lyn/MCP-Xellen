using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McpXellen.Core;

public enum LogLevel { Debug = 0, Info = 1, Warn = 2, Error = 3 }

/// <summary>One structured log record (JSONL on disk, ring buffer for UI).</summary>
public sealed class LogLine
{
    [JsonPropertyName("ts")] public string Ts { get; set; } = "";
    [JsonPropertyName("level")] public string Level { get; set; } = "Info";
    [JsonPropertyName("component")] public string Component { get; set; } = "";
    [JsonPropertyName("serverId")] public string? ServerId { get; set; }
    [JsonPropertyName("pid")] public int Pid { get; set; }
    [JsonPropertyName("msg")] public string Msg { get; set; } = "";
    [JsonPropertyName("data")] public object? Data { get; set; }

    public override string ToString() =>
        $"{Ts} [{Level,-5}] [{Component}]{(ServerId == null ? "" : $" [{ServerId}]")} {Msg}";
}

/// <summary>
/// Structured JSONL logging with size-based rotation (default 10 MB x 5 backups), severity,
/// component, serverId, PID, and automatic redaction of any token value loaded from AuthTokenFiles.
/// The legacy mcp-xellen-app.log (65.9 MB) is intentionally NOT touched — it stays as evidence
/// at its original path; this service writes to D:\MCP-Bridge\logs\.
/// </summary>
public static class LogService
{
    private static readonly object Gate = new();
    private static string _dir = @"D:\MCP-Bridge\logs";
    private static string _base = "xellen-core";
    private static long _maxBytes = 10 * 1024 * 1024;
    private static int _maxBackups = 5;
    private static LogLevel _min = LogLevel.Info;
    private static readonly List<string> Redactions = new();
    private static readonly Queue<LogLine> Ring = new();
    private const int RingCapacity = 1000;
    private static readonly JsonSerializerOptions JsonOpts = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static event Action<LogLine>? Emitted;

    public static void Init(LogSettings s, IEnumerable<string> tokenValuesToRedact)
    {
        lock (Gate)
        {
            _dir = string.IsNullOrWhiteSpace(s.Directory) ? _dir : s.Directory;
            _base = string.IsNullOrWhiteSpace(s.BaseFileName) ? _base : s.BaseFileName;
            _maxBytes = Math.Max(64 * 1024, s.MaxFileBytes);
            _maxBackups = Math.Clamp(s.MaxBackups, 0, 50);
            _min = ParseLevel(s.MinLevel);
            Directory.CreateDirectory(_dir);
            Redactions.Clear();
            foreach (var t in tokenValuesToRedact)
                if (!string.IsNullOrWhiteSpace(t) && t.Trim().Length >= 8)
                    Redactions.Add(t.Trim());
        }
        Info("core", null, $"LogService initialized: dir={_dir}, maxFileBytes={_maxBytes}, maxBackups={_maxBackups}, redactionEntries={Redactions.Count}");
    }

    private static LogLevel ParseLevel(string v) => v?.ToLowerInvariant() switch
    {
        "debug" => LogLevel.Debug,
        "info" => LogLevel.Info,
        "warn" or "warning" => LogLevel.Warn,
        "error" => LogLevel.Error,
        _ => LogLevel.Info,
    };

    public static void Debug(string component, string? serverId, string msg, object? data = null) => Write(LogLevel.Debug, component, serverId, msg, data);
    public static void Info(string component, string? serverId, string msg, object? data = null) => Write(LogLevel.Info, component, serverId, msg, data);
    public static void Warn(string component, string? serverId, string msg, object? data = null) => Write(LogLevel.Warn, component, serverId, msg, data);
    public static void Error(string component, string? serverId, string msg, object? data = null) => Write(LogLevel.Error, component, serverId, msg, data);

    private static void Write(LogLevel level, string component, string? serverId, string msg, object? data)
    {
        if (level < _min) return;
        var line = new LogLine
        {
            Ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            Level = level.ToString(),
            Component = component,
            ServerId = serverId,
            Pid = Environment.ProcessId,
            Msg = Redact(msg),
            Data = data is string s ? Redact(s) : data,
        };
        lock (Gate)
        {
            Ring.Enqueue(line);
            while (Ring.Count > RingCapacity) Ring.Dequeue();
            try
            {
                var path = CurrentFile();
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length >= _maxBytes) Rotate();
                File.AppendAllText(CurrentFile(), JsonSerializer.Serialize(line, JsonOpts) + Environment.NewLine, Encoding.UTF8);
            }
            catch { /* logging must never crash the app */ }
        }
        try { Emitted?.Invoke(line); } catch { }
    }

    private static string CurrentFile() => Path.Combine(_dir, _base + ".log");

    private static void Rotate()
    {
        // shift: base.(n-1) -> base.n ... base.log -> base.1
        for (int i = _maxBackups - 1; i >= 1; i--)
        {
            var src = Path.Combine(_dir, $"{_base}.{i}.log");
            var dst = Path.Combine(_dir, $"{_base}.{i + 1}.log");
            if (File.Exists(src)) { try { File.Move(src, dst, overwrite: true); } catch { } }
        }
        if (_maxBackups > 0)
        {
            var first = Path.Combine(_dir, $"{_base}.1.log");
            try { File.Move(CurrentFile(), first, overwrite: true); } catch { }
        }
        else
        {
            try { File.Delete(CurrentFile()); } catch { }
        }
    }

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        foreach (var r in Redactions)
            if (text.Contains(r, StringComparison.Ordinal))
                text = text.Replace(r, "[REDACTED]", StringComparison.Ordinal);
        return text;
    }

    /// <summary>Latest records for the UI log viewer.</summary>
    public static List<LogLine> Snapshot(int max) { lock (Gate) { return Ring.TakeLast(max).ToList(); } }
}
