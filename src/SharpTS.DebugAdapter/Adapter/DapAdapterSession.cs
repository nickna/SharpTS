using System.Collections.Concurrent;
using System.Text.Json;
using SharpTS.DebugAdapter.Protocol;
using SharpTS.Execution.Debugging;
using SharpTS.Parsing;
using SharpTS.Runtime;

namespace SharpTS.DebugAdapter.Adapter;

internal enum DapSessionState
{
    Created,
    Initialized,
    Launched,
    Configured,
    Terminated,
}

internal sealed class DapAdapterSession(DapProtocolConnection connection, TextWriter log)
    : IAsyncDisposable
{
    private const int MaximumBreakpointsPerSource = 10_000;
    private readonly object _stateGate = new();
    private readonly HashSet<int> _seenSequences = [];
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _requestCancellation = [];
    private readonly InterpreterDebugHost _debugHost = new();
    private readonly DebugHandleStore _handles = new();
    private readonly Dictionary<int, DapFrameHandle> _frameHandles = [];
    private readonly Dictionary<DapFrameKey, int> _reverseFrameHandles = [];
    private readonly CancellationTokenSource _sessionCancellation = new();

    private DapSessionState _state;
    private DebuggeeSession? _debuggee;
    private bool _clientLinesStartAtOne = true;
    private bool _clientColumnsStartAtOne = true;
    private int _nextFrameHandle;
    private bool _breakOnCaughtException;
    private bool _breakOnUncaughtException = true;
    private bool _breakOnUnhandledRejection = true;
    private bool _disposed;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _sessionCancellation.Token);
        Task processing = Task.CompletedTask;

        try
        {
            while (!linked.IsCancellationRequested)
            {
                DapRequest? request = await connection.ReadRequestAsync(linked.Token).ConfigureAwait(false);
                if (request is null)
                    break;

                lock (_stateGate)
                {
                    if (!_seenSequences.Add(request.Sequence))
                    {
                        _ = connection.SendResponseAsync(
                            request, false, message: "Duplicate request sequence number.", errorId: 1002);
                        continue;
                    }
                }

                if (request.Command == "cancel")
                {
                    await HandleCancelAsync(request).ConfigureAwait(false);
                    continue;
                }

                var requestCts = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                _requestCancellation[request.Sequence] = requestCts;
                processing = processing.ContinueWith(
                    _ => HandleRequestAsync(request, requestCts.Token),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default).Unwrap();
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
        }
        finally
        {
            if (linked.IsCancellationRequested)
            {
                foreach (CancellationTokenSource cts in _requestCancellation.Values)
                    cts.Cancel();
            }
            try { await processing.ConfigureAwait(false); }
            catch (Exception exception) { await log.WriteLineAsync(Redact(exception.ToString())).ConfigureAwait(false); }
            await DisconnectDebuggeeAsync().ConfigureAwait(false);
        }
    }

    private async Task HandleRequestAsync(DapRequest request, CancellationToken cancellationToken)
    {
        try
        {
            switch (request.Command)
            {
                case "initialize": await InitializeAsync(request, cancellationToken).ConfigureAwait(false); break;
                case "launch": await LaunchAsync(request, cancellationToken).ConfigureAwait(false); break;
                case "configurationDone": await ConfigurationDoneAsync(request, cancellationToken).ConfigureAwait(false); break;
                case "setBreakpoints": await SetBreakpointsAsync(request, cancellationToken).ConfigureAwait(false); break;
                case "setExceptionBreakpoints": await SetExceptionBreakpointsAsync(request, cancellationToken).ConfigureAwait(false); break;
                case "threads": await ThreadsAsync(request, cancellationToken).ConfigureAwait(false); break;
                case "stackTrace": await StackTraceAsync(request, cancellationToken).ConfigureAwait(false); break;
                case "scopes": await ScopesAsync(request, cancellationToken).ConfigureAwait(false); break;
                case "variables": await VariablesAsync(request, cancellationToken).ConfigureAwait(false); break;
                case "evaluate": await EvaluateAsync(request, cancellationToken).ConfigureAwait(false); break;
                case "exceptionInfo": await ExceptionInfoAsync(request, cancellationToken).ConfigureAwait(false); break;
                case "continue": await ContinueAsync(request, DebugStepKind.None, cancellationToken).ConfigureAwait(false); break;
                case "next": await ContinueAsync(request, DebugStepKind.Over, cancellationToken).ConfigureAwait(false); break;
                case "stepIn": await ContinueAsync(request, DebugStepKind.In, cancellationToken).ConfigureAwait(false); break;
                case "stepOut": await ContinueAsync(request, DebugStepKind.Out, cancellationToken).ConfigureAwait(false); break;
                case "pause": await PauseAsync(request, cancellationToken).ConfigureAwait(false); break;
                case "loadedSources": await LoadedSourcesAsync(request, cancellationToken).ConfigureAwait(false); break;
                case "source": await SourceAsync(request, cancellationToken).ConfigureAwait(false); break;
                case "modules": await ModulesAsync(request, cancellationToken).ConfigureAwait(false); break;
                case "terminate": await TerminateAsync(request, cancellationToken).ConfigureAwait(false); break;
                case "disconnect": await DisconnectAsync(request, cancellationToken).ConfigureAwait(false); break;
                default:
                    throw new DapRequestException($"Unsupported request '{request.Command}'.", 1010);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await connection.SendResponseAsync(
                request, false, message: "Request cancelled.", errorId: 1003).ConfigureAwait(false);
        }
        catch (DapRequestException exception)
        {
            await connection.SendResponseAsync(
                request, false, message: exception.Message, errorId: exception.ErrorId)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await log.WriteLineAsync(Redact(exception.ToString())).ConfigureAwait(false);
            await connection.SendResponseAsync(
                request, false, message: exception.Message, errorId: 1099).ConfigureAwait(false);
        }
        finally
        {
            if (_requestCancellation.TryRemove(request.Sequence, out CancellationTokenSource? cts))
                cts.Dispose();
        }
    }

    private async Task InitializeAsync(DapRequest request, CancellationToken cancellationToken)
    {
        Transition(DapSessionState.Created, DapSessionState.Initialized, "initialize");
        _clientLinesStartAtOne = request.Arguments.OptionalBoolean("linesStartAt1", true);
        _clientColumnsStartAtOne = request.Arguments.OptionalBoolean("columnsStartAt1", true);

        await connection.SendResponseAsync(request, true, new
        {
            supportsConfigurationDoneRequest = true,
            supportsTerminateRequest = true,
            supportsCancelRequest = true,
            supportsLoadedSourcesRequest = true,
            supportsModulesRequest = true,
            supportsExceptionInfoRequest = true,
            supportsEvaluateForHovers = true,
            supportsSetVariable = false,
            supportsSetExpression = false,
            supportsTerminateThreadsRequest = false,
            supportsRestartRequest = false,
            supportsSingleThreadExecutionRequests = false,
            exceptionBreakpointFilters = new object[]
            {
                new { filter = "caught", label = "Caught Exceptions", @default = false },
                new { filter = "uncaught", label = "Uncaught Exceptions", @default = true },
                new { filter = "unhandledRejection", label = "Unhandled Promise Rejections", @default = true },
            },
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
        await connection.SendEventAsync("initialized", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task LaunchAsync(DapRequest request, CancellationToken cancellationToken)
    {
        RequireState(DapSessionState.Initialized, "launch");
        string requestedProgram = request.Arguments.RequiredString("program");
        string? requestedCwd = request.Arguments.OptionalString("cwd");
        string baseDirectory = requestedCwd is null
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(requestedCwd);
        string program = Path.GetFullPath(requestedProgram, baseDirectory);
        if (!File.Exists(program))
            throw new DapRequestException($"Program does not exist: {program}");
        string extension = Path.GetExtension(program);
        if (!new[] { ".ts", ".tsx", ".mts", ".cts" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new DapRequestException("'program' must be a TypeScript source file.");

        string console = request.Arguments.OptionalString("console") ?? "internalConsole";
        if (console != "internalConsole")
            throw new DapRequestException("Interpreter debugging currently supports only internalConsole.");

        IReadOnlyDictionary<string, string?> environment = ParseEnvironment(request.Arguments);
        string diagnostics = request.Arguments.OptionalString("diagnostics") ?? "errors";
        if (diagnostics is not ("errors" or "all" or "none"))
            throw new DapRequestException("'diagnostics' must be 'errors', 'all', or 'none'.");
        var options = new DebuggeeLaunchOptions(
            program,
            requestedCwd is null ? Path.GetDirectoryName(program)! : baseDirectory,
            request.Arguments.OptionalStringArray("args"),
            environment,
            ResolveOptionalPath(request.Arguments.OptionalString("project"), baseDirectory),
            request.Arguments.OptionalStringArray("references")
                .Select(path => Path.GetFullPath(path, baseDirectory)).ToArray(),
            request.Arguments.OptionalBoolean("stopOnEntry"),
            request.Arguments.OptionalBoolean("justMyCode", true),
            diagnostics);

        _debugHost.ConfigureExceptionFilters(
            _breakOnCaughtException, _breakOnUncaughtException, _breakOnUnhandledRejection);
        _debuggee = await DebuggeeSession.PrepareAsync(
            options, _debugHost, EmitOutput, cancellationToken).ConfigureAwait(false);
        _debugHost.Stopped += OnStopped;
        _debugHost.Continued += OnContinued;
        _debugHost.ThreadStarted += OnThreadStarted;
        _debugHost.ThreadExited += OnThreadExited;
        _debugHost.SourceChanged += OnSourceChanged;
        _debugHost.BreakpointChanged += OnBreakpointChanged;

        lock (_stateGate)
            _state = DapSessionState.Launched;
        await connection.SendResponseAsync(request, true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await connection.SendEventAsync("thread", new { reason = "started", threadId = 1 },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ConfigurationDoneAsync(DapRequest request, CancellationToken cancellationToken)
    {
        RequireState(DapSessionState.Launched, "configurationDone");
        DebuggeeSession debuggee = RequireDebuggee();
        Task<DebuggeeExit> execution = debuggee.Start();
        lock (_stateGate)
            _state = DapSessionState.Configured;
        _ = ObserveDebuggeeExitAsync(execution);
        await connection.SendResponseAsync(request, true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SetBreakpointsAsync(DapRequest request, CancellationToken cancellationToken)
    {
        RequireAtLeastInitialized("setBreakpoints");
        if (request.Arguments.ValueKind != JsonValueKind.Object
            || !request.Arguments.TryGetProperty("source", out JsonElement source)
            || source.ValueKind != JsonValueKind.Object)
            throw new DapRequestException("setBreakpoints requires a source object.");
        string path = source.RequiredString("path");
        var requested = new List<(int Line, int Column)>();
        if (request.Arguments.TryGetProperty("breakpoints", out JsonElement breakpoints)
            && breakpoints.ValueKind == JsonValueKind.Array)
        {
            if (breakpoints.GetArrayLength() > MaximumBreakpointsPerSource)
                throw new DapRequestException(
                    $"A source may have at most {MaximumBreakpointsPerSource} breakpoints.");
            foreach (JsonElement breakpoint in breakpoints.EnumerateArray())
            {
                int line = FromClientLine(breakpoint.RequiredInt32("line"));
                int column = breakpoint.ValueKind == JsonValueKind.Object
                    && breakpoint.TryGetProperty("column", out JsonElement value)
                    && value.TryGetInt32(out int parsed)
                        ? FromClientColumn(parsed)
                        : 1;
                requested.Add((line, column));
            }
        }
        IReadOnlyList<DebugBreakpointBinding> bindings = _debugHost.SetBreakpoints(path, requested);
        await connection.SendResponseAsync(request, true, new
        {
            breakpoints = bindings.Select(binding => new
            {
                id = binding.Id,
                verified = binding.Verified,
                line = binding.Line is int line ? ToClientLine(line) : (int?)null,
                column = binding.Column is int column ? ToClientColumn(column) : (int?)null,
                message = binding.Message,
                source = new { name = Path.GetFileName(binding.SourcePath), path = binding.SourcePath },
            }).ToArray(),
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private Task SetExceptionBreakpointsAsync(DapRequest request, CancellationToken cancellationToken)
    {
        RequireAtLeastInitialized("setExceptionBreakpoints");
        HashSet<string> filters = request.Arguments.OptionalStringArray("filters")
            .ToHashSet(StringComparer.Ordinal);
        _breakOnCaughtException = filters.Contains("caught");
        _breakOnUncaughtException = filters.Contains("uncaught");
        _breakOnUnhandledRejection = filters.Contains("unhandledRejection");
        _debugHost.ConfigureExceptionFilters(
            _breakOnCaughtException, _breakOnUncaughtException, _breakOnUnhandledRejection);
        return connection.SendResponseAsync(request, true, new
        {
            breakpoints = new object[]
            {
                new { verified = true, message = "Exception filters applied." },
            },
        },
            cancellationToken: cancellationToken);
    }

    private Task ThreadsAsync(DapRequest request, CancellationToken cancellationToken)
    {
        RequireAtLeastInitialized("threads");
        return connection.SendResponseAsync(request, true, new
        {
            threads = _debugHost.Threads.Select(thread => new
            {
                id = thread.Id,
                name = thread.Name,
            }).ToArray(),
        }, cancellationToken: cancellationToken);
    }

    private Task StackTraceAsync(DapRequest request, CancellationToken cancellationToken)
    {
        int threadId = request.Arguments.RequiredInt32("threadId");
        (InterpreterDebugThreadInfo thread, DebugStopSnapshot stop) = RequireStop(threadId);
        ResetStopHandles(_debugHost.CurrentStopEpoch);
        int start = Math.Max(0, TryGetInt(request.Arguments, "startFrame") ?? 0);
        int levels = Math.Clamp(TryGetInt(request.Arguments, "levels") ?? stop.Frames.Count, 0, 1_000);
        object[] frames = stop.Frames.Select((frame, index) => (frame, index))
            .Skip(start).Take(levels).Select(item => new
            {
                id = GetFrameHandle(thread.Id, stop, item.index, item.frame),
                name = item.frame.Name,
                source = SourceDescriptor(item.frame.Location.Document),
                line = ToClientLine(item.frame.Location.Line),
                column = ToClientColumn(item.frame.Location.Column),
                endLine = ToClientLine(item.frame.Location.EndLine),
                endColumn = ToClientColumn(item.frame.Location.EndColumn),
                presentationHint = item.frame.IsAsyncOrigin ? "subtle" : "normal",
            }).Cast<object>().ToArray();
        return connection.SendResponseAsync(request, true, new
        {
            stackFrames = frames,
            totalFrames = stop.Frames.Count,
        }, cancellationToken: cancellationToken);
    }

    private Task ScopesAsync(DapRequest request, CancellationToken cancellationToken)
    {
        int frameId = request.Arguments.RequiredInt32("frameId");
        DapFrameHandle handle = RequireFrame(frameId);
        (_, DebugStopSnapshot stop) = RequireStop(handle.ThreadId);
        if (stop.Generation != handle.ControllerGeneration)
            throw new DapRequestException("Stack frame is stale or invalid.");
        DebugStackFrame frame = handle.Frame;
        ResetStopHandles(_debugHost.CurrentStopEpoch);

        var scopes = new List<object>();
        RuntimeEnvironment? environment = frame.Environment;
        int depth = 0;
        while (environment is not null)
        {
            string[] allNames = environment.Names.OrderBy(name => name, StringComparer.Ordinal).ToArray();
            if (depth == 0)
            {
                var argumentNames = new HashSet<string>(
                    frame.ArgumentNames?.Where(name => allNames.Contains(name, StringComparer.Ordinal))
                        ?? [],
                    StringComparer.Ordinal);
                if (allNames.Contains("arguments", StringComparer.Ordinal))
                    argumentNames.Add("arguments");
                if (argumentNames.Count != 0)
                {
                    int argumentsReference = _handles.Add(new DebugScopeHandle(environment, argumentNames));
                    scopes.Add(new
                    {
                        name = "Arguments",
                        presentationHint = "arguments",
                        variablesReference = argumentsReference,
                        expensive = false,
                        namedVariables = argumentNames.Count,
                    });
                    allNames = allNames.Where(name => !argumentNames.Contains(name)).ToArray();
                }
            }

            string scopeName = depth switch
            {
                0 when frame.Name.StartsWith("<module:", StringComparison.Ordinal) => "Module",
                0 => "Locals",
                _ when environment.Enclosing is null => "Global",
                1 => "Closure",
                _ => $"Closure {depth}",
            };
            int reference = _handles.Add(new DebugScopeHandle(
                environment, new HashSet<string>(allNames, StringComparer.Ordinal)));
            scopes.Add(new
            {
                name = scopeName,
                presentationHint = depth == 0 ? "locals" : null,
                variablesReference = reference,
                expensive = false,
                namedVariables = allNames.Length,
            });
            environment = environment.Enclosing;
            depth++;
        }

        return connection.SendResponseAsync(request, true, new { scopes = scopes.ToArray() },
            cancellationToken: cancellationToken);
    }

    private Task VariablesAsync(DapRequest request, CancellationToken cancellationToken)
    {
        RequireAnyStop();
        int reference = request.Arguments.RequiredInt32("variablesReference");
        object value = _handles.Get<object>(reference);
        int start = TryGetInt(request.Arguments, "start") ?? 0;
        int? count = TryGetInt(request.Arguments, "count");
        IReadOnlyList<DebugVariableValue> values = value is DebugScopeHandle scope
            ? DebugValueInspector.EnumerateScope(scope).Skip(Math.Max(0, start)).Take(count ?? 1_000).ToArray()
            : DebugValueInspector.EnumerateChildren(value, start, count);

        return connection.SendResponseAsync(request, true, new
        {
            variables = values.Select(ToDapVariable).ToArray(),
        }, cancellationToken: cancellationToken);
    }

    private async Task EvaluateAsync(DapRequest request, CancellationToken cancellationToken)
    {
        string expression = request.Arguments.RequiredString("expression");
        string context = request.Arguments.OptionalString("context") ?? "watch";
        int? frameId = TryGetInt(request.Arguments, "frameId");
        InterpreterDebugThreadInfo thread;
        DebugStopSnapshot stop;
        DebugStackFrame frame;
        if (frameId is int requestedFrame)
        {
            DapFrameHandle handle = RequireFrame(requestedFrame);
            (thread, stop) = RequireStop(handle.ThreadId);
            if (stop.Generation != handle.ControllerGeneration)
                throw new DapRequestException("Stack frame is stale or invalid.");
            frame = handle.Frame;
        }
        else
        {
            thread = _debugHost.Threads.FirstOrDefault(candidate => candidate.CurrentStop is not null)
                ?? throw new DapRequestException("No interpreter thread is stopped.");
            stop = thread.CurrentStop!;
            frame = stop.Frames[0];
        }

        object? result = await thread.Controller.InvokeWhileStoppedAsync(
            interpreter => interpreter.EvaluateDebuggerExpression(
                expression, frame.Environment, context != "hover", cancellationToken),
            cancellationToken).ConfigureAwait(false);
        DebugVariableValue described = DebugValueInspector.Describe("result", result, expression);
        int variablesReference = described.ExpandableValue is null
            ? 0
            : _handles.Add(described.ExpandableValue);
        await connection.SendResponseAsync(request, true, new
        {
            result = described.Value,
            type = described.Type,
            variablesReference,
            namedVariables = described.NamedVariables,
            indexedVariables = described.IndexedVariables,
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private Task ExceptionInfoAsync(DapRequest request, CancellationToken cancellationToken)
    {
        int threadId = request.Arguments.RequiredInt32("threadId");
        (_, DebugStopSnapshot stop) = RequireStop(threadId);
        if (stop.Reason != DebugStopReason.Exception)
            throw new DapRequestException("The current stop is not an exception stop.");
        DebugVariableValue exception = DebugValueInspector.Describe("exception", stop.Exception);
        return connection.SendResponseAsync(request, true, new
        {
            exceptionId = exception.Type,
            description = exception.Value,
            breakMode = stop.IsUnhandledException ? "unhandled" : "always",
            details = new { message = exception.Value, typeName = exception.Type },
        }, cancellationToken: cancellationToken);
    }

    private Task ContinueAsync(
        DapRequest request,
        DebugStepKind step,
        CancellationToken cancellationToken)
    {
        RequireConfigured(request.Command);
        int threadId = request.Arguments.RequiredInt32("threadId");
        try { _debugHost.Continue(threadId, step); }
        catch (KeyNotFoundException exception) { throw new DapRequestException(exception.Message); }
        catch (InvalidOperationException exception) { throw new DapRequestException(exception.Message); }
        return connection.SendResponseAsync(request, true, new { allThreadsContinued = true },
            cancellationToken: cancellationToken);
    }

    private Task PauseAsync(DapRequest request, CancellationToken cancellationToken)
    {
        RequireConfigured("pause");
        int threadId = request.Arguments.RequiredInt32("threadId");
        try { _debugHost.RequestPause(threadId); }
        catch (KeyNotFoundException exception) { throw new DapRequestException(exception.Message); }
        return connection.SendResponseAsync(request, true, cancellationToken: cancellationToken);
    }

    private Task LoadedSourcesAsync(DapRequest request, CancellationToken cancellationToken)
    {
        _ = RequireDebuggee();
        return connection.SendResponseAsync(request, true, new
        {
            sources = _debugHost.Sources.Select(source => SourceDescriptor(source.Document)).ToArray(),
        }, cancellationToken: cancellationToken);
    }

    private Task SourceAsync(DapRequest request, CancellationToken cancellationToken)
    {
        int reference = request.Arguments.RequiredInt32("sourceReference");
        if (!_debugHost.TryGetSource(reference, out InterpreterDebugSourceInfo? source)
            || source is null || !source.Document.IsVirtual)
            throw new DapRequestException("Source reference is stale or invalid.");
        SourceDocument document = source.Document;
        return connection.SendResponseAsync(request, true, new
        {
            content = document.Text,
            mimeType = document.Path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
                ? "text/typescript-jsx"
                : "text/typescript",
        }, cancellationToken: cancellationToken);
    }

    private Task ModulesAsync(DapRequest request, CancellationToken cancellationToken)
    {
        _ = RequireDebuggee();
        IReadOnlyList<InterpreterDebugSourceInfo> sources = _debugHost.Sources;
        return connection.SendResponseAsync(request, true, new
        {
            modules = sources.Select(source => ModuleDescriptor(source.Document, source.Id)).ToArray(),
            totalModules = sources.Count,
        }, cancellationToken: cancellationToken);
    }

    private async Task TerminateAsync(DapRequest request, CancellationToken cancellationToken)
    {
        RequireDebuggee().Terminate();
        await connection.SendResponseAsync(request, true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task DisconnectAsync(DapRequest request, CancellationToken cancellationToken)
    {
        await DisconnectDebuggeeAsync().ConfigureAwait(false);
        lock (_stateGate)
            _state = DapSessionState.Terminated;
        await connection.SendResponseAsync(request, true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _sessionCancellation.Cancel();
    }

    private async Task HandleCancelAsync(DapRequest request)
    {
        int? requestId = TryGetInt(request.Arguments, "requestId");
        if (requestId is int id && _requestCancellation.TryGetValue(id, out CancellationTokenSource? cts))
            cts.Cancel();
        await connection.SendResponseAsync(request, true).ConfigureAwait(false);
    }

    private void OnStopped(InterpreterDebugStopEvent stopped)
    {
        ResetStopHandles(stopped.Epoch);
        DebugStopSnapshot stop = stopped.Stop;
        string reason = stop.Reason switch
        {
            DebugStopReason.Entry => "entry",
            DebugStopReason.Breakpoint => "breakpoint",
            DebugStopReason.Step => "step",
            DebugStopReason.Pause => "pause",
            DebugStopReason.Exception => "exception",
            _ => "pause",
        };
        Observe(connection.SendEventAsync("stopped", new
        {
            reason,
            description = stop.Description,
            threadId = stopped.ThreadId,
            allThreadsStopped = stopped.AllThreadsStopped,
            text = stop.Exception is null ? null : DebugValueInspector.Describe("exception", stop.Exception).Value,
        }));
    }

    private void OnContinued(InterpreterDebugContinueEvent continued)
    {
        ClearStopHandles();
        Observe(connection.SendEventAsync("continued", new
        {
            threadId = continued.ThreadId,
            allThreadsContinued = continued.AllThreadsContinued,
        }));
    }

    private void OnThreadStarted(InterpreterDebugThreadInfo thread) =>
        Observe(connection.SendEventAsync("thread", new
        {
            reason = "started",
            threadId = thread.Id,
        }));

    private void OnThreadExited(int threadId) =>
        Observe(connection.SendEventAsync("thread", new
        {
            reason = "exited",
            threadId,
        }));

    private void OnSourceChanged(InterpreterDebugSourceEvent change)
    {
        try
        {
            Observe(connection.SendEventAsync("loadedSource", new
            {
                reason = change.Reason == InterpreterDebugSourceChangeReason.New ? "new" : "removed",
                source = SourceDescriptor(change.Source.Document, change.Source.Id),
            }));
            Observe(connection.SendEventAsync("module", new
            {
                reason = change.Reason == InterpreterDebugSourceChangeReason.New ? "new" : "removed",
                module = ModuleDescriptor(change.Source.Document, change.Source.Id),
            }));
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void OnBreakpointChanged(DebugBreakpointBinding binding) =>
        Observe(connection.SendEventAsync("breakpoint", new
        {
            reason = "changed",
            breakpoint = ToDapBreakpoint(binding),
        }));

    private void EmitOutput(string category, string output) =>
        Observe(connection.SendEventAsync("output", new { category, output }));

    private async Task ObserveDebuggeeExitAsync(Task<DebuggeeExit> execution)
    {
        DebuggeeExit exit = await execution.ConfigureAwait(false);
        await connection.SendEventAsync("exited", new { exitCode = exit.ExitCode }).ConfigureAwait(false);
        await connection.SendEventAsync("terminated").ConfigureAwait(false);
        lock (_stateGate)
            _state = DapSessionState.Terminated;
    }

    private object ToDapVariable(DebugVariableValue value)
    {
        int reference = value.ExpandableValue is null ? 0 : _handles.Add(value.ExpandableValue);
        return new
        {
            name = value.Name,
            value = value.Value,
            type = value.Type,
            variablesReference = reference,
            namedVariables = value.NamedVariables,
            indexedVariables = value.IndexedVariables,
            evaluateName = value.EvaluateName,
        };
    }

    private object ToDapBreakpoint(DebugBreakpointBinding binding) => new
    {
        id = binding.Id,
        verified = binding.Verified,
        line = binding.Line is int line ? ToClientLine(line) : (int?)null,
        column = binding.Column is int column ? ToClientColumn(column) : (int?)null,
        message = binding.Message,
        source = new { name = Path.GetFileName(binding.SourcePath), path = binding.SourcePath },
    };

    private object SourceDescriptor(SourceDocument document, int? knownSourceId = null)
    {
        if (!document.IsVirtual)
            return new { name = Path.GetFileName(document.Path), path = document.Path, sourceReference = 0 };
        int sourceId = knownSourceId ?? _debugHost.GetSource(document).Id;
        return new { name = Path.GetFileName(document.Path), path = (string?)null, sourceReference = sourceId };
    }

    private static object ModuleDescriptor(SourceDocument document, int id) => new
    {
        id,
        name = Path.GetFileName(document.Path),
        path = document.Path,
        isOptimized = false,
        isUserCode = !document.IsVirtual,
        version = typeof(DapAdapterSession).Assembly.GetName().Version?.ToString(),
    };

    private static IReadOnlyDictionary<string, string?> ParseEnvironment(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty("env", out JsonElement environment)
            || environment.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return new Dictionary<string, string?>();
        if (environment.ValueKind != JsonValueKind.Object)
            throw new DapRequestException("'env' must be an object of string or null values.");
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in environment.EnumerateObject())
        {
            result[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Null => null,
                _ => throw new DapRequestException("Environment values must be strings or null."),
            };
        }
        return result;
    }

    private async Task DisconnectDebuggeeAsync()
    {
        DebuggeeSession? debuggee = Interlocked.Exchange(ref _debuggee, null);
        if (debuggee is null)
            return;
        _debugHost.Stopped -= OnStopped;
        _debugHost.Continued -= OnContinued;
        _debugHost.ThreadStarted -= OnThreadStarted;
        _debugHost.ThreadExited -= OnThreadExited;
        _debugHost.SourceChanged -= OnSourceChanged;
        _debugHost.BreakpointChanged -= OnBreakpointChanged;
        await debuggee.DisposeAsync().ConfigureAwait(false);
    }

    private DebuggeeSession RequireDebuggee() =>
        _debuggee ?? throw new DapRequestException("No debuggee has been launched.");

    private (InterpreterDebugThreadInfo Thread, DebugStopSnapshot Stop) RequireStop(int threadId)
    {
        _ = RequireDebuggee();
        InterpreterDebugThreadInfo thread;
        try { thread = _debugHost.GetThread(threadId); }
        catch (KeyNotFoundException exception) { throw new DapRequestException(exception.Message); }
        return thread.CurrentStop is DebugStopSnapshot stop
            ? (thread, stop)
            : throw new DapRequestException("The selected interpreter thread is not stopped.");
    }

    private void RequireAnyStop()
    {
        _ = RequireDebuggee();
        if (_debugHost.CurrentStopEpoch == 0
            || !_debugHost.Threads.Any(thread => thread.CurrentStop is not null))
            throw new DapRequestException("No interpreter thread is stopped.");
    }

    private DapFrameHandle RequireFrame(int frameId)
    {
        lock (_stateGate)
        {
            return _frameHandles.TryGetValue(frameId, out DapFrameHandle? frame)
                ? frame
                : throw new DapRequestException("Stack frame is stale or invalid.");
        }
    }

    private int GetFrameHandle(
        int threadId,
        DebugStopSnapshot stop,
        int frameIndex,
        DebugStackFrame frame)
    {
        var key = new DapFrameKey(threadId, stop.Generation, frameIndex);
        lock (_stateGate)
        {
            if (_reverseFrameHandles.TryGetValue(key, out int existing))
                return existing;
            int id = checked(++_nextFrameHandle);
            _reverseFrameHandles.Add(key, id);
            _frameHandles.Add(id, new DapFrameHandle(
                threadId, stop.Generation, frameIndex, frame));
            return id;
        }
    }

    private void ResetStopHandles(int epoch)
    {
        if (epoch <= 0)
            return;
        lock (_stateGate)
        {
            if (_handles.Generation == epoch)
                return;
            _handles.Reset(epoch);
            _frameHandles.Clear();
            _reverseFrameHandles.Clear();
        }
    }

    private void ClearStopHandles()
    {
        lock (_stateGate)
        {
            _handles.Clear();
            _frameHandles.Clear();
            _reverseFrameHandles.Clear();
        }
    }

    private void Transition(DapSessionState expected, DapSessionState next, string command)
    {
        lock (_stateGate)
        {
            if (_state != expected)
                throw InvalidOrder(command, expected);
            _state = next;
        }
    }

    private void RequireState(DapSessionState expected, string command)
    {
        lock (_stateGate)
        {
            if (_state != expected)
                throw InvalidOrder(command, expected);
        }
    }

    private void RequireAtLeastInitialized(string command)
    {
        lock (_stateGate)
        {
            if (_state is DapSessionState.Created or DapSessionState.Terminated)
                throw new DapRequestException($"'{command}' is not valid in state {_state}.", 1004);
        }
    }

    private void RequireConfigured(string command) => RequireState(DapSessionState.Configured, command);

    private DapRequestException InvalidOrder(string command, DapSessionState expected) =>
        new($"'{command}' requires state {expected}; current state is {_state}.", 1004);

    private int FromClientLine(int line) => _clientLinesStartAtOne ? line : line + 1;
    private int FromClientColumn(int column) => _clientColumnsStartAtOne ? column : column + 1;
    private int ToClientLine(int line) => _clientLinesStartAtOne ? line : line - 1;
    private int ToClientColumn(int column) => _clientColumnsStartAtOne ? column : column - 1;

    private static int? TryGetInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out JsonElement value)
        && value.TryGetInt32(out int result)
            ? result
            : null;

    private static string? ResolveOptionalPath(string? path, string baseDirectory) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path, baseDirectory);

    internal static string Redact(string message)
    {
        foreach (System.Collections.DictionaryEntry variable in System.Environment.GetEnvironmentVariables())
        {
            if (variable.Value is string value && value.Length >= 8)
                message = message.Replace(value, "<redacted>", StringComparison.Ordinal);
        }
        return message;
    }

    private static void Observe(Task task) => _ = task.ContinueWith(
        static completed => _ = completed.Exception,
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _sessionCancellation.Cancel();
        await DisconnectDebuggeeAsync().ConfigureAwait(false);
        _debugHost.Dispose();
        _sessionCancellation.Dispose();
    }

    private readonly record struct DapFrameKey(
        int ThreadId,
        int ControllerGeneration,
        int FrameIndex);

    private sealed record DapFrameHandle(
        int ThreadId,
        int ControllerGeneration,
        int FrameIndex,
        DebugStackFrame Frame);
}
