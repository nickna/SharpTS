#pragma warning disable SHARPTS_HOSTING001

using System.Diagnostics.CodeAnalysis;
using SharpTS.Execution;

namespace SharpTS.Hosting;

[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public sealed class HostedInterpreterRuntime : ISharpTSHostedRuntime
{
    private const int SchedulerIdle = 0;
    private const int SchedulerScheduled = 1;
    private const int SchedulerRunning = 2;

    private readonly ISharpTSHostDispatcher _dispatcher;
    private readonly ISharpTSHostLifetime _lifetime;
    private readonly ISharpTSHostedErrorSink _errorSink;
    private readonly SharpTSProgram _program;
    private readonly Interpreter _interpreter;
    private readonly TaskCompletionSource _initialization =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _shutdown =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _cleanupGate = new();
    private readonly List<Action> _cleanup = [];
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _hostMicrotasks = new();
    private readonly object _timerGate = new();

    private int _state = (int)SharpTSHostedRuntimeState.Created;
    private int _schedulerState;
    private int _repostRequested;
    private int _timerDeadlineElapsed;
    private int _guestBoundaryDepth;
    private int? _ownerThreadId;
    private int _moduleIndex;
    private Task? _moduleTask;
    private Task<int?>? _mainTask;
    private bool _mainStarted;
    private bool _interpreterConfigured;
    private SharpTSHostedShutdownReason? _shutdownReason;
    private int _exitCode;
    private ISharpTSScheduledWork? _scheduledTimer;
    private (long FireTime, long Sequence)? _scheduledTimerKey;
    private long _timerGeneration;
    private bool _disposed;

    public HostedInterpreterRuntime(
        ISharpTSHostDispatcher dispatcher,
        ISharpTSHostLifetime lifetime,
        ISharpTSHostedErrorSink errorSink,
        SharpTSProgram program,
        TextWriter? stdout = null,
        TextWriter? stderr = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _errorSink = errorSink ?? throw new ArgumentNullException(nameof(errorSink));
        _program = program ?? throw new ArgumentNullException(nameof(program));
        _interpreter = new Interpreter(stdout ?? Console.Out, stderr ?? Console.Error);
    }

    public SharpTSHostedRuntimeState State =>
        (SharpTSHostedRuntimeState)Volatile.Read(ref _state);

    public SharpTSHostedShutdownReason? ShutdownReason => _shutdownReason;

    public int? OwnerThreadId => _ownerThreadId;

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

    public Task ShutdownAsync(
        SharpTSHostedShutdownReason reason = SharpTSHostedShutdownReason.HostRequested,
        int exitCode = 0)
    {
        ThrowIfDisposed();
        SharpTSHostedRuntimeState state = State;
        if (state is SharpTSHostedRuntimeState.Stopped or SharpTSHostedRuntimeState.Faulted)
            return Task.CompletedTask;
        BeginShutdown(reason, exitCode);
        return _shutdown.Task;
    }

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

    public void Notify(Action guestNotification)
    {
        ArgumentNullException.ThrowIfNull(guestNotification);
        if (State is not (SharpTSHostedRuntimeState.Initializing or SharpTSHostedRuntimeState.Running))
            return; // Late completion after shutdown is deliberately ignored.

        if (_dispatcher.CheckAccess())
        {
            AssertOwnerThread();
            try
            {
                ExecuteGuestBoundary(guestNotification);
            }
            catch (HostedProcessExitException)
            {
                // The process-exit request already selected forced shutdown.
            }
            catch (Exception exception)
            {
                HandleFatal(
                    exception,
                    State == SharpTSHostedRuntimeState.Initializing
                        ? SharpTSHostedErrorPhase.Initialization
                        : SharpTSHostedErrorPhase.Running);
            }
            return;
        }

        _interpreter.EnqueueCallback(guestNotification);
    }

    /// <summary>Queues host-integrated guest work for the next hosted microtask checkpoint.</summary>
    public void EnqueueMicrotask(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (State is not (SharpTSHostedRuntimeState.Initializing or SharpTSHostedRuntimeState.Running))
            return;
        _hostMicrotasks.Enqueue(callback);
        RequestTurn();
    }

    public T Invoke<T>(Func<T> guestCallback)
    {
        ArgumentNullException.ThrowIfNull(guestCallback);
        if (!_dispatcher.CheckAccess())
        {
            throw new InvalidOperationException(
                "A synchronous return-valued hosted callback cannot run off the owner thread.");
        }
        if (State is not (SharpTSHostedRuntimeState.Initializing or SharpTSHostedRuntimeState.Running))
            throw new InvalidOperationException($"Guest work is not accepted in state {State}.");

        AssertOwnerThread();
        T result = default!;
        ExecuteGuestBoundary(() => result = guestCallback());
        return result;
    }

    public object? Invoke(Func<object?> guestCallback) => Invoke<object?>(guestCallback);

    public void Invoke(Action guestCallback)
    {
        ArgumentNullException.ThrowIfNull(guestCallback);
        _ = Invoke(() =>
        {
            guestCallback();
            return true;
        });
    }

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
        catch (HostedProcessExitException)
        {
            // process.exit() already selected the forced shutdown path.
        }
        catch (Exception exception)
        {
            SharpTSHostedErrorPhase phase = State == SharpTSHostedRuntimeState.Initializing
                ? SharpTSHostedErrorPhase.Initialization
                : State == SharpTSHostedRuntimeState.Stopping
                    ? SharpTSHostedErrorPhase.Shutdown
                    : SharpTSHostedErrorPhase.Running;
            HandleFatal(exception, phase);
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
                SharpTSHostedErrorPhase phase = State == SharpTSHostedRuntimeState.Initializing
                    ? SharpTSHostedErrorPhase.Initialization
                    : State == SharpTSHostedRuntimeState.Stopping
                        ? SharpTSHostedErrorPhase.Shutdown
                        : SharpTSHostedErrorPhase.Running;
                HandleFatal(exception, phase);
            }
            finally
            {
                Interlocked.Exchange(ref _schedulerState, SchedulerIdle);
                bool repost = Interlocked.Exchange(ref _repostRequested, 0) != 0 || NeedsAnotherTurn();
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
        if (!_interpreterConfigured)
        {
            _interpreter.ConfigureHosted(
                _program.RuntimeModules,
                _program.Resolver,
                _program.TypeMap,
                _program.DecoratorMode,
                RequestTurn,
                RequestTurn,
                exception => HandleFatal(exception, SharpTSHostedErrorPhase.Initialization),
                RequestProcessExit);
            _interpreterConfigured = true;
        }

        if (_moduleTask != null)
        {
            if (!_moduleTask.IsCompleted)
            {
                RunGuestWorkTurn();
                return;
            }

            ObserveModuleCompletion(_moduleTask);
            _moduleTask = null;
            _moduleIndex++;
        }

        if (_moduleIndex >= _program.RuntimeModules.Count)
        {
            if (!_mainStarted && _program.RuntimeModules.Count > 0)
            {
                _mainStarted = true;
                _mainTask = _interpreter.ExecuteHostedMainAsync(
                    _program.RuntimeModules[^1].Statements);
                if (!_mainTask.IsCompleted)
                {
                    _mainTask.ContinueWith(
                        static (_, state) => ((HostedInterpreterRuntime)state!).RequestTurn(),
                        this,
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    return;
                }
            }

            if (_mainTask is not null)
            {
                if (!_mainTask.IsCompleted)
                {
                    RunGuestWorkTurn();
                    return;
                }
                ObserveModuleCompletion(_mainTask);
                int? exitCode = _mainTask.Result;
                _mainTask = null;
                if (exitCode is int code)
                {
                    _initialization.TrySetResult();
                    BeginShutdown(SharpTSHostedShutdownReason.ProgramCompleted, code);
                    return;
                }
            }

            Interlocked.Exchange(ref _state, (int)SharpTSHostedRuntimeState.Running);
            _initialization.TrySetResult();
            return;
        }

        _moduleTask = _interpreter.ExecuteHostedModuleAsync(
            _program.RuntimeModules[_moduleIndex],
            _moduleIndex == _program.RuntimeModules.Count - 1);
        if (!_moduleTask.IsCompleted)
        {
            _moduleTask.ContinueWith(
                static (_, state) => ((HostedInterpreterRuntime)state!).RequestTurn(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private static void ObserveModuleCompletion(Task task)
    {
        if (task.IsCanceled)
            throw new OperationCanceledException("Hosted module initialization was canceled.");
        if (task.Exception != null)
            throw task.Exception.InnerException ?? task.Exception;
    }

    private void RunGuestWorkTurn()
    {
        bool deadlineElapsed = Volatile.Read(ref _timerDeadlineElapsed) != 0;
        _interpreter.TryExecuteOneHostedMacrotask(deadlineElapsed, out bool ranTimer);
        if (ranTimer)
            Interlocked.Exchange(ref _timerDeadlineElapsed, 0);
    }

    private void RunShutdownTurn()
    {
        SharpTSHostedShutdownReason reason = _shutdownReason ?? SharpTSHostedShutdownReason.HostRequested;
        int exitCode = _exitCode;

        if (_interpreterConfigured && reason != SharpTSHostedShutdownReason.ProcessExit)
        {
            _interpreter.EmitHostedBeforeExit(exitCode);
            _interpreter.ProcessMicrotasks();
        }

        if (_interpreterConfigured)
            _interpreter.BeginHostedShutdown();
        CancelHostTimer();

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

        if (_interpreterConfigured)
        {
            _interpreter.ProcessMicrotasks();
            if (reason != SharpTSHostedShutdownReason.ProcessExit)
                _interpreter.EmitHostedExit(exitCode);
        }

        _interpreter.Dispose();
        Interlocked.Exchange(ref _state, (int)SharpTSHostedRuntimeState.Stopped);
        _initialization.TrySetException(new InvalidOperationException(
            $"Hosted runtime stopped during initialization ({reason})."));
        _shutdown.TrySetResult();

        if (reason is SharpTSHostedShutdownReason.ProgramCompleted or
            SharpTSHostedShutdownReason.ProcessExit or
            SharpTSHostedShutdownReason.StartupFailure or
            SharpTSHostedShutdownReason.UncaughtError)
        {
            try { _lifetime.RequestExit(exitCode); }
            catch (Exception exception) { ReportError(exception, SharpTSHostedErrorPhase.Shutdown); }
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
            if (_guestBoundaryDepth == 0 && _interpreterConfigured && !_disposed)
            {
                while (_hostMicrotasks.TryDequeue(out Action? microtask))
                    microtask();
                _interpreter.ProcessMicrotasks();
            }
        }
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
        if (_interpreterConfigured)
            _interpreter.RejectHostedWork();
        RequestTurn();
    }

    private void RequestProcessExit(int exitCode)
    {
        BeginShutdown(SharpTSHostedShutdownReason.ProcessExit, exitCode);
        throw new HostedProcessExitException(exitCode);
    }

    private void HandleFatal(Exception exception, SharpTSHostedErrorPhase phase)
    {
        if (exception is HostedProcessExitException)
            return;

        SharpTSHostedRuntimeState state = State;
        ReportError(exception, phase);
        if (state == SharpTSHostedRuntimeState.Initializing)
        {
            _shutdownReason = SharpTSHostedShutdownReason.StartupFailure;
            _exitCode = 1;
            _interpreter.RejectHostedWork();
            _interpreter.Dispose();
            Interlocked.Exchange(ref _state, (int)SharpTSHostedRuntimeState.Faulted);
            _initialization.TrySetException(exception);
            _shutdown.TrySetResult();
            try { _lifetime.RequestExit(1); }
            catch (Exception lifetimeError) { ReportError(lifetimeError, SharpTSHostedErrorPhase.Shutdown); }
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
            // A host diagnostic sink is observational and cannot destabilize the runtime.
        }
    }

    private bool NeedsAnotherTurn()
    {
        if (_disposed)
            return false;
        return State switch
        {
            SharpTSHostedRuntimeState.Initializing =>
                !_hostMicrotasks.IsEmpty ||
                (_mainTask?.IsCompleted ?? false) ||
                (_mainTask == null && (_moduleTask == null || _moduleTask.IsCompleted)) ||
                _interpreter.HasHostedImmediateWork(Volatile.Read(ref _timerDeadlineElapsed) != 0),
            SharpTSHostedRuntimeState.Running =>
                !_hostMicrotasks.IsEmpty ||
                _interpreter.HasHostedImmediateWork(Volatile.Read(ref _timerDeadlineElapsed) != 0),
            SharpTSHostedRuntimeState.Stopping => true,
            _ => false,
        };
    }

    private void ReconcileHostTimer()
    {
        if (!_interpreterConfigured || State is not (
                SharpTSHostedRuntimeState.Initializing or SharpTSHostedRuntimeState.Running))
        {
            CancelHostTimer();
            return;
        }

        var next = _interpreter.GetHostedNextTimer();
        lock (_timerGate)
        {
            if (next == null)
            {
                CancelHostTimerUnderLock();
                return;
            }

            var key = (next.Value.FireTime, next.Value.Sequence);
            if (_scheduledTimer != null && _scheduledTimerKey == key)
                return;

            CancelHostTimerUnderLock();
            long generation = ++_timerGeneration;
            _scheduledTimerKey = key;
            _scheduledTimer = _dispatcher.Schedule(next.Value.Delay, () => OnTimerDeadline(generation));
        }
    }

    private void OnTimerDeadline(long generation)
    {
        lock (_timerGate)
        {
            if (generation != _timerGeneration || _scheduledTimer == null)
                return;
            _scheduledTimer.Dispose();
            _scheduledTimer = null;
            _scheduledTimerKey = null;
        }
        Interlocked.Exchange(ref _timerDeadlineElapsed, 1);
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
        _scheduledTimerKey = null;
    }

    private void CaptureOrAssertOwnerThread()
    {
        int current = Environment.CurrentManagedThreadId;
        if (!_dispatcher.CheckAccess())
            throw new InvalidOperationException("The host dispatcher invoked a SharpTS turn without owner-thread access.");
        if (_ownerThreadId == null)
            _ownerThreadId = current;
        else if (_ownerThreadId != current)
            throw new InvalidOperationException(
                $"Hosted SharpTS runtime belongs to managed thread {_ownerThreadId}; thread {current} cannot run it.");
    }

    private void AssertOwnerThread()
    {
        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException(
                $"Hosted SharpTS runtime belongs to managed thread {_ownerThreadId}; " +
                $"thread {Environment.CurrentManagedThreadId} cannot run it.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(HostedInterpreterRuntime));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        if (State is not (SharpTSHostedRuntimeState.Stopped or SharpTSHostedRuntimeState.Faulted))
            await ShutdownAsync(SharpTSHostedShutdownReason.Disposed).ConfigureAwait(false);
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        CancelHostTimer();
        _interpreter.Dispose();
        Interlocked.Exchange(ref _state, (int)SharpTSHostedRuntimeState.Disposed);
        _initialization.TrySetException(new ObjectDisposedException(nameof(HostedInterpreterRuntime)));
        _shutdown.TrySetResult();
        GC.SuppressFinalize(this);
    }

    private sealed class HostedProcessExitException(int exitCode) : Exception
    {
        public int ExitCode { get; } = exitCode;
    }
}
