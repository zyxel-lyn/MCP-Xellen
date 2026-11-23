using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using McpXellen.Core;
using McpXellen.Transport;

namespace McpXellen.AvaloniaPoc;

/// <summary>
/// Headless proof run: REAL tool call (native transport → synthetic stdio child) → EventBus →
/// Avalonia ViewModel/Windows updated. No mock events. Prints PASS/FAIL lines; exit code 0 = all pass.
/// </summary>
public static class HeadlessTest
{
    public static int Run()
    {
        var results = new List<(string name, bool pass, string details)>();
        void Check(string name, bool pass, string details)
        {
            results.Add((name, pass, details));
            Console.WriteLine($"[poc] {(pass ? "PASS" : "FAIL")} {name}: {details}");
        }

        try
        {
            return RunInner(Check, results);
        }
        catch (Exception ex)
        {
            Check("P0_no_crash", false, $"POC crashed (unhandled): {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Own synthetic MCP stdio server (same protocol as the selftest one) — written into
    /// the PoC sandbox so the PoC never depends on another harness's file layout.</summary>
    private static string WriteSyntheticServer(string dir)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "synthetic-mcp-server.mjs");
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
        File.WriteAllText(path, js);
        return path;
    }

    private static int RunInner(Action<string, bool, string> Check,
        List<(string name, bool pass, string details)> results)
    {

        // Sandbox Core (never touches production config/endpoints).
        var sandbox = @"D:\MCP-Bridge\tests\avalonia-poc";
        Directory.CreateDirectory(sandbox);
        ConfigStore.BaseDirOverride = sandbox;
        LogService.Init(new LogSettings { Directory = Path.Combine(sandbox, "logs"), BaseFileName = "poc", MaxFileBytes = 2 * 1024 * 1024, MaxBackups = 2, MinLevel = "Debug" }, Array.Empty<string>());
        var cfg = ConfigStore.LoadOrCreate();
        var syntheticPath = WriteSyntheticServer(sandbox);
        // Purge any stale defs with this id (older runs persisted wrong paths), then add fresh.
        cfg.Servers.RemoveAll(s => s.Id == "synthetic-poc");
        cfg.Servers.Add(new ServerConfig
        {
            Id = "synthetic-poc",
            Name = "Synthetic PoC",
            Command = "node",
            Arguments = new List<string> { syntheticPath },
            WorkingDirectory = sandbox,
            Transport = "stdio-native",
        });
        cfg.NativeTransport = new NativeTransportSettings { Enabled = true, PreferredPort = 8391, RateLimitPerMinute = 5000 };
        Registry.Initialize(cfg);
        AuthStore.Initialize(ConfigStore.ConfigDir);

        var bridge = new StdioMcpBridge();
        var port = NativeTransportHost.Start(8391, cfg, bridge);
        Check("P1_native_host_started", NativeTransportHost.Running && port == 8391, $"port={port}");

        var vm = new ActivityMonitorViewModel();
        var main = new MainWindow(vm);
        main.Show();
        var monitor = new ActivityMonitorWindow(vm);
        monitor.Show();
        Dispatcher.UIThread.RunJobs();
        Check("P2_windows_created", main.IsVisible && monitor.IsVisible &&
            main.Title.Contains("Avalonia") && monitor.Title.Contains("Activity Monitor"),
            $"mainVisible={main.IsVisible} monitorVisible={monitor.IsVisible} mainTitle='{main.Title}' monitorTitle='{monitor.Title}'");

        // REAL tool call through the native transport (HTTP POST with a server-scoped identity).
        var tok = AuthStore.Issue("poc-server", AuthKind.Server, new[] { "synthetic-poc" }).PlaintextToken;
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        var initReq = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/srv/synthetic-poc/mcp")
        {
            Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"poc\",\"version\":\"0\"}}}",
                Encoding.UTF8, "application/json"),
        };
        initReq.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        initReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tok);
        var initResp = http.SendAsync(initReq).GetAwaiter().GetResult();
        var sid = initResp.Headers.Contains("Mcp-Session-Id") ? string.Join("", initResp.Headers.GetValues("Mcp-Session-Id")) : "";
        Check("P3_real_initialize", (int)initResp.StatusCode == 200 && sid.Length > 8,
            $"status={(int)initResp.StatusCode} sessionId={sid[..Math.Min(8, sid.Length)]}… (real HTTP handshake)");

        var callReq = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/srv/synthetic-poc/mcp")
        {
            Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"synthetic-poc__echo\",\"arguments\":{\"text\":\"avalonia-poc-real-call\"}}}",
                Encoding.UTF8, "application/json"),
        };
        callReq.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        callReq.Headers.Add("Mcp-Session-Id", sid);
        callReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tok);
        var callResp = http.SendAsync(callReq).GetAwaiter().GetResult();
        var callBody = callResp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        Check("P4_real_tool_call", (int)callResp.StatusCode == 200 && callBody.Contains("echo:avalonia-poc-real-call"),
            $"status={(int)callResp.StatusCode} body={Redaction.Sanitize(callBody, 140)}");

        // Events must have reached the Avalonia ViewModel through the SHARED EventBus.
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline && vm.Items.Count < 2) Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        Check("P5_vm_received_real_events",
            vm.Items.Count >= 2 && vm.Items.Any(e => e.Status == ActivityStatus.Running) && vm.Items.Any(e => e.Status == ActivityStatus.Success),
            $"vmEvents={vm.Items.Count} (>=2: Running + Success dari tool call nyata)");

        // Filters work on the VM.
        vm.StatusFilter = "Failed";
        var failedVisible = vm.Items.Count;
        vm.StatusFilter = "Success";
        var successVisible = vm.Items.Count;
        vm.StatusFilter = "(all)";
        Check("P6_vm_filter_works", failedVisible == 0 && successVisible >= 1,
            $"failedView={failedVisible} successView={successVisible} (filter mengubah tampilan)");

        // Resize semantics (headless): window size properties are settable; interactive visual check = BLOCKED.
        monitor.Width = 1000;
        monitor.Height = 600;
        Dispatcher.UIThread.RunJobs();
        Check("P7_resize_semantics", monitor.Width == 1000 && monitor.Height == 600,
            $"w={monitor.Width} h={monitor.Height} (property-level; visual multi-monitor/DPI = BLOCKED, butuh sesi interaktif)");

        // Cleanup.
        AuthStore.Revoke("poc-server");
        NativeTransportHost.Stop();
        bridge.StopAll();
        main.Close();
        monitor.Close();
        Dispatcher.UIThread.RunJobs();

        var fail = results.Count(r => !r.pass);
        Console.WriteLine($"[poc] done: {results.Count(r => r.pass)} pass, {fail} fail");
        return fail == 0 ? 0 : 1;
    }
}
