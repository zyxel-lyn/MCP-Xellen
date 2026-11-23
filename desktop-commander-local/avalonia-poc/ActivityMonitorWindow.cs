using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using McpXellen.Core;

namespace McpXellen.AvaloniaPoc;

/// <summary>Avalonia Activity Monitor — separate top-level window bound to the shared ViewModel.</summary>
public sealed class ActivityMonitorWindow : Window
{
    private readonly ListBox _list = new();
    private readonly ComboBox _statusFilter = new();
    private readonly TextBlock _summary = new();

    public ActivityMonitorWindow(ActivityMonitorViewModel vm)
    {
        Title = "AI Activity Monitor — Avalonia PoC";
        Width = 900;
        Height = 560;
        MinWidth = 640;
        MinHeight = 420;

        _statusFilter.ItemsSource = vm.StatusOptions;
        _statusFilter.SelectedIndex = 0;
        _statusFilter.SelectionChanged += (_, _) => vm.StatusFilter = _statusFilter.SelectedItem as string ?? "(all)";

        _summary.Foreground = new SolidColorBrush(Color.Parse("#F4D06F"));

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Avalonia.Thickness(12, 10, 0, 6),
            Children =
            {
                new TextBlock { Text = "Status:", VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(0, 0, 6, 0) },
                _statusFilter,
            },
        };

        Content = new DockPanel
        {
            Margin = new Avalonia.Thickness(8),
            Children = { _list },
            LastChildFill = true,
        };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_summary, Dock.Bottom);
        ((DockPanel)Content).Children.Insert(0, header);
        ((DockPanel)Content).Children.Insert(1, _summary);

        _list.ItemsSource = vm.Items;
        vm.ItemsChanged += () => _summary.Text = vm.Summary;
        _summary.Text = vm.Summary;
    }
}
