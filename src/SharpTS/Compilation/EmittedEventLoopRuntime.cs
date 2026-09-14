using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Required event-loop and synchronization-context metadata with optional hosted hooks.
/// Declarations support forward references; completion validates and freezes every handle.
/// </summary>
public sealed class EmittedEventLoopRuntime
{
    internal EmittedEventLoopRuntime() { }

    public bool IsComplete { get; private set; }

    private TypeBuilder? _type;
    public TypeBuilder Type
    {
        get => Require(_type);
        internal set => Set(ref _type, value);
    }

    private MethodBuilder? _getInstance;
    public MethodBuilder GetInstance
    {
        get => Require(_getInstance);
        internal set => Set(ref _getInstance, value);
    }

    private MethodBuilder? _ref;
    public MethodBuilder Ref
    {
        get => Require(_ref);
        internal set => Set(ref _ref, value);
    }

    private MethodBuilder? _unref;
    public MethodBuilder Unref
    {
        get => Require(_unref);
        internal set => Set(ref _unref, value);
    }

    private MethodBuilder? _schedule;
    public MethodBuilder Schedule
    {
        get => Require(_schedule);
        internal set => Set(ref _schedule, value);
    }

    private MethodBuilder? _run;
    public MethodBuilder Run
    {
        get => Require(_run);
        internal set => Set(ref _run, value);
    }

    private MethodBuilder? _wake;
    public MethodBuilder Wake
    {
        get => Require(_wake);
        internal set => Set(ref _wake, value);
    }

    private MethodBuilder? _waitForTask;
    public MethodBuilder WaitForTask
    {
        get => Require(_waitForTask);
        internal set => Set(ref _waitForTask, value);
    }

    private MethodBuilder? _pumpOnce;
    /// <summary>
    /// <c>$EventLoop.PumpOnce()</c> — drives one cooperative tick of the loop
    /// (drain queued continuations, fire due timers, 1ms idle sleep) and returns
    /// <c>1</c> if work is still pending (a timer is due later, an active handle
    /// is open, or the queue is non-empty) or <c>0</c> when idle/quiescent. Used
    /// by the synchronous <c>$ReadableStream.PipeTo</c> pump to wait for a
    /// push-style (event-loop-driven) read/write without blocking the loop (#448).
    /// Signal-agnostic by design: the abort check lives in the pump, which (unlike
    /// the early-emitted <c>$EventLoop</c>) can reference <c>AbortSignalGetAborted</c>.
    /// </summary>
    public MethodBuilder PumpOnce
    {
        get => Require(_pumpOnce);
        internal set => Set(ref _pumpOnce, value);
    }

    private FieldBuilder? _timerProcessorField;
    public FieldBuilder TimerProcessorField
    {
        get => Require(_timerProcessorField);
        internal set => Set(ref _timerProcessorField, value);
    }

    private MethodBuilder? _hasPendingWork;
    /// <summary>
    /// $EventLoop.HasPendingWork() — true while active handles remain or
    /// callbacks are queued. Read by the process beforeExit lifecycle (#1080).
    /// </summary>
    public MethodBuilder HasPendingWork
    {
        get => Require(_hasPendingWork);
        internal set => Set(ref _hasPendingWork, value);
    }

    private ConstructorBuilder? _syncContextCtor;
    /// <summary>
    /// Parameterless constructor of the emitted <c>$EventLoopSyncContext</c> (a
    /// <see cref="System.Threading.SynchronizationContext"/> subclass whose
    /// <c>Post</c> schedules continuations onto the event-loop queue). The entry
    /// point installs an instance via <c>SetSynchronizationContext</c> so
    /// async/await continuations resume on the event-loop thread instead of
    /// escaping to the thread pool.
    /// </summary>
    public ConstructorBuilder SyncContextCtor
    {
        get => Require(_syncContextCtor);
        internal set => Set(ref _syncContextCtor, value);
    }

    private FieldBuilder? _activeHandlesField;
    public FieldBuilder ActiveHandlesField
    {
        get => Require(_activeHandlesField);
        internal set => Set(ref _activeHandlesField, value);
    }

    private FieldBuilder? _queueField;
    public FieldBuilder QueueField
    {
        get => Require(_queueField);
        internal set => Set(ref _queueField, value);
    }

    private FieldBuilder? _wakeField;
    public FieldBuilder WakeField
    {
        get => Require(_wakeField);
        internal set => Set(ref _wakeField, value);
    }

    public EmittedHostedEventLoopRuntime? Hosted { get; private set; }

    public EmittedHostedEventLoopRuntime RequireHosted() => Hosted
        ?? throw new InvalidOperationException("Hosted event-loop metadata was not enabled for this compilation.");

    internal void BeginHostedEmission()
    {
        EnsureMutable();
        if (Hosted is not null)
            throw new InvalidOperationException("Hosted event-loop metadata emission has already started.");
        Hosted = new EmittedHostedEventLoopRuntime();
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Event-loop metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Event-loop metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = Type;
        _ = GetInstance;
        _ = Ref;
        _ = Unref;
        _ = Schedule;
        _ = Run;
        _ = Wake;
        _ = WaitForTask;
        _ = PumpOnce;
        _ = TimerProcessorField;
        _ = HasPendingWork;
        _ = SyncContextCtor;
        _ = ActiveHandlesField;
        _ = QueueField;
        _ = WakeField;
        Hosted?.CompleteEmission();
        IsComplete = true;
    }
}
