using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using McpXellen.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace McpXellen.Transport;

/// <summary>
/// Native MCP Streamable HTTP host (spec 2025-06-18): POST endpoints per server/project,
/// Bearer auth per identity, Origin validation, per-identity rate limit, Mcp-Session-Id
/// sessions with expiry, JSON and SSE responses, MCP-Protocol-Version handling.
/// Binds 127.0.0.1 only. Dual-run: the Supergateway compatibility endpoint (8023) is untouched.
/// </summary>
public static class NativeTransportHost
{
    private static WebApplication? _app;
    private static int _port;
    private static bool _running;
    private static StdioMcpBridge? _bridge;
    private static XellenConfig? _config;

    private sealed class Session
    {
        public string IdentityId = "";
        public string? ProjectId;
        public List<string> ServerIds = new();
        public DateTime ExpiresUtc;
        public DateTime LastSeenUtc = DateTime.UtcNow;
    }

    private static readonly ConcurrentDictionary<string, Session> Sessions = new();
    private static readonly ConcurrentDictionary<string, (DateTime WinStart, int Count)> RateMap = new();
    private static System.Threading.Timer? _cleanup;

    public static int Port => _port;
    public static bool Running => _running;
    public static int ActiveSessions => Sessions.Count;

    private static readonly string[] AllowedProtocolVersions = { "2025-06-18", "2025-03-26" };
    private const string DefaultProtocolVersion = "2025-03-26";

    public static int Start(int preferredPort, XellenConfig config, StdioMcpBridge bridge)
    {
        if (_running) return _port;
        _config = config;
        _bridge = bridge;
        var port = FindFreePort(preferredPort, 50);
        if (port == 0) throw new InvalidOperationException("No free port in candidate range 8300-8349.");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = Array.Empty<string>() });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        var app = builder.Build();

        app.Use(async (ctx, next) =>
        {
            // Origin validation (spec: MUST). Non-browser clients send no Origin — allowed.
            var origin = ctx.Request.Headers["Origin"].FirstOrDefault();
            if (!string.IsNullOrEmpty(origin) &&
                origin != $"http://127.0.0.1:{port}" && origin != $"http://localhost:{port}")
            {
                ctx.Response.StatusCode = 403;
                await ctx.Response.WriteAsync("forbidden origin");
                return;
            }
            if (ctx.Request.ContentLength is long len && len > config.NativeTransport.MaxBodyBytes)
            {
                ctx.Response.StatusCode = 413;
                await ctx.Response.WriteAsync("payload too large");
                return;
            }
            await next();
        });

        app.MapGet("/healthz", async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync("ok native transport");
        });

        app.MapPost("/srv/{serverId}/mcp", async ctx =>
            await HandleMcpAsync(ctx, ScopeKind.Server, ctx.Request.RouteValues["serverId"]?.ToString() ?? ""));
        app.MapPost("/prj/{projectId}/mcp", async ctx =>
            await HandleMcpAsync(ctx, ScopeKind.Project, ctx.Request.RouteValues["projectId"]?.ToString() ?? ""));

        _cleanup = new System.Threading.Timer(_ =>
        {
            var now = DateTime.UtcNow;
            foreach (var kv in Sessions)
            {
                if (kv.Value.ExpiresUtc <= now && Sessions.TryRemove(kv.Key, out var removedDead)) { }
            }
        }, null, 30000, 30000);

        app.StartAsync().GetAwaiter().GetResult();
        _app = app;
        _port = port;
        _running = true;
        LogService.Warn("transport", null, $"Native MCP Streamable HTTP host STARTED on http://127.0.0.1:{port} (dual-run: Supergateway 8023 untouched)");
        return port;
    }

    public static void Stop()
    {
        if (!_running) return;
        try { _app?.StopAsync().Wait(5000); _app?.DisposeAsync().AsTask().Wait(3000); } catch { }
        _cleanup?.Dispose();
        Sessions.Clear();
        _running = false;
        LogService.Warn("transport", null, "Native transport host stopped.");
    }

    private static int FindFreePort(int preferred, int attempts)
    {
        for (int p = preferred; p < preferred + attempts; p++)
        {
            try
            {
                var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, p);
                l.Start();
                l.Stop();
                return p;
            }
            catch { }
        }
        return 0;
    }

    private enum ScopeKind { Server, Project }

    private sealed class Scope
    {
        public string ClientId = "";
        public string? ProjectId;
        public List<string> ServerIds = new();
    }

    private static async Task<Scope?> ResolveScopeAsync(HttpContext ctx, ScopeKind kind, string key, string token)
    {
        var identity = AuthStore.Verify(token);
        if (identity == null) return null;
        var scope = new Scope { ClientId = identity.Id };
        if (kind == ScopeKind.Server)
        {
            if (identity.Kind == AuthKind.Server &&
                (identity.Scopes.Contains(key) || identity.Scopes.Contains("*")))
            {
                scope.ServerIds = new List<string> { key };
                return scope;
            }
            if (identity.Kind == AuthKind.Project && identity.Scopes.Contains(key))
            {
                var p = Registry.GetProjectByIdentity(identity.Id);
                scope.ProjectId = p?.Id;
                scope.ServerIds = new List<string> { key };
                return scope;
            }
            return null;
        }
        // Project scope: the identity must BE that project's client identity.
        var proj = Registry.GetProject(key);
        if (proj == null || proj.ClientIdentityId != identity.Id || proj.Status != "active") return null;
        scope.ProjectId = proj.Id;
        scope.ServerIds = proj.AllowedServers.Where(sid => Registry.GetServer(sid) != null).Select(sid => sid).ToList();
        return scope;
    }

    private static async Task HandleMcpAsync(HttpContext ctx, ScopeKind kind, string key)
    {
        // ---- auth ----
        var authHeader = ctx.Request.Headers["Authorization"].FirstOrDefault() ?? "";
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await Reject(ctx, 401, "Unauthorized: a valid Bearer token is required.", jsonRpc: false);
            return;
        }
        var token = authHeader["Bearer ".Length..].Trim();
        var scope = await ResolveScopeAsync(ctx, kind, key, token);
        if (scope == null)
        {
            await Reject(ctx, 401, "Unauthorized: token invalid, revoked, expired, or out of scope.", jsonRpc: false);
            return;
        }

        // ---- rate limit (per identity, fixed window) ----
        var limit = Math.Max(1, _config!.NativeTransport.RateLimitPerMinute);
        var nowUtc = DateTime.UtcNow;
        var window = RateMap.AddOrUpdate(scope.ClientId,
            _ => (nowUtc, 1),
            (_, old) => (nowUtc - old.WinStart).TotalMinutes >= 1 ? (nowUtc, 1) : (old.WinStart, old.Count + 1));
        if (window.Count > limit)
        {
            ctx.Response.StatusCode = 429;
            ctx.Response.Headers["Retry-After"] = "30";
            await ctx.Response.WriteAsync("rate limit exceeded");
            return;
        }

        // ---- protocol version header (initialize exempt) ----
        var body = await ReadBodyAsync(ctx);
        if (body == null) { await Reject(ctx, 400, "invalid JSON body", jsonRpc: true); return; }
        if (!body.Value.TryGetProperty("method", out var methodEl))
        {
            await JsonRpcError(ctx, null, -32600, "invalid request: missing method");
            return;
        }
        var method = methodEl.GetString() ?? "";
        LogService.Info("transport", null, $"req START method={method} path={ctx.Request.Path}");
        bool isInitialize = method == "initialize";
        if (!isInitialize)
        {
            var pv = ctx.Request.Headers["MCP-Protocol-Version"].FirstOrDefault();
            if (!string.IsNullOrEmpty(pv) && !AllowedProtocolVersions.Contains(pv))
            {
                await Reject(ctx, 400, $"unsupported MCP-Protocol-Version: {pv}", jsonRpc: true);
                return;
            }
        }

        bool hasId = body.Value.TryGetProperty("id", out var idEl);
        if (!hasId)
        {
            // notification → 202 (spec)
            ctx.Response.StatusCode = 202;
            return;
        }

        // ---- session handling ----
        string sessionId;
        List<string> serverIds;
        if (isInitialize)
        {
            sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var entry = new Session
            {
                IdentityId = scope.ClientId,
                ProjectId = scope.ProjectId,
                ServerIds = scope.ServerIds,
                ExpiresUtc = DateTime.UtcNow.AddMinutes(Math.Max(5, _config.NativeTransport.SessionTimeoutMinutes)),
            };
            Sessions[sessionId] = entry;
            serverIds = scope.ServerIds;
        }
        else
        {
            var sid = ctx.Request.Headers["Mcp-Session-Id"].FirstOrDefault();
            if (string.IsNullOrEmpty(sid))
            {
                await JsonRpcError(ctx, idEl, -32000, "missing Mcp-Session-Id (initialize first)", 400);
                return;
            }
            if (!Sessions.TryGetValue(sid, out var session) || session.ExpiresUtc <= DateTime.UtcNow)
            {
                Sessions.TryRemove(sid ?? "", out _);
                await JsonRpcError(ctx, idEl, -32001, "session expired or unknown — re-initialize", 404);
                return;
            }
            session.LastSeenUtc = DateTime.UtcNow;
            sessionId = sid;
            serverIds = session.ServerIds;
        }

        try
        {
            switch (method)
            {
                case "initialize":
                {
                    var requested = body.Value.TryGetProperty("params", out var pr) && pr.TryGetProperty("protocolVersion", out var pvv)
                        ? pvv.GetString() : null;
                    var negotiated = requested != null && AllowedProtocolVersions.Contains(requested) ? requested : AllowedProtocolVersions[0];
                    var result = JsonSerializer.Serialize(new
                    {
                        protocolVersion = negotiated,
                        capabilities = new { tools = new { listChanged = false } },
                        serverInfo = new { name = "mcp-xellen-native", version = "1.0.0" },
                    });
                    ctx.Response.Headers["Mcp-Session-Id"] = sessionId;
                    await JsonRpcResult(ctx, idEl, result);
                    LogService.Info("transport", null, $"initialize ok session={sessionId[..8]}… client={scope.ClientId} servers=[{string.Join(",", serverIds)}]");
                    return;
                }
                case "ping":
                    await JsonRpcResult(ctx, idEl, "{}");
                    return;
                case "tools/list":
                {
                    var tools = new List<object>();
                    foreach (var sid in serverIds)
                    {
                        var def = Registry.GetServer(sid);
                        if (def == null) continue;
                        try { await _bridge!.EnsureServerAndToolsAsync(def); } catch (Exception ex)
                        {
                            LogService.Warn("transport", sid, "ensure server failed: " + ex.Message);
                            continue;
                        }
                        foreach (var t in _bridge.ListTools(sid))
                            tools.Add(new
                            {
                                name = $"{sid}__{t.Name}",
                                description = (t.Description ?? "") + $" [server: {sid}]",
                                inputSchema = t.InputSchema.HasValue ? t.InputSchema.Value : JsonSerializer.Deserialize<JsonElement>("{\"type\":\"object\",\"properties\":{}}"),
                            });
                    }
                    await JsonRpcResult(ctx, idEl, JsonSerializer.Serialize(new { tools }));
                    return;
                }
                case "tools/call":
                {
                    if (!body.Value.TryGetProperty("params", out var callParams))
                    {
                        await JsonRpcError(ctx, idEl, -32602, "missing params");
                        return;
                    }
                    var fullTool = callParams.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
                    var sep = fullTool.IndexOf("__", StringComparison.Ordinal);
                    if (sep <= 0 || !serverIds.Contains(fullTool[..sep]))
                    {
                        await JsonRpcError(ctx, idEl, -32602, $"unknown tool '{fullTool}' for this scope");
                        return;
                    }
                    var serverId = fullTool[..sep];
                    var toolName = fullTool[(sep + 2)..];
                    JsonElement? args = callParams.TryGetProperty("arguments", out var ag) ? ag.Clone() : null;
                    var def = Registry.GetServer(serverId)!;
                    var res = await _bridge!.CallToolAsync(def, toolName, args, clientId: scope.ClientId,
                        projectId: scope.ProjectId);
                    await JsonRpcResult(ctx, idEl, JsonSerializer.Serialize(new
                    {
                        content = new object[] { new { type = "text", text = res.TextSummary } },
                        isError = res.IsError,
                    }));
                    return;
                }
                default:
                    await JsonRpcError(ctx, idEl, -32601, $"method not found: {method}");
                    return;
            }
        }
        catch (Exception ex)
        {
            LogService.Error("transport", null, $"handler error for {method}", ex.Message);
            await JsonRpcError(ctx, idEl, -32000, "internal error: " + ex.Message);
        }
    }

    private static async Task<JsonElement?> ReadBodyAsync(HttpContext ctx)
    {
        try
        {
            using var sr = new StreamReader(ctx.Request.Body, Encoding.UTF8);
            var raw = await sr.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return JsonSerializer.Deserialize<JsonElement>(raw);
        }
        catch { return null; }
    }

    private static bool ClientAcceptsJson(HttpContext ctx)
    {
        var accept = ctx.Request.Headers["Accept"].ToString();
        return accept.Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task JsonRpcResult(HttpContext ctx, JsonElement idEl, string resultJson)
    {
        var payload = $"{{\"jsonrpc\":\"2.0\",\"id\":{idEl.GetRawText()},\"result\":{resultJson}}}";
        await WriteJsonRpc(ctx, payload);
    }

    private static async Task JsonRpcError(HttpContext ctx, JsonElement? idEl, int code, string message, int? httpStatus = null)
    {
        var idRaw = idEl.HasValue ? idEl.Value.GetRawText() : "null";
        var payload = $"{{\"jsonrpc\":\"2.0\",\"id\":{idRaw},\"error\":{{\"code\":{code},\"message\":{JsonSerializer.Serialize(message)}}}}}";
        if (httpStatus.HasValue) ctx.Response.StatusCode = httpStatus.Value;
        await WriteJsonRpc(ctx, payload);
    }

    private static async Task Reject(HttpContext ctx, int status, string message, bool jsonRpc)
    {
        ctx.Response.StatusCode = status;
        if (status == 401) ctx.Response.Headers["WWW-Authenticate"] = "Bearer realm=\"mcp-xellen-native\"";
        if (jsonRpc)
        {
            var payload = $"{{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{{\"code\":-32000,\"message\":{JsonSerializer.Serialize(message)}}}}}";
            await WriteJsonRpc(ctx, payload);
        }
        else
        {
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync(message);
        }
    }

    private static async Task WriteJsonRpc(HttpContext ctx, string payload)
    {
        ctx.Response.ContentType = "application/json";
        ctx.Response.Headers["MCP-Protocol-Version"] = "2025-06-18";
        LogService.Info("transport", null, $"resp WRITE status={ctx.Response.StatusCode} len={payload.Length}");
        var accept = ctx.Request.Headers["Accept"].ToString();
        if (!ClientAcceptsJson(ctx) && accept.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            // SSE envelope: single message event, then close (spec: server MAY stream; JSON body
            // carried as one data event).
            ctx.Response.ContentType = "text/event-stream";
            await ctx.Response.WriteAsync("event: message\n");
            await ctx.Response.WriteAsync("data: " + payload.Replace("\n", " ") + "\n\n");
        }
        else
        {
            await ctx.Response.WriteAsync(payload);
        }
    }
}
