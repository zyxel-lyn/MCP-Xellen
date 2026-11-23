using System.Diagnostics;
using System.Text;
using McpXellen.Core;
using McpXellen.Transport;
using McpXellen.UI;

namespace McpXellen;

/// <summary>Remote Access — READ-ONLY status viewer. It NEVER mutates Tailscale Funnel/Serve,
/// firewall, or endpoints; it only reports what is actually live plus the planned commands.</summary>
internal sealed partial class MainForm
{
    private readonly RichTextBox rtbRemote = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9f),
        BackColor = Color.FromArgb(250, 250, 252),
    };
    private readonly FlowLayoutPanel remoteBar = new() { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6) };
    private readonly Button btnRefreshRemote = new() { Text = "Refresh status (read-only)", AutoSize = true };
    private readonly Label lblRemoteNote = new()
    {
        AutoSize = true, ForeColor = DesignSystem.Slate,
        Text = "Viewer hanya membaca. Perubahan Funnel/Serve/firewall TIDAK dilakukan otomatis — butuh persetujuan eksplisit.",
    };

    private void InitRemoteTab(TabControl tabs)
    {
        remoteBar.Controls.Add(btnRefreshRemote);
        remoteBar.Controls.Add(lblRemoteNote);
        var page = new TabPage("Remote");
        page.Controls.Add(rtbRemote);
        page.Controls.Add(remoteBar);
        tabs.TabPages.Add(page);
        btnRefreshRemote.Click += (_, _) => RunRemoteStatus();
        RunRemoteStatus();
    }

    private void RunRemoteStatus()
    {
        btnRefreshRemote.Enabled = false;
        Task.Run(() =>
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Tailscale status (read-only) ===");
            sb.AppendLine(RunTool("tailscale.exe", "status --peers=false", 6));
            sb.AppendLine();
            sb.AppendLine("=== Tailscale serve/funnel status (read-only) ===");
            sb.AppendLine(RunTool(@"C:\Program Files\Tailscale\tailscale.exe", "serve status", 6));
            sb.AppendLine();
            sb.AppendLine("=== Xellen endpoints (health, read-only) ===");
            foreach (var p in new[] { 8023, 8024, 8025, 8089 })
            {
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                    var resp = http.GetAsync($"http://127.0.0.1:{p}/healthz").GetAwaiter().GetResult();
                    sb.AppendLine($"port {p} /healthz -> {(int)resp.StatusCode}");
                }
                catch (Exception ex) { sb.AppendLine($"port {p} /healthz -> UNREACHABLE ({ex.Message})"); }
            }
            if (NativeTransportHost.Running)
                sb.AppendLine($"native transport : {NativeTransportHost.Port} (sessions={NativeTransportHost.ActiveSessions})");
            else
                sb.AppendLine("native transport : OFF (aktifkan via config nativeTransport.enabled=true)");
            sb.AppendLine();
            sb.AppendLine("=== Deteksi route basi (report-only, tidak dihapus) ===");
            sb.AppendLine("- /oc -> 127.0.0.1:8801 dan /wcu -> 127.0.0.1:8802 tercatat di audit 11 Nov sebagai route tanpa listener. Verifikasi manual dengan 'tailscale serve status'; penghapusan butuh persetujuan owner.");

            BeginInvoke(new Action(() =>
            {
                rtbRemote.Clear();
                rtbRemote.Text = sb.ToString();
                btnRefreshRemote.Enabled = true;
            }));
        });
    }

    private static string RunTool(string exe, string args, int timeoutSec)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true, // jangan buka jendela console saat owner klik Refresh
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutSec * 1000)) { try { p.Kill(); } catch { } return "(timeout)"; }
            var s = outTask.Result + errTask.Result;
            return Redaction.Sanitize(s.TrimEnd(), 2000);
        }
        catch (Exception ex) { return "(gagal: " + ex.Message + ")"; }
    }
}
