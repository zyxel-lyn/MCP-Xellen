using System.Diagnostics;
using System.Text;
using System.Text.Json;
using McpXellen.Core;
using ActivityEvent = McpXellen.Core.ActivityEvent;

namespace McpXellen.Transport;

public sealed class McpToolDef
{
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public JsonElement? InputSchema { get; init; }
}

public sealed class McpCallResult
{
    public bool IsError { get; init; }
    public string TextSummary { get; init; } = "";
    public string RawJson { get; init; } = "";
}

/// <summary>
/// STDIO MCP client bridge: spawns a child MCP server (newline-delimited JSON-RPC over stdio),
/// performs the initialize handshake, caches tools/list, and forwards tools/call.
/// The child process is fully owned by this bridge (PID recorded, graceful stdin-close stop,
/// tree-kill escalation) — it never touches processes owned by anything else.
/// </summary>
public sealed class StdioMcpBridge : IDisposable
{
    private sealed class Child
    {
        public required string ServerId;
        public required Process Proc;
        public required DateTime StartedUtc;
        public List<McpToolDef> Tools = new();
        public DateTime ToolsFetchedUtc;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Child> _children = new();
    private long _nextId = 1;

    public bool IsRunning(string serverId) { lock (_gate) return _children.ContainsKey(serverId); }
    public int? ChildPid(string serverId) { lock (_gate) return _children.TryGetValue(serverId, out var c) ? c.Proc.Id : null; }

    /// <summary>Start (or reuse) the child for a server definition, then complete the initialize
    /// handshake and cache tools/list. Idempotent per serverId.</summary>
    public async Task EnsureServerAsync(ServerConfig def, int startupTimeoutMs = 20000)
    {
        Child? child;
        lock (_gate) _children.TryGetValue(def.Id, out child);
        if (child != null && !child.Proc.HasExited) return;

        if (string.IsNullOrEmpty(def.Command)) throw new InvalidOperationException($"Server '{def.Id}' has no command.");
        var psi = new ProcessStartInfo
        {
            FileName = def.Command,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = string.IsNullOrEmpty(def.WorkingDirectory) ? @"D:\MCP-Bridge" : def.WorkingDirectory,
        };
        foreach (var a in def.Arguments) psi.ArgumentList.Add(a);
        foreach (var kv in def.Environment) psi.Environment[kv.Key] = kv.Value;

        var proc = Process.Start(psi) ?? throw new InvalidOperationException("child spawn failed");
        var child2 = new Child { ServerId = def.Id, Proc = proc, StartedUtc = DateTime.UtcNow };
        lock (_gate) _children[def.Id] = child2;
        LogService.Info("bridge", def.Id, $"STDIO child spawned PID={proc.Id}");
        _ = ReadStdoutLoopAsync(child2);
        _ = ReadStderrLoopAsync(child2);
        proc.Exited += (_, _) => LogService.Warn("bridge", def.Id, $"child exited code={SafeExitCode(proc)}");

        // initialize handshake (single in-flight request before reader loop needs the map —
        // the reader loop handles responses via the pending map below, so send through SendAsync)
        var initResult = await SendRequestAsync(child2, "initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "mcp-xellen-native", version = "1.0.0" },
        }, startupTimeoutMs);
        var ver = initResult.TryGetProperty("protocolVersion", out var pv) ? pv.GetString() : null;
        LogService.Info("bridge", def.Id, $"handshake ok, child protocolVersion={ver ?? "(none)"}");
        await SendNotificationAsync(child2, "notifications/initialized");
        await RefreshToolsAsync(child2, def.Id);
    }

    private static int SafeExitCode(Process p) { try { return p.ExitCode; } catch { return -1; } }

    private readonly Dictionary<long, TaskCompletionSource<JsonElement>> _pending = new();

    private async Task ReadStdoutLoopAsync(Child child)
    {
        try
        {
            while (!child.Proc.HasExited)
            {
                var line = await child.Proc.StandardOutput.ReadLineAsync();
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                JsonElement msg;
                try { msg = JsonSerializer.Deserialize<JsonElement>(line); }
                catch (Exception ex) { LogService.Warn("bridge", child.ServerId, "unparseable child line: " + ex.Message); continue; }
                if (msg.ValueKind == JsonValueKind.Object && msg.TryGetProperty("id", out var idEl))
                {
                    // Complete the pending request for BOTH success and error envelopes — a child
                    // JSON-RPC error must never leave the caller hanging until timeout.
                    var key = idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt64()
                            : idEl.ValueKind == JsonValueKind.String ? long.TryParse(idEl.GetString(), out var sv) ? sv : -1 : -1;
                    TaskCompletionSource<JsonElement>? tcs;
                    lock (_pending) _pending.Remove(key, out tcs);
                    if (tcs == null) continue;
                    if (msg.TryGetProperty("result", out var result))
                    {
                        tcs.TrySetResult(result.Clone());
                    }
                    else if (msg.TryGetProperty("error", out var err))
                    {
                        var code = err.TryGetProperty("code", out var ce) ? ce.GetInt64() : -32000;
                        var message = err.TryGetProperty("message", out var me) ? me.GetString() ?? "error" : "error";
                        tcs.TrySetException(new InvalidOperationException($"[{code}] {message}"));
                    }
                    else
                    {
                        tcs.TrySetResult(JsonSerializer.Deserialize<JsonElement>("{}"));
                    }
                }
                if (msg.ValueKind == JsonValueKind.Object && msg.TryGetProperty("method", out var m) && !msg.TryGetProperty("id", out _))
                {
                    // Pure notification from the child — nothing pending to complete.
                    LogService.Debug("bridge", child.ServerId, "child notification: " + Redaction.Sanitize(m.GetString() ?? "", 80));
                }
            }
        }
        catch (Exception ex) { LogService.Warn("bridge", child.ServerId, "stdout loop ended: " + ex.Message); }
    }

    private async Task ReadStderrLoopAsync(Child child)
    {
        try
        {
            while (!child.Proc.HasExited)
            {
                var line = await child.Proc.StandardError.ReadLineAsync();
                if (line == null) break;
                if (!string.IsNullOrWhiteSpace(line)) LogService.Debug($"child:{child.ServerId}", child.ServerId, Redaction.Sanitize(line, 300));
            }
        }
        catch { }
    }

    private async Task<JsonElement> SendRequestAsync(Child child, string method, object? @params, int timeoutMs)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending) _pending[id] = tcs;
        var req = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params });
        await SendRawAsync(child, req);
        try
        {
            var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            if (done != tcs.Task)
            {
                lock (_pending) _pending.Remove(id);
                throw new TimeoutException($"{method} timed out after {timeoutMs}ms");
            }
            return tcs.Task.Result;
        }
        catch (AggregateException agg)
        {
            var e = agg.InnerException?.Message ?? agg.Message;
            throw new InvalidOperationException($"{method} error: {e}");
        }
    }

    private static async Task SendRawAsync(Child child, string line)
    {
        await child.Proc.StandardInput.WriteLineAsync(line);
        await child.Proc.StandardInput.FlushAsync();
    }

    private async Task SendNotificationAsync(Child child, string method, object? @params = null)
    {
        var n = JsonSerializer.Serialize(new { jsonrpc = "2.0", method, @params });
        await SendRawAsync(child, n);
    }

    private async Task RefreshToolsAsync(Child child, string serverId)
    {
        var res = await SendRequestAsync(child, "tools/list", new { }, 20000);
        var tools = new List<McpToolDef>();
        if (res.ValueKind == JsonValueKind.Object && res.TryGetProperty("tools", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in arr.EnumerateArray())
            {
                tools.Add(new McpToolDef
                {
                    Name = t.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Description = t.TryGetProperty("description", out var d) ? d.GetString() : null,
                    InputSchema = t.TryGetProperty("inputSchema", out var sc) ? sc.Clone() : null,
                });
            }
        }
        lock (_gate) { child.Tools = tools; child.ToolsFetchedUtc = DateTime.UtcNow; }
        LogService.Info("bridge", serverId, $"tools/list cached: {tools.Count} tools");
    }

    public async Task EnsureServerAndToolsAsync(ServerConfig def)
    {
        await EnsureServerAsync(def);
        Child? c; lock (_gate) _children.TryGetValue(def.Id, out c);
        if (c != null && (DateTime.UtcNow - c.ToolsFetchedUtc).TotalMinutes > 10) await RefreshToolsAsync(c, def.Id);
    }

    public List<McpToolDef> ListTools(string serverId)
    {
        lock (_gate) return _children.TryGetValue(serverId, out var c) ? c.Tools.ToList() : new();
    }

    public async Task<McpCallResult> CallToolAsync(ServerConfig def, string toolName, JsonElement? arguments,
        string? clientId = null, string? projectId = null, int timeoutMs = 120000)
    {
        await EnsureServerAsync(def);
        Child? c; lock (_gate) _children.TryGetValue(def.Id, out c);
        if (c == null) throw new InvalidOperationException($"server '{def.Id}' not running");
        var correlation = Guid.NewGuid();
        var sw = Stopwatch.StartNew();
        var ev = new ActivityEvent
        {
            CorrelationId = correlation,
            EventType = "tool_call",
            ClientId = clientId,
            ProjectId = projectId,
            ServerId = def.Id,
            ToolName = toolName,
            Status = ActivityStatus.Running,
            InputSummary = arguments.HasValue ? Redaction.Sanitize(arguments.Value.GetRawText(), 200) : "",
            ProcessId = c.Proc.Id,
        };
        EventBus.Instance.Publish(ev);
        try
        {
            var res = await SendRequestAsync(c, "tools/call", new { name = toolName, arguments = arguments }, timeoutMs);
            sw.Stop();
            bool isError = res.ValueKind == JsonValueKind.Object && res.TryGetProperty("isError", out var ie) && ie.ValueKind == JsonValueKind.True;
            var text = new StringBuilder();
            if (res.ValueKind == JsonValueKind.Object && res.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in content.EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var ty) && ty.GetString() == "text" && item.TryGetProperty("text", out var tx))
                        text.AppendLine(tx.GetString() ?? "");
                }
            }
            var summary = Redaction.Sanitize(text.ToString().TrimEnd(), 400);
            EventBus.Instance.Publish(new ActivityEvent
            {
                CorrelationId = correlation,
                EventType = "tool_call",
                ClientId = clientId,
                ProjectId = projectId,
                ServerId = def.Id,
                ToolName = toolName,
                Status = isError ? ActivityStatus.Failed : ActivityStatus.Success,
                DurationMs = sw.ElapsedMilliseconds,
                InputSummary = ev.InputSummary,
                OutputSummary = summary,
                ErrorCode = isError ? "tool_is_error" : null,
                ProcessId = c.Proc.Id,
            });
            ActivityStore.Add(ev); ActivityStore.Add(new ActivityEvent { CorrelationId = correlation, EventType = "tool_call", ClientId = clientId, ProjectId = projectId, ServerId = def.Id, ToolName = toolName, Status = isError ? ActivityStatus.Failed : ActivityStatus.Success, DurationMs = sw.ElapsedMilliseconds, InputSummary = ev.InputSummary, OutputSummary = summary, ErrorCode = isError ? "tool_is_error" : null, ProcessId = c.Proc.Id });
            return new McpCallResult { IsError = isError, TextSummary = text.ToString().TrimEnd(), RawJson = res.GetRawText() };
        }
        catch (TimeoutException)
        {
            sw.Stop();
            EventBus.Instance.Publish(new ActivityEvent { CorrelationId = correlation, EventType = "tool_call", ClientId = clientId, ProjectId = projectId, ServerId = def.Id, ToolName = toolName, Status = ActivityStatus.Timeout, DurationMs = sw.ElapsedMilliseconds, InputSummary = ev.InputSummary, ErrorCode = "timeout", ProcessId = c.Proc.Id });
            ActivityStore.Add(new ActivityEvent { CorrelationId = correlation, EventType = "tool_call", ClientId = clientId, ProjectId = projectId, ServerId = def.Id, ToolName = toolName, Status = ActivityStatus.Timeout, DurationMs = sw.ElapsedMilliseconds, InputSummary = ev.InputSummary, ErrorCode = "timeout", ProcessId = c.Proc.Id });
            throw;
        }
        catch (Exception)
        {
            sw.Stop();
            EventBus.Instance.Publish(new ActivityEvent { CorrelationId = correlation, EventType = "tool_call", ClientId = clientId, ProjectId = projectId, ServerId = def.Id, ToolName = toolName, Status = ActivityStatus.Failed, DurationMs = sw.ElapsedMilliseconds, InputSummary = ev.InputSummary, ErrorCode = "bridge_error", ProcessId = c.Proc.Id });
            ActivityStore.Add(new ActivityEvent { CorrelationId = correlation, EventType = "tool_call", ClientId = clientId, ProjectId = projectId, ServerId = def.Id, ToolName = toolName, Status = ActivityStatus.Failed, DurationMs = sw.ElapsedMilliseconds, InputSummary = ev.InputSummary, ErrorCode = "bridge_error", ProcessId = c.Proc.Id });
            throw;
        }
    }

    public void Stop(string serverId)
    {
        Child? c; lock (_gate) { if (!_children.Remove(serverId, out c)) return; }
        try
        {
            try { c.Proc.StandardInput?.Close(); } catch { }
            if (!c.Proc.WaitForExit(3000))
            {
                LogService.Warn("bridge", serverId, "graceful timeout — escalating to tree kill");
                try { c.Proc.Kill(entireProcessTree: true); } catch { }
            }
        }
        catch { }
        LogService.Info("bridge", serverId, $"STDIO child stopped (PID was {c.Proc.Id}).");
        try { c.Proc.Dispose(); } catch { }
    }

    public void StopAll()
    {
        List<string> ids;
        lock (_gate) ids = _children.Keys.ToList();
        foreach (var id in ids) Stop(id);
    }

    public void Dispose() => StopAll();
}
