using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace SharpTS.Hosting;

/// <summary>
/// Framework-neutral scheduler used by emitted hosted programs. Guest assemblies
/// supply only the small set of runtime hooks below, keeping their hosted ABI
/// dependency limited to this assembly.
/// </summary>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public abstract class SharpTSHostedRuntimeBase : ISharpTSHostedRuntime
{
    private const int SchedulerIdle = 0;
    private const int SchedulerScheduled = 1;
    private const int SchedulerRunning = 2;

    private readonly ISharpTSHostDispatcher _dispatcher;
    private readonly ISharpTSHostLifetime _lifetime;
    private readonly ISharpTSHostedErrorSink _errorSink;
    private readonly ConcurrentQueue<Action> _macrotasks = new();
    private readonly ConcurrentQueue<Action> _microtasks = new();
    private readonly List<Action> _cleanup = [];
    private readonly object _cleanupGate = new();
    private readonly object _timerGate = new();
    private readonly object _moduleGate = new();
    private readonly Dictionary<string, HostedModuleRegistration> _hostedModules =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<object?>> _hostedModuleTasks =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _activeHostedModules = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource _initialization =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _shutdown =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _state = (int)SharpTSHostedRuntimeState.Created;
    private int _schedulerState;
    private int _repostRequested;
    private int _guestBoundaryDepth;
    private int _timerElapsed;
    private int? _ownerThreadId;
    private Task? _guestInitialization;
    private SharpTSHostedShutdownReason? _shutdownReason;
    private int _exitCode;
    private ISharpTSScheduledWork? _scheduledTimer;
    private TimeSpan? _scheduledDelay;
    private long _timerGeneration;
    private bool _disposed;

    protected SharpTSHostedRuntimeBase(
        ISharpTSHostDispatcher dispatcher,
        ISharpTSHostLifetime lifetime,
        ISharpTSHostedErrorSink errorSink)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _errorSink = errorSink ?? throw new ArgumentNullException(nameof(errorSink));
    }

    /// <summary>
    /// Gets the current state of the hosted runtime.
    /// </summary>
    public SharpTSHostedRuntimeState State =>
        (SharpTSHostedRuntimeState)Volatile.Read(ref _state);

    /// <summary>
    /// Gets the reason for shutdown, if the runtime is stopping or stopped.
    /// </summary>
    public SharpTSHostedShutdownReason? ShutdownReason => _shutdownReason;

    /// <summary>
    /// Gets the managed thread ID of the owner thread, if captured.
    /// </summary>
    public int? OwnerThreadId => _ownerThreadId;

    /// <summary>
    /// Initializes the guest runtime asynchronously.
    /// </summary>
    /// <returns>A task representing the guest initialization.</returns>
    protected abstract Task InitializeGuestAsync();

    /// <summary>
    /// Attempts to run one guest macrotask.
    /// </summary>
    /// <returns>True if a macrotask was run; otherwise, false.</returns>
    protected abstract bool TryRunOneGuestMacrotask();

    /// <summary>
    /// Gets whether the guest has pending macrotasks.
    /// </summary>
    protected abstract bool HasGuestMacrotasks { get; }

    /// <summary>
    /// Drains all pending guest microtasks.
    /// </summary>
    protected abstract void DrainGuestMicrotasks();

    /// <summary>
    /// Gets whether the guest has pending microtasks.
    /// </summary>
    protected abstract bool HasGuestMicrotasks { get; }

    /// <summary>
    /// Attempts to run one guest timer callback.
    /// </summary>
    /// <returns>True if a timer was run; otherwise, false.</returns>
    protected abstract bool TryRunOneGuestTimer();

    /// <summary>
    /// Gets the delay until the next guest timer deadline.
    /// </summary>
    /// <returns>The delay, or null if no timers are pending.</returns>
    protected abstract TimeSpan? GetNextGuestTimerDelay();

    /// <summary>
    /// Rejects pending guest work during shutdown.
    /// </summary>
    protected abstract void RejectGuestWork();

    /// <summary>
    /// Cancels guest resources during shutdown.
    /// </summary>
    protected abstract void CancelGuestResources();

    /// <summary>
    /// Emits the guest beforeExit event.
    /// </summary>
    /// <param name="exitCode">The exit code for the program.</param>
    protected abstract void EmitGuestBeforeExit(int exitCode);

    /// <summary>
    /// Emits the guest exit event.
    /// </summary>
    /// <param name="exitCode">The exit code for the program.</param>
    protected abstract void EmitGuestExit(int exitCode);

    /// <summary>
    /// Initializes the hosted runtime asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A task that completes when initialization is finished.</returns>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (Interlocked.CompareExchange(
                ref _state,
                (int)SharpTSHostedRuntimeState.Initializing,
                (int)SharpTSHostedRuntimeState.Created) != (int)SharpTSHostedRuntimeState.Created)
        {
            return State switch
            {
                SharpTSHostedRuntimeState.Initializing or SharpTSHostedRuntimeState.Running =>
                    _initialization.Task,
                _ => Task.FromException(new InvalidOperationException(
                    $"Cannot initialize a hosted runtime in state {State}.")),
            };
        }

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() =>
            {
                if (State == SharpTSHostedRuntimeState.Initializing)
                {
                    _initialization.TrySetCanceled(cancellationToken);
                    BeginShutdown(SharpTSHostedShutdownReason.HostRequested, 0);
                }
            });
        }

        RequestTurn();
        return _initialization.Task;
    }

    /// <summary>
    /// Shuts down the hosted runtime asynchronously.
    /// </summary>
    /// <param name="reason">The reason for shutdown.</param>
    /// <param name="exitCode">The exit code for the program.</param>
    /// <returns>A task that completes when shutdown is finished.</returns>
    public Task ShutdownAsync(
        SharpTSHostedShutdownReason reason = SharpTSHostedShutdownReason.HostRequested,
        int exitCode = 0)
    {
        ThrowIfDisposed();
        if (State is SharpTSHostedRuntimeState.Stopped or SharpTSHostedRuntimeState.Faulted)
            return Task.CompletedTask;
        BeginShutdown(reason, exitCode);
        return _shutdown.Task;
    }

    /// <summary>
    /// Registers a cleanup action to run during shutdown.
    /// </summary>
    /// <param name="cleanup">The cleanup action to register.</param>
    public void RegisterCleanup(Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        ThrowIfDisposed();
        lock (_cleanupGate)
        {
            if (State >= SharpTSHostedRuntimeState.Stopping)
                throw new InvalidOperationException("Cleanup cannot be registered after shutdown begins.");
            _cleanup.Add(cleanup);
        }
    }

    /// <summary>
    /// Notifies the guest runtime to execute an action as a macrotask.
    /// </summary>
    /// <param name="guestNotification">The action to execute.</param>
    public void Notify(Action guestNotification)
    {
        ArgumentNullException.ThrowIfNull(guestNotification);
        if (!AcceptsGuestWork)
            return;
        EnqueueMacrotask(guestNotification);
    }

    /// <summary>
    /// Invokes a return-valued native callback synchronously while deferring the
    /// guest microtask checkpoint until a posted host turn. Native frameworks can
    /// therefore obtain handled/cancel results without allowing rendering to
    /// re-enter the routed-event stack.
    /// </summary>
    /// <param name="guestCallback">The callback to invoke.</param>
    /// <returns>The result of the callback.</returns>
    public object? InvokeNativeCallback(Func<object?> guestCallback)
    {
        ArgumentNullException.ThrowIfNull(guestCallback);
        if (!_dispatcher.CheckAccess())
            throw new InvalidOperationException(
                "A synchronous native callback cannot run off the owner thread.");
        if (!AcceptsGuestWork)
            throw new InvalidOperationException($"Guest work is not accepted in state {State}.");

        AssertOwnerThread();
        _guestBoundaryDepth++;
        try
        {
            return guestCallback();
        }
        finally
        {
            _guestBoundaryDepth--;
            if (_guestBoundaryDepth == 0 && !_disposed)
                RequestTurn();
        }
    }

    /// <summary>
    /// Invokes a guest callback synchronously on the owner thread and returns its result.
    /// </summary>
    /// <param name="guestCallback">The callback to invoke.</param>
    /// <returns>The result of the callback.</returns>
    public object? Invoke(Func<object?> guestCallback)
    {
        ArgumentNullException.ThrowIfNull(guestCallback);
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException(
                "A synchronous return-valued hosted callback cannot run off the owner thread.");
        }
        if (!AcceptsGuestWork)
            throw new InvalidOperationException($"Guest work is not accepted in state {State}.");

        AssertOwnerThread();
        object? result = null;
        ExecuteGuestBoundary(() => result = guestCallback());
        return result;
    }

    /// <summary>
    /// Invokes a guest callback synchronously on the owner thread.
    /// </summary>
    /// <param name="guestCallback">The callback to invoke.</param>
    public void Invoke(Action guestCallback)
    {
        ArgumentNullException.ThrowIfNull(guestCallback);
        _ = Invoke(() =>
        {
            guestCallback();
            return null;
        });
    }

    /// <summary>Queues one external guest callback as a macrotask.</summary>
    /// <param name="callback">The callback to enqueue.</param>
    public void EnqueueMacrotask(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (!AcceptsGuestWork)
            return;
        _macrotasks.Enqueue(callback);
        RequestTurn();
    }

    /// <summary>Queues an await/promise continuation for the next microtask checkpoint.</summary>
    /// <param name="callback">The callback to enqueue.</param>
    public void EnqueueMicrotask(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (!AcceptsGuestWork)
            return;
        _microtasks.Enqueue(callback);
        RequestTurn();
    }

    /// <summary>
    /// Wakes the runtime to process pending work.
    /// </summary>
    public void Wake() => RequestTurn();

    /// <summary>
    /// Wraps a guest await so completion is transferred on the owner thread. The
    /// transfer TCS deliberately permits inline continuations: its SetResult runs
    /// inside the hosted microtask checkpoint, so the generated async state machine
    /// resumes there without installing a synchronization context.
    /// </summary>
    /// <param name="task">The task to prepare for await.</param>
    /// <returns>A task that completes on the owner thread.</returns>
    public Task<object?> PrepareAwait(Task<object?> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.IsCompleted)
            return task;

        var transfer = new TaskCompletionSource<object?>();
        task.ContinueWith(
            completed => EnqueueMicrotask(() => TransferCompletion(completed, transfer)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return transfer.Task;
    }

    /// <summary>
    /// Transfers an awaited value to the owner thread, captures it there, and
    /// completes only after the capture has run. Generated module initializers
    /// use this to resume a declaration in a later hosted turn.
    /// </summary>
    /// <param name="task">The task to await.</param>
    /// <param name="capture">The action to invoke with the completed result.</param>
    /// <returns>A task that completes when the capture has run.</returns>
    public Task CaptureAwait(Task<object?> task, Action<object?> capture)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(capture);

        Task<object?> prepared = PrepareAwait(task);
        var completion = new TaskCompletionSource();
        prepared.ContinueWith(
            completed =>
            {
                try
                {
                    if (completed.IsCanceled)
                        completion.TrySetCanceled();
                    else if (completed.Exception is not null)
                        completion.TrySetException(completed.Exception.InnerExceptions);
                    else
                    {
                        capture(completed.Result);
                        completion.TrySetResult();
                    }
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return completion.Task;
    }

    /// <summary>
    /// Runs a sequence of initialization steps asynchronously.
    /// </summary>
    /// <param name="steps">The list of initialization steps to run.</param>
    /// <returns>A task that completes when all steps have completed.</returns>
    public Task RunInitializationSteps(IReadOnlyList<Func<Task>> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var sequence = new InitializationSequence(this, steps);
        return sequence.Run();
    }

    /// <summary>Registers a compiled module initializer under a runtime import alias.</summary>
    /// <param name="alias">The import alias for the module.</param>
    /// <param name="canonicalPath">The canonical path of the module.</param>
    /// <param name="initializer">The module initializer function.</param>
    public void RegisterHostedModule(
        string alias,
        string canonicalPath,
        Func<Task> initializer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        ArgumentNullException.ThrowIfNull(initializer);
        lock (_moduleGate)
            _hostedModules[alias] = new HostedModuleRegistration(canonicalPath, initializer);
    }

    /// <summary>
    /// Initializes a compiled module once and returns its namespace. Active self-imports
    /// reject instead of awaiting their own initialization task indefinitely.
    /// </summary>
    /// <param name="alias">The import alias for the module.</param>
    /// <param name="namespaceFactory">Factory function to create the module namespace.</param>
    /// <returns>A task that completes with the module namespace.</returns>
    public Task<object?> ImportHostedModule(string alias, Func<object?> namespaceFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        ArgumentNullException.ThrowIfNull(namespaceFactory);

        HostedModuleRegistration registration;
        lock (_moduleGate)
        {
            if (!_hostedModules.TryGetValue(alias, out registration!))
                return Task.FromResult(namespaceFactory());
            if (_activeHostedModules.Contains(registration.CanonicalPath))
            {
                return Task.FromException<object?>(new InvalidOperationException(
                    $"Dynamic import of evaluating module '{registration.CanonicalPath}' would deadlock."));
            }
            if (_hostedModuleTasks.TryGetValue(
                    registration.CanonicalPath, out Task<object?>? existing))
                return existing;

            _activeHostedModules.Add(registration.CanonicalPath);
            Task<object?> pending = CompleteHostedModuleAsync(registration, namespaceFactory);
            _hostedModuleTasks[registration.CanonicalPath] = pending;
            return pending;
        }
    }

    private async Task<object?> CompleteHostedModuleAsync(
        HostedModuleRegistration registration,
        Func<object?> namespaceFactory)
    {
        try
        {
            await registration.Initializer().ConfigureAwait(false);
            return namespaceFactory();
        }
        finally
        {
            lock (_moduleGate)
                _activeHostedModules.Remove(registration.CanonicalPath);
        }
    }

    /// <summary>
    /// Attributes an uncaught compiled module-initialization failure to its source
    /// module without changing errors handled by guest try/catch code.
    /// </summary>
    /// <param name="task">The module initialization task to observe.</param>
    /// <param name="modulePath">The path of the module being initialized.</param>
    /// <returns>A task that completes when initialization succeeds or throws with attributed error.</returns>
    public static async Task AttributeModuleInitialization(Task task, string modulePath)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Module initialization failed in '{modulePath}': {exception.Message}",
                exception);
        }
    }

    /// <summary>
    /// Observes a program main task and completes the program when it finishes.
    /// </summary>
    /// <param name="task">The main task to observe.</param>
    /// <param name="useNumericResult">Whether to use numeric result as exit code.</param>
    /// <returns>A task that completes when observation is finished.</returns>
    public Task ObserveProgramMain(Task<object?> task, bool useNumericResult)
    {
        ArgumentNullException.ThrowIfNull(task);
        Task<object?> prepared = PrepareAwait(task);
        var completion = new TaskCompletionSource();
        prepared.ContinueWith(
            completed =>
            {
                try
                {
                    if (completed.IsCanceled)
                        completion.TrySetCanceled();
                    else if (completed.Exception is not null)
                        completion.TrySetException(completed.Exception.InnerExceptions);
                    else
                    {
                        if (useNumericResult && completed.Result is double exitCode)
                            CompleteProgram((int)exitCode);
                        completion.TrySetResult();
                    }
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return completion.Task;
    }

    /// <summary>Implements the forced hosted process-exit boundary.</summary>
    /// <param name="exitCode">The exit code for the process.</param>
    public void RequestProcessExit(int exitCode)
    {
        BeginShutdown(SharpTSHostedShutdownReason.ProcessExit, exitCode);
        throw new SharpTSHostedProcessExitException(exitCode);
    }

    /// <summary>Completes a numeric hosted main through the graceful lifecycle.</summary>
    /// <param name="exitCode">The exit code for the program.</param>
    public void CompleteProgram(int exitCode)
    {
        if (State == SharpTSHostedRuntimeState.Initializing)
            _initialization.TrySetResult();
        BeginShutdown(SharpTSHostedShutdownReason.ProgramCompleted, exitCode);
    }

    private bool AcceptsGuestWork =>
        State is SharpTSHostedRuntimeState.Initializing or SharpTSHostedRuntimeState.Running;

    private void RequestTurn()
    {
        if (_disposed)
            return;
        while (true)
        {
            int scheduler = Volatile.Read(ref _schedulerState);
            if (scheduler == SchedulerScheduled)
                return;
            if (scheduler == SchedulerRunning)
            {
                Interlocked.Exchange(ref _repostRequested, 1);
                return;
            }
            if (Interlocked.CompareExchange(
                    ref _schedulerState, SchedulerScheduled, SchedulerIdle) != SchedulerIdle)
                continue;

            try
            {
                _dispatcher.Post(RunPostedTurn);
            }
            catch (Exception exception)
            {
                Interlocked.Exchange(ref _schedulerState, SchedulerIdle);
                HandleFatal(exception, SharpTSHostedErrorPhase.Creation);
            }
            return;
        }
    }

    private void RunPostedTurn()
    {
        if (Interlocked.CompareExchange(
                ref _schedulerState, SchedulerRunning, SchedulerScheduled) != SchedulerScheduled)
            return;

        try
        {
            CaptureOrAssertOwnerThread();
            ExecuteGuestBoundary(RunOneTurn);
        }
        catch (SharpTSHostedProcessExitException)
        {
            // The process-exit request already selected the forced shutdown path.
        }
        catch (Exception exception)
        {
            HandleFatal(exception, State switch
            {
                SharpTSHostedRuntimeState.Initializing => SharpTSHostedErrorPhase.Initialization,
                SharpTSHostedRuntimeState.Stopping => SharpTSHostedErrorPhase.Shutdown,
                _ => SharpTSHostedErrorPhase.Running,
            });
        }
        finally
        {
            try
            {
                if (!_disposed)
                    ReconcileHostTimer();
            }
            catch (Exception exception)
            {
                HandleFatal(exception, SharpTSHostedErrorPhase.Running);
            }
            finally
            {
                Interlocked.Exchange(ref _schedulerState, SchedulerIdle);
                bool repost = Interlocked.Exchange(ref _repostRequested, 0) != 0 || HasImmediateWork();
                if (repost)
                    RequestTurn();
            }
        }
    }

    private void RunOneTurn()
    {
        switch (State)
        {
            case SharpTSHostedRuntimeState.Initializing:
                RunInitializationTurn();
                break;
            case SharpTSHostedRuntimeState.Running:
                RunGuestWorkTurn();
                break;
            case SharpTSHostedRuntimeState.Stopping:
                RunShutdownTurn();
                break;
        }
    }

    private void RunInitializationTurn()
    {
        if (_guestInitialization is null)
        {
            _guestInitialization = InitializeGuestAsync()
                ?? throw new InvalidOperationException("The hosted guest returned no initialization task.");
            if (!_guestInitialization.IsCompleted)
            {
                _guestInitialization.ContinueWith(
                    static (_, state) => ((SharpTSHostedRuntimeBase)state!).RequestTurn(),
                    this,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        if (_guestInitialization.IsCompleted)
        {
            ObserveTask(_guestInitialization);
            if (State != SharpTSHostedRuntimeState.Initializing)
                return;
            Interlocked.Exchange(ref _state, (int)SharpTSHostedRuntimeState.Running);
            _initialization.TrySetResult();
            return;
        }

        RunGuestWorkTurn();
    }

    private void RunGuestWorkTurn()
    {
        if (_macrotasks.TryDequeue(out Action? callback))
        {
            callback();
            return;
        }

        if (TryRunOneGuestMacrotask())
            return;

        if (Volatile.Read(ref _timerElapsed) != 0 && TryRunOneGuestTimer())
        {
            Interlocked.Exchange(ref _timerElapsed, 0);
        }
    }

    private void RunShutdownTurn()
    {
        SharpTSHostedShutdownReason reason = _shutdownReason ?? SharpTSHostedShutdownReason.HostRequested;
        int exitCode = _exitCode;

        if (reason != SharpTSHostedShutdownReason.ProcessExit)
        {
            EmitGuestBeforeExit(exitCode);
            DrainMicrotaskCheckpoint();
        }

        RejectGuestWork();
        CancelHostTimer();
        CancelGuestResources();

        Action[] cleanup;
        lock (_cleanupGate)
            cleanup = _cleanup.ToArray();
        for (int index = cleanup.Length - 1; index >= 0; index--)
        {
            try
            {
                cleanup[index]();
            }
            catch (Exception exception)
            {
                ReportError(exception, SharpTSHostedErrorPhase.Cleanup);
            }
        }

        DrainMicrotaskCheckpoint();
        if (reason != SharpTSHostedShutdownReason.ProcessExit)
            EmitGuestExit(exitCode);

        Interlocked.Exchange(ref _state, (int)SharpTSHostedRuntimeState.Stopped);
        _initialization.TrySetException(new InvalidOperationException(
            $"Hosted runtime stopped during initialization ({reason})."));
        _shutdown.TrySetResult();

        if (reason is SharpTSHostedShutdownReason.ProgramCompleted or
            SharpTSHostedShutdownReason.ProcessExit or
            SharpTSHostedShutdownReason.StartupFailure or
            SharpTSHostedShutdownReason.UncaughtError)
        {
            try
            {
                _lifetime.RequestExit(exitCode);
            }
            catch (Exception exception)
            {
                ReportError(exception, SharpTSHostedErrorPhase.Shutdown);
            }
        }
    }

    private void ExecuteGuestBoundary(Action action)
    {
        _guestBoundaryDepth++;
        try
        {
            action();
        }
        finally
        {
            _guestBoundaryDepth--;
            if (_guestBoundaryDepth == 0 && !_disposed)
                DrainMicrotaskCheckpoint();
        }
    }

    private void DrainMicrotaskCheckpoint()
    {
        AssertOwnerThread();
        do
        {
            while (_microtasks.TryDequeue(out Action? microtask))
                microtask();
            DrainGuestMicrotasks();
        }
        while (!_microtasks.IsEmpty || HasGuestMicrotasks);
    }

    private void BeginShutdown(SharpTSHostedShutdownReason reason, int exitCode)
    {
        while (true)
        {
            SharpTSHostedRuntimeState state = State;
            if (state is SharpTSHostedRuntimeState.Stopping or SharpTSHostedRuntimeState.Stopped or
                SharpTSHostedRuntimeState.Faulted or SharpTSHostedRuntimeState.Disposed)
                return;
            if (Interlocked.CompareExchange(
                    ref _state,
                    (int)SharpTSHostedRuntimeState.Stopping,
                    (int)state) == (int)state)
                break;
        }

        _shutdownReason = reason;
        _exitCode = exitCode;
        RejectGuestWork();
        RequestTurn();
    }

    private void HandleFatal(Exception exception, SharpTSHostedErrorPhase phase)
    {
        if (exception is SharpTSHostedProcessExitException)
            return;

        SharpTSHostedRuntimeState state = State;
        ReportError(exception, phase);
        if (state == SharpTSHostedRuntimeState.Initializing)
        {
            _shutdownReason = SharpTSHostedShutdownReason.StartupFailure;
            _exitCode = 1;
            RejectGuestWork();
            CancelHostTimer();
            CancelGuestResources();
            Interlocked.Exchange(ref _state, (int)SharpTSHostedRuntimeState.Faulted);
            _initialization.TrySetException(exception);
            _shutdown.TrySetResult();
            try
            {
                _lifetime.RequestExit(1);
            }
            catch (Exception lifetimeError)
            {
                ReportError(lifetimeError, SharpTSHostedErrorPhase.Shutdown);
            }
            return;
        }

        if (state == SharpTSHostedRuntimeState.Running)
            BeginShutdown(SharpTSHostedShutdownReason.UncaughtError, 1);
    }

    private void ReportError(Exception exception, SharpTSHostedErrorPhase phase)
    {
        try
        {
            _errorSink.Report(new SharpTSHostedError(exception, phase, State, _shutdownReason));
        }
        catch
        {
            // The diagnostic sink is observational and cannot destabilize guest execution.
        }
    }

    private bool HasImmediateWork()
    {
        if (_disposed)
            return false;
        if (State == SharpTSHostedRuntimeState.Stopping)
            return true;
        if (!AcceptsGuestWork)
            return false;
        if (!_macrotasks.IsEmpty || !_microtasks.IsEmpty || HasGuestMacrotasks || HasGuestMicrotasks)
            return true;
        return Volatile.Read(ref _timerElapsed) != 0 &&
            GetNextGuestTimerDelay() is TimeSpan delay && delay <= TimeSpan.Zero;
    }

    private void ReconcileHostTimer()
    {
        if (!AcceptsGuestWork)
        {
            CancelHostTimer();
            return;
        }

        TimeSpan? next = GetNextGuestTimerDelay();
        lock (_timerGate)
        {
            if (next is null)
            {
                CancelHostTimerUnderLock();
                return;
            }

            TimeSpan delay = next.Value < TimeSpan.Zero ? TimeSpan.Zero : next.Value;
            if (_scheduledTimer != null && _scheduledDelay == delay)
                return;

            CancelHostTimerUnderLock();
            long generation = ++_timerGeneration;
            _scheduledDelay = delay;
            _scheduledTimer = _dispatcher.Schedule(delay, () => OnTimerDeadline(generation));
        }
    }

    private void OnTimerDeadline(long generation)
    {
        lock (_timerGate)
        {
            if (generation != _timerGeneration || _scheduledTimer is null)
                return;
            _scheduledTimer.Dispose();
            _scheduledTimer = null;
            _scheduledDelay = null;
        }
        Interlocked.Exchange(ref _timerElapsed, 1);
        RequestTurn();
    }

    private void CancelHostTimer()
    {
        lock (_timerGate)
            CancelHostTimerUnderLock();
    }

    private void CancelHostTimerUnderLock()
    {
        ++_timerGeneration;
        _scheduledTimer?.Cancel();
        _scheduledTimer?.Dispose();
        _scheduledTimer = null;
        _scheduledDelay = null;
    }

    private void CaptureOrAssertOwnerThread()
    {
        int current = Environment.CurrentManagedThreadId;
        if (!_dispatcher.CheckAccess())
            throw new InvalidOperationException("The host dispatcher invoked a SharpTS turn without owner-thread access.");
        if (_ownerThreadId is null)
            _ownerThreadId = current;
        else if (_ownerThreadId != current)
            throw new InvalidOperationException(
                $"Hosted SharpTS runtime belongs to managed thread {_ownerThreadId}; thread {current} cannot run guest work.");
    }

    private void AssertOwnerThread()
    {
        if (_ownerThreadId is null)
            throw new InvalidOperationException("Hosted SharpTS runtime has not captured an owner thread.");
        CaptureOrAssertOwnerThread();
    }

    private static void ObserveTask(Task task)
    {
        if (task.IsCanceled)
            throw new OperationCanceledException("Hosted guest initialization was canceled.");
        if (task.Exception is not null)
            throw task.Exception.InnerException ?? task.Exception;
    }

    private static void TransferCompletion(
        Task<object?> source,
        TaskCompletionSource<object?> destination)
    {
        if (source.IsCanceled)
            destination.TrySetCanceled();
        else if (source.Exception is not null)
            destination.TrySetException(source.Exception.InnerExceptions);
        else
            destination.TrySetResult(source.Result);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
    }

    /// <summary>
    /// Disposes the hosted runtime asynchronously.
    /// </summary>
    /// <returns>A task that completes when disposal is finished.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        if (State is not (SharpTSHostedRuntimeState.Stopped or SharpTSHostedRuntimeState.Faulted))
            await ShutdownAsync(SharpTSHostedShutdownReason.Disposed).ConfigureAwait(false);
        Dispose();
    }

    /// <summary>
    /// Disposes the hosted runtime synchronously.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        CancelHostTimer();
        try
        {
            RejectGuestWork();
            CancelGuestResources();
        }
        catch
        {
            // Disposal is best-effort after the public lifecycle has ended.
        }
        Interlocked.Exchange(ref _state, (int)SharpTSHostedRuntimeState.Disposed);
        _initialization.TrySetException(new ObjectDisposedException(GetType().Name));
        _shutdown.TrySetResult();
    }

    private sealed class InitializationSequence(
        SharpTSHostedRuntimeBase runtime,
        IReadOnlyList<Func<Task>> steps)
    {
        private readonly TaskCompletionSource _completion = new();
        private int _index;

        public Task Run()
        {
            Advance();
            return _completion.Task;
        }

        private void Advance()
        {
            try
            {
                while (_index < steps.Count)
                {
                    Task task = steps[_index++]()
                        ?? throw new InvalidOperationException("A hosted initialization step returned no task.");
                    if (task.IsCompleted)
                    {
                        ObserveTask(task);
                        runtime.DrainMicrotaskCheckpoint();
                        continue;
                    }

                    task.ContinueWith(
                        completed => runtime.EnqueueMicrotask(() =>
                        {
                            try
                            {
                                ObserveTask(completed);
                                runtime.DrainMicrotaskCheckpoint();
                                Advance();
                            }
                            catch (Exception exception)
                            {
                                _completion.TrySetException(exception);
                            }
                        }),
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    return;
                }

                _completion.TrySetResult();
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
            }
        }
    }

    private sealed record HostedModuleRegistration(
        string CanonicalPath,
        Func<Task> Initializer);
}

/// <summary>
/// Exception thrown when a hosted SharpTS program requests immediate process exit.
/// </summary>
/// <param name="exitCode">The exit code for the process.</param>
[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public sealed class SharpTSHostedProcessExitException(int exitCode) : Exception
{
    /// <summary>
    /// Gets the exit code for the process.
    /// </summary>
    public int ExitCode { get; } = exitCode;
}
