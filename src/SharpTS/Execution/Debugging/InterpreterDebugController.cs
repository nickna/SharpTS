using System.Collections.Concurrent;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.Runtime;

namespace SharpTS.Execution.Debugging;

internal enum DebugExecutionState
{
    Created,
    Running,
    PauseRequested,
    Stopped,
    Continuing,
    Terminating,
    Exited,
}

internal enum DebugStopReason
{
    Entry,
    Breakpoint,
    Step,
    Pause,
    Exception,
}

internal enum DebugStepKind
{
    None,
    In,
    Over,
    Out,
}

internal sealed record DebugSourceLocation(
    SourceDocument Document,
    SourceSpan Span,
    Stmt Statement,
    int Line,
    int Column,
    int EndLine,
    int EndColumn);

internal sealed record DebugStackFrame(
    string Name,
    RuntimeEnvironment Environment,
    DebugSourceLocation Location,
    bool IsAsyncOrigin = false,
    IReadOnlySet<string>? ArgumentNames = null);

internal sealed record DebugStopSnapshot(
    int Generation,
    DebugStopReason Reason,
    string Description,
    IReadOnlyList<DebugStackFrame> Frames,
    object? Exception = null,
    bool IsUnhandledException = false,
    int FunctionDepth = 0);

internal sealed record DebugBreakpointBinding(
    int Id,
    string SourcePath,
    int RequestedLine,
    int RequestedColumn,
    bool Verified,
    int? Line,
    int? Column,
    string? Message);

internal sealed record DebugBreakpointRequest(int Id, int Line, int Column);

/// <summary>
/// Debugger-neutral cooperative execution controller for the AST interpreter.
/// DAP types deliberately do not cross this boundary.
/// </summary>
internal sealed class InterpreterDebugController : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, SourceEntry> _sources = new(SourcePathComparer);
    private readonly Dictionary<object, DebugSourceLocation> _locations =
        new(Runtime.Types.ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, List<DebugBreakpointBinding>> _breakpoints =
        new(SourcePathComparer);
    private readonly ConcurrentQueue<IDebuggerWorkItem> _work = new();
    private readonly AsyncLocal<FrameContext?> _currentFrame = new();
    private readonly AsyncLocal<LastExecutionPoint?> _lastExecution = new();

    private DebugExecutionState _state = DebugExecutionState.Created;
    private DebugStepKind _stepKind;
    private DebugSourceLocation? _resumeLocation;
    private int _resumeDepth;
    private int _generation;
    private int _nextBreakpointId;
    private bool _entryStopPending;
    private bool _breakOnCaughtException;
    private bool _breakOnUncaughtException = true;
    private bool _breakOnUnhandledRejection = true;
    private bool _justMyCode = true;
    private bool _disposed;
    private bool _executingDebuggerWork;
    private DebugStopSnapshot? _currentStop;

    private static StringComparer SourcePathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public event Action<DebugStopSnapshot>? Stopped;
    public event Action? Continued;
    public event Action<SourceDocument>? SourceRegistered;

    public DebugExecutionState State
    {
        get { lock (_gate) return _state; }
    }

    public DebugStopSnapshot? CurrentStop
    {
        get { lock (_gate) return _currentStop; }
    }

    public IReadOnlyList<SourceDocument> Sources
    {
        get { lock (_gate) return _sources.Values.Select(source => source.Document).ToArray(); }
    }

    public bool BreakOnCaughtException
    {
        get { lock (_gate) return _breakOnCaughtException; }
    }

    public bool BreakOnUncaughtException
    {
        get { lock (_gate) return _breakOnUncaughtException; }
    }

    public bool BreakOnUnhandledRejection
    {
        get { lock (_gate) return _breakOnUnhandledRejection; }
    }

    public void ConfigureExceptionFilters(
        bool caught,
        bool uncaught,
        bool unhandledRejection)
    {
        lock (_gate)
        {
            _breakOnCaughtException = caught;
            _breakOnUncaughtException = uncaught;
            _breakOnUnhandledRejection = unhandledRejection;
        }
    }

    public void ConfigureJustMyCode(bool enabled)
    {
        lock (_gate)
            _justMyCode = enabled;
    }

    public void RegisterModules(IEnumerable<ParsedModule> modules)
    {
        foreach (ParsedModule module in modules)
            RegisterModule(module);
    }

    public void RegisterModule(ParsedModule module)
    {
        SourceDocument? document = module.Document;
        if (document is null)
            return;

        string identity = NormalizeSourceIdentity(document.Path);
        bool registered = false;
        lock (_gate)
        {
            if (_sources.ContainsKey(identity))
                return;

            var collector = new ExecutableStatementCollector(document);
            foreach (Stmt statement in module.Statements)
                collector.Visit(statement);

            DebugSourceLocation[] locations = collector.Locations
                .GroupBy(location => location.Span.Start)
                .Select(group => group
                    .OrderBy(location => location.Span.Length)
                    .First())
                .OrderBy(location => location.Span.Start)
                .ToArray();

            _sources.Add(identity, new SourceEntry(document, locations));
            foreach (DebugSourceLocation location in locations)
                _locations.TryAdd(location.Statement, location);
            registered = true;
        }
        if (registered)
            SourceRegistered?.Invoke(document);
    }

    public IReadOnlyList<DebugBreakpointBinding> SetBreakpoints(
        string sourcePath,
        IReadOnlyList<(int Line, int Column)> requested)
    {
        DebugBreakpointRequest[] requests;
        lock (_gate)
        {
            requests = requested.Select(point => new DebugBreakpointRequest(
                ++_nextBreakpointId, point.Line, point.Column)).ToArray();
        }
        return SetBreakpoints(sourcePath, requests);
    }

    internal IReadOnlyList<DebugBreakpointBinding> SetBreakpoints(
        string sourcePath,
        IReadOnlyList<DebugBreakpointRequest> requested)
    {
        string identity = NormalizeSourceIdentity(sourcePath);
        lock (_gate)
        {
            var bindings = new List<DebugBreakpointBinding>(requested.Count);
            foreach (DebugBreakpointRequest request in requested)
            {
                int id = request.Id;
                int requestedLine = request.Line;
                int requestedColumn = request.Column;
                if (!_sources.TryGetValue(identity, out SourceEntry? source))
                {
                    bindings.Add(new DebugBreakpointBinding(
                        id, identity, requestedLine, requestedColumn, false, null, null,
                        "Source has not been loaded."));
                    continue;
                }

                if (HasSourceChanged(source.Document))
                {
                    bindings.Add(new DebugBreakpointBinding(
                        id, identity, requestedLine, requestedColumn, false, null, null,
                        "Source changed after launch; restart the debug session."));
                    continue;
                }

                DebugSourceLocation? location = BindLocation(source.Locations, requestedLine, requestedColumn);
                if (location is null)
                {
                    bindings.Add(new DebugBreakpointBinding(
                        id, identity, requestedLine, requestedColumn, false, null, null,
                        "No executable statement exists at or after this location."));
                    continue;
                }

                string? message = location.Line == requestedLine
                    ? null
                    : $"Breakpoint moved from line {requestedLine} to executable line {location.Line}.";
                bindings.Add(new DebugBreakpointBinding(
                    id, identity, requestedLine, requestedColumn, true,
                    location.Line, location.Column, message));
            }

            _breakpoints[identity] = bindings;
            return bindings.ToArray();
        }
    }

    public void Start(bool stopOnEntry)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_state != DebugExecutionState.Created)
                throw new InvalidOperationException($"Cannot start debugger from state {_state}.");
            _entryStopPending = stopOnEntry;
            _state = DebugExecutionState.Running;
        }
    }

    public void RequestPause()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_state is DebugExecutionState.Running or DebugExecutionState.Continuing)
                _state = DebugExecutionState.PauseRequested;
        }
    }

    internal void CancelPauseRequest()
    {
        lock (_gate)
        {
            if (_state == DebugExecutionState.PauseRequested)
                _state = DebugExecutionState.Running;
        }
    }

    public void Continue(DebugStepKind stepKind = DebugStepKind.None)
    {
        PrepareContinue(stepKind);
        ReleasePreparedContinue();
        Continued?.Invoke();
    }

    internal void PrepareContinue(DebugStepKind stepKind)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_state != DebugExecutionState.Stopped || _currentStop is null)
                throw new InvalidOperationException("The interpreter is not stopped.");

            _stepKind = stepKind;
            _resumeLocation = _currentStop.Frames.Count == 0 ? null : _currentStop.Frames[0].Location;
            _resumeDepth = _currentStop.FunctionDepth;
            _currentStop = null;
            _state = DebugExecutionState.Continuing;
        }
    }

    internal void ReleasePreparedContinue()
    {
        lock (_gate)
        {
            if (_state is DebugExecutionState.Continuing or DebugExecutionState.PauseRequested)
                Monitor.PulseAll(_gate);
        }
    }

    public void Terminate()
    {
        lock (_gate)
        {
            if (_state == DebugExecutionState.Exited)
                return;
            _state = DebugExecutionState.Terminating;
            CancelPendingWork(new OperationCanceledException("Debug session terminated."));
            Monitor.PulseAll(_gate);
        }
    }

    public void MarkExited()
    {
        lock (_gate)
        {
            _currentStop = null;
            _state = DebugExecutionState.Exited;
            CancelPendingWork(new OperationCanceledException("Debuggee exited."));
            Monitor.PulseAll(_gate);
        }
    }

    public IDisposable EnterFrame(string name, RuntimeEnvironment environment, object declaration)
    {
        lock (_gate)
        {
            if (_state is DebugExecutionState.Terminating or DebugExecutionState.Exited || _disposed)
                return EmptyDisposable.Instance;

            _locations.TryGetValue(declaration, out DebugSourceLocation? declarationLocation);
            LastExecutionPoint? caller = _lastExecution.Value;
            var frame = new FrameContext(
                name,
                environment,
                declarationLocation,
                GetArgumentNames(declaration),
                _currentFrame.Value,
                caller?.Location,
                caller?.Environment);
            _currentFrame.Value = frame;
            return new FrameLease(this, frame);
        }
    }

    public void OnSafePoint(
        Interpreter interpreter,
        Stmt statement,
        RuntimeEnvironment environment,
        ParsedModule? module)
    {
        if (_executingDebuggerWork)
            return;
        if (module is not null && module.Document is not null)
            RegisterModule(module);

        DebugStopSnapshot? stopped = null;
        lock (_gate)
        {
            if (_state == DebugExecutionState.Terminating)
                throw new DebuggerTerminationException();
            if (_state is DebugExecutionState.Created or DebugExecutionState.Exited || _disposed)
                return;
            if (!_locations.TryGetValue(statement, out DebugSourceLocation? location))
                return;

            FrameContext? frame = _currentFrame.Value;
            if (frame is not null)
                frame.LastLocation = location;
            _lastExecution.Value = new LastExecutionPoint(location, environment);

            DebugStopReason? reason = SelectStopReason(location);
            if (reason is null)
            {
                if (_state == DebugExecutionState.Continuing)
                    _state = DebugExecutionState.Running;
                return;
            }

            stopped = CreateSnapshot(reason.Value, location, environment, exception: null, unhandled: false);
            _currentStop = stopped;
            _state = DebugExecutionState.Stopped;
        }

        Stopped?.Invoke(stopped);
        WaitWhileStopped(interpreter);
    }

    public void OnException(
        Interpreter interpreter,
        object? exception,
        bool unhandled,
        bool shouldStop)
    {
        if (!shouldStop || _executingDebuggerWork)
            return;

        DebugStopSnapshot? stopped;
        lock (_gate)
        {
            if (_state == DebugExecutionState.Terminating)
                throw new DebuggerTerminationException();
            if (_state is DebugExecutionState.Created or DebugExecutionState.Exited || _disposed)
                return;

            FrameContext? frame = _currentFrame.Value;
            LastExecutionPoint? last = _lastExecution.Value;
            DebugSourceLocation? location = frame?.LastLocation
                ?? _currentStop?.Frames.FirstOrDefault()?.Location
                ?? last?.Location;
            if (location is null)
                return;

            RuntimeEnvironment environment = frame?.Environment
                ?? last?.Environment
                ?? interpreter.Environment;
            stopped = CreateSnapshot(
                DebugStopReason.Exception, location, environment, exception, unhandled);
            _currentStop = stopped;
            _state = DebugExecutionState.Stopped;
        }

        Stopped?.Invoke(stopped);
        WaitWhileStopped(interpreter);
    }

    internal void OnIdleSafePoint(Interpreter interpreter)
    {
        if (_executingDebuggerWork)
            return;

        DebugStopSnapshot? stopped = null;
        lock (_gate)
        {
            if (_state == DebugExecutionState.Terminating)
                throw new DebuggerTerminationException();
            if (_state != DebugExecutionState.PauseRequested || _disposed)
                return;

            FrameContext? frame = _currentFrame.Value;
            LastExecutionPoint? last = _lastExecution.Value;
            DebugSourceLocation? location = frame?.LastLocation ?? last?.Location;
            RuntimeEnvironment? environment = frame?.Environment ?? last?.Environment;
            if (location is null || environment is null)
                return;

            stopped = CreateSnapshot(
                DebugStopReason.Pause, location, environment, exception: null, unhandled: false);
            _currentStop = stopped;
            _state = DebugExecutionState.Stopped;
        }

        Stopped?.Invoke(stopped);
        WaitWhileStopped(interpreter);
    }

    public Task<T> InvokeWhileStoppedAsync<T>(
        Func<Interpreter, T> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var item = new DebuggerWorkItem<T>(operation, cancellationToken);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_state != DebugExecutionState.Stopped)
                throw new InvalidOperationException("Inspection is available only while stopped.");
            _work.Enqueue(item);
            Monitor.PulseAll(_gate);
        }
        return item.Task;
    }

    private void WaitWhileStopped(Interpreter interpreter)
    {
        while (true)
        {
            IDebuggerWorkItem? work = null;
            lock (_gate)
            {
                if (_state == DebugExecutionState.Terminating)
                    throw new DebuggerTerminationException();
                if (_state != DebugExecutionState.Stopped)
                    return;
                if (!_work.TryDequeue(out work))
                {
                    Monitor.Wait(_gate);
                    continue;
                }
            }

            try
            {
                _executingDebuggerWork = true;
                work.Execute(interpreter);
            }
            finally
            {
                _executingDebuggerWork = false;
            }
        }
    }

    private DebugStopReason? SelectStopReason(DebugSourceLocation location)
    {
        bool hiddenByJustMyCode = _justMyCode && location.Document.IsVirtual;
        if (_entryStopPending && !hiddenByJustMyCode)
        {
            _entryStopPending = false;
            return DebugStopReason.Entry;
        }

        if (MatchesBreakpoint(location))
            return DebugStopReason.Breakpoint;

        if (_state == DebugExecutionState.PauseRequested)
            return DebugStopReason.Pause;

        if (hiddenByJustMyCode)
            return null;

        if (_stepKind == DebugStepKind.None || SameLocation(location, _resumeLocation))
            return null;

        int depth = CountFrames(_currentFrame.Value);
        bool shouldStop = _stepKind switch
        {
            DebugStepKind.In => true,
            DebugStepKind.Over => depth <= _resumeDepth,
            DebugStepKind.Out => depth < _resumeDepth,
            _ => false,
        };
        if (!shouldStop)
            return null;

        _stepKind = DebugStepKind.None;
        return DebugStopReason.Step;
    }

    private bool MatchesBreakpoint(DebugSourceLocation location)
    {
        string identity = NormalizeSourceIdentity(location.Document.Path);
        return _breakpoints.TryGetValue(identity, out List<DebugBreakpointBinding>? bindings)
            && bindings.Any(binding => binding.Verified
                && binding.Line == location.Line
                && (binding.Column is null || binding.Column <= location.Column));
    }

    private DebugStopSnapshot CreateSnapshot(
        DebugStopReason reason,
        DebugSourceLocation location,
        RuntimeEnvironment environment,
        object? exception,
        bool unhandled)
    {
        int generation = ++_generation;
        var frames = new List<DebugStackFrame>();

        FrameContext? currentFrame = _currentFrame.Value;
        int functionDepth = CountFrames(currentFrame);
        if (currentFrame is null)
        {
            frames.Add(new DebugStackFrame(
                ModuleFrameName(location.Document), environment, location));
        }
        else
        {
            int dapIndex = 0;
            FrameContext? outermost = null;
            for (FrameContext? context = currentFrame; context is not null; context = context.Parent)
            {
                DebugSourceLocation frameLocation = dapIndex == 0
                    ? location
                    : context.LastLocation ?? location;
                frames.Add(new DebugStackFrame(
                    context.Name,
                    context.Environment,
                    frameLocation,
                    ArgumentNames: context.ArgumentNames));
                outermost = context;
                dapIndex++;
            }

            if (outermost?.CallerLocation is not null && outermost.CallerEnvironment is not null)
            {
                frames.Add(new DebugStackFrame(
                    ModuleFrameName(outermost.CallerLocation.Document),
                    outermost.CallerEnvironment,
                    outermost.CallerLocation));
            }
        }

        string description = reason switch
        {
            DebugStopReason.Entry => "Stopped on entry",
            DebugStopReason.Breakpoint => "Paused on breakpoint",
            DebugStopReason.Step => "Paused after step",
            DebugStopReason.Pause => "Paused by client request",
            DebugStopReason.Exception when unhandled => "Paused on uncaught exception",
            DebugStopReason.Exception => "Paused on exception",
            _ => "Paused",
        };
        return new DebugStopSnapshot(
            generation, reason, description, frames.ToArray(), exception, unhandled, functionDepth);
    }

    private static string ModuleFrameName(SourceDocument document) =>
        $"<module: {Path.GetFileName(document.Path)}>";

    private static bool SameLocation(DebugSourceLocation location, DebugSourceLocation? other) =>
        other is not null
        && SourcePathComparer.Equals(
            NormalizeSourceIdentity(location.Document.Path),
            NormalizeSourceIdentity(other.Document.Path))
        && location.Span.Start == other.Span.Start;

    private static DebugSourceLocation? BindLocation(
        IReadOnlyList<DebugSourceLocation> locations,
        int line,
        int column)
    {
        DebugSourceLocation? sameLine = locations
            .Where(location => location.Line == line)
            .OrderBy(location => location.Column < column ? 1 : 0)
            .ThenBy(location => Math.Abs(location.Column - column))
            .FirstOrDefault();
        return sameLine ?? locations.FirstOrDefault(location => location.Line > line);
    }

    private static bool HasSourceChanged(SourceDocument document)
    {
        if (document.IsVirtual || !File.Exists(document.Path))
            return false;
        try
        {
            byte[] current = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(document.Path));
            return !current.AsSpan().SequenceEqual(document.Checksum);
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    internal static string NormalizeSourceIdentity(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;
        if (Uri.TryCreate(path, UriKind.Absolute, out Uri? uri) && !uri.IsFile)
            return uri.AbsoluteUri;
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception) when (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return path;
        }
    }

    private static int CountFrames(FrameContext? frame)
    {
        int count = 0;
        for (; frame is not null; frame = frame.Parent)
            count++;
        return count;
    }

    private static IReadOnlySet<string> GetArgumentNames(object declaration)
    {
        IEnumerable<Stmt.Parameter> parameters = declaration switch
        {
            Stmt.Function function => function.Parameters,
            Expr.ArrowFunction arrow => arrow.Parameters,
            _ => [],
        };
        return parameters.Select(parameter => parameter.Name.Lexeme)
            .ToHashSet(StringComparer.Ordinal);
    }

    private void ExitFrame(FrameContext frame)
    {
        if (ReferenceEquals(_currentFrame.Value, frame))
            _currentFrame.Value = frame.Parent;
    }

    private void CancelPendingWork(Exception exception)
    {
        while (_work.TryDequeue(out IDebuggerWorkItem? item))
            item.Cancel(exception);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _state = DebugExecutionState.Terminating;
            CancelPendingWork(new ObjectDisposedException(nameof(InterpreterDebugController)));
            Monitor.PulseAll(_gate);
        }
    }

    private sealed record SourceEntry(
        SourceDocument Document,
        IReadOnlyList<DebugSourceLocation> Locations);

    private sealed class FrameContext(
        string name,
        RuntimeEnvironment environment,
        DebugSourceLocation? lastLocation,
        IReadOnlySet<string> argumentNames,
        FrameContext? parent,
        DebugSourceLocation? callerLocation,
        RuntimeEnvironment? callerEnvironment)
    {
        public string Name { get; } = name;
        public RuntimeEnvironment Environment { get; } = environment;
        public DebugSourceLocation? LastLocation { get; set; } = lastLocation;
        public IReadOnlySet<string> ArgumentNames { get; } = argumentNames;
        public FrameContext? Parent { get; } = parent;
        public DebugSourceLocation? CallerLocation { get; } = callerLocation;
        public RuntimeEnvironment? CallerEnvironment { get; } = callerEnvironment;
    }

    private sealed record LastExecutionPoint(
        DebugSourceLocation Location,
        RuntimeEnvironment Environment);

    private sealed class FrameLease(InterpreterDebugController owner, FrameContext frame) : IDisposable
    {
        private InterpreterDebugController? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ExitFrame(frame);
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static EmptyDisposable Instance { get; } = new();
        public void Dispose() { }
    }

    private interface IDebuggerWorkItem
    {
        void Execute(Interpreter interpreter);
        void Cancel(Exception exception);
    }

    private sealed class DebuggerWorkItem<T> : IDebuggerWorkItem
    {
        private readonly Func<Interpreter, T> _operation;
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<T> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DebuggerWorkItem(Func<Interpreter, T> operation, CancellationToken cancellationToken)
        {
            _operation = operation;
            _cancellationToken = cancellationToken;
        }

        public Task<T> Task => _completion.Task;

        public void Execute(Interpreter interpreter)
        {
            if (_cancellationToken.IsCancellationRequested)
            {
                _completion.TrySetCanceled(_cancellationToken);
                return;
            }
            try
            {
                _completion.TrySetResult(_operation(interpreter));
            }
            catch (OperationCanceledException exception)
            {
                _completion.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
            }
        }

        public void Cancel(Exception exception) => _completion.TrySetException(exception);
    }

    private sealed class ExecutableStatementCollector(SourceDocument document) : AstVisitorBase
    {
        public List<DebugSourceLocation> Locations { get; } = [];

        public override void Visit(Stmt statement)
        {
            if (IsExecutable(statement)
                && document.Spans.TryGetSpan(statement, out SourceSpan span)
                && !span.IsHidden
                && !span.IsEmpty)
            {
                (int line, int column) = document.Lines.ToPosition(span.Start);
                (int endLine, int endColumn) = document.Lines.ToPosition(span.End);
                Locations.Add(new DebugSourceLocation(
                    document, span, statement, line, column, endLine, endColumn));
            }
            base.Visit(statement);
        }

        private static bool IsExecutable(Stmt statement) => statement is not (
            Stmt.Block
            or Stmt.Sequence
            or Stmt.Function
            or Stmt.Interface
            or Stmt.TypeAlias
            or Stmt.Import
            or Stmt.ImportAlias
            or Stmt.ImportRequire
            or Stmt.Export
            or Stmt.FileDirective
            or Stmt.Directive
            or Stmt.DeclareModule
            or Stmt.DeclareGlobal);
    }
}

internal sealed class DebuggerTerminationException : OperationCanceledException
{
    public DebuggerTerminationException() : base("Debuggee terminated by the debugger.") { }
}
