using System.Threading.Channels;

namespace McpXellen.Core;

/// <summary>
/// In-process event bus (PRD: event-driven push, no aggressive polling). Publishers never block:
/// each subscriber has a bounded channel; overflow events are DROPPED for that subscriber and
/// counted honestly (droppedEventCount is visible in the Activity Monitor status bar).
/// </summary>
public sealed class EventBus
{
    public static EventBus Instance { get; } = new();

    private readonly List<Subscription> _subs = new();
    private readonly object _gate = new();
    private long _published;
    private long _droppedTotal;

    public long Published => Interlocked.Read(ref _published);
    public long DroppedTotal => Interlocked.Read(ref _droppedTotal);

    public sealed class Subscription : IDisposable
    {
        private readonly Channel<ActivityEvent> _channel;
        private readonly Task _pump;
        private readonly Action<ActivityEvent> _handler;
        private long _dropped;
        public long Dropped => Interlocked.Read(ref _dropped);
        public bool Closed { get; private set; }

        internal Subscription(int capacity, Action<ActivityEvent> handler)
        {
            _handler = handler;
            _channel = Channel.CreateBounded<ActivityEvent>(new BoundedChannelOptions(capacity)
            {
                // Wait mode: TryWrite returns FALSE when full → the publisher drops the NEWEST
                // event and the drop is counted honestly (DropOldest would hide the drop).
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
            });
            _pump = Task.Run(PumpAsync);
        }

        internal bool TryPublish(ActivityEvent e)
        {
            if (Closed) return false;
            return _channel.Writer.TryWrite(e);
        }

        internal void MarkDropped() => Interlocked.Increment(ref _dropped);

        private async Task PumpAsync()
        {
            await foreach (var e in _channel.Reader.ReadAllAsync())
            {
                try { _handler(e); }
                catch { /* a bad subscriber must not kill the bus */ }
            }
        }

        public void Dispose()
        {
            if (Closed) return;
            Closed = true;
            _channel.Writer.TryComplete();
            try { _pump.Wait(1500); } catch { }
        }
    }

    public Subscription Subscribe(Action<ActivityEvent> handler, int capacity = 1000)
    {
        var sub = new Subscription(capacity, handler);
        lock (_gate) _subs.Add(sub);
        return sub;
    }

    public void Unsubscribe(Subscription sub)
    {
        lock (_gate) _subs.Remove(sub);
        sub.Dispose();
    }

    public void Publish(ActivityEvent e)
    {
        Interlocked.Increment(ref _published);
        Subscription[] snapshot;
        lock (_gate) snapshot = _subs.ToArray();
        foreach (var s in snapshot)
        {
            if (s.TryPublish(e)) continue;
            s.MarkDropped();
            Interlocked.Increment(ref _droppedTotal);
        }
    }
}
