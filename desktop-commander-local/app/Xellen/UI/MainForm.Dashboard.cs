using McpXellen.Core;

namespace McpXellen;

internal sealed partial class MainForm
{
    private readonly ListView lvServers = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        HideSelection = true, BorderStyle = BorderStyle.None,
    };
    private readonly Label metricsSummary = new()
    {
        Dock = DockStyle.Top, Height = 56, Padding = new Padding(8, 8, 8, 0),
        ForeColor = Color.DimGray,
    };
    private readonly System.Windows.Forms.Timer dashTimer = new() { Interval = 5000 };

    private void InitDashboardTab(TabControl tabs)
    {
        lvServers.Columns.Add("Server", 250);
        lvServers.Columns.Add("Status", 120);
        lvServers.Columns.Add("PID", 70);
        lvServers.Columns.Add("Port", 60);
        lvServers.Columns.Add("Uptime (m)", 85);
        lvServers.Columns.Add("RAM MB (proses)", 120);
        lvServers.Columns.Add("CPU %", 70);
        lvServers.Columns.Add("Sesi Child", 85);
        lvServers.Columns.Add("RAM MB (child)", 110);
        lvServers.Columns.Add("Health", 90);
        lvServers.Columns.Add("Restart", 70);

        var page = new TabPage("Dashboard");
        page.Controls.Add(lvServers);
        page.Controls.Add(metricsSummary);
        tabs.TabPages.Add(page);
        dashTimer.Tick += (_, _) => RefreshDashboard();
        dashTimer.Start();
        RefreshDashboard();
    }

    private void RefreshDashboard()
    {
        try
        {
            var snap = MetricsService.Snapshot(ProcessHost.Instance.StateSnapshot);
            metricsSummary.Text =
                $"Server dikelola: {snap.ManagedServers}   •   Proses aktif: {snap.ActiveProcesses}   •   Gagal: {snap.FailedServers}   •   " +
                $"Orphan terdeteksi (kumulatif): {snap.OrphansDetectedTotal} (dihentikan: {snap.OrphansKilledTotal})\n" +
                $"RAM Xellen Core: {snap.CoreRamMb} MB (WorkingSet terukur)   •   RAM child: {snap.TotalChildRamMb} MB (terukur)   •   Sweep: {snap.SweepStatus}";

            lvServers.BeginUpdate();
            lvServers.Items.Clear();
            foreach (var m in snap.Servers)
            {
                var item = new ListViewItem(m.Name) { Tag = m.Id };
                item.SubItems.Add(m.State.ToString());
                item.SubItems.Add(m.Pid?.ToString() ?? "—");
                item.SubItems.Add(m.Port?.ToString() ?? "—");
                item.SubItems.Add(m.UptimeMinutes > 0 ? m.UptimeMinutes.ToString("F0") : "—");
                item.SubItems.Add(m.ProcRamMb > 0 ? m.ProcRamMb.ToString("F1") : "—");
                item.SubItems.Add(m.CpuPercent >= 0 ? m.CpuPercent.ToString("F1") : "—");
                item.SubItems.Add(m.SessionChildren.ToString());
                item.SubItems.Add(m.ChildRamMb > 0 ? m.ChildRamMb.ToString("F1") : "—");
                item.SubItems.Add(m.Health ?? "—");
                item.SubItems.Add(m.Restarts.ToString());
                item.ForeColor = m.State switch
                {
                    ProcState.Running => Color.DarkGreen,
                    ProcState.Starting or ProcState.RestartPending => Color.DarkGoldenrod,
                    ProcState.Failed or ProcState.Crashed => Color.Firebrick,
                    _ => Color.DimGray,
                };
                lvServers.Items.Add(item);
            }
            lvServers.EndUpdate();
        }
        catch (Exception ex)
        {
            LogService.Warn("ui", null, "Dashboard refresh failed", ex.Message);
        }
    }
}
