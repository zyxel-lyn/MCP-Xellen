using McpXellen.Core;

namespace McpXellen;

internal sealed partial class MainForm
{
    private readonly RichTextBox rtbLog = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 8.5f),
        BackColor = Color.FromArgb(250, 250, 250), DetectUrls = false, WordWrap = false,
    };
    private readonly FlowLayoutPanel logBar = new()
    {
        Dock = DockStyle.Top, Height = 40, Padding = new Padding(6),
    };
    private readonly ComboBox cboLevel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly CheckBox chkFollow = new() { Text = "Follow", Checked = true, AutoSize = true, Padding = new Padding(6, 6, 0, 0) };
    private readonly System.Windows.Forms.Timer logTimer = new() { Interval = 2000 };

    private void InitLogTab(TabControl tabs)
    {
        logBar.Controls.Add(new Label { Text = "Level min:", AutoSize = true, Padding = new Padding(0, 8, 4, 0) });
        logBar.Controls.Add(cboLevel);
        logBar.Controls.Add(chkFollow);
        cboLevel.Items.AddRange(["Debug", "Info", "Warn", "Error"]);
        cboLevel.SelectedIndex = 1;

        var page = new TabPage("Log");
        page.Controls.Add(rtbLog);
        page.Controls.Add(logBar);
        tabs.TabPages.Add(page);
        logTimer.Tick += (_, _) => RefreshLog();
        logTimer.Start();
    }

    private void RefreshLog()
    {
        try
        {
            if (tabs.SelectedTab?.Text != "Log") return; // only render when visible
            var min = cboLevel.SelectedIndex switch
            {
                0 => LogLevel.Debug, 1 => LogLevel.Info, 2 => LogLevel.Warn, _ => LogLevel.Error,
            };
            var lines = LogService.Snapshot(400).Where(l => LevelValue(l.Level) >= LevelValue(min.ToString())).ToList();
            rtbLog.SuspendLayout();
            rtbLog.Clear();
            foreach (var l in lines)
            {
                var color = l.Level switch
                {
                    "Error" => Color.Firebrick,
                    "Warn" => Color.DarkGoldenrod,
                    "Debug" => Color.Gray,
                    _ => Color.Black,
                };
                rtbLog.SelectionStart = rtbLog.TextLength;
                rtbLog.SelectionColor = color;
                rtbLog.AppendText(l + Environment.NewLine);
            }
            rtbLog.ResumeLayout();
            if (chkFollow.Checked) rtbLog.SelectionStart = rtbLog.TextLength; rtbLog.ScrollToCaret();
        }
        catch { }
    }

    private static int LevelValue(string l) => l switch
    {
        "Debug" => 0, "Info" => 1, "Warn" => 2, _ => 3,
    };
}
