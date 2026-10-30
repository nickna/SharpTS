using System.Threading;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

// A caller-owned lifetime, separate from the ambient scope used for one VM call.
// It contains only actual caller capabilities. Context markers never store it.
internal sealed class VmExecutionHost : IDisposable
{
    private static AsyncLocal<VmExecutionHost?>? _ambient;
    private readonly object _gate = new();
    private readonly List<WeakReference<Execution.VmEvaluationLease>> _leases = [];
    private Action<Action>? _queuePromiseJob, _scheduleCallback, _queueAfterCheckpoint;
    private Action? _refHandle, _unrefHandle, _checkCancellation;
    private Func<object?, object?>? _fromVm, _toVm;
    private bool _adapterConfigured;
    private bool _disposed;
    private int _acquiredRefs;

    internal VmExecutionHost(Action<Action> queuePromiseJob, Action<Action> scheduleCallback,
        Action<Action> queueAfterCheckpoint, Action refHandle, Action unrefHandle, Action checkCancellation)
    {
        ArgumentNullException.ThrowIfNull(queuePromiseJob);
        ArgumentNullException.ThrowIfNull(scheduleCallback);
        ArgumentNullException.ThrowIfNull(queueAfterCheckpoint);
        ArgumentNullException.ThrowIfNull(refHandle);
        ArgumentNullException.ThrowIfNull(unrefHandle);
        ArgumentNullException.ThrowIfNull(checkCancellation);
        _queuePromiseJob = queuePromiseJob;
        _scheduleCallback = scheduleCallback;
        _queueAfterCheckpoint = queueAfterCheckpoint;
        _refHandle = refHandle;
        _unrefHandle = unrefHandle;
        _checkCancellation = checkCancellation;
    }

    internal static VmExecutionHost? Current => _ambient?.Value is { IsDisposed: false } host ? host : null;
    internal bool IsDisposed { get { lock (_gate) return _disposed; } }

    internal IDisposable Enter()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var ambient = LazyInitializer.EnsureInitialized(ref _ambient);
        var previous = ambient.Value;
        ambient.Value = this;
        return new HostScope(ambient, previous);
    }

    private sealed class HostScope(AsyncLocal<VmExecutionHost?> ambient, VmExecutionHost? previous) : IDisposable
    {
        public void Dispose() => ambient.Value = previous is { IsDisposed: false } ? previous : null;
    }

    internal void Register(Execution.VmEvaluationLease lease)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _leases.RemoveAll(reference => !reference.TryGetTarget(out _));
            _leases.Add(new(lease));
        }
    }

    internal void Unregister(Execution.VmEvaluationLease lease)
    {
        lock (_gate)
            _leases.RemoveAll(reference => !reference.TryGetTarget(out var target) || ReferenceEquals(target, lease));
    }

    internal int ActiveLeaseCount
    {
        get { lock (_gate) return _leases.Count(reference => reference.TryGetTarget(out _)); }
    }

    internal void ConfigureValueAdapters(Func<object?, object?> fromVm, Func<object?, object?> toVm)
    {
        ArgumentNullException.ThrowIfNull(fromVm);
        ArgumentNullException.ThrowIfNull(toVm);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_adapterConfigured || _leases.Count != 0)
                throw new InvalidOperationException("VM value adapters must be configured once before execution.");
            _adapterConfigured = true;
            _fromVm = fromVm;
            _toVm = toVm;
        }
    }

    internal object? FromVm(object? value)
    {
        return _fromVm is { } adapt ? adapt(value) : value;
    }
    internal object? ToVm(object? value) => _toVm is { } adapt ? adapt(value) : value;

    internal void QueuePromiseJob(Action job) { if (!IsDisposed) _queuePromiseJob?.Invoke(job); }
    internal void ScheduleCallback(Action job) { if (!IsDisposed) _scheduleCallback?.Invoke(job); }
    internal void QueueAfterCheckpoint(Action job) { if (!IsDisposed) _queueAfterCheckpoint?.Invoke(job); }
    internal bool TryAcquireRef()
    {
        lock (_gate)
        {
            if (_disposed) return false;
            _refHandle!();
            _acquiredRefs++;
            return true;
        }
    }

    internal void ReleaseRef()
    {
        lock (_gate)
        {
            if (_acquiredRefs == 0) return;
            _acquiredRefs--;
            _unrefHandle!();
        }
    }
    internal void CheckCancellation()
    {
        if (IsDisposed) throw new Exceptions.WorkerTerminatedException();
        _checkCancellation?.Invoke();
    }

    public void Dispose()
    {
        List<Execution.VmEvaluationLease> leases;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            leases = _leases.Select(reference => reference.TryGetTarget(out var lease) ? lease : null)
                .OfType<Execution.VmEvaluationLease>().ToList();
            _leases.Clear();
        }
        List<Exception>? failures = null;
        try
        {
            foreach (var lease in leases)
            {
                try { lease.Dispose(); }
                catch (Exception exception) { (failures ??= []).Add(exception); }
            }
        }
        finally
        {
            lock (_gate)
            {
                while (_acquiredRefs > 0)
                {
                    _acquiredRefs--;
                    try { _unrefHandle!(); }
                    catch (Exception exception) { (failures ??= []).Add(exception); }
                }
                _queuePromiseJob = _scheduleCallback = _queueAfterCheckpoint = null;
                _refHandle = _unrefHandle = _checkCancellation = null;
                _fromVm = _toVm = null;
            }
            if (ReferenceEquals(_ambient?.Value, this)) _ambient.Value = null;
        }
        if (failures is not null) throw new AggregateException("VM lease cleanup failed.", failures);
    }
}
