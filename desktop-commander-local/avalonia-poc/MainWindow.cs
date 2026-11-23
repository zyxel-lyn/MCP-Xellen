using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using McpXellen.Core;
using McpXellen.Transport;

namespace McpXellen.AvaloniaPoc;

/// <summary>Avalonia dashboard PoC — real controls bound to the SHARED Core (no duplicated logic).</summary>
public sealed class MainWindow : Window
{
    private readonly TextBlock _counts = new();
    private readonly ActivityMonitorViewModel _vm;
    private ActivityMonitorWindow? _monitor;

    public MainWindow(ActivityMonitorViewModel vm)
    {
        _vm = vm;
        Title = "MCP Xellen — Avalonia PoC Dashboard";
        Width = 860;
        Height = 520;
        MinWidth = 640;
        MinHeight = 420;
        Background = new SolidColorBrush(Color.Parse("#020402"));

        var title = new TextBlock
        {
            Text = "MCP Xellen — Avalonia UI Proof of Concept",
            FontSize = 20, FontWeight = Avalonia.Media.FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#FF8811")),
            Margin = new Avalonia.Thickness(16, 16, 0, 4),
        };

        _counts.Foreground = new SolidColorBrush(Color.Parse("#C0FDFB"));
        _counts.Margin = new Avalonia.Thickness(16, 4, 0, 8);

        var openMonitor = new Button
        {
            Content = "Open AI Activity Monitor (separate window)",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Avalonia.Thickness(16, 0, 0, 12),
        };
        openMonitor.Click += (_, _) =>
        {
            if (_monitor is null || !_monitor.IsVisible)
            {
                _monitor = new ActivityMonitorWindow(_vm);
                _monitor.Show(); // separate top-level window — survives dashboard minimize
            }
            else _monitor.Activate();
        };

        var note = new TextBlock
        {
            Text = "PoC: business logic 100% dari Xellen Core (EventBus/ActivityStore/AuthStore/Registry) — tanpa duplikasi.\n" +
                   "Events di bawah adalah tool call NYATA melalui native transport (bukan mock).",
            Foreground = new SolidColorBrush(Color.Parse("#5D737E")),
            Margin = new Avalonia.Thickness(16, 0, 0, 0),
        };

        Content = new StackPanel
        {
            Children = { title, _counts, openMonitor, note, BuildMiniList() },
        };

        vm.PropertyChanged += (_, _) => UpdateCounts();
        UpdateCounts();
    }

    private ListBox BuildMiniList()
    {
        var list = new ListBox { Margin = new Avalonia.Thickness(16, 0, 16, 12), Height = 220 };
        void OnVmItems() => list.ItemsSource = _vm.Items.Take(50).Select(e =>
            $"{e.TimestampUtc.ToLocalTime():HH:mm:ss.fff}  [{e.Status}]  {e.ServerId}__{e.ToolName}  {e.DurationMs}ms  {e.OutputSummary}").ToList();
        list.ItemsSource = Array.Empty<string>();
        _vm.Items.CollectionChanged += (_, _) => OnVmItems();
        OnVmItems();
        return list;
    }

    private void UpdateCounts() =>
        _counts.Text = $"published: {EventBus.Instance.Published} • dropped: {EventBus.Instance.DroppedTotal} • " +
                       $"RAM core: {Math.Round(System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0, 1)} MB (measured)" +
                       (NativeTransportHost.Running ? $" • native :{NativeTransportHost.Port}" : "");
}
