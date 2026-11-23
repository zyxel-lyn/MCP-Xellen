using System.Diagnostics;
using McpXellen.Browser;
using McpXellen.Core;

namespace McpXellen;

/// <summary>Tab Browser: kelola sesi browser terisolasi per client (Browser Manager).
/// Operator view: bisa melihat semua sesi; mutasi tetap lewat API ownership-checked.</summary>
internal sealed partial class MainForm
{
    private readonly ListView lvBrowser = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        HideSelection = true, BorderStyle = BorderStyle.None, MultiSelect = false,
    };
    private readonly Label browserSummary = new()
    {
        Dock = DockStyle.Top, Height = 56, Padding = new Padding(8, 8, 8, 0),
        ForeColor = Color.DimGray,
    };
    private readonly TextBox txtBrowserClientId = new() { Width = 140, Text = "client-a" };
    private readonly TextBox txtBrowserSessionName = new() { Width = 160, PlaceholderText = "nama sesi (opsional)" };
    private readonly Button btnBrowserNew = new() { Text = "New Session", AutoSize = true };
    private readonly Button btnBrowserStart = new() { Text = "Start (lazy)", AutoSize = true, Enabled = false };
    private readonly Button btnBrowserClose = new() { Text = "Close Session", AutoSize = true, Enabled = false };
    private readonly Button btnBrowserProfile = new() { Text = "Open Profile Folder", AutoSize = true, Enabled = false };
    private readonly Button btnBrowserEndpoint = new() { Text = "Copy MCP Endpoint", AutoSize = true, Enabled = false };
    private readonly Button btnBrowserRefresh = new() { Text = "Refresh", AutoSize = true };
    private readonly System.Windows.Forms.Timer browserTimer = new() { Interval = 5000 };

    private void InitBrowserTab(TabControl tabs)
    {
        lvBrowser.Columns.Add("Session", 120);
        lvBrowser.Columns.Add("Client", 100);
        lvBrowser.Columns.Add("Status", 90);
        lvBrowser.Columns.Add("Agent Port", 80);
        lvBrowser.Columns.Add("Wrapper Port", 90);
        lvBrowser.Columns.Add("PID(s)", 110);
        lvBrowser.Columns.Add("Idle (m)", 70);
        lvBrowser.Columns.Add("Headless", 70);
        lvBrowser.Columns.Add("Created", 120);
        lvBrowser.Columns.Add("Catatan", 220);

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 8, 8, 0), FlowDirection = FlowDirection.LeftToRight,
        };
        top.Controls.Add(new Label { Text = "Client:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        top.Controls.Add(txtBrowserClientId);
        top.Controls.Add(new Label { Text = "Nama:", AutoSize = true, Margin = new Padding(8, 8, 3, 0) });
        top.Controls.Add(txtBrowserSessionName);
        top.Controls.Add(btnBrowserNew);
        top.Controls.Add(btnBrowserStart);
        top.Controls.Add(btnBrowserClose);
        top.Controls.Add(btnBrowserProfile);
        top.Controls.Add(btnBrowserEndpoint);
        top.Controls.Add(btnBrowserRefresh);

        var page = new TabPage("Browser");
        page.Controls.Add(lvBrowser);
        page.Controls.Add(browserSummary);
        page.Controls.Add(top);
        tabs.TabPages.Add(page);

        btnBrowserNew.Click += (_, _) => BrowserNewSession();
        btnBrowserStart.Click += (_, _) => BrowserStartSelected();
        btnBrowserClose.Click += (_, _) => BrowserCloseSelected();
        btnBrowserProfile.Click += (_, _) => BrowserOpenProfile();
        btnBrowserEndpoint.Click += (_, _) => BrowserCopyEndpoint();
        btnBrowserRefresh.Click += (_, _) => RefreshBrowser();
        lvBrowser.SelectedIndexChanged += (_, _) => BrowserUpdateButtonStates();

        browserTimer.Tick += (_, _) => RefreshBrowser();
        browserTimer.Start();
        RefreshBrowser();
    }

    private BrowserSessionRecord? BrowserSelected()
    {
        if (lvBrowser.SelectedItems.Count == 0) return null;
        var sid = lvBrowser.SelectedItems[0].Tag as string;
        return BrowserManager.Instance.ListSessions().FirstOrDefault(s => s.SessionId == sid);
    }

    private void BrowserUpdateButtonStates()
    {
        var sel = BrowserSelected();
        bool active = sel is { Status: "Ready" or "Starting" or "Creating" };
        btnBrowserStart.Enabled = sel is { Status: "Creating" or "Failed" or "Stopped" };
        btnBrowserClose.Enabled = active;
        btnBrowserProfile.Enabled = sel != null && Directory.Exists(sel.ProfileDir);
        btnBrowserEndpoint.Enabled = sel is { Status: "Ready" };
    }

    private void RefreshBrowser()
    {
        try
        {
            var bm = BrowserManager.Instance;
            var sessions = bm.ListSessions();
            var active = sessions.Count(s => s.Status is "Ready" or "Starting" or "Creating");
            browserSummary.Text =
                $"Browser Manager: {(bm.Enabled ? "ENABLED" : "DISABLED (config.browserManager.enabled=false)")}   •   " +
                $"Sesi aktif: {active}/{bm.MaxActiveSessions}   •   Idle stop: {bm.IdleTimeoutMinutes} menit (parameter terkunci owner)\n" +
                $"Isolasi: agent-infra per client + user-data-dir per sesi + Bearer per sesi   •   Daemon 9010 / Chrome pribadi / Hermes: TIDAK disentuh";

            lvBrowser.BeginUpdate();
            lvBrowser.Items.Clear();
            foreach (var s in sessions)
            {
                var item = new ListViewItem(s.SessionId[..Math.Min(8, s.SessionId.Length)]) { Tag = s.SessionId };
                item.SubItems.Add(s.ClientId);
                item.SubItems.Add(s.Status);
                item.SubItems.Add(s.AgentPort > 0 ? s.AgentPort.ToString() : "—");
                item.SubItems.Add(s.WrapperPort > 0 ? s.WrapperPort.ToString() : "—");
                item.SubItems.Add(s.OwnedPids.Count > 0 ? string.Join(",", s.OwnedPids) : "—");
                item.SubItems.Add(s.Status == "Ready" ? ((DateTime.UtcNow - s.LastActivityUtc).TotalMinutes).ToString("F0") : "—");
                item.SubItems.Add(s.Headless ? "yes" : "no");
                item.SubItems.Add(s.CreatedUtc.ToLocalTime().ToString("MM-dd HH:mm"));
                item.SubItems.Add(s.Error ?? (s.Status == "Ready" ? $"MCP: http://127.0.0.1:{s.WrapperPort}/mcp" : ""));
                item.ForeColor = s.Status switch
                {
                    "Ready" => Color.DarkGreen,
                    "Starting" or "Creating" => Color.DarkGoldenrod,
                    "Failed" => Color.Firebrick,
                    _ => Color.DimGray,
                };
                lvBrowser.Items.Add(item);
            }
            lvBrowser.EndUpdate();
            BrowserUpdateButtonStates();
        }
        catch (Exception ex)
        {
            LogService.Warn("ui", null, "Browser tab refresh failed", ex.Message);
        }
    }

    private void BrowserNewSession()
    {
        try
        {
            var rec = BrowserManager.Instance.CreateSession(txtBrowserClientId.Text.Trim(), txtBrowserSessionName.Text.Trim());
            Notify($"Browser session dibuat untuk {rec.ClientId} (lazy). Token per sesi tersimpan di file secrets (tidak ditampilkan).", error: false);
            RefreshBrowser();
        }
        catch (Exception ex)
        {
            Notify("Gagal membuat browser session: " + ex.Message, error: true);
        }
    }

    private void BrowserStartSelected()
    {
        var sel = BrowserSelected();
        if (sel == null) return;
        btnBrowserStart.Enabled = false;
        Notify($"Lazy start sesi {sel.SessionId[..8]}… (agent-infra + wrapper auth, window: tidak ada)", error: false);
        Task.Run(async () =>
        {
            bool ok = await BrowserManager.Instance.StartSessionAsync(sel.SessionId, sel.ClientId);
            try { BeginInvoke(new Action(() => { RefreshBrowser(); Notify(ok ? $"Browser session ready: http://127.0.0.1:{sel.WrapperPort}/mcp (Bearer per session)" : "Start browser session GAGAL — lihat tab Logs / Activity Monitor", error: !ok); })); }
            catch { }
        });
    }

    private void BrowserCloseSelected()
    {
        var sel = BrowserSelected();
        if (sel == null) return;
        var answer = MessageBox.Show(
            $"Tutup sesi {sel.SessionId[..8]} (client {sel.ClientId})?\nProses dihentikan; profil tetap tersimpan (tidak pernah dihapus otomatis).",
            "MCP Xellen", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;
        Task.Run(() =>
        {
            bool ok = BrowserManager.Instance.CloseSession(sel.SessionId, sel.ClientId, "owner-ui");
            try { BeginInvoke(new Action(() => { RefreshBrowser(); Notify(ok ? "Browser session ditutup; profil persisten." : "Gagal menutup sesi (ownership).", error: !ok); })); }
            catch { }
        });
    }

    private void BrowserOpenProfile()
    {
        var sel = BrowserSelected();
        if (sel == null || !Directory.Exists(sel.ProfileDir)) return;
        try { Process.Start("explorer.exe", sel.ProfileDir); }
        catch (Exception ex) { Notify("Gagal membuka folder profil: " + ex.Message, error: true); }
    }

    private void BrowserCopyEndpoint()
    {
        var sel = BrowserSelected();
        if (sel is not { Status: "Ready" }) return;
        // Endpoint TIDAK mengandung token (Bearer dikirim lewat header) — aman untuk disalin.
        try
        {
            Clipboard.SetText($"http://127.0.0.1:{sel.WrapperPort}/mcp");
            Notify("MCP endpoint disalin (Bearer dikirim lewat header; token tidak pernah di URL).", error: false);
        }
        catch { MessageBox.Show("Gagal menyalin endpoint.", "MCP Xellen"); }
    }
}
