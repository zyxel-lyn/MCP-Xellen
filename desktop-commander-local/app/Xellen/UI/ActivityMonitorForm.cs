using McpXellen.Core;
using McpXellen.Transport;

namespace McpXellen.UI;

/// <summary>
/// AI Activity Monitor — a SEPARATE top-level window (movable to another monitor, resizable,
/// survives dashboard minimize). Shows REAL tool-call events from the event bus; every summary
/// is pre-redacted. No mock data: an empty stream simply means no tool calls happened yet.
/// </summary>
public sealed class ActivityMonitorForm : Form
{
    private readonly ListView lv = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false };
    private readonly FlowLayoutPanel filters = new() { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6) };
    private readonly ComboBox cboStatus = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly TextBox txtTool = new() { Width = 140, PlaceholderText = "tool contains…" };
    private readonly ComboBox cboClient = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly ComboBox cboProject = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly ComboBox cboServer = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly RichTextBox detail = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 8.5f),
        BackColor = Color.FromArgb(248, 250, 252),
    };
    private readonly SplitContainer split = new() { Dock = DockStyle.Fill, SplitterDistance = 380, Orientation = Orientation.Vertical };
    private readonly StatusStrip strip = new();
    private readonly ToolStripStatusLabel lblStats = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private IDisposable? _pushSub;

    public ActivityMonitorForm()
    {
        Text = "AI Activity Monitor — MCP Xellen";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1080, 660);
        MinimumSize = new Size(760, 460);
        Font = new Font("Segoe UI", 9f);
        BackColor = DesignSystem.NearBlack;

        lv.Columns.Add("Waktu", 130);
        lv.Columns.Add("Client", 130);
        lv.Columns.Add("Project", 110);
        lv.Columns.Add("Server", 130);
        lv.Columns.Add("Tool", 170);
        lv.Columns.Add("Status", 90);
        lv.Columns.Add("ms", 70);
        lv.Columns.Add("Ringkasan (disanitasi)", 420);

        filters.Controls.Add(new Label { Text = "Status:", AutoSize = true, Padding = new Padding(0, 8, 4, 0) });
        filters.Controls.Add(cboStatus);
        filters.Controls.Add(new Label { Text = "Tool:", AutoSize = true, Padding = new Padding(8, 8, 4, 0) });
        filters.Controls.Add(txtTool);
        filters.Controls.Add(new Label { Text = "Client:", AutoSize = true, Padding = new Padding(8, 8, 4, 0) });
        filters.Controls.Add(cboClient);
        filters.Controls.Add(new Label { Text = "Project:", AutoSize = true, Padding = new Padding(8, 8, 4, 0) });
        filters.Controls.Add(cboProject);
        filters.Controls.Add(new Label { Text = "Server:", AutoSize = true, Padding = new Padding(8, 8, 4, 0) });
        filters.Controls.Add(cboServer);

        cboStatus.Items.AddRange(["All", "Queued", "Running", "Success", "Failed", "Cancelled", "Timeout"]);
        cboStatus.SelectedIndex = 0;

        split.Panel1.Controls.Add(lv);
        split.Panel2.Controls.Add(detail);
        split.FixedPanel = FixedPanel.None;

        strip.Items.Add(lblStats);

        Controls.Add(split);
        Controls.Add(filters);
        Controls.Add(strip);

        lv.SelectedIndexChanged += (_, _) => ShowDetail();
        foreach (var c in new ComboBox[] { cboStatus, cboClient, cboProject, cboServer }) c.SelectedIndexChanged += (_, _) => RefreshView();
        txtTool.TextChanged += (_, _) => RefreshView();

        // Event-driven PUSH (PRD §5.1): bus → UI thread → immediate refresh. The 1s timer stays
        // as a fallback (status counters/selection hygiene), so poll is not the primary channel.
        _pushSub = EventBus.Instance.Subscribe(_ =>
        {
            if (IsDisposed || Disposing) { return; }
            try { BeginInvoke(new Action(RefreshView)); } catch (ObjectDisposedException) { }
        });

        lv.ClientSizeChanged += (_, _) => FillLastColumn();

        timer.Tick += (_, _) => RefreshView();
        timer.Start();
        RefreshView();
    }

    /// <summary>Last column fills the remaining ListView width (no dead space/clipping on resize).</summary>
    private void FillLastColumn()
    {
        if (lv.Columns.Count == 0) return;
        var others = 0;
        for (int i = 0; i < lv.Columns.Count - 1; i++) others += lv.Columns[i].Width;
        lv.Columns[lv.Columns.Count - 1].Width = Math.Max(200, lv.ClientSize.Width - others - 4);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _pushSub?.Dispose();
        _pushSub = null;
        timer.Stop();
        base.OnFormClosed(e);
    }

    private ActivityStatus? SelectedStatus => cboStatus.SelectedIndex switch
    {
        1 => ActivityStatus.Queued, 2 => ActivityStatus.Running, 3 => ActivityStatus.Success,
        4 => ActivityStatus.Failed, 5 => ActivityStatus.Cancelled, 6 => ActivityStatus.Timeout,
        _ => null,
    };

    private string ComboValue(ComboBox cb) => cb.SelectedItem is string s && s != "(all)" && s != "" ? s : "";

    private void FillCombo(ComboBox cb, IEnumerable<string> values)
    {
        var keep = cb.SelectedItem as string;
        var items = new List<string> { "(all)" };
        items.AddRange(values.Distinct().OrderBy(x => x));
        cb.Items.Clear();
        cb.Items.AddRange(items.ToArray());
        var idx = items.IndexOf(keep ?? "(all)");
        cb.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void RefreshView()
    {
        try
        {
            // Preserve the user's selection across live refreshes (push or poll).
            Guid? selected = lv.SelectedItems.Count > 0 && lv.SelectedItems[0].Tag is ActivityEvent se
                ? se.EventId : null;

            var events = ActivityStore.Query(
                ComboValue(cboClient) == "(all)" ? "" : ComboValue(cboClient),
                ComboValue(cboProject) == "(all)" ? "" : ComboValue(cboProject),
                ComboValue(cboServer) == "(all)" ? "" : ComboValue(cboServer),
                txtTool.Text?.Trim() ?? "",
                SelectedStatus);

            FillCombo(cboClient, ActivityStore.Query("", "", "", "", null).Select(e => e.ClientId ?? "").Where(x => x.Length > 0));
            FillCombo(cboProject, ActivityStore.Query("", "", "", "", null).Select(e => e.ProjectId ?? "").Where(x => x.Length > 0));
            FillCombo(cboServer, ActivityStore.Query("", "", "", "", null).Select(e => e.ServerId ?? "").Where(x => x.Length > 0));

            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var e in events)
            {
                var row = new ListViewItem(e.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff"))
                { ForeColor = DesignSystem.StatusColor(e.Status), Tag = e };
                row.SubItems.Add(e.ClientId ?? "—");
                row.SubItems.Add(e.ProjectId ?? "—");
                row.SubItems.Add(e.ServerId ?? "—");
                row.SubItems.Add(e.ToolName ?? e.EventType);
                row.SubItems.Add(e.Status.ToString());
                row.SubItems.Add(e.DurationMs > 0 ? e.DurationMs.ToString() : "—");
                row.SubItems.Add(e.OutputSummary ?? e.InputSummary ?? "");
                lv.Items.Add(row);
                if (selected.HasValue && e.EventId == selected.Value) { row.Selected = true; }
            }
            lv.EndUpdate();
            FillLastColumn();

            var (_, dropped) = ActivityStore.Counters();
            var ram = Math.Round(System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0, 1);
            lblStats.Text =
                $"Live (push+poll) • events in store: {events.Count} • published: {EventBus.Instance.Published} • dropped: {dropped} • " +
                $"RAM core: {ram} MB (terukur)" +
                (NativeTransportHost.Running ? $" • native :{NativeTransportHost.Port} sessions={NativeTransportHost.ActiveSessions}" : " • native: off");
        }
        catch (Exception ex) { LogService.Warn("ui", null, "activity monitor refresh failed", ex.Message); }
    }

    private void ShowDetail()
    {
        if (lv.SelectedItems.Count == 0) return;
        if (lv.SelectedItems[0].Tag is not ActivityEvent e) return;
        detail.Clear();
        void Line(string k, string? v)
        {
            detail.SelectionColor = DesignSystem.Slate;
            detail.AppendText(k.PadRight(18));
            detail.SelectionColor = Color.Black;
            detail.AppendText((string.IsNullOrEmpty(v) ? "—" : v) + Environment.NewLine);
        }
        detail.AppendText($"— event {e.EventId} —" + Environment.NewLine);
        Line("correlationId", e.CorrelationId.ToString());
        Line("timestampUtc", e.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        Line("eventType", e.EventType);
        Line("clientId", e.ClientId);
        Line("projectId", e.ProjectId);
        Line("serverId", e.ServerId);
        Line("toolName", e.ToolName);
        Line("status", e.Status.ToString());
        Line("durationMs", e.DurationMs > 0 ? e.DurationMs.ToString() : "—");
        Line("riskLevel", e.RiskLevel);
        Line("processId", e.ProcessId?.ToString());
        Line("errorCode", e.ErrorCode);
        detail.AppendText(Environment.NewLine);
        Line("inputSummary", Redaction.Sanitize(e.InputSummary, 600));
        Line("outputSummary", Redaction.Sanitize(e.OutputSummary, 600));
    }
}
