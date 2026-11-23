using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using McpXellen.Browser;
using McpXellen.Core;
using McpXellen.Transport;
using McpXellen.UI;

namespace McpXellen;

internal static class Program
{
    internal const string GatewayServerId = "dc-staging-8023";

    [STAThread]
    static void Main(string[] args)
    {
        // Headless automated test mode (P1 acceptance tests). Never touches production endpoints.
        if (args.Any(a => a.StartsWith("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            var filter = args.FirstOrDefault(a => a.Contains('='))?.Split('=', 2)[1];
            Environment.Exit(McpXellen.SelfTest.SelfTestRunner.RunAll(filter));
        }

        // TEST-ONLY: live native transport for external client tests (e.g. the Z.ai Python adapter).
        if (args.Contains("--transport-demo", StringComparer.OrdinalIgnoreCase))
        {
            McpXellen.Transport.TransportDemo.Run();
            return;
        }

        // Core init before UI so lifecycle/logging works even if the form fails.
        var config = ConfigStore.LoadOrCreate();
        var tokens = config.Servers
            .Where(s => !string.IsNullOrEmpty(s.AuthTokenFile) && File.Exists(s.AuthTokenFile))
            .Select(s => File.ReadAllText(s.AuthTokenFile!));
        LogService.Init(config.Log, tokens);
        ProcessHost.ProtectedPatterns = config.ProtectedProcessPatterns;
        var state = ConfigStore.LoadRuntimeState();
        ProcessHost.Instance.Initialize(config, state);
        MetricsService.Start(config);
        Registry.Initialize(config);
        Registry.EnsureNativeServerDefinition();
        AuthStore.Initialize(ConfigStore.ConfigDir);
        ActivityStore.Configure(config.Log.Directory, persist: true);

        // Browser Manager (Opsi 3 Hybrid, disetujui owner): opt-in via config.browserManager.enabled.
        // Initialize selalu dijalankan agar startup orphan sweep record milik sendiri tetap jalan;
        // CreateSession menolak aman selama enabled=false. Daemon 9010 / Chrome pribadi tidak disentuh.
        BrowserManager.Instance.Initialize(config.BrowserManager);

        // Native MCP Streamable HTTP (dual-run; Supergateway 8023 untouched). Opt-in via config
        // so existing installs keep running exactly as before until they set enabled=true.
        if (config.NativeTransport.Enabled)
        {
            try { NativeTransportHost.Start(config.NativeTransport.PreferredPort, config, XellenRuntime.Bridge); }
            catch (Exception ex)
            {
                LogService.Error("transport", null, "native transport start FAILED — continuing in compatibility mode only", ex.Message);
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed partial class MainForm : Form
{
    private const string Root = @"D:\MCP-Bridge";
    private const string TokenFile = @"D:\MCP-Bridge\secrets\mcp-xellen-staging-token.txt";
    private const int Port = 8023;
    private const string PublicEndpoint = "https://laptop-see8pp9p.tail943ca4.ts.net:8443/mcp";

    private readonly Label status = new() { AutoSize = true, Text = "Stopped", Font = new Font("Segoe UI", 10, FontStyle.Bold) };
    private readonly Label connection = new() { AutoSize = true, Text = "Z.ai: Not connected", ForeColor = Color.DimGray };
    private readonly Button copyPrompt = new() { Text = "Copy Z.ai Magic Prompt", AutoSize = true, Padding = new Padding(10, 6, 10, 6), Enabled = false };
    private readonly Button locateToken = new() { Text = "Locate Token File", AutoSize = true, Padding = new Padding(10, 6, 10, 6), Enabled = false };
    private readonly Button verify = new() { Text = "Verify Gateway", AutoSize = true, Padding = new Padding(10, 6, 10, 6), Enabled = false };
    private readonly Button stop = new() { Text = "Stop", AutoSize = true, Padding = new Padding(10, 6, 10, 6), Enabled = false };
    private readonly System.Windows.Forms.Timer healthTimer = new() { Interval = 1500 };
    private readonly Button openActivity = new() { Text = "Open AI Activity Monitor", AutoSize = true, Padding = new Padding(10, 6, 10, 6), Enabled = true };
    private ActivityMonitorForm? activityWindow;
    private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly StatusStrip strip = new();
    private readonly ToolStripStatusLabel notifyLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

    public MainForm()
    {
        Text = "MCP Xellen";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = true;
        MaximizeBox = true;
        ClientSize = new Size(1000, 700);
        MinimumSize = new Size(840, 580);
        Font = new Font("Segoe UI", 9);

        // ---- Tab 1: existing gateway functions, preserved verbatim ----
        var tpGateway = new TabPage("Gateway (Existing)");
        var heading = new Label { Text = "MCP Xellen", AutoSize = true, Font = new Font("Segoe UI", 19, FontStyle.Bold), Location = new Point(24, 18) };
        var sub = new Label { Text = "Direct Bearer Token • Supergateway • Official Desktop Commander", AutoSize = true, ForeColor = Color.DimGray, Location = new Point(27, 57) };
        status.Location = new Point(27, 90);
        connection.Location = new Point(27, 122);
        copyPrompt.Location = new Point(27, 165);
        openActivity.Location = new Point(240, 165);
        locateToken.Location = new Point(27, 215);
        verify.Location = new Point(200, 215);
        stop.Location = new Point(430, 215);
        var endpoint = new Label { Text = "Staging: " + PublicEndpoint, AutoSize = true, ForeColor = Color.DimGray, Location = new Point(27, 275) };
        var note = new Label { Text = "Token tersimpan lokal • Upload file token secara manual ke sandbox Z.ai", AutoSize = true, ForeColor = Color.DimGray, Location = new Point(27, 305) };
        var production = new Label { Text = "Production :443 / port 8020 tidak diubah", AutoSize = true, ForeColor = Color.DarkGreen, Location = new Point(27, 330) };
        tpGateway.Controls.AddRange([heading, sub, status, connection, copyPrompt, locateToken, verify, stop, openActivity, endpoint, note, production]);

        tabs.TabPages.Add(tpGateway);
        InitDashboardTab(tabs);
        InitProjectsTab(tabs);
        InitRemoteTab(tabs);
        InitProcessesTab(tabs);
        InitBrowserTab(tabs);
        InitLogTab(tabs);

        strip.Items.Add(notifyLabel);
        Controls.Add(tabs);
        Controls.Add(strip);

        copyPrompt.Click += (_, _) => CopyPrompt();
        locateToken.Click += (_, _) => LocateToken();
        verify.Click += async (_, _) => await VerifyGateway();
        stop.Click += (_, _) => StopGateway();
        openActivity.Click += (_, _) => { activityWindow?.Close(); activityWindow = new ActivityMonitorForm(); activityWindow.Show(); };
        healthTimer.Tick += async (_, _) => await CheckHealth();
        ProcessHost.Instance.Notification += msg => { try { BeginInvoke(new Action(() => Notify(msg, error: true))); } catch { } };
        ProcessHost.Instance.ServerChanged += _ => { try { BeginInvoke(new Action(() => RefreshDashboard())); } catch { } };
        FormClosing += (_, _) =>
        {
            NativeTransportHost.Stop();
            XellenRuntime.Bridge.StopAll();
            ProcessHost.Instance.ShutdownAll();
            BrowserManager.Instance.ShutdownAll();
        };
        Shown += (_, _) => StartServer();
    }

    private void Notify(string msg, bool error)
    {
        notifyLabel.Text = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        notifyLabel.ForeColor = error ? Color.Firebrick : Color.DimGray;
        LogService.Info("ui", null, msg);
    }

    private static bool PortListening(int port)
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

    // ---------------------------------------------------------------- existing gateway functions

    private void StartServer()
    {
        var t = ProcessHost.Instance.Get(Program.GatewayServerId);
        if (t is { Proc: { HasExited: false } }) { healthTimer.Start(); return; }

        // Side-by-side safety: if something else already serves 8023 (e.g. the legacy launcher
        // still running), NEVER fight it. Report and manage nothing.
        if (PortListening(Port))
        {
            status.Text = "Running (external)";
            connection.Text = "Gateway 8023 dijalankan proses lain — mode berdampingan, endpoint tidak disentuh.";
            SetButtons(true);
            stop.Enabled = false;
            healthTimer.Start();
            Notify("Gateway 8023 sudah berjalan oleh proses lain. Mode berdampingan aktif; endpoint tidak disentuh.", error: false);
            return;
        }

        if (!File.Exists(TokenFile))
            Notify("Token staging tidak ditemukan di secrets\\mcp-xellen-staging-token.txt.", error: true);

        if (ProcessHost.Instance.StartServer(Program.GatewayServerId, out var err))
        {
            status.Text = "Starting Supergateway + Desktop Commander...";
            SetButtons(false);
            stop.Enabled = true;
            healthTimer.Start();
        }
        else
        {
            MessageBox.Show("Gagal menjalankan staging gateway: " + err, "MCP Xellen", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Notify("Start gateway gagal: " + err, error: true);
        }
    }

    private void SetButtons(bool ready)
    {
        copyPrompt.Enabled = ready;
        locateToken.Enabled = ready;
        verify.Enabled = ready;
    }

    private async Task CheckHealth()
    {
        var t = ProcessHost.Instance.Get(Program.GatewayServerId);
        bool managedAlive = t?.Proc is { HasExited: false };
        try
        {
            using var response = await http.GetAsync($"http://127.0.0.1:{Port}/healthz");
            if (response.IsSuccessStatusCode)
            {
                status.Text = managedAlive ? "Running • Bearer Token required" : "Running (external) • Bearer Token required";
                connection.Text = "Gateway ready • Z.ai not yet verified";
                SetButtons(true);
                if (managedAlive) stop.Enabled = true;
            }
            else status.Text = "Waiting for Desktop Commander...";
        }
        catch
        {
            if (managedAlive) status.Text = "Starting / waiting for backend...";
            else if (PortListening(Port)) status.Text = "Running (external)";
            else { status.Text = "Stopped"; SetButtons(false); }
        }
    }

    private void StopGateway()
    {
        healthTimer.Stop();
        var t = ProcessHost.Instance.Get(Program.GatewayServerId);
        if (t is { Proc: { HasExited: false } })
        {
            ProcessHost.Instance.StopServer(Program.GatewayServerId);
            Notify("Gateway dihentikan (graceful stdin-close + tree cleanup).", error: false);
        }
        else
        {
            Notify("Tidak ada gateway terkelola yang berjalan; gateway eksternal tidak disentuh.", error: false);
        }
        status.Text = "Stopped";
        SetButtons(false);
        stop.Enabled = false;
    }

    private void CopyPrompt()
    {
        var prompt = """
Anda adalah agent yang akan menghubungkan Z.ai Website Agent Mode ke MCP Xellen melalui Python HTTP client. Ini BUKAN konfigurasi menu MCP Settings dan BUKAN ZCode.

Endpoint MCP Streamable HTTP:
https://laptop-see8pp9p.tail943ca4.ts.net:8443/mcp

Token sudah saya unggah sebagai file upload/mcp-xellen-staging-token.txt.
Baca token langsung dari file tersebut menggunakan Python. Jangan pernah mencetak token, memasukkannya ke prompt, menulisnya ke log, atau mengirimkannya ke URL selain endpoint MCP di atas. Jika file tidak ditemukan, berhenti dan minta saya mengunggah file token dari aplikasi MCP Xellen.

Gunakan Python requests dari Agent Mode Bash. Buat klien MCP Streamable HTTP yang:
1. Mengirim Authorization: Bearer <token>, Accept: application/json, text/event-stream, dan MCP-Protocol-Version: 2025-06-18.
2. Melakukan initialize dengan protocolVersion 2025-06-18, lalu menyimpan header Mcp-Session-Id.
3. Mengirim notifications/initialized.
4. Memanggil tools/list, menangani respons JSON maupun SSE (data:), dan menampilkan nama serta deskripsi tools tanpa menampilkan token.
5. Menyimpan helper untuk tools/call dengan session ID yang sama. Jika server meminta session baru atau session kedaluwarsa, ulangi handshake.
6. Tidak menganggap koneksi berhasil hanya dari health check. Buktikan dengan hasil initialize dan tools/list.

Simpan klien di scripts/mcp_xellen_client.py. Jangan melakukan perubahan file, penghapusan, commit, push, atau menjalankan perintah mutasi sebelum menjelaskan target dan perubahan lalu meminta persetujuan saya secara eksplisit. Mulai dengan koneksi dan daftar tools saja.
""";
        try { Clipboard.SetText(prompt); connection.Text = "Magic Prompt copied • upload token file separately"; }
        catch { MessageBox.Show("Gagal menyalin prompt.", "MCP Xellen"); }
    }

    private void LocateToken()
    {
        if (!File.Exists(TokenFile)) { MessageBox.Show("File token staging tidak ditemukan.", "MCP Xellen"); return; }
        Process.Start("explorer.exe", @"D:\MCP-Bridge\secrets");
    }

    private async Task VerifyGateway()
    {
        try
        {
            using var health = await http.GetAsync($"http://127.0.0.1:{Port}/healthz");
            using var probe = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{Port}/mcp");
            probe.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var unauth = await http.SendAsync(probe);
            if (health.IsSuccessStatusCode && unauth.StatusCode == HttpStatusCode.Unauthorized)
                connection.Text = "Verified locally • healthy + rejects missing token";
            else
                connection.Text = "Verification incomplete • check gateway logs";
        }
        catch { connection.Text = "Cannot verify staging gateway"; }
    }
}
