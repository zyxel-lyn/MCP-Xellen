using McpXellen.Core;

namespace McpXellen;

internal sealed partial class MainForm
{
    private readonly ListView lvProcs = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = true,
    };
    private readonly TextBox txtSweep = new()
    {
        Dock = DockStyle.Bottom, Multiline = true, ReadOnly = true, Height = 130,
        Font = new Font("Consolas", 8.5f), ScrollBars = ScrollBars.Vertical,
        BackColor = Color.White,
    };
    private readonly FlowLayoutPanel procButtons = new()
    {
        Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(6), FlowDirection = FlowDirection.LeftToRight,
    };
    private readonly Button btnSweep = new() { Text = "Sweep Orphan Sekarang", AutoSize = true, Padding = new Padding(8, 5, 8, 5) };
    private readonly Button btnStopSrv = new() { Text = "Stop Gateway", AutoSize = true, Padding = new Padding(8, 5, 8, 5) };
    private readonly Button btnRestartSrv = new() { Text = "Restart Gateway", AutoSize = true, Padding = new Padding(8, 5, 8, 5) };
    private readonly Label lblPolicy = new() { AutoSize = true, Padding = new Padding(8, 10, 8, 0), ForeColor = Color.DimGray };
    private readonly System.Windows.Forms.Timer procTimer = new() { Interval = 8000 };

    private void InitProcessesTab(TabControl tabs)
    {
        lvProcs.Columns.Add("PID", 70);
        lvProcs.Columns.Add("PPID", 70);
        lvProcs.Columns.Add("Nama", 90);
        lvProcs.Columns.Add("Mulai", 130);
        lvProcs.Columns.Add("RAM MB", 80);
        lvProcs.Columns.Add("Command line", 520);

        procButtons.Controls.Add(btnSweep);
        procButtons.Controls.Add(btnStopSrv);
        procButtons.Controls.Add(btnRestartSrv);
        procButtons.Controls.Add(lblPolicy);

        var page = new TabPage("Proses");
        page.Controls.Add(lvProcs);
        page.Controls.Add(procButtons);
        page.Controls.Add(txtSweep);
        tabs.TabPages.Add(page);

        btnSweep.Click += (_, _) => RunSweepNow();
        btnStopSrv.Click += (_, _) => StopGateway();
        btnRestartSrv.Click += (_, _) =>
        {
            if (MessageBox.Show("Restart gateway 8023? Sesi Z.ai aktif akan terputus sesaat dan reconnect otomatis.",
                    "MCP Xellen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                ProcessHost.Instance.RestartServer(Program.GatewayServerId, out var err);
                if (err.Length > 0) Notify("Restart gagal: " + err, error: true);
            }
        };
        procTimer.Tick += (_, _) => RefreshProcesses();
        procTimer.Start();
        RefreshProcesses();
    }

    private void RefreshProcesses()
    {
        try
        {
            var cfg = ConfigStore.LoadOrCreate(); // cheap enough at 8s; reflects policy label live
            lblPolicy.Text = $"Child policy: MONITOR ({(cfg.ChildPolicy.Enabled ? "peringatan aktif" : "nonaktif")}) • peringatan lifetime>{cfg.ChildPolicy.MaxChildLifetimeMinutes}m • cap {cfg.ChildPolicy.MaxConcurrentChildren} | Auto-cull: NONAKTIF — tidak ada bukti idle" + (cfg.ChildPolicy.AutoCullEnabled ? " [flag aktif tapi DIABAIKAN]" : "") + $" | Sweep mode: {cfg.OrphanSweep.Mode}";

            lvProcs.BeginUpdate();
            lvProcs.Items.Clear();
            foreach (var t in ProcessHost.Instance.Servers)
            {
                var root = new ListViewItem(t.Proc is { HasExited: false } ? t.Proc.Id.ToString() : "—") { ForeColor = Color.MidnightBlue };
                root.SubItems.Add("—");
                root.SubItems.Add("[server] " + t.State);
                root.SubItems.Add(t.StartUtc?.ToLocalTime().ToString("dd-MM HH:mm:ss") ?? "—");
                root.SubItems.Add(t.Proc is { HasExited: false } ? (t.Proc.WorkingSet64 / 1024.0 / 1024.0).ToString("F1") : "—");
                root.SubItems.Add(ProcessHost.TruncCmd(t.Config.Command + " " + string.Join(" ", t.Config.Arguments)));
                lvProcs.Items.Add(root);
                foreach (var c in t.Children.Values.OrderBy(x => x.StartedUtc))
                {
                    var row = new ListViewItem(c.Pid.ToString()) { ForeColor = Color.DimGray };
                    row.SubItems.Add(c.Ppid.ToString());
                    row.SubItems.Add(c.Name.Replace(".exe", ""));
                    row.SubItems.Add(c.StartedUtc?.ToLocalTime().ToString("dd-MM HH:mm:ss") ?? "?");
                    row.SubItems.Add((c.WsBytes / 1024.0 / 1024.0).ToString("F1"));
                    row.SubItems.Add(ProcessHost.TruncCmd(c.Cmd));
                    lvProcs.Items.Add(row);
                }
            }
            lvProcs.EndUpdate();
        }
        catch (Exception ex)
        {
            LogService.Warn("ui", null, "Process list refresh failed", ex.Message);
        }
    }

    private void RunSweepNow()
    {
        btnSweep.Enabled = false;
        Task.Run(() =>
        {
            var report = ProcessHost.Instance.RunSweep();
            BeginInvoke(new Action(() =>
            {
                txtSweep.AppendText(report.ToString() + Environment.NewLine);
                foreach (var f in report.Findings)
                    txtSweep.AppendText($"  PID={f.Pid} PPID={f.Ppid} → {f.Action}: {f.Reason}{Environment.NewLine}    cmd: {ProcessHost.TruncCmd(f.Cmd)}{Environment.NewLine}");
                if (report.Findings.Count == 0)
                    txtSweep.AppendText("  Tidak ada temuan. Tidak ada proses yang disentuh." + Environment.NewLine);
                btnSweep.Enabled = true;
                RefreshProcesses();
            }));
        });
    }
}
