#pragma warning disable SHARPTS_HOSTING001

using SharpTS.Hosting;

namespace SharpTS.Tests.Hosting;

internal sealed class DeterministicHostDispatcher : ISharpTSHostDispatcher
{
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly object _gate = new();
    private readonly Queue<Action> _posted = new();
    private readonly PriorityQueue<ScheduledItem, (long Due, long Sequence)> _scheduled = new();
    private readonly DispatcherSynchronizationContext _synchronizationContext;
    private long _nowTicks;
    private long _sequence;

    public DeterministicHostDispatcher()
    {
        _synchronizationContext = new DispatcherSynchronizationContext(this);
    }

    public List<string> Trace { get; } = [];
    public int PostCount { get; private set; }
    public int OwnerThreadId => _ownerThreadId;
    public SynchronizationContext SynchronizationContext => _synchronizationContext;

    public bool CheckAccess() => Environment.CurrentManagedThreadId == _ownerThreadId;

    public void Post(Action hostTurn)
    {
        ArgumentNullException.ThrowIfNull(hostTurn);
        lock (_gate)
        {
            _posted.Enqueue(hostTurn);
            PostCount++;
            Trace.Add("post");
        }
    }

    public ISharpTSScheduledWork Schedule(TimeSpan delay, Action hostTurn)
    {
        ArgumentNullException.ThrowIfNull(hostTurn);
        var item = new ScheduledItem(hostTurn);
        lock (_gate)
        {
            long due = checked(_nowTicks + Math.Max(0, delay.Ticks));
            _scheduled.Enqueue(item, (due, _sequence++));
            Trace.Add($"schedule:{delay.TotalMilliseconds:0.###}");
        }
        return item;
    }

    public bool RunNext()
    {
        AssertOwner();
        Action? action = null;
        lock (_gate)
        {
            if (_posted.Count != 0)
            {
                action = _posted.Dequeue();
            }
            else
            {
                while (_scheduled.TryPeek(out ScheduledItem? item, out var priority) &&
                       priority.Due <= _nowTicks)
                {
                    _scheduled.Dequeue();
                    if (!item.IsCancelled)
                    {
                        action = item.Action;
                        break;
                    }
                }
            }
        }
        if (action == null)
            return false;

        SynchronizationContext? previous = System.Threading.SynchronizationContext.Current;
        System.Threading.SynchronizationContext.SetSynchronizationContext(_synchronizationContext);
        try
        {
            Trace.Add("run");
            action();
            if (!ReferenceEquals(_synchronizationContext, System.Threading.SynchronizationContext.Current))
                throw new InvalidOperationException("Guest replaced the host SynchronizationContext.");
        }
        finally
        {
            System.Threading.SynchronizationContext.SetSynchronizationContext(previous);
        }
        return true;
    }

    public bool AdvanceToNextScheduled()
    {
        AssertOwner();
        lock (_gate)
        {
            while (_scheduled.TryPeek(out ScheduledItem? item, out var priority))
            {
                if (item.IsCancelled)
                {
                    _scheduled.Dequeue();
                    continue;
                }
                _nowTicks = Math.Max(_nowTicks, priority.Due);
                Trace.Add($"advance:{TimeSpan.FromTicks(_nowTicks).TotalMilliseconds:0.###}");
                return true;
            }
        }
        return false;
    }

    public void AdvanceBy(TimeSpan duration)
    {
        AssertOwner();
        lock (_gate)
            _nowTicks = checked(_nowTicks + duration.Ticks);
    }

    public void RunUntil(Func<bool> condition, int maximumTurns = 10_000)
    {
        for (int turn = 0; turn < maximumTurns && !condition(); turn++)
        {
            if (RunNext())
                continue;
            if (AdvanceToNextScheduled())
                continue;
            Thread.Yield();
        }
        if (!condition())
            throw new TimeoutException($"Deterministic dispatcher exceeded {maximumTurns} turns.");
    }

    public void RunUntilIdle(int maximumTurns = 10_000)
    {
        for (int turn = 0; turn < maximumTurns; turn++)
        {
            if (!RunNext())
                return;
        }
        throw new TimeoutException($"Deterministic dispatcher exceeded {maximumTurns} immediate turns.");
    }

    private void AssertOwner()
    {
        if (!CheckAccess())
            throw new InvalidOperationException("Deterministic dispatcher can only be driven by its owner thread.");
    }

    private sealed class DispatcherSynchronizationContext(DeterministicHostDispatcher dispatcher)
        : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) =>
            dispatcher.Post(() => d(state));

        public override SynchronizationContext CreateCopy() => this;
    }

    private sealed class ScheduledItem(Action action) : ISharpTSScheduledWork
    {
        private int _cancelled;
        public Action Action { get; } = action;
        public bool IsCancelled => Volatile.Read(ref _cancelled) != 0;
        public void Cancel() => Interlocked.Exchange(ref _cancelled, 1);
        public void Dispose() => Cancel();
    }
}

internal sealed class RecordingLifetime : ISharpTSHostLifetime
{
    public List<(int ExitCode, int ThreadId)> Exits { get; } = [];
    public void RequestExit(int exitCode) => Exits.Add((exitCode, Environment.CurrentManagedThreadId));
}

internal sealed class RecordingErrorSink : ISharpTSHostedErrorSink
{
    public List<SharpTSHostedError> Errors { get; } = [];
    public void Report(SharpTSHostedError error) => Errors.Add(error);
}
