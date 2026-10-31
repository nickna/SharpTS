using System.Runtime.CompilerServices;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;

namespace SharpTS.Execution;

// Lifetime is held by real unfinished tasks/jobs/handles, never by a context marker.
// Pending promises retain their evaluator through weak-key task anchors but do not Ref
// the caller's loop. All guest work is posted to the caller's actual owner queue.
internal sealed class VmEvaluationLease : IDisposable
{
    private static AsyncLocal<VmEvaluationLease?>? _observer;
    private static ConditionalWeakTable<Task, List<VmEvaluationLease>>? _taskOwners;
    private static object? _taskOwnersGate;
    private static object TaskOwnersGate => LazyInitializer.EnsureInitialized(ref _taskOwnersGate);
    private readonly object _gate = new();
    private Interpreter? _interpreter;
    private Action? _publish;
    private readonly HashSet<Task> _pending = [];
    private readonly VmEvaluationLease? _parent;
    private CancellationTokenSource? _timerWake;
    private int _jobs, _running, _refs, _children, _pumpScheduled, _escapedValues;
    private bool _guestAbrupt;
    private readonly int _ownerThread = System.Environment.CurrentManagedThreadId;
    private bool _initialFinished, _disposed;
    private bool _hasTimer;

    internal VmExecutionHost Host { get; }
    internal static Interpreter? CurrentInterpreter => _observer?.Value is { IsDisposed: false } lease ? lease._interpreter : null;
    internal bool IsDisposed { get { lock (_gate) return _disposed; } }

    private VmEvaluationLease(Interpreter interpreter, VmExecutionHost host, Action publish,
        VmEvaluationLease? parent)
    {
        _interpreter = interpreter;
        _publish = publish;
        Host = host;
        _parent = parent;
        parent?.AddChild();
        try { host.Register(this); }
        catch { parent?.RemoveChild(); throw; }
        interpreter.AttachVmEvaluation(this);
    }

    internal static VmEvaluationLease? Create(Interpreter interpreter, Interpreter? parent, Action publish)
    {
        var host = parent?.GetVmExecutionHost() ?? VmExecutionHost.Current;
        return host is null ? null : new VmEvaluationLease(interpreter, host, publish, parent?.VmEvaluation);
    }

    internal IDisposable Enter()
    {
        if (System.Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("VM guest work must run on its caller's owner thread.");
        Host.CheckCancellation();
        var hostScope = Host.Enter();
        try
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _running++;
            }
            var observer = LazyInitializer.EnsureInitialized(ref _observer);
            var previous = observer.Value;
            observer.Value = this;
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(_interpreter!.VmSynchronizationContext);
            return new EvaluationScope(this, observer, previous, previousContext, hostScope);
        }
        catch { hostScope.Dispose(); throw; }
    }

    private sealed class EvaluationScope(VmEvaluationLease lease, AsyncLocal<VmEvaluationLease?> observer,
        VmEvaluationLease? previous, SynchronizationContext? previousContext, IDisposable hostScope) : IDisposable
    {
        public void Dispose()
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            hostScope.Dispose();
            observer.Value = previous is { IsDisposed: false } ? previous : null;
            lock (lease._gate) lease._running--;
            if (lease.IsDisposed) lease.CompleteDisposal();
            else lease.TryComplete();
        }
    }

    internal static void ObservePromise(Task task)
    {
        if (_observer?.Value is not { } lease || task.IsCompleted) return;
        lock (lease._gate)
        {
            if (lease._disposed || !lease._pending.Add(task)) return;
            lock (TaskOwnersGate)
            {
                var owners = LazyInitializer.EnsureInitialized(ref _taskOwners).GetValue(task, _ => []);
                owners.Add(lease);
            }
        }
        task.ContinueWith(static (completed, state) =>
        {
            if (((WeakReference<VmEvaluationLease>)state!).TryGetTarget(out var owner))
                owner.PromiseCompleted(completed);
        }, new WeakReference<VmEvaluationLease>(lease), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void RemoveTaskAnchor(Task task)
    {
        lock (TaskOwnersGate)
        {
            if (_taskOwners is not { } table || !table.TryGetValue(task, out var owners)) return;
            owners.Remove(this);
            if (owners.Count == 0) table.Remove(task);
        }
    }

    private void PromiseCompleted(Task task)
    {
        if (task is Task<object?> { IsCompletedSuccessfully: true } result)
            VmModuleInterpreter.RetainVmErrorValue(result.Result);
        lock (_gate) _pending.Remove(task);
        RemoveTaskAnchor(task);
        // Cleanup follows the checkpoint in which settling tasks post their jobs.
        // This does not run guest code on the completing Task's worker thread.
        var weak = new WeakReference<VmEvaluationLease>(this);
        Host.QueueAfterCheckpoint(() => { if (weak.TryGetTarget(out var owner)) owner.TryComplete(); });
    }

    internal bool Queue(Action job, bool afterCheckpoint = false)
    {
        lock (_gate)
        {
            if (_disposed) return false;
            _jobs++;
        }
        var context = ExecutionContext.Capture();
        void Run()
        {
            lock (_gate)
            {
                if (_disposed) { _jobs--; return; }
            }
            using var scope = Enter();
            lock (_gate) _jobs--;
            try
            {
                if (context is null) job();
                else ExecutionContext.Run(context, _ => job(), null);
            }
            finally { _publish?.Invoke(); }
        }
        try
        {
            if (afterCheckpoint) Host.QueueAfterCheckpoint(Run);
            else Host.QueuePromiseJob(Run);
        }
        catch
        {
            lock (_gate) _jobs--;
            throw;
        }
        return true;
    }

    internal void AddRef()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (Host.TryAcquireRef()) _refs++;
        }
    }

    internal void RemoveRef()
    {
        lock (_gate)
        {
            if (_disposed || _refs == 0) return;
            _refs--;
            Host.ReleaseRef();
        }
        TryComplete();
    }

    private void AddChild() { lock (_gate) _children++; }
    private void RemoveChild() { lock (_gate) _children--; TryComplete(); }

    internal void NotifyWork()
    {
        if (IsDisposed || Interlocked.Exchange(ref _pumpScheduled, 1) != 0) return;
        Host.ScheduleCallback(() =>
        {
            Interlocked.Exchange(ref _pumpScheduled, 0);
            if (IsDisposed) return;
            using var scope = Enter();
            try
            {
                _interpreter!.TryExecuteOneVmMacrotask();
                if (_interpreter.HasVmCallbacks) NotifyWork();
            }
            finally
            {
                _publish?.Invoke();
                RefreshTimer();
            }
        });
    }

    internal void RefreshTimer()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _timerWake?.Cancel();
            _timerWake?.Dispose();
            _timerWake = null;
            _hasTimer = false;
            if (_interpreter!.GetHostedNextTimer() is { } timer)
            {
                _hasTimer = true;
                var wake = _timerWake = new CancellationTokenSource();
                // This real scheduled wake owns the lease until it fires/cancels.
                // A caller ref counter alone is not a GC root for timer callbacks.
                _ = Task.Delay(timer.Delay, wake.Token).ContinueWith(static (_, state) =>
                    ((VmEvaluationLease)state!).NotifyWork(), this, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
            }
        }
        TryComplete();
    }

    internal void FinishInitialEvaluation(bool succeeded)
    {
        if (!succeeded && !_guestAbrupt) { Dispose(); return; }
        lock (_gate) _initialFinished = true;
        RefreshTimer();
        TryComplete();
    }

    private void TryComplete()
    {
        lock (_gate)
        {
            if (_disposed || !_initialFinished || _running != 0 || _jobs != 0
                || _refs != 0 || _children != 0 || _pending.Count != 0 || _escapedValues != 0 || _hasTimer) return;
        }
        Dispose();
    }

    internal void RetainEscapedValue() { lock (_gate) { if (!_disposed) _escapedValues++; } }
    internal void MarkGuestAbrupt() => _guestAbrupt = true;
    internal void Publish() => _publish?.Invoke();

    public void Dispose()
    {
        Interpreter? interpreter;
        List<Task> tasks;
        int refs;
        bool deferred;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            interpreter = _interpreter;
            deferred = _running != 0;
            if (!deferred) _interpreter = null;
            _publish = null;
            _timerWake?.Cancel();
            _timerWake?.Dispose();
            _timerWake = null;
            refs = _refs;
            _refs = 0;
            tasks = _pending.ToList();
            _pending.Clear();
        }
        foreach (var task in tasks) RemoveTaskAnchor(task);
        List<Exception>? failures = null;
        for (int index = 0; index < refs; index++)
        {
            try { Host.ReleaseRef(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        try { if (!deferred) interpreter?.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        finally { if (!deferred) { Host.Unregister(this); _parent?.RemoveChild(); } }
        if (failures is not null) throw new AggregateException("VM evaluation cleanup failed.", failures);
    }

    private void CompleteDisposal()
    {
        Interpreter? interpreter;
        lock (_gate)
        {
            if (!_disposed || _running != 0) return;
            interpreter = _interpreter;
            _interpreter = null;
        }
        if (interpreter is null) return;
        try { interpreter.Dispose(); }
        finally { Host.Unregister(this); _parent?.RemoveChild(); }
    }
}
