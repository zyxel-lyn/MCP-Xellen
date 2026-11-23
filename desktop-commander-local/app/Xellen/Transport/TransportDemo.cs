using System.Text.Json;
using McpXellen.Core;

namespace McpXellen.Transport;

/// <summary>
/// TEST-ONLY harness: starts the native transport against a sandbox config with a synthetic
/// stdio server, publishes port/endpoint/token files for external clients (e.g. the Z.ai Python
/// adapter), and waits for a stop flag. NEVER used by the production app.
/// </summary>
public static class TransportDemo
{
    public static int Run()
    {
        var sandbox = @"D:\MCP-Bridge\tests\adapter";
        Directory.CreateDirectory(sandbox);
        ConfigStore.BaseDirOverride = sandbox;
        LogService.Init(new LogSettings
        {
            Directory = Path.Combine(sandbox, "logs"),
            BaseFileName = "transport-demo",
            MaxFileBytes = 2 * 1024 * 1024,
            MaxBackups = 2,
            MinLevel = "Info",
        }, Array.Empty<string>());

        // Own synthetic server copy (id "synthetic-adapter", tool echo/fail_echo).
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
        File.WriteAllText(Path.Combine(sandbox, "synthetic-mcp-server.mjs"), js);

        var cfg = ConfigStore.LoadOrCreate();
        // Update-or-add (idempotent across runs): stale persisted defs must not resurrect old paths.
        var demoDef = cfg.Servers.FirstOrDefault(s => s.Id == "synthetic-adapter");
        if (demoDef != null)
        {
            demoDef.Command = "node";
            demoDef.Arguments = new List<string> { Path.Combine(sandbox, "synthetic-mcp-server.mjs") };
            demoDef.WorkingDirectory = sandbox;
            demoDef.Transport = "stdio-native";
        }
        else
        {
            cfg.Servers.Add(new ServerConfig
            {
                Id = "synthetic-adapter",
                Name = "Synthetic Adapter Server",
                Command = "node",
                Arguments = new List<string> { Path.Combine(sandbox, "synthetic-mcp-server.mjs") },
                WorkingDirectory = sandbox,
                Transport = "stdio-native",
            });
        }
        cfg.NativeTransport = new NativeTransportSettings { Enabled = true, PreferredPort = 8399, RateLimitPerMinute = 5000 };
        Registry.Initialize(cfg);
        Registry.EnsureNativeServerDefinition();
        AuthStore.Initialize(ConfigStore.ConfigDir);
        // Idempotent project: reuse if a previous demo run created it (fresh token each run).
        var existingProj = Registry.GetProject("adapter-demo");
        if (existingProj != null) Registry.DeleteProject(existingProj.Id);
        var (proj, issued) = Registry.CreateProject("adapter-demo", "Z.ai Adapter Demo", new[] { "synthetic-adapter" });

        var bridge = new StdioMcpBridge();
        var port = NativeTransportHost.Start(8399, cfg, bridge);

        var endpoint = $"http://127.0.0.1:{port}/prj/{proj.Slug}/mcp";
        File.WriteAllText(Path.Combine(sandbox, "port.txt"), port.ToString());
        File.WriteAllText(Path.Combine(sandbox, "endpoint.txt"), endpoint);
        File.WriteAllText(Path.Combine(sandbox, "token.txt"), issued.PlaintextToken);
        LogService.Warn("transport", null, $"TRANSPORT DEMO READY: {endpoint} (wait for stop.flag or 240s)");
        Console.WriteLine($"[demo] READY endpoint={endpoint}");

        var stopFlag = Path.Combine(sandbox, "stop.flag");
        var deadline = DateTime.UtcNow.AddSeconds(240);
        while (!File.Exists(stopFlag) && DateTime.UtcNow < deadline)
            Thread.Sleep(500);

        NativeTransportHost.Stop();
        bridge.StopAll();
        LogService.Warn("transport", null, "TRANSPORT DEMO stopped.");
        Console.WriteLine("[demo] stopped");
        return 0;
    }
}
