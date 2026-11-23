using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using McpXellen.Core;
using McpXellen.Transport;

namespace McpXellen.AvaloniaPoc;

/// <summary>
/// ViewModel for the Avalonia Activity Monitor. Subscribes to the SHARED EventBus (no duplicated
/// logic) and marshals events onto the UI thread. Filterable, bound via INotifyPropertyChanged.
/// </summary>
public sealed class ActivityMonitorViewModel : INotifyPropertyChanged
{
    public ObservableCollection<ActivityEvent> Items { get; } = new();
    public ObservableCollection<string> StatusOptions { get; } = new() { "(all)", "Queued", "Running", "Success", "Failed", "Cancelled", "Timeout" };

    private string _statusFilter = "(all)";
    public string StatusFilter
    {
        get => _statusFilter;
        set { _statusFilter = value; OnPropertyChanged(nameof(StatusFilter)); ApplyFilter(); }
    }

    private string _summary = "";
    public string Summary
    {
        get => _summary;
        private set { _summary = value; OnPropertyChanged(nameof(Summary)); }
    }

    private readonly IDisposable? _sub;
    private Guid? _selectedCorrelation;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? ItemsChanged;

    public ActivityMonitorViewModel()
    {
        _sub = EventBus.Instance.Subscribe(e =>
        {
            // Cross-thread safety: bus events arrive on worker threads → marshal to UI thread.
            Dispatcher.UIThread.Post(() => OnEvent(e), DispatcherPriority.Background);
        });
        Summary = "waiting for real tool calls…";
    }

    public void Close() => _sub?.Dispose();

    private void OnEvent(ActivityEvent e)
    {
        if (PassesFilter(e))
        {
            Items.Insert(0, e);
            while (Items.Count > 300) Items.RemoveAt(Items.Count - 1);
        }
        RecomputeSummary();
        ItemsChanged?.Invoke();
    }

    public void ApplySelectionFilter(Guid? correlation)
    {
        _selectedCorrelation = correlation;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = ActivityStore.Query("", "", "", "", ParseStatus(_statusFilter));
        Items.Clear();
        foreach (var e in query) Items.Add(e);
        RecomputeSummary();
    }

    private static ActivityStatus? ParseStatus(string s) => s switch
    {
        "Queued" => ActivityStatus.Queued, "Running" => ActivityStatus.Running,
        "Success" => ActivityStatus.Success, "Failed" => ActivityStatus.Failed,
        "Cancelled" => ActivityStatus.Cancelled, "Timeout" => ActivityStatus.Timeout,
        _ => null,
    };

    private bool PassesFilter(ActivityEvent e) =>
        _statusFilter == "(all)" || e.Status.ToString() == _statusFilter;

    private void RecomputeSummary()
    {
        var (_, dropped) = ActivityStore.Counters();
        var ram = Math.Round(System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0, 1);
        Summary = $"events visible: {Items.Count} • published: {EventBus.Instance.Published} • dropped: {dropped} • RAM: {ram} MB (measured)" +
                  (NativeTransportHost.Running ? $" • native :{NativeTransportHost.Port}" : " • native: off");
    }

    private void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
