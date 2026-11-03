using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace McpXellen;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private static string Root => Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName ?? AppContext.BaseDirectory;
    private static string BackendDir => Path.Combine(Root, "backend");
    private static string DcDir => Path.Combine(BackendDir, "node_modules", "@wonderwhy-er", "desktop-commander");
    private static string Gateway => Path.Combine(BackendDir, "node_modules", "supergateway", "dist", "index.js");
    private static string TokenFile => Path.Combine(Root, "secrets", "mcp-xellen-token.txt");
    private static string SettingsFile => Path.Combine(Root, "config.json");
    private static string LogFile => Path.Combine(Root, "logs", "mcp-xellen.log");
    private const int Port = 8023;
    private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(3) };

    private Process? server;
    private readonly Label status = new() { AutoSize = true, Text = "Stopped", Font = new Font("Segoe UI", 10, FontStyle.Bold) };
    private readonly Label connection = new() { AutoSize = true, Text = "Gateway not started", ForeColor = Color.DimGray };
    private readonly Button copyPrompt = new() { Text = "Copy Z.ai Magic Prompt", AutoSize = true, Padding = new Padding(10, 6, 10, 6), Enabled = false };
    private readonly Button locateToken = new() { Text = "Locate Token File", AutoSize = true, Padding = new Padding(10, 6, 10, 6), Enabled = false };
    private readonly Button verify = new() { Text = "Verify Gateway", AutoSize = true, Padding = new Padding(10, 6, 10, 6), Enabled = false };
    private readonly Button stop = new() { Text = "Stop", AutoSize = true, Padding = new Padding(10, 6, 10, 6), Enabled = false };
    private readonly System.Windows.Forms.Timer healthTimer = new() { Interval = 1500 };

    public MainForm()
    {
        Text = "MCP Xellen";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        ClientSize = new Size(590, 365);
        Font = new Font("Segoe UI", 9);

        var heading = new Label { Text = "MCP Xellen", AutoSize = true, Font = new Font("Segoe UI", 19, FontStyle.Bold), Location = new Point(24, 18) };
        var sub = new Label { Text = "Supergateway â€¢ Official Desktop Commander â€¢ Streamable HTTP", AutoSize = true, ForeColor = Color.DimGray, Location = new Point(27, 57) };
        status.Location = new Point(27, 90);
        connection.Location = new Point(27, 122);
        copyPrompt.Location = new Point(27, 165);
        locateToken.Location = new Point(27, 215);
        verify.Location = new Point(200, 215);
        stop.Location = new Point(455, 215);
        var endpoint = new Label { Text = "Endpoint: " + (LoadPublicEndpoint() is { Length: > 0 } value ? value : "Set publicEndpoint in config.json"), AutoSize = true, ForeColor = Color.DimGray, Location = new Point(27, 275) };
        var note = new Label { Text = "Token stays on this PC. Upload the token file separately to your Z.ai Agent Mode session.", AutoSize = true, ForeColor = Color.DimGray, Location = new Point(27, 305) };
        var security = new Label { Text = "All Desktop Commander tools inherit your Windows account permissions.", AutoSize = true, ForeColor = Color.DarkRed, Location = new Point(27, 330) };
        Controls.AddRange([heading, sub, status, connection, copyPrompt, locateToken, verify, stop, endpoint, note, security]);

        copyPrompt.Click += (_, _) => CopyPrompt();
        locateToken.Click += (_, _) => LocateToken();
        verify.Click += async (_, _) => await VerifyGateway();
        stop.Click += (_, _) => StopServer();
        healthTimer.Tick += async (_, _) => await CheckHealth();
        FormClosing += (_, _) => StopServer();
        Shown += (_, _) => StartServer();
    }

    private static string LoadPublicEndpoint()
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(SettingsFile));
            return json.RootElement.TryGetProperty("publicEndpoint", out var value) ? value.GetString()?.Trim() ?? "" : "";
        }
        catch { return ""; }
    }

    private void StartServer()
    {
        if (server is { HasExited: false }) return;
        var dcEntry = Path.Combine(DcDir, "dist", "index.js");
        if (!File.Exists(TokenFile) || !File.Exists(Gateway) || !File.Exists(dcEntry))
        {
            MessageBox.Show("Komponen belum lengkap. Jalankan scripts\\install.ps1 dan periksa Node.js serta file token.", "MCP Xellen", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
            var psi = new ProcessStartInfo
            {
                FileName = "node",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Root
            };
            psi.ArgumentList.Add(Gateway);
            psi.ArgumentList.Add("--stdio");
            psi.ArgumentList.Add("node \"" + dcEntry + "\" --no-onboarding");
            psi.ArgumentList.Add("--outputTransport");
            psi.ArgumentList.Add("streamableHttp");
            psi.ArgumentList.Add("--port");
            psi.ArgumentList.Add(Port.ToString());
            psi.ArgumentList.Add("--host");
            psi.ArgumentList.Add("127.0.0.1");
            psi.ArgumentList.Add("--apiKeyFile");
            psi.ArgumentList.Add(TokenFile);
            psi.ArgumentList.Add("--stateful");
            psi.ArgumentList.Add("--sessionTimeout");
            psi.ArgumentList.Add("1800000");
            psi.ArgumentList.Add("--healthEndpoint");
            psi.ArgumentList.Add("/healthz");

            server = new Process { StartInfo = psi, EnableRaisingEvents = true };
            server.OutputDataReceived += (_, e) => WriteLog(e.Data);
            server.ErrorDataReceived += (_, e) => WriteLog(e.Data);
            server.Exited += (_, _) =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke(new Action(() => { healthTimer.Stop(); status.Text = "Stopped"; SetButtons(false); })); } catch { }
            };

            if (!server.Start()) throw new InvalidOperationException("Node.js gagal dijalankan. Pastikan Node.js tersedia di PATH.");
            server.BeginOutputReadLine();
            server.BeginErrorReadLine();
            status.Text = "Starting Supergateway + Desktop Commander...";
            SetButtons(false);
            stop.Enabled = true;
            healthTimer.Start();
        }
        catch (Exception ex)
        {
            StopServer();
            MessageBox.Show("Gagal menjalankan gateway: " + ex.Message, "MCP Xellen", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        if (server is null || server.HasExited) { status.Text = "Stopped"; SetButtons(false); return; }
        try
        {
            using var response = await http.GetAsync($"http://127.0.0.1:{Port}/healthz");
            if (response.IsSuccessStatusCode)
            {
                status.Text = "Running â€¢ Bearer Token required";
                connection.Text = "Gateway ready â€¢ Z.ai not yet verified";
                SetButtons(true);
            }
            else status.Text = "Waiting for Desktop Commander...";
        }
        catch { status.Text = "Starting / waiting for backend..."; }
    }

    private void CopyPrompt()
    {
        var endpoint = LoadPublicEndpoint();
        if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            MessageBox.Show("Isi publicEndpoint dengan URL HTTPS Funnel milik Anda di config.json, lalu restart MCP Xellen.", "MCP Xellen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var prompt = $$"""
Anda adalah agent yang akan menghubungkan Z.ai Website Agent Mode ke MCP Xellen melalui Python HTTP client. Ini BUKAN konfigurasi menu MCP Settings dan BUKAN ZCode.

Endpoint MCP Streamable HTTP:
{{endpoint}}

Token sudah saya unggah sebagai file upload/mcp-xellen-token.txt.
Baca token langsung dari file tersebut menggunakan Python. Jangan pernah mencetak token, memasukkannya ke prompt, menulisnya ke log, atau mengirimkannya ke URL selain endpoint MCP di atas. Jika file tidak ditemukan, berhenti dan minta saya mengunggah file token dari aplikasi MCP Xellen.

Gunakan Python requests dari Agent Mode Bash. Buat klien MCP Streamable HTTP yang:
1. Mengirim Authorization: Bearer <token>, Accept: application/json, text/event-stream, dan MCP-Protocol-Version: 2025-06-18.
2. Melakukan initialize dengan protocolVersion 2025-06-18, lalu menyimpan header Mcp-Session-Id.
3. Mengirim notifications/initialized.
4. Memanggil tools/list, menangani respons JSON maupun SSE (data:), dan menampilkan nama serta deskripsi tools tanpa menampilkan token.
5. Menyimpan helper untuk tools/call dengan session ID yang sama. Jika server meminta session baru atau session kedaluwarsa, ulangi handshake.
6. Tidak menganggap koneksi berhasil hanya dari health check. Buktikan dengan hasil initialize dan tools/list.

Simpan klien di scripts/mcp_xellen_client.py. Mulai dengan koneksi dan daftar tools saja. Sebelum perubahan file, penghapusan, commit, push, atau perintah yang mengubah sistem, jelaskan target dan perubahan lalu minta persetujuan eksplisit saya.
""";
        try { Clipboard.SetText(prompt); connection.Text = "Magic Prompt copied â€¢ upload token file separately"; }
        catch { MessageBox.Show("Gagal menyalin prompt.", "MCP Xellen"); }
    }

    private void LocateToken()
    {
        if (!File.Exists(TokenFile)) { MessageBox.Show("File token belum ditemukan.", "MCP Xellen"); return; }
        Process.Start("explorer.exe", Path.Combine(Root, "secrets"));
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
                connection.Text = "Verified locally â€¢ healthy + rejects missing token";
            else
                connection.Text = "Verification incomplete â€¢ check gateway logs";
        }
        catch { connection.Text = "Cannot verify local gateway"; }
    }

    private void StopServer()
    {
        healthTimer.Stop();
        try { if (server is { HasExited: false }) server.Kill(entireProcessTree: true); server?.WaitForExit(2500); } catch { }
        server?.Dispose();
        server = null;
        status.Text = "Stopped";
        SetButtons(false);
        stop.Enabled = false;
    }

    private void WriteLog(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        try { File.AppendAllText(LogFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}", Encoding.UTF8); } catch { }
    }
}
