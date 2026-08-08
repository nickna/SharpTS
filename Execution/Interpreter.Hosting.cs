using SharpTS.Modules;
using SharpTS.Modules.Stdlib;
using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.DotNet;
using SharpTS.Runtime.Types;
using SharpTS.TypeSystem;

namespace SharpTS.Execution;

public partial class Interpreter
{
    private int? _hostedOwnerThreadId;
    private volatile bool _hostedAcceptingWork;
    private Action? _hostedWorkAvailable;
    private Action? _hostedTimerChanged;
    private Action<Exception>? _hostedUnhandledError;
    private Action<int>? _hostedProcessExit;
    private RuntimeEnvironment? _hostedScriptEnvironment;
    private int _hostedExitCode;

    internal void ConfigureHosted(
        IReadOnlyList<ParsedModule> modules,
        ModuleResolver resolver,
        TypeMap typeMap,
        DecoratorMode decoratorMode,
        Action workAvailable,
        Action timerChanged,
        Action<Exception> unhandledError,
        Action<int> processExit)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(typeMap);
        ArgumentNullException.ThrowIfNull(workAvailable);
        ArgumentNullException.ThrowIfNull(timerChanged);
        ArgumentNullException.ThrowIfNull(unhandledError);
        ArgumentNullException.ThrowIfNull(processExit);
        ThrowIfDisposedForHostedOperation();

        int ownerThreadId = System.Environment.CurrentManagedThreadId;
        if (_hostedOwnerThreadId != null)
            throw new InvalidOperationException("Hosted SharpTS interpreter has already been configured.");

        _hostedOwnerThreadId = ownerThreadId;
        _hostedAcceptingWork = true;
        _hostedWorkAvailable = workAvailable;
        _hostedTimerChanged = timerChanged;
        _hostedUnhandledError = unhandledError;
        _hostedProcessExit = processExit;
        _waitForTopLevelPromises = false;
        _typeMap = typeMap;
        _moduleResolver = resolver;
        SetDecoratorMode(decoratorMode);
        EmitProcessLifecycleEvents = true;
        _hostedScriptEnvironment = new RuntimeEnvironment(_environment);

        if (modules.Count > 0)
            EntryModulePath ??= modules[^1].Path;

        // Register ESM instances before any body runs. This makes cyclic import
        // graphs deterministic: imports observe the cycle's currently initialized
        // bindings instead of recursively entering a dependency and deadlocking.
        foreach (ParsedModule module in modules)
        {
            if (!module.IsScript && !module.IsCommonJs)
                _loadedModules.TryAdd(module.Path, new ModuleInstance());
        }

        var variableResolver = new VariableResolver(this);
        foreach (ParsedModule module in modules)
        {
            if (!module.IsBuiltIn)
                variableResolver.Resolve(module.Statements);
        }
    }

    internal async Task ExecuteHostedModuleAsync(ParsedModule module, bool isEntryModule)
    {
        AssertHostedOwnerThread();
        if (!_hostedAcceptingWork)
            return;

        if ((module.IsScript || module.IsCommonJs) && TopLevelAwaitDetector.Contains(module.Statements))
        {
            string kind = module.IsCommonJs ? "CommonJS modules" : "scripts";
            throw new InterpreterException($"Top-level await is not supported in {kind}: '{module.Path}'.");
        }

        if (module.IsScript)
        {
            ExecuteScriptFile(module, _hostedScriptEnvironment!);
            return;
        }

        if (module.IsCommonJs)
        {
            if (isEntryModule)
                ExecuteModule(module);
            return;
        }

        ModuleInstance moduleInstance = _loadedModules[module.Path];
        if (moduleInstance.IsExecuted)
            return;

        if (InitializeHostedSpecialModule(module, moduleInstance))
            return;

        var moduleEnvironment = new RuntimeEnvironment(_environment);
        BindModuleImports(module, moduleEnvironment);

        using (PushModuleContext(moduleEnvironment, module, moduleInstance))
        {
            HoistFunctionDeclarations(module.Statements);
            foreach (Stmt statement in module.Statements)
            {
                ExecutionResult result = statement is Stmt.Export export
                    ? await ExecuteHostedExportAsync(export)
                    : await ExecuteStatementAsync(statement);
                if (result.Type == ExecutionResult.ResultType.Throw)
                    throw new InterpreterException(Stringify(result.Value.ToObject()));
                if (result.IsAbrupt)
                    break;
            }
        }

        moduleInstance.IsExecuted = true;
    }

    internal async Task<int?> ExecuteHostedMainAsync(IReadOnlyList<Stmt> statements)
    {
        AssertHostedOwnerThread();
        Stmt.Function? declaration = statements.OfType<Stmt.Function>().FirstOrDefault(function =>
            function.Name.Lexeme == "main" &&
            function.Body != null &&
            (function.Parameters.Count == 0 ||
                (function.Parameters.Count == 1 && function.Parameters[0].Type == "string[]")) &&
            (function.ReturnType is null or "void" or "number" or "Promise<void>" or "Promise<number>"));
        if (declaration is null ||
            !_environment.TryGet(declaration.Name.Lexeme, out RuntimeValue runtimeValue) ||
            runtimeValue.ToObject() is not ISharpTSCallable callable)
        {
            return null;
        }

        List<object?> arguments = declaration.Parameters.Count == 0
            ? []
            : [ProcessBuiltIns.GetArgv()];
        object? result = callable.CallBoxed(this, arguments);
        if (result is SharpTSPromise promise)
            result = await promise.Task;
        else if (result is Task<object?> task)
            result = await task;
        return result is double exitCode ? (int)exitCode : null;
    }

    private bool InitializeHostedSpecialModule(ParsedModule module, ModuleInstance moduleInstance)
    {
        if (module.IsDotNetModule)
        {
            foreach (var (name, clrType) in module.DotNetExports!)
                moduleInstance.SetExport(name, new DotNetClass(name, clrType));
            moduleInstance.IsExecuted = true;
            return true;
        }

        if (module.IsDotNetExtensionModule)
        {
            moduleInstance.IsExecuted = true;
            return true;
        }

        if (!module.IsBuiltIn)
            return false;

        string? primitiveName = PrimitiveRegistry.GetPrimitiveName(module.Path);
        if (primitiveName != null && PrimitiveModuleValues.HasInterpreterSupport(primitiveName))
        {
            foreach (var (name, value) in PrimitiveModuleValues.GetPrimitiveExports(primitiveName))
                moduleInstance.SetExport(name, value);
            moduleInstance.DefaultExport = moduleInstance.ExportsAsObject();
            moduleInstance.IsExecuted = true;
            return true;
        }

        string? moduleName = BuiltInModuleRegistry.GetModuleName(module.Path);
        if (moduleName != null && BuiltInModuleValues.HasInterpreterSupport(moduleName))
        {
            var exports = BuiltInModuleValues.GetModuleExports(moduleName);
            if (moduleName == "worker_threads" && WorkerThreadsContext is { } worker)
            {
                exports["workerData"] = worker.WorkerData;
                exports["parentPort"] = worker.ParentPort;
                exports["threadId"] = worker.ThreadId;
                exports["isMainThread"] = false;
            }
            foreach (var (name, value) in exports)
                moduleInstance.SetExport(name, value);
            moduleInstance.NamespaceObject = BuiltInModuleValues.TryCreateNamespaceOverride(moduleName, exports);
            moduleInstance.DefaultExport = moduleInstance.ExportsAsObject();
        }
        moduleInstance.IsExecuted = true;
        return true;
    }

    private async Task<ExecutionResult> ExecuteHostedExportAsync(Stmt.Export export)
    {
        if (export.IsTypeOnly)
            return ExecutionResult.Success();

        if (export.ExportAssignment != null)
        {
            object? value = (await EvaluateAsync(export.ExportAssignment)).ToObject();
            if (_currentModule != null)
            {
                _currentModule.HasExportAssignment = true;
                _currentModule.ExportAssignmentValue = value;
            }
            return ExecutionResult.Success();
        }

        if (export.IsDefaultExport)
        {
            if (export.Declaration != null)
            {
                ExecutionResult result = await ExecuteStatementAsync(export.Declaration);
                if (result.IsAbrupt)
                    return result;
                _currentModuleInstance!.DefaultExport = GetDeclaredValue(export.Declaration);
            }
            else if (export.DefaultExpr != null)
            {
                _currentModuleInstance!.DefaultExport = (await EvaluateAsync(export.DefaultExpr)).ToObject();
            }
            return ExecutionResult.Success();
        }

        if (export.Declaration != null)
        {
            ExecutionResult result = await ExecuteStatementAsync(export.Declaration);
            if (result.IsAbrupt)
                return result;
            if (!IsTypeOnlyDeclaration(export.Declaration))
            {
                string name = GetDeclaredName(export.Declaration);
                _currentModuleInstance!.SetExport(name, GetDeclaredValue(export.Declaration));
            }
            return ExecutionResult.Success();
        }

        // Named exports and re-exports contain no locally evaluated expression.
        return ExecuteExport(export);
    }

    internal bool TryExecuteOneHostedMacrotask(bool timerDeadlineElapsed, out bool ranTimer)
    {
        AssertHostedOwnerThread();
        ranTimer = false;
        if (!_hostedAcceptingWork)
            return false;

        if (_callbackQueue.TryTake(out Action? callback, TimeSpan.Zero))
        {
            callback();
            return true;
        }

        VirtualTimer? timer = null;
        lock (_virtualTimersLock)
        {
            while (_virtualTimerQueue.TryPeek(out VirtualTimer? candidate, out var priority))
            {
                if (candidate.IsCancelled)
                {
                    _virtualTimerQueue.Dequeue();
                    continue;
                }

                if (!timerDeadlineElapsed && priority.FireTime > TimerNowMs)
                    break;

                timer = _virtualTimerQueue.Dequeue();
                if (timer.IsInterval && !timer.IsCancelled)
                {
                    timer.FireTimeMs += timer.IntervalMs;
                    _virtualTimerQueue.Enqueue(timer, (timer.FireTimeMs, _timerSequence++));
                }
                break;
            }
            _hasScheduledTimers = _virtualTimerQueue.Count > 0;
        }

        if (timer == null || timer.IsCancelled || _isDisposed)
            return false;
        ranTimer = true;
        timer.Callback();
        return true;
    }

    internal bool HasHostedImmediateWork(bool timerDeadlineElapsed)
    {
        if (!_hostedAcceptingWork || _isDisposed)
            return false;
        if (_callbackQueue.Count != 0)
            return true;
        lock (_microtaskQueueLock)
        {
            if (_microtaskQueue.Count != 0)
                return true;
        }
        lock (_virtualTimersLock)
        {
            while (_virtualTimerQueue.TryPeek(out VirtualTimer? timer, out var priority))
            {
                if (!timer.IsCancelled)
                    return timerDeadlineElapsed || priority.FireTime <= TimerNowMs;
                _virtualTimerQueue.Dequeue();
            }
            _hasScheduledTimers = false;
        }
        return false;
    }

    internal (long FireTime, long Sequence, TimeSpan Delay)? GetHostedNextTimer()
    {
        lock (_virtualTimersLock)
        {
            while (_virtualTimerQueue.TryPeek(out VirtualTimer? timer, out var priority))
            {
                if (timer.IsCancelled)
                {
                    _virtualTimerQueue.Dequeue();
                    continue;
                }
                long milliseconds = Math.Max(0, priority.FireTime - TimerNowMs);
                return (priority.FireTime, priority.Seq, TimeSpan.FromMilliseconds(milliseconds));
            }
            _hasScheduledTimers = false;
            return null;
        }
    }

    internal void RejectHostedWork() => _hostedAcceptingWork = false;

    internal bool IsHostedExecution => _hostedOwnerThreadId != null;

    internal int GetProcessExitCode() =>
        IsHostedExecution ? _hostedExitCode : System.Environment.ExitCode;

    internal void SetProcessExitCode(int exitCode)
    {
        if (IsHostedExecution)
            _hostedExitCode = exitCode;
        else
            System.Environment.ExitCode = exitCode;
    }

    internal Task<object?> QueuePromiseReaction(Func<Task<object?>> reaction)
    {
        // The ordinary console runtime already schedules continuations through
        // its event-loop SynchronizationContext and must retain that path (it
        // also carries AsyncLocalStorage state). Hosted execution has no private
        // SynchronizationContext, so it explicitly models promise reactions as
        // guest microtasks instead.
        if (_hostedOwnerThreadId == null)
            return reaction();

        var completion = new TaskCompletionSource<object?>();
        lock (_microtaskQueueLock)
        {
            _microtaskQueue.Enqueue(() =>
            {
                Task<object?> reactionTask;
                try
                {
                    reactionTask = reaction();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                    return;
                }

                if (reactionTask.IsCompleted)
                {
                    TransferCompletion(reactionTask, completion);
                    return;
                }
                reactionTask.ContinueWith(
                    static (task, state) =>
                    {
                        var target = (TaskCompletionSource<object?>)state!;
                        TransferCompletion(task, target);
                    },
                    completion,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            });
        }
        WakeEventLoop();
        return completion.Task;
    }

    private static void TransferCompletion(
        Task<object?> source,
        TaskCompletionSource<object?> destination)
    {
        if (source.IsCanceled)
            destination.TrySetCanceled();
        else if (source.Exception != null)
            destination.TrySetException(source.Exception.InnerException ?? source.Exception);
        else
            destination.TrySetResult(source.Result);
    }

    internal void CancelTimer(VirtualTimer timer)
    {
        timer.IsCancelled = true;
        _hostedTimerChanged?.Invoke();
    }

    internal void BeginHostedShutdown()
    {
        _hostedAcceptingWork = false;
        try { _shutdownCts.Cancel(); }
        catch (ObjectDisposedException) { }

        while (_callbackQueue.TryTake(out _, TimeSpan.Zero)) { }
        while (_pendingTimers.TryTake(out SharpTSTimeout? timer))
            timer.Cancel();
        lock (_virtualTimersLock)
        {
            _virtualTimerQueue.Clear();
            _hasScheduledTimers = false;
        }
    }

    internal void EmitHostedBeforeExit(int exitCode)
    {
        AssertHostedOwnerThread();
        if (!_exitEventEmitted)
            SharpTSProcess.Instance.EmitWith(this, "beforeExit", (double)exitCode);
    }

    internal void EmitHostedExit(int exitCode)
    {
        AssertHostedOwnerThread();
        if (_exitEventEmitted)
            return;
        _exitEventEmitted = true;
        ProcessBuiltIns.EmitExitEvent(this, exitCode);
    }

    internal void RequestProcessExit(int exitCode)
    {
        if (_hostedProcessExit == null)
        {
            ProcessControl.Exit(exitCode);
            return;
        }

        _exitEventEmitted = true; // ProcessBuiltIns emitted it synchronously first.
        _hostedProcessExit(exitCode);
    }

    private void AssertHostedOwnerThread()
    {
        ThrowIfDisposedForHostedOperation();
        int current = System.Environment.CurrentManagedThreadId;
        if (_hostedOwnerThreadId is not int owner)
            throw new InvalidOperationException("Hosted SharpTS interpreter has not been configured.");
        if (owner != current)
        {
            throw new InvalidOperationException(
                $"Hosted SharpTS interpreter belongs to managed thread {owner}; thread {current} cannot run guest work.");
        }
    }

    private void ThrowIfDisposedForHostedOperation()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(Interpreter));
    }
}
