using System.Threading;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

// A caller-owned lifetime, separate from the ambient scope used for one VM call.
// It contains only actual caller capabilities. Context markers never store it.
internal sealed partial class VmExecutionHost : IDisposable
{
    private static AsyncLocal<VmExecutionHost?>? _ambient;
    private readonly object _gate = new();
    private readonly ConditionalWeakTable<object, Types.CompiledCallableAdapter> _compiledCallables = new();
    private readonly List<WeakReference<Execution.VmEvaluationLease>> _leases = [];
    private Action<Action>? _queuePromiseJob, _scheduleCallback, _queueAfterCheckpoint;
    private Action? _refHandle, _unrefHandle, _checkCancellation;
    private Func<object?, object?>? _fromVm, _toVm;
    private bool _adapterConfigured;
    private Func<object, int>? _getNativeCallbackRole;
    private bool _nativeCallbackRolesConfigured, _hasCompiledCallables;
    private Func<Exception, bool>? _isThrownValue;
    private Func<Exception, object?>? _getThrownValue;
    private bool _thrownAdapterConfigured, _executionStarted;
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
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _executionStarted = true;
        }
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
            _executionStarted = true;
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

    internal void ConfigureThrownValueAdapter(Func<Exception, bool> isThrown, Func<Exception, object?> getThrown)
    {
        ArgumentNullException.ThrowIfNull(isThrown);
        ArgumentNullException.ThrowIfNull(getThrown);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thrownAdapterConfigured || _executionStarted || _leases.Count != 0)
                throw new InvalidOperationException("VM thrown-value adapters must be configured once before execution.");
            _thrownAdapterConfigured = true;
            _isThrownValue = isThrown;
            _getThrownValue = getThrown;
        }
    }

    internal void ConfigureNativeCallbackRoles(Func<object, int> getRole)
    {
        ArgumentNullException.ThrowIfNull(getRole);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_nativeCallbackRolesConfigured || _executionStarted || _leases.Count != 0 || _hasCompiledCallables)
                throw new InvalidOperationException("VM native callback roles must be configured once before execution.");
            _nativeCallbackRolesConfigured = true;
            _getNativeCallbackRole = getRole;
        }
    }

    internal bool TryGetThrownValue(Exception exception, out object? value)
    {
        value = null;
        if (_isThrownValue is not { } isThrown || _getThrownValue is not { } getThrown || !isThrown(exception))
            return false;
        value = getThrown(exception);
        return true;
    }
    internal object? FromVm(object? value)
    {
        if (value is Types.CompiledCallableAdapter callback) value = callback.Original;
        return _fromVm is { } adapt ? adapt(value) : value;
    }
    internal object? ToVm(object? value)
        => Types.VmContext.WrapVmCompiledCallableForHost(_toVm is { } adapt ? adapt(value) : value, this);

    // Called only after the existing managed-function boundary has admitted the actual value.
    // Weak keys preserve identity without rooting otherwise unreachable caller functions.
    internal Types.CompiledCallableAdapter GetCompiledCallable(object target, MethodInfo invoke, MethodInfo? invokeWithThis)
    {
        Func<object, int>? getRole;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_compiledCallables.TryGetValue(target, out var cached)) return cached;
            _hasCompiledCallables = true;
            getRole = _getNativeCallbackRole;
        }
        int role = getRole?.Invoke(target) ?? 0;
        if (role is not (1 or 2)) role = 0;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var result = _compiledCallables.GetValue(target,
                value => new Types.CompiledCallableAdapter(value, invoke, invokeWithThis, this, role));
            return result;
        }
    }

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
                _contextStorage = null;
                _compiledCallables.Clear();
                _getNativeCallbackRole = null;
                _isThrownValue = null;
                _getThrownValue = null;
            }
            if (ReferenceEquals(_ambient?.Value, this)) _ambient.Value = null;
        }
        if (failures is not null) throw new AggregateException("VM lease cleanup failed.", failures);
    }
}
