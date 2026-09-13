using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Required timeout and virtual timer metadata.
/// Declarations support forward calls; completion validates and freezes every handle.
/// </summary>
public sealed class EmittedTimerRuntime
{
    internal EmittedTimerRuntime() { }

    public bool IsComplete { get; private set; }

    private TypeBuilder? _timeoutType;
    public TypeBuilder TimeoutType
    {
        get => Require(_timeoutType);
        internal set => Set(ref _timeoutType, value);
    }

    private ConstructorBuilder? _timeoutCtor;
    public ConstructorBuilder TimeoutCtor
    {
        get => Require(_timeoutCtor);
        internal set => Set(ref _timeoutCtor, value);
    }

    private MethodBuilder? _timeoutCancel;
    public MethodBuilder TimeoutCancel
    {
        get => Require(_timeoutCancel);
        internal set => Set(ref _timeoutCancel, value);
    }

    private MethodBuilder? _timeoutRef;
    public MethodBuilder TimeoutRef
    {
        get => Require(_timeoutRef);
        internal set => Set(ref _timeoutRef, value);
    }

    private MethodBuilder? _timeoutUnref;
    public MethodBuilder TimeoutUnref
    {
        get => Require(_timeoutUnref);
        internal set => Set(ref _timeoutUnref, value);
    }

    private MethodBuilder? _timeoutHasRefGetter;
    public MethodBuilder TimeoutHasRefGetter
    {
        get => Require(_timeoutHasRefGetter);
        internal set => Set(ref _timeoutHasRefGetter, value);
    }

    private MethodBuilder? _setTimeout;
    public MethodBuilder SetTimeout
    {
        get => Require(_setTimeout);
        internal set => Set(ref _setTimeout, value);
    }

    private MethodBuilder? _clearTimeout;
    public MethodBuilder ClearTimeout
    {
        get => Require(_clearTimeout);
        internal set => Set(ref _clearTimeout, value);
    }

    private MethodBuilder? _setInterval;
    public MethodBuilder SetInterval
    {
        get => Require(_setInterval);
        internal set => Set(ref _setInterval, value);
    }

    private MethodBuilder? _clearInterval;
    public MethodBuilder ClearInterval
    {
        get => Require(_clearInterval);
        internal set => Set(ref _clearInterval, value);
    }

    private TypeBuilder? _virtualTimerType;
    public TypeBuilder VirtualTimerType
    {
        get => Require(_virtualTimerType);
        internal set => Set(ref _virtualTimerType, value);
    }

    private ConstructorBuilder? _virtualTimerCtor;
    public ConstructorBuilder VirtualTimerCtor
    {
        get => Require(_virtualTimerCtor);
        internal set => Set(ref _virtualTimerCtor, value);
    }

    private FieldBuilder? _virtualTimerCallback;
    public FieldBuilder VirtualTimerCallback
    {
        get => Require(_virtualTimerCallback);
        internal set => Set(ref _virtualTimerCallback, value);
    }

    private FieldBuilder? _virtualTimerArgs;
    public FieldBuilder VirtualTimerArgs
    {
        get => Require(_virtualTimerArgs);
        internal set => Set(ref _virtualTimerArgs, value);
    }

    private FieldBuilder? _virtualTimerScheduledTime;
    public FieldBuilder VirtualTimerScheduledTime
    {
        get => Require(_virtualTimerScheduledTime);
        internal set => Set(ref _virtualTimerScheduledTime, value);
    }

    private FieldBuilder? _virtualTimerIsCancelled;
    public FieldBuilder VirtualTimerIsCancelled
    {
        get => Require(_virtualTimerIsCancelled);
        internal set => Set(ref _virtualTimerIsCancelled, value);
    }

    private FieldBuilder? _virtualTimerIsInterval;
    public FieldBuilder VirtualTimerIsInterval
    {
        get => Require(_virtualTimerIsInterval);
        internal set => Set(ref _virtualTimerIsInterval, value);
    }

    private FieldBuilder? _virtualTimerIntervalMs;
    public FieldBuilder VirtualTimerIntervalMs
    {
        get => Require(_virtualTimerIntervalMs);
        internal set => Set(ref _virtualTimerIntervalMs, value);
    }

    private FieldBuilder? _virtualTimerHasRef;
    public FieldBuilder VirtualTimerHasRef
    {
        get => Require(_virtualTimerHasRef);
        internal set => Set(ref _virtualTimerHasRef, value);
    }

    private MethodBuilder? _ensureTimerInitialized;
    public MethodBuilder EnsureTimerInitialized
    {
        get => Require(_ensureTimerInitialized);
        internal set => Set(ref _ensureTimerInitialized, value);
    }

    private MethodBuilder? _getCurrentTimeMs;
    public MethodBuilder GetCurrentTimeMs
    {
        get => Require(_getCurrentTimeMs);
        internal set => Set(ref _getCurrentTimeMs, value);
    }

    private MethodBuilder? _processPendingTimers;
    public MethodBuilder ProcessPendingTimers
    {
        get => Require(_processPendingTimers);
        internal set => Set(ref _processPendingTimers, value);
    }

    private MethodBuilder? _processOnePendingTimer;
    public MethodBuilder ProcessOnePendingTimer
    {
        get => Require(_processOnePendingTimer);
        internal set => Set(ref _processOnePendingTimer, value);
    }

    private MethodBuilder? _getNextTimerDelay;
    public MethodBuilder GetNextTimerDelay
    {
        get => Require(_getNextTimerDelay);
        internal set => Set(ref _getNextTimerDelay, value);
    }

    private MethodBuilder? _cancelAllTimers;
    public MethodBuilder CancelAllTimers
    {
        get => Require(_cancelAllTimers);
        internal set => Set(ref _cancelAllTimers, value);
    }

    private MethodBuilder? _addVirtualTimer;
    public MethodBuilder AddVirtualTimer
    {
        get => Require(_addVirtualTimer);
        internal set => Set(ref _addVirtualTimer, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Timers metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Timers metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = TimeoutType;
        _ = TimeoutCtor;
        _ = TimeoutCancel;
        _ = TimeoutRef;
        _ = TimeoutUnref;
        _ = TimeoutHasRefGetter;
        _ = SetTimeout;
        _ = ClearTimeout;
        _ = SetInterval;
        _ = ClearInterval;
        _ = VirtualTimerType;
        _ = VirtualTimerCtor;
        _ = VirtualTimerCallback;
        _ = VirtualTimerArgs;
        _ = VirtualTimerScheduledTime;
        _ = VirtualTimerIsCancelled;
        _ = VirtualTimerIsInterval;
        _ = VirtualTimerIntervalMs;
        _ = VirtualTimerHasRef;
        _ = EnsureTimerInitialized;
        _ = GetCurrentTimeMs;
        _ = ProcessPendingTimers;
        _ = ProcessOnePendingTimer;
        _ = GetNextTimerDelay;
        _ = CancelAllTimers;
        _ = AddVirtualTimer;
        IsComplete = true;
    }
}
