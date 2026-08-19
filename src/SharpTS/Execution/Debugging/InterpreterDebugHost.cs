using SharpTS.Parsing;

namespace SharpTS.Execution.Debugging;

internal sealed record InterpreterDebugThreadInfo(
    int Id,
    string Name,
    Interpreter Interpreter,
    InterpreterDebugController Controller,
    DebugStopSnapshot? CurrentStop);

internal sealed record InterpreterDebugStopEvent(
    int ThreadId,
    DebugStopSnapshot Stop,
    int Epoch,
    bool AllThreadsStopped);

internal sealed record InterpreterDebugContinueEvent(
    int ThreadId,
    bool AllThreadsContinued);

internal sealed record InterpreterDebugSourceInfo(int Id, SourceDocument Document);

internal enum InterpreterDebugSourceChangeReason
{
    New,
    Removed,
}

internal sealed record InterpreterDebugSourceEvent(
    InterpreterDebugSourceChangeReason Reason,
    InterpreterDebugSourceInfo Source);

/// <summary>
/// Session-wide, protocol-neutral coordinator for all interpreters owned by one debug launch.
/// </summary>
internal sealed class InterpreterDebugHost : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<int, ThreadState> _threads = [];
    private readonly Dictionary<string, SourceState> _sources = new(SourcePathComparer);
    private readonly Dictionary<string, BreakpointState> _breakpoints = new(SourcePathComparer);
    private readonly Queue<InterpreterDebugStopEvent> _pendingStopEvents = [];
    private int _nextThreadId = 1;
    private int _nextSourceId;
    private int _nextBreakpointId;
    private int _stopEpoch;
    private int _lastStoppedThreadId;
    private bool _stopEpochActive;
    private bool _allStopReported;
    private bool _dispatchingStopEvents;
    private bool _started;
    private bool _terminating;
    private bool _disposed;
    private bool _breakOnCaughtException;
    private bool _breakOnUncaughtException = true;
    private bool _breakOnUnhandledRejection = true;
    private bool _justMyCode = true;
    private Task _terminationTask = Task.CompletedTask;

    private static StringComparer SourcePathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public event Action<InterpreterDebugThreadInfo>? ThreadStarted;
    public event Action<int>? ThreadExited;
    public event Action<InterpreterDebugStopEvent>? Stopped;
    public event Action<InterpreterDebugContinueEvent>? Continued;
    public event Action<InterpreterDebugSourceEvent>? SourceChanged;
    public event Action<DebugBreakpointBinding>? BreakpointChanged;

    public IReadOnlyList<InterpreterDebugThreadInfo> Threads
    {
        get
        {
            lock (_gate)
            {
                return _threads.Values
                    .OrderBy(thread => thread.Id)
                    .Select(ToInfo)
                    .ToArray();
            }
        }
    }

    public IReadOnlyList<InterpreterDebugSourceInfo> Sources
    {
        get
        {
            lock (_gate)
                return _sources.Values.OrderBy(source => source.Id).Select(ToInfo).ToArray();
        }
    }

    public int CurrentStopEpoch
    {
        get { lock (_gate) return _stopEpochActive ? _stopEpoch : 0; }
    }

    public InterpreterDebugThreadInfo RegisterMain(
        Interpreter interpreter,
        string name = "SharpTS interpreter")
    {
        ArgumentNullException.ThrowIfNull(interpreter);
        ThreadState thread;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_threads.ContainsKey(1))
                throw new InvalidOperationException("The main interpreter is already registered.");
            if (_terminating)
                throw new DebuggerTerminationException();

            thread = CreateThread(1, name, interpreter, static interpreter => interpreter.Shutdown());
            _threads.Add(1, thread);
            interpreter.DebugHost = this;
            interpreter.DebugController = thread.Controller;
        }

        InterpreterDebugThreadInfo info = ToInfo(thread);
        ThreadStarted?.Invoke(info);
        return info;
    }

    public IDisposable RegisterWorker(
        Interpreter interpreter,
        string name,
        Action<Interpreter> requestShutdown)
    {
        ArgumentNullException.ThrowIfNull(interpreter);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(requestShutdown);

        ThreadState thread;
        bool pauseImmediately;
        int pauseEpoch;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_terminating)
                throw new DebuggerTerminationException();

            int id = checked(++_nextThreadId);
            thread = CreateThread(id, name, interpreter, requestShutdown);
            _threads.Add(id, thread);
            interpreter.DebugHost = this;
            interpreter.DebugController = thread.Controller;
            thread.Controller.Start(stopOnEntry: false);

            pauseImmediately = _stopEpochActive;
            pauseEpoch = _stopEpoch;
            if (pauseImmediately)
                _allStopReported = false;
        }

        ThreadStarted?.Invoke(ToInfo(thread));
        if (pauseImmediately)
        {
            lock (_gate)
                pauseImmediately = _stopEpochActive && _stopEpoch == pauseEpoch
                    && _threads.TryGetValue(thread.Id, out ThreadState? live)
                    && ReferenceEquals(live, thread);
            if (pauseImmediately)
            {
                try { thread.Controller.RequestPause(); }
                catch (ObjectDisposedException) { pauseImmediately = false; }
                if (pauseImmediately)
                    interpreter.WakeDebugger();
            }
        }
        return new ThreadRegistration(this, thread.Id);
    }

    public void StartMain(bool stopOnEntry)
    {
        InterpreterDebugController controller;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_started)
                throw new InvalidOperationException("The debug host has already started.");
            controller = GetThreadState(1).Controller;
            _started = true;
        }
        controller.Start(stopOnEntry);
    }

    public void ConfigureExceptionFilters(bool caught, bool uncaught, bool unhandledRejection)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _breakOnCaughtException = caught;
            _breakOnUncaughtException = uncaught;
            _breakOnUnhandledRejection = unhandledRejection;
            foreach (ThreadState thread in _threads.Values)
                thread.Controller.ConfigureExceptionFilters(caught, uncaught, unhandledRejection);
        }
    }

    public void ConfigureJustMyCode(bool enabled)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _justMyCode = enabled;
            foreach (ThreadState thread in _threads.Values)
                thread.Controller.ConfigureJustMyCode(enabled);
        }
    }

    public IReadOnlyList<DebugBreakpointBinding> SetBreakpoints(
        string sourcePath,
        IReadOnlyList<(int Line, int Column)> requested)
    {
        string identity = InterpreterDebugController.NormalizeSourceIdentity(sourcePath);
        lock (_gate)
        {
            ThrowIfDisposed();
            var requests = requested.Select(point => new DebugBreakpointRequest(
                checked(++_nextBreakpointId), point.Line, point.Column)).ToArray();
            var state = new BreakpointState(identity, requests);
            _breakpoints[identity] = state;

            foreach (ThreadState thread in _threads.Values)
            {
                thread.BreakpointBindings[identity] = thread.Controller
                    .SetBreakpoints(identity, requests);
            }

            state.Aggregate = AggregateBindings(state);
            return state.Aggregate;
        }
    }

    public void RequestPause(int threadId)
    {
        ThreadState[] threads;
        int epoch;
        lock (_gate)
        {
            ThrowIfDisposed();
            _ = GetThreadState(threadId);
            if (!_stopEpochActive)
            {
                _stopEpochActive = true;
                _stopEpoch = checked(_stopEpoch + 1);
            }
            _allStopReported = false;
            epoch = _stopEpoch;
            threads = _threads.Values.ToArray();
        }

        foreach (ThreadState thread in threads)
        {
            lock (_gate)
            {
                if (!_stopEpochActive || _stopEpoch != epoch
                    || !_threads.TryGetValue(thread.Id, out ThreadState? live)
                    || !ReferenceEquals(live, thread))
                    continue;
            }
            try { thread.Controller.RequestPause(); }
            catch (ObjectDisposedException) { continue; }
            thread.Interpreter.WakeDebugger();
        }
    }

    public void Continue(int threadId, DebugStepKind stepKind = DebugStepKind.None)
    {
        ThreadState[] threads;
        lock (_gate)
        {
            ThrowIfDisposed();
            ThreadState selected = GetThreadState(threadId);
            if (selected.CurrentStop is null || selected.Controller.State != DebugExecutionState.Stopped)
                throw new InvalidOperationException("The selected interpreter is not stopped.");

            threads = _threads.Values.ToArray();
            _stopEpochActive = false;
            _allStopReported = false;
            _pendingStopEvents.Clear();
            foreach (ThreadState thread in threads)
            {
                thread.CurrentStop = null;
                if (thread.Controller.State == DebugExecutionState.Stopped)
                {
                    thread.Controller.PrepareContinue(
                        thread.Id == threadId ? stepKind : DebugStepKind.None);
                }
                else
                {
                    thread.Controller.CancelPauseRequest();
                }
            }
        }

        foreach (ThreadState thread in threads)
        {
            thread.Controller.ReleasePreparedContinue();
            thread.Interpreter.WakeDebugger();
        }

        Continued?.Invoke(new InterpreterDebugContinueEvent(threadId, AllThreadsContinued: true));
    }

    public InterpreterDebugThreadInfo GetThread(int threadId)
    {
        lock (_gate)
            return ToInfo(GetThreadState(threadId));
    }

    public bool TryGetThread(int threadId, out InterpreterDebugThreadInfo? thread)
    {
        lock (_gate)
        {
            if (_threads.TryGetValue(threadId, out ThreadState? state))
            {
                thread = ToInfo(state);
                return true;
            }
            thread = null;
            return false;
        }
    }

    public InterpreterDebugSourceInfo GetSource(SourceDocument document)
    {
        string identity = InterpreterDebugController.NormalizeSourceIdentity(document.Path);
        lock (_gate)
        {
            if (!_sources.TryGetValue(identity, out SourceState? source))
                throw new InvalidOperationException("The source is no longer loaded.");
            return ToInfo(source);
        }
    }

    public bool TryGetSource(int sourceId, out InterpreterDebugSourceInfo? source)
    {
        lock (_gate)
        {
            SourceState? state = _sources.Values.FirstOrDefault(candidate => candidate.Id == sourceId);
            source = state is null ? null : ToInfo(state);
            return state is not null;
        }
    }

    public Task Terminate()
    {
        ThreadState[] threads;
        Task[] workerExits;
        lock (_gate)
        {
            if (_terminating || _disposed)
                return _terminationTask;
            _terminating = true;
            _stopEpochActive = false;
            _pendingStopEvents.Clear();
            threads = _threads.Values.ToArray();
            workerExits = threads.Where(thread => thread.Id != 1)
                .Select(thread => thread.Exited.Task).ToArray();
            _terminationTask = workerExits.Length == 0
                ? Task.CompletedTask
                : Task.WhenAll(workerExits);
            foreach (ThreadState thread in threads)
                thread.CurrentStop = null;
        }

        foreach (ThreadState thread in threads)
        {
            thread.Controller.Terminate();
            thread.RequestShutdown(thread.Interpreter);
            thread.Interpreter.WakeDebugger();
        }
        return _terminationTask;
    }

    public void MarkMainExited() => Unregister(1, disposeController: false);

    private ThreadState CreateThread(
        int id,
        string name,
        Interpreter interpreter,
        Action<Interpreter> requestShutdown)
    {
        var controller = new InterpreterDebugController();
        controller.ConfigureExceptionFilters(
            _breakOnCaughtException, _breakOnUncaughtException, _breakOnUnhandledRejection);
        controller.ConfigureJustMyCode(_justMyCode);
        var thread = new ThreadState(id, name, interpreter, controller, requestShutdown);
        thread.StoppedHandler = stop => OnControllerStopped(id, stop);
        thread.SourceHandler = document => OnSourceRegistered(id, document);
        controller.Stopped += thread.StoppedHandler;
        controller.SourceRegistered += thread.SourceHandler;
        foreach ((string identity, BreakpointState breakpoint) in _breakpoints)
        {
            thread.BreakpointBindings[identity] = controller
                .SetBreakpoints(identity, breakpoint.Requests);
        }
        return thread;
    }

    private void OnControllerStopped(int threadId, DebugStopSnapshot stop)
    {
        ThreadState[] pauseTargets;
        InterpreterDebugStopEvent stopped;
        bool dispatchStops;
        int epoch;
        lock (_gate)
        {
            if (!_threads.TryGetValue(threadId, out ThreadState? thread) || _terminating
                || thread.Controller.State != DebugExecutionState.Stopped
                || thread.Controller.CurrentStop?.Generation != stop.Generation)
                return;

            thread.CurrentStop = stop;
            _lastStoppedThreadId = threadId;
            if (!_stopEpochActive)
            {
                _stopEpochActive = true;
                _allStopReported = false;
                _stopEpoch = checked(_stopEpoch + 1);
            }

            pauseTargets = _threads.Values
                .Where(candidate => candidate.Id != threadId
                    && candidate.Controller.State is DebugExecutionState.Running
                        or DebugExecutionState.Continuing)
                .ToArray();
            bool allStopped = AreAllThreadsStopped();
            if (allStopped)
                _allStopReported = true;
            stopped = new InterpreterDebugStopEvent(threadId, stop, _stopEpoch, allStopped);
            epoch = _stopEpoch;
            _pendingStopEvents.Enqueue(stopped);
            dispatchStops = !_dispatchingStopEvents;
            if (dispatchStops)
                _dispatchingStopEvents = true;
        }

        if (dispatchStops)
            DrainStopEvents();
        lock (_gate)
        {
            if (!_stopEpochActive || _stopEpoch != epoch)
                pauseTargets = [];
        }
        foreach (ThreadState target in pauseTargets)
        {
            lock (_gate)
            {
                if (!_stopEpochActive || _stopEpoch != epoch
                    || !_threads.TryGetValue(target.Id, out ThreadState? live)
                    || !ReferenceEquals(live, target))
                    continue;
            }
            try { target.Controller.RequestPause(); }
            catch (ObjectDisposedException) { continue; }
            target.Interpreter.WakeDebugger();
        }
    }

    private void DrainStopEvents()
    {
        while (true)
        {
            InterpreterDebugStopEvent stopped;
            lock (_gate)
            {
                if (_pendingStopEvents.Count == 0)
                {
                    _dispatchingStopEvents = false;
                    return;
                }
                stopped = _pendingStopEvents.Dequeue();
            }

            try { Stopped?.Invoke(stopped); }
            catch
            {
                lock (_gate)
                    _dispatchingStopEvents = false;
                throw;
            }
        }
    }

    private void OnSourceRegistered(int threadId, SourceDocument document)
    {
        InterpreterDebugSourceEvent? sourceEvent = null;
        DebugBreakpointBinding[] changed = [];
        lock (_gate)
        {
            if (!_threads.TryGetValue(threadId, out ThreadState? thread))
                return;
            string identity = InterpreterDebugController.NormalizeSourceIdentity(document.Path);
            if (!thread.SourceIdentities.Add(identity))
                return;

            if (!_sources.TryGetValue(identity, out SourceState? source))
            {
                source = new SourceState(checked(++_nextSourceId), identity, document);
                _sources.Add(identity, source);
                sourceEvent = new InterpreterDebugSourceEvent(
                    InterpreterDebugSourceChangeReason.New, ToInfo(source));
            }
            source.ThreadIds.Add(threadId);

            if (_breakpoints.TryGetValue(identity, out BreakpointState? breakpoint))
            {
                IReadOnlyList<DebugBreakpointBinding> before = breakpoint.Aggregate;
                thread.BreakpointBindings[identity] = thread.Controller
                    .SetBreakpoints(identity, breakpoint.Requests);
                breakpoint.Aggregate = AggregateBindings(breakpoint);
                changed = ChangedBindings(before, breakpoint.Aggregate);
            }
        }

        if (sourceEvent is not null)
            SourceChanged?.Invoke(sourceEvent);
        foreach (DebugBreakpointBinding binding in changed)
            BreakpointChanged?.Invoke(binding);
    }

    private void Unregister(int threadId, bool disposeController = true)
    {
        ThreadState? thread;
        List<InterpreterDebugSourceEvent> sourceEvents = [];
        List<DebugBreakpointBinding> changedBreakpoints = [];
        InterpreterDebugStopEvent? convergenceStop = null;
        bool dispatchStops = false;
        lock (_gate)
        {
            if (!_threads.Remove(threadId, out thread))
                return;

            thread.Controller.Stopped -= thread.StoppedHandler;
            thread.Controller.SourceRegistered -= thread.SourceHandler;
            foreach (string identity in thread.SourceIdentities)
            {
                if (_sources.TryGetValue(identity, out SourceState? source))
                {
                    source.ThreadIds.Remove(threadId);
                    if (source.ThreadIds.Count == 0)
                    {
                        _sources.Remove(identity);
                        sourceEvents.Add(new InterpreterDebugSourceEvent(
                            InterpreterDebugSourceChangeReason.Removed, ToInfo(source)));
                    }
                }

                if (_breakpoints.TryGetValue(identity, out BreakpointState? breakpoint))
                {
                    IReadOnlyList<DebugBreakpointBinding> before = breakpoint.Aggregate;
                    breakpoint.Aggregate = AggregateBindings(breakpoint);
                    changedBreakpoints.AddRange(ChangedBindings(before, breakpoint.Aggregate));
                }
            }

            if (ReferenceEquals(thread.Interpreter.DebugHost, this))
                thread.Interpreter.DebugHost = null;
            if (ReferenceEquals(thread.Interpreter.DebugController, thread.Controller))
                thread.Interpreter.DebugController = null;

            if (_stopEpochActive && !_allStopReported && AreAllThreadsStopped()
                && _threads.TryGetValue(_lastStoppedThreadId, out ThreadState? stoppedThread)
                && stoppedThread.CurrentStop is not null)
            {
                _allStopReported = true;
                convergenceStop = new InterpreterDebugStopEvent(
                    stoppedThread.Id, stoppedThread.CurrentStop, _stopEpoch, AllThreadsStopped: true);
                _pendingStopEvents.Enqueue(convergenceStop);
                dispatchStops = !_dispatchingStopEvents;
                if (dispatchStops)
                    _dispatchingStopEvents = true;
            }
        }

        thread.Controller.MarkExited();
        if (disposeController)
            thread.Controller.Dispose();
        thread.Exited.TrySetResult();
        foreach (InterpreterDebugSourceEvent sourceEvent in sourceEvents)
            SourceChanged?.Invoke(sourceEvent);
        foreach (DebugBreakpointBinding binding in changedBreakpoints)
            BreakpointChanged?.Invoke(binding);
        ThreadExited?.Invoke(threadId);
        if (dispatchStops)
            DrainStopEvents();
    }

    private IReadOnlyList<DebugBreakpointBinding> AggregateBindings(BreakpointState state)
    {
        var result = new DebugBreakpointBinding[state.Requests.Count];
        for (int index = 0; index < state.Requests.Count; index++)
        {
            DebugBreakpointRequest request = state.Requests[index];
            DebugBreakpointBinding[] candidates = _threads.Values
                .SelectMany(thread => thread.BreakpointBindings.TryGetValue(
                    state.SourcePath, out IReadOnlyList<DebugBreakpointBinding>? bindings)
                        ? bindings
                        : [])
                .Where(binding => binding.Id == request.Id)
                .ToArray();
            result[index] = candidates.FirstOrDefault(binding => binding.Verified)
                ?? candidates.FirstOrDefault(binding =>
                    !string.Equals(binding.Message, "Source has not been loaded.", StringComparison.Ordinal))
                ?? candidates.FirstOrDefault()
                ?? new DebugBreakpointBinding(
                    request.Id, state.SourcePath, request.Line, request.Column,
                    false, null, null, "Source has not been loaded.");
        }
        return result;
    }

    private static DebugBreakpointBinding[] ChangedBindings(
        IReadOnlyList<DebugBreakpointBinding> before,
        IReadOnlyList<DebugBreakpointBinding> after)
    {
        var previous = before.ToDictionary(binding => binding.Id);
        return after.Where(binding =>
            !previous.TryGetValue(binding.Id, out DebugBreakpointBinding? old)
            || old.Verified != binding.Verified
            || old.Line != binding.Line
            || old.Column != binding.Column
            || !string.Equals(old.Message, binding.Message, StringComparison.Ordinal))
            .ToArray();
    }

    private bool AreAllThreadsStopped() =>
        _threads.Count != 0 && _threads.Values.All(thread =>
            thread.CurrentStop is not null
            && thread.Controller.State == DebugExecutionState.Stopped);

    private ThreadState GetThreadState(int threadId) =>
        _threads.TryGetValue(threadId, out ThreadState? thread)
            ? thread
            : throw new KeyNotFoundException($"Unknown or exited interpreter thread {threadId}.");

    private static InterpreterDebugThreadInfo ToInfo(ThreadState thread) => new(
        thread.Id, thread.Name, thread.Interpreter, thread.Controller, thread.CurrentStop);

    private static InterpreterDebugSourceInfo ToInfo(SourceState source) =>
        new(source.Id, source.Document);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        ThreadState[] threads;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _terminating = true;
            _stopEpochActive = false;
            _pendingStopEvents.Clear();
            threads = _threads.Values.ToArray();
            _threads.Clear();
            _sources.Clear();
            _breakpoints.Clear();
        }

        foreach (ThreadState thread in threads)
        {
            thread.Controller.Stopped -= thread.StoppedHandler;
            thread.Controller.SourceRegistered -= thread.SourceHandler;
            thread.Controller.Terminate();
            thread.RequestShutdown(thread.Interpreter);
            thread.Controller.Dispose();
            thread.Exited.TrySetResult();
        }
    }

    private sealed class ThreadState(
        int id,
        string name,
        Interpreter interpreter,
        InterpreterDebugController controller,
        Action<Interpreter> requestShutdown)
    {
        public int Id { get; } = id;
        public string Name { get; } = name;
        public Interpreter Interpreter { get; } = interpreter;
        public InterpreterDebugController Controller { get; } = controller;
        public Action<Interpreter> RequestShutdown { get; } = requestShutdown;
        public HashSet<string> SourceIdentities { get; } = new(SourcePathComparer);
        public Dictionary<string, IReadOnlyList<DebugBreakpointBinding>> BreakpointBindings { get; } =
            new(SourcePathComparer);
        public TaskCompletionSource Exited { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DebugStopSnapshot? CurrentStop { get; set; }
        public Action<DebugStopSnapshot> StoppedHandler { get; set; } = null!;
        public Action<SourceDocument> SourceHandler { get; set; } = null!;
    }

    private sealed class SourceState(int id, string identity, SourceDocument document)
    {
        public int Id { get; } = id;
        public string Identity { get; } = identity;
        public SourceDocument Document { get; } = document;
        public HashSet<int> ThreadIds { get; } = [];
    }

    private sealed class BreakpointState(
        string sourcePath,
        IReadOnlyList<DebugBreakpointRequest> requests)
    {
        public string SourcePath { get; } = sourcePath;
        public IReadOnlyList<DebugBreakpointRequest> Requests { get; } = requests;
        public IReadOnlyList<DebugBreakpointBinding> Aggregate { get; set; } = [];
    }

    private sealed class ThreadRegistration(InterpreterDebugHost host, int threadId) : IDisposable
    {
        private InterpreterDebugHost? _host = host;
        public void Dispose() => Interlocked.Exchange(ref _host, null)?.Unregister(threadId);
    }
}
