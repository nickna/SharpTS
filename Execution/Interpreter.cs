using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.EventLoop;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using SharpTS.TypeSystem;
using System.Collections.Frozen;
using System.Threading;

namespace SharpTS.Execution;

/// <summary>
/// Tree-walking interpreter that executes the AST.
/// </summary>
/// <remarks>
/// One of two execution paths after type checking (the other being <see cref="ILCompiler"/>).
/// Traverses the AST recursively, evaluating expressions and executing statements. Uses
/// <see cref="RuntimeEnvironment"/> for variable scopes and <see cref="ExecutionResult"/>
/// for lightweight flow control (return, break, continue, throw). Runtime values include
/// <see cref="SharpTSClass"/>, <see cref="SharpTSInstance"/>, <see cref="SharpTSFunction"/>,
/// <see cref="SharpTSArray"/>, and <see cref="SharpTSObject"/>.
///
/// This class is split across multiple partial class files:
/// <list type="bullet">
///   <item><description>Interpreter.cs - Core infrastructure and statement dispatch</description></item>
///   <item><description>Interpreter.Statements.cs - Statement execution helpers (block, switch, try/catch, loops)</description></item>
///   <item><description>Interpreter.Expressions.cs - Expression dispatch and basic evaluators</description></item>
///   <item><description>Interpreter.Properties.cs - Property/member access (Get, Set, New, This)</description></item>
///   <item><description>Interpreter.Calls.cs - Function calls and binary/logical operators</description></item>
///   <item><description>Interpreter.Operators.cs - Compound assignment, increment, and utility methods</description></item>
/// </list>
/// </remarks>
/// <seealso cref="RuntimeEnvironment"/>
/// <seealso cref="ILCompiler"/>
public partial class Interpreter : IDisposable
{
    /// <summary>
    /// Static registry containing handlers for all AST node types.
    /// Initialized once at startup and validated for exhaustiveness.
    /// </summary>
    private static readonly NodeRegistry<Interpreter, object?, ExecutionResult> _registry =
        InterpreterRegistry.Create();

    /// <summary>
    /// Frozen dictionary of global constants and built-in singletons for fast lookup.
    /// Combines global constants (NaN, Infinity, undefined) with built-in namespaces
    /// (Math, JSON, Object, etc.) into a single lookup to minimize dictionary operations.
    /// </summary>
    private static readonly FrozenDictionary<string, object> _globalConstants = CreateGlobalsLookup();

    private static FrozenDictionary<string, object> CreateGlobalsLookup()
    {
        var globals = new Dictionary<string, object>
        {
            [BuiltInNames.NaN] = double.NaN,
            [BuiltInNames.Infinity] = double.PositiveInfinity,
            [BuiltInNames.Undefined] = Runtime.Types.SharpTSUndefined.Instance,
            [BuiltInNames.Fetch] = Runtime.BuiltIns.FetchBuiltIns.FetchMethod,

            // SharedArrayBuffer constructor
            [BuiltInNames.SharedArrayBuffer] = WorkerBuiltIns.SharedArrayBufferConstructor,
        };

        // Add TypedArray constructors using centralized names
        foreach (var typedArrayName in BuiltInNames.TypedArrayNames)
        {
            globals[typedArrayName] = WorkerBuiltIns.GetTypedArrayConstructor(typedArrayName);
        }

        // Add built-in singletons (Math, JSON, Object, etc.)
        // These are namespaces that resolve to singleton instances when accessed as variables
        string[] singletonNames =
        [
            BuiltInNames.Math, BuiltInNames.JSON, BuiltInNames.Object, BuiltInNames.Array,
            BuiltInNames.Number, BuiltInNames.String, BuiltInNames.Boolean, BuiltInNames.Symbol,
            BuiltInNames.Console, BuiltInNames.Process, BuiltInNames.GlobalThis,
            BuiltInNames.Reflect, BuiltInNames.Promise, BuiltInNames.Atomics
        ];
        foreach (var name in singletonNames)
        {
            var singleton = BuiltInRegistry.Instance.GetSingleton(name);
            if (singleton != null)
            {
                globals[name] = singleton;
            }
        }

        return globals.ToFrozenDictionary();
    }

    private RuntimeEnvironment _environment = new();
    private readonly Dictionary<Expr, int> _locals = []; // Depth for resolved variables
    private TypeMap? _typeMap;

    // Evaluation contexts for unified sync/async handling
    private readonly SyncEvaluationContext _syncContext;
    private readonly AsyncEvaluationContext _asyncContext;

    /// <summary>
    /// Gets the sync evaluation context for use in unified core methods.
    /// </summary>
    internal SyncEvaluationContext SyncContext => _syncContext;

    /// <summary>
    /// Gets the async evaluation context for use in unified core methods.
    /// </summary>
    internal AsyncEvaluationContext AsyncContext => _asyncContext;

    /// <summary>
    /// Initializes a new instance of the Interpreter with evaluation contexts.
    /// </summary>
    public Interpreter()
    {
        _syncContext = new SyncEvaluationContext(this);
        _asyncContext = new AsyncEvaluationContext(this);
    }

    // Module support
    private readonly Dictionary<string, ModuleInstance> _loadedModules = [];
    private ModuleResolver? _moduleResolver;
    private ParsedModule? _currentModule;
    private ModuleInstance? _currentModuleInstance;

    // Flag to indicate interpreter has been disposed - timer callbacks should not execute
    private volatile bool _isDisposed;

    // Track all pending timers for cleanup on disposal
    private readonly System.Collections.Concurrent.ConcurrentBag<Runtime.Types.SharpTSTimeout> _pendingTimers = new();

    // Virtual timer system - timers are checked and executed on the main thread during loop iterations.
    // This avoids thread scheduling issues on macOS where background threads may not get CPU time.
    // Uses PriorityQueue for O(log n) insert and O(log n) extraction of due timers.
    private readonly PriorityQueue<VirtualTimer, long> _virtualTimerQueue = new();
    private readonly object _virtualTimersLock = new();
    // Volatile flag for O(1) "queue empty" check without acquiring lock
    private volatile bool _hasScheduledTimers;

    // Active handles counter - keeps the event loop alive while there are active operations
    private int _activeHandles;
    private readonly object _activeHandlesLock = new();

    // Event loop infrastructure - BlockingCollection for efficient waiting (no polling)
    // SynchronizationContext routes async/await continuations back to the main thread
    private readonly System.Collections.Concurrent.BlockingCollection<Action> _callbackQueue = new();
    private InterpreterSynchronizationContext? _eventLoopSyncContext;

    /// <summary>
    /// Represents a scheduled timer callback that will be executed by the main thread.
    /// </summary>
    internal class VirtualTimer
    {
        public long FireTimeMs { get; set; }
        public int IntervalMs { get; }
        public Action Callback { get; }
        public bool IsCancelled { get; set; }
        public bool IsExpired { get; set; }  // For one-shot timers that have fired
        public bool IsInterval { get; }

        public VirtualTimer(long fireTimeMs, int intervalMs, Action callback, bool isInterval)
        {
            FireTimeMs = fireTimeMs;
            IntervalMs = intervalMs;
            Callback = callback;
            IsInterval = isInterval;
        }
    }

    /// <summary>
    /// Custom SynchronizationContext that routes async/await continuations back to the event loop.
    /// Ensures all user callbacks execute on the main interpreter thread (Node.js semantics).
    /// </summary>
    private sealed class InterpreterSynchronizationContext : SynchronizationContext
    {
        private readonly Action<Action> _enqueue;

        public InterpreterSynchronizationContext(Action<Action> enqueue)
            => _enqueue = enqueue;

        /// <summary>
        /// Posts a callback to be executed asynchronously on the event loop thread.
        /// Called by .NET when an async operation completes.
        /// </summary>
        public override void Post(SendOrPostCallback d, object? state)
            => _enqueue(() => d(state));

        /// <summary>
        /// Sends a callback to be executed synchronously. Simplified to use Post.
        /// </summary>
        public override void Send(SendOrPostCallback d, object? state)
            => Post(d, state);

        /// <summary>
        /// Creates a copy of this SynchronizationContext.
        /// </summary>
        public override SynchronizationContext CreateCopy() => this;
    }

    /// <summary>
    /// Gets whether this interpreter has been disposed.
    /// Timer callbacks check this before executing to prevent race conditions.
    /// </summary>
    internal bool IsDisposed => _isDisposed;

    internal RuntimeEnvironment Environment => _environment;
    internal TypeMap? TypeMap => _typeMap;
    internal void SetEnvironment(RuntimeEnvironment env) => _environment = env;

    /// <summary>
    /// Registers a timer for tracking. Called by TimerBuiltIns when creating setTimeout/setInterval.
    /// Enables proper cleanup of all pending timers when the interpreter is disposed.
    /// </summary>
    /// <param name="timer">The timer to track.</param>
    internal void RegisterTimer(Runtime.Types.SharpTSTimeout timer)
    {
        _pendingTimers.Add(timer);
    }

    /// <summary>
    /// Schedules a virtual timer to be executed on the main thread.
    /// Returns the VirtualTimer so it can be cancelled later.
    /// </summary>
    internal VirtualTimer ScheduleTimer(int delayMs, int intervalMs, Action callback, bool isInterval)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var fireTime = now + delayMs;
        var timer = new VirtualTimer(fireTime, intervalMs, callback, isInterval);
        lock (_virtualTimersLock)
        {
            _virtualTimerQueue.Enqueue(timer, fireTime);
            _hasScheduledTimers = true;
        }
        // Wake the event loop if the timer fires soon (within 10ms)
        // This ensures immediate timers (setTimeout(fn, 0)) are processed promptly
        if (delayMs <= 10)
        {
            WakeEventLoop();
        }
        return timer;
    }

    /// <summary>
    /// Wakes the event loop by enqueueing a no-op action.
    /// Used when a timer or other operation needs prompt processing.
    /// </summary>
    private void WakeEventLoop()
    {
        if (!_isDisposed && !_callbackQueue.IsAddingCompleted)
        {
            try { _callbackQueue.Add(() => { }); }
            catch (InvalidOperationException) { /* queue completed */ }
        }
    }

    /// <summary>
    /// Enqueues a callback to be executed on the main event loop thread.
    /// Thread-safe - can be called from any thread (HTTP accept loop, async I/O, etc).
    /// </summary>
    /// <param name="action">The callback action to execute on the main thread.</param>
    internal void EnqueueCallback(Action action)
    {
        if (!_isDisposed && !_callbackQueue.IsAddingCompleted)
        {
            try { _callbackQueue.Add(action); }
            catch (InvalidOperationException) { /* queue completed */ }
        }
    }

    /// <summary>
    /// Calculates the timeout until the next timer fires.
    /// Used by the event loop to efficiently wait without polling.
    /// </summary>
    /// <returns>TimeSpan until next timer, or 60 seconds if no timers pending.</returns>
    private TimeSpan GetNextTimerTimeout()
    {
        lock (_virtualTimersLock)
        {
            // Remove cancelled timers at the front of the queue
            while (_virtualTimerQueue.TryPeek(out var timer, out _))
            {
                if (!timer.IsCancelled) break;
                _virtualTimerQueue.Dequeue();
            }

            if (!_virtualTimerQueue.TryPeek(out _, out var fireTime))
            {
                _hasScheduledTimers = false;
                return TimeSpan.FromSeconds(60);
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var ms = fireTime - now;

            // Clamp to reasonable range: 0ms to 60 seconds
            if (ms <= 0) return TimeSpan.Zero;
            if (ms > 60000) return TimeSpan.FromSeconds(60);
            return TimeSpan.FromMilliseconds(ms);
        }
    }

    /// <summary>
    /// Increments the active handles count. Used by servers, timers, etc. to keep the event loop alive.
    /// </summary>
    internal void Ref()
    {
        lock (_activeHandlesLock)
        {
            _activeHandles++;
        }
    }

    /// <summary>
    /// Decrements the active handles count. When count reaches zero, the event loop can exit.
    /// </summary>
    internal void Unref()
    {
        bool shouldWake = false;
        lock (_activeHandlesLock)
        {
            if (_activeHandles > 0)
            {
                _activeHandles--;
                shouldWake = _activeHandles == 0;
            }
        }

        if (shouldWake)
        {
            WakeEventLoop();
        }
    }

    /// <summary>
    /// Registers an async handle with the interpreter's event loop.
    /// Compatibility shim for existing handle-based callers.
    /// </summary>
    internal void RegisterHandle(IAsyncHandle handle)
    {
        Ref();
    }

    /// <summary>
    /// Unregisters an async handle from the interpreter's event loop.
    /// Compatibility shim for existing handle-based callers.
    /// </summary>
    internal void UnregisterHandle(IAsyncHandle handle)
    {
        Unref();
    }

    /// <summary>
    /// Gets whether there are active handles keeping the event loop alive.
    /// </summary>
    internal bool HasActiveHandles
    {
        get
        {
            lock (_activeHandlesLock)
            {
                return _activeHandles > 0;
            }
        }
    }

    /// <summary>
    /// Runs the event loop, processing callbacks until there are no more active handles.
    /// This is the main loop that keeps the program alive for servers, timers, etc.
    /// </summary>
    /// <remarks>
    /// Uses a BlockingCollection for efficient waiting (no CPU polling).
    /// Sets up a SynchronizationContext to route async/await continuations back to this thread.
    /// This provides Node.js-compatible single-threaded semantics where all user callbacks
    /// execute on the main thread, while I/O operations run on the ThreadPool.
    /// </remarks>
    public void RunEventLoop()
    {
        // Set up SynchronizationContext so async/await continuations come back to this thread
        _eventLoopSyncContext = new InterpreterSynchronizationContext(EnqueueCallback);
        var previousSyncContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(_eventLoopSyncContext);

        try
        {
            while (!_isDisposed)
            {
                // Exit immediately if there's no work keeping the loop alive
                if (!HasActiveHandles && _callbackQueue.Count == 0)
                {
                    break;
                }

                // Calculate timeout until next timer fires
                var timeout = GetNextTimerTimeout();

                // Efficient wait: blocks until callback arrives OR timeout expires
                // This uses no CPU while waiting (unlike Thread.Sleep polling)
                if (_callbackQueue.TryTake(out var action, timeout))
                {
                    // Execute the queued callback (HTTP request handler, async continuation, etc.)
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        // Log uncaught exceptions but don't crash the event loop
                        Console.Error.WriteLine($"Uncaught exception in event loop callback: {ex.Message}");
                    }
                }

                // Process any due timers (setTimeout, setInterval callbacks)
                ProcessPendingCallbacks();

                // Exit condition: no active handles AND queue is empty
                // This ensures all queued callbacks are processed before exiting (like Node.js)
                if (!HasActiveHandles && _callbackQueue.Count == 0)
                {
                    break;
                }
            }
        }
        finally
        {
            // Drain any remaining callbacks before fully exiting
            // This handles edge cases where callbacks were queued during shutdown
            DrainCallbackQueue();

            // Restore previous SynchronizationContext
            SynchronizationContext.SetSynchronizationContext(previousSyncContext);

            // Complete the queue so any pending Add() calls don't block
            try { _callbackQueue.CompleteAdding(); }
            catch (ObjectDisposedException) { /* already disposed */ }
        }
    }

    /// <summary>
    /// Drains any remaining callbacks from the queue during shutdown.
    /// Ensures all queued work completes before the event loop fully exits.
    /// </summary>
    private void DrainCallbackQueue()
    {
        // Process any remaining callbacks synchronously
        while (_callbackQueue.TryTake(out var action, TimeSpan.Zero))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Uncaught exception during event loop drain: {ex.Message}");
            }
        }

        // Final timer processing
        ProcessPendingCallbacks();
    }

    /// <summary>
    /// Processes all due virtual timers. Called during loop iterations to execute
    /// timer callbacks without relying on background thread scheduling.
    /// Uses priority queue for O(log n) timer extraction.
    /// </summary>
    internal void ProcessPendingCallbacks()
    {
        // Quick checks before acquiring lock
        if (_isDisposed || !_hasScheduledTimers) return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        List<VirtualTimer>? toExecute = null;
        List<VirtualTimer>? toReschedule = null;

        lock (_virtualTimersLock)
        {
            // Dequeue all due timers - PriorityQueue is min-heap, so lowest fireTime comes first
            while (_virtualTimerQueue.TryPeek(out var timer, out var fireTime))
            {
                // If the next timer isn't due yet, stop processing
                if (fireTime > now) break;

                // Remove the timer from queue
                _virtualTimerQueue.Dequeue();

                // Skip cancelled timers
                if (timer.IsCancelled) continue;

                // Collect for execution
                toExecute ??= new List<VirtualTimer>();
                toExecute.Add(timer);

                // Collect interval timers for rescheduling
                if (timer.IsInterval)
                {
                    timer.FireTimeMs += timer.IntervalMs;
                    toReschedule ??= new List<VirtualTimer>();
                    toReschedule.Add(timer);
                }
            }

            // Re-enqueue interval timers with updated fire times
            if (toReschedule != null)
            {
                foreach (var timer in toReschedule)
                {
                    _virtualTimerQueue.Enqueue(timer, timer.FireTimeMs);
                }
            }

            // Update flag - only clear if queue is truly empty
            _hasScheduledTimers = _virtualTimerQueue.Count > 0;
        }

        // Execute callbacks outside the lock to avoid deadlocks
        if (toExecute != null)
        {
            foreach (var timer in toExecute)
            {
                if (!timer.IsCancelled && !_isDisposed)
                {
                    timer.Callback();
                }
            }
        }
    }

    /// <summary>
    /// Disposes the interpreter, cancelling all pending timers and marking as disposed.
    /// This prevents race conditions where timer callbacks fire after the test/execution context has ended.
    /// </summary>
    public void Dispose()
    {
        _isDisposed = true;

        // Complete the callback queue to unblock any waiting TryTake
        try { _callbackQueue.CompleteAdding(); }
        catch (ObjectDisposedException) { /* already disposed */ }

        // Cancel all pending timers to release resources immediately
        while (_pendingTimers.TryTake(out var timer))
        {
            timer.Cancel();
        }

        // Clear virtual timers to prevent memory leaks
        lock (_virtualTimersLock)
        {
            _virtualTimerQueue.Clear();
            _hasScheduledTimers = false;
        }

        // Dispose the callback queue
        try { _callbackQueue.Dispose(); }
        catch (ObjectDisposedException) { /* already disposed */ }

        GC.SuppressFinalize(this);
    }

    public void Resolve(Expr expr, int depth)
    {
        _locals[expr] = depth;
    }

    private object? LookupVariable(Token name, Expr expr)
    {
        // Fast path: resolved locals with known depth
        if (_locals.TryGetValue(expr, out int distance))
        {
            return _environment.GetAt(distance, name.Lexeme);
        }

        // Scope chain traversal for user-defined variables
        // User variables can shadow built-in globals, so check environment first
        if (_environment.TryGet(name.Lexeme, out object? value))
        {
            return value;
        }

        // Check global constants and built-in singletons (single frozen dictionary lookup)
        // This handles: NaN, Infinity, undefined, Math, JSON, Object, console, process, etc.
        if (_globalConstants.TryGetValue(name.Lexeme, out var constant))
        {
            return constant;
        }

        // Check for Node.js module globals (__dirname, __filename)
        if (name.Lexeme == "__filename") return _currentModule?.Path ?? "";
        if (name.Lexeme == "__dirname") return Path.GetDirectoryName(_currentModule?.Path) ?? "";

        throw new InterpreterException($"Undefined variable '{name.Lexeme}'.");
    }

    /// <summary>
    /// Executes a list of statements as the main entry point for interpretation.
    /// </summary>
    /// <param name="statements">The list of parsed statements to execute.</param>
    /// <param name="typeMap">Optional type map from static analysis for type-aware dispatch.</param>
    /// <remarks>
    /// Catches and reports runtime errors to the console. Each statement is executed
    /// sequentially via <see cref="Execute"/>.
    /// </remarks>
    public void Interpret(List<Stmt> statements, TypeMap? typeMap = null)
    {
        _typeMap = typeMap;
        try
        {
            // Check for "use strict" directive at file level
            bool isStrict = CheckForUseStrict(statements);
            if (isStrict)
            {
                // Wrap the current environment with strict mode enabled
                _environment = new RuntimeEnvironment(_environment, strictMode: true);
            }

            // Hoist function declarations first
            HoistFunctionDeclarations(statements);

            foreach (Stmt statement in statements)
            {
                // For expression statements, we may get a Promise that needs to be awaited
                // This provides "top-level await" behavior for the interpreter
                if (statement is Stmt.Expression exprStmt)
                {
                    object? result = Evaluate(exprStmt.Expr);
                    // Wait for top-level Promises to complete before continuing
                    if (result is SharpTSPromise promise)
                    {
                        promise.Task.GetAwaiter().GetResult();
                    }
                }
                else
                {
                    var result = Execute(statement);
                    if (result.Type == ExecutionResult.ResultType.Throw)
                    {
                        Console.WriteLine($"Runtime Error: {Stringify(result.Value)}");
                        return;
                    }
                    if (result.IsAbrupt)
                    {
                        // Top-level break/continue/return is usually a syntax error handled by parser
                        // but if it reaches here, we stop execution.
                        return;
                    }
                }
            }

            // After executing all statements, check for a main() function and call it
            TryCallMainWithExitCode(statements);

            // Always run the event loop - servers/timers may have been registered
            RunEventLoop();
        }
        catch (Exception error)
        {
            Console.WriteLine($"Runtime Error: {error.Message}");
            throw;
        }
    }

    /// <summary>
    /// Interprets multiple modules in dependency order.
    /// </summary>
    /// <param name="modules">Modules in dependency order (dependencies first)</param>
    /// <param name="resolver">Module resolver for path resolution</param>
    /// <param name="typeMap">Optional type map from static analysis</param>
    public void InterpretModules(List<ParsedModule> modules, ModuleResolver resolver, TypeMap? typeMap = null)
    {
        _typeMap = typeMap;
        _moduleResolver = resolver;

        try
        {
            // Create a shared script environment for script files (they share global scope)
            var scriptEnv = new RuntimeEnvironment(_environment);

            foreach (var module in modules)
            {
                if (module.IsScript)
                {
                    ExecuteScriptFile(module, scriptEnv);
                }
                else
                {
                    ExecuteModule(module);
                }
            }

            // After executing all modules, check for main() in the entry module (last one)
            // Note: main() may have already been called during module execution if there's
            // a top-level main() call. TryCallMainWithExitCode handles exit codes but
            // the event loop should run regardless of main().
            if (modules.Count > 0)
            {
                TryCallMainWithExitCode(modules[^1].Statements);
            }

            // Always run the event loop at the end - servers/timers may have been
            // registered during module execution (even without a main function)
            RunEventLoop();
        }
        catch (Exception error)
        {
            Console.WriteLine($"Runtime Error: {error.Message}");
            throw;
        }
    }

    /// <summary>
    /// Executes a script file in the shared script environment.
    /// Scripts share global scope, so all declarations are visible to other scripts.
    /// </summary>
    private void ExecuteScriptFile(ParsedModule script, RuntimeEnvironment scriptEnv)
    {
        // Skip if already executed
        if (script.IsExecuted)
        {
            return;
        }

        using (PushScriptContext(scriptEnv, script))
        {
            // Check for "use strict" directive
            bool isStrict = CheckForUseStrict(script.Statements);
            if (isStrict && !_environment.IsStrictMode)
            {
                _environment = new RuntimeEnvironment(_environment, strictMode: true);
            }

            // Hoist function declarations first
            HoistFunctionDeclarations(script.Statements);

            // Execute all statements in the shared environment
            foreach (var stmt in script.Statements)
            {
                if (stmt is Stmt.Expression exprStmt)
                {
                    object? result = Evaluate(exprStmt.Expr);
                    if (result is SharpTSPromise promise)
                    {
                        promise.Task.GetAwaiter().GetResult();
                    }
                }
                else
                {
                    var result = Execute(stmt);
                    if (result.Type == ExecutionResult.ResultType.Throw)
                    {
                        throw new InterpreterException(Stringify(result.Value));
                    }
                    if (result.IsAbrupt) break;
                }
            }

            script.IsExecuted = true;
        }
    }

    /// <summary>
    /// Checks for a main(args: string[]) function in the statements and calls it if found.
    /// If main() returns a number, calls Environment.Exit with that number as the exit code.
    /// </summary>
    private void TryCallMainWithExitCode(List<Stmt> statements)
    {
        // Look for a function named "main" with the expected signature
        Stmt.Function? mainFunc = null;
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Function func && func.Name.Lexeme == "main" && func.Body != null)
            {
                // Accept signatures: main() or main(args: string[])
                var paramCount = func.Parameters.Count;
                if (paramCount == 0 || (paramCount == 1 && func.Parameters[0].Type == "string[]"))
                {
                    // Accept return types: void, null (implicit), number, Promise<void>, Promise<number>
                    var rt = func.ReturnType;
                    if (rt == null || rt == "void" || rt == "number" ||
                        rt == "Promise<void>" || rt == "Promise<number>")
                    {
                        mainFunc = func;
                        break;
                    }
                }
            }
        }

        if (mainFunc == null)
            return;

        // Get the main function from the environment (single scope traversal)
        if (!_environment.TryGet(mainFunc.Name.Lexeme, out object? mainValue))
            return;

        if (mainValue is not SharpTSFunction mainFn)
            return;

        // Call main with process.argv (pass args even if main() doesn't take them - JS allows this)
        var argv = ProcessBuiltIns.GetArgv();
        object? result;
        try
        {
            // Pass argv only if main expects it
            result = mainFunc.Parameters.Count == 0
                ? mainFn.Call(this, [])
                : mainFn.Call(this, [argv]);
        }
        catch (Runtime.Exceptions.ReturnException ret)
        {
            result = ret.Value;
        }

        // If result is a Promise, await it
        if (result is SharpTSPromise promise)
        {
            result = promise.Task.GetAwaiter().GetResult();
        }

        // If result is a number, use it as exit code
        if (result is double exitCode)
        {
            System.Environment.Exit((int)exitCode);
        }

        // Note: RunEventLoop is called by the caller (Interpret or InterpretModules)
        // after this method returns, so handles registered during main() or module
        // execution will keep the process alive.
    }

    /// <summary>
    /// Executes a single module, caching its exports.
    /// </summary>
    private void ExecuteModule(ParsedModule module)
    {
        // Skip if already executed
        if (_loadedModules.ContainsKey(module.Path))
        {
            return;
        }

        // Create module instance to track exports
        var moduleInstance = new ModuleInstance();
        _loadedModules[module.Path] = moduleInstance;

        // Handle built-in modules specially - populate exports from interpreter implementations
        if (module.IsBuiltIn)
        {
            var moduleName = BuiltInModuleRegistry.GetModuleName(module.Path);
            if (moduleName != null && BuiltInModuleValues.HasInterpreterSupport(moduleName))
            {
                var exports = BuiltInModuleValues.GetModuleExports(moduleName);
                foreach (var (name, value) in exports)
                {
                    moduleInstance.SetExport(name, value);
                }
                // Set default export to all exports, enabling: import fs from 'fs'
                moduleInstance.DefaultExport = moduleInstance.ExportsAsObject();
            }
            moduleInstance.IsExecuted = true;
            return;
        }

        // Create module-scoped environment
        var moduleEnv = new RuntimeEnvironment(_environment);

        // Bind imports from dependencies
        BindModuleImports(module, moduleEnv);

        using (PushModuleContext(moduleEnv, module, moduleInstance))
        {
            // First pass: hoist function declarations
            HoistFunctionDeclarations(module.Statements);

            // Second pass: execute all statements
            foreach (var stmt in module.Statements)
            {
                // For expression statements, we may get a Promise that needs to be awaited
                // This provides "top-level await" behavior for modules
                if (stmt is Stmt.Expression exprStmt)
                {
                    object? result = Evaluate(exprStmt.Expr);
                    // Wait for top-level Promises to complete before continuing
                    if (result is SharpTSPromise promise)
                    {
                        promise.Task.GetAwaiter().GetResult();
                    }
                }
                else
                {
                    var result = Execute(stmt);
                    if (result.Type == ExecutionResult.ResultType.Throw)
                    {
                        throw new InterpreterException(Stringify(result.Value));
                    }
                    if (result.IsAbrupt) break;
                }
            }
            moduleInstance.IsExecuted = true;
        }
    }

    /// <summary>
    /// Binds imported values into the module's environment.
    /// </summary>
    private void BindModuleImports(ParsedModule module, RuntimeEnvironment env)
    {
        foreach (var stmt in module.Statements)
        {
            if (stmt is Stmt.Import import)
            {
                // Skip type-only imports entirely - they have no runtime binding
                if (import.IsTypeOnly)
                    continue;

                string importedPath = _moduleResolver!.ResolveModulePath(import.ModulePath, module.Path);
                var importedModuleInstance = _loadedModules.GetValueOrDefault(importedPath);

                if (importedModuleInstance == null)
                {
                    throw new InterpreterException($"Module '{import.ModulePath}' not loaded.");
                }

                // Default import
                if (import.DefaultImport != null)
                {
                    env.Define(import.DefaultImport.Lexeme, importedModuleInstance.DefaultExport);
                }

                // Namespace import: import * as Module from './file'
                if (import.NamespaceImport != null)
                {
                    env.Define(import.NamespaceImport.Lexeme, importedModuleInstance.ExportsAsObject());
                }

                // Named imports: import { x, y as z } from './file'
                // Skip individual type-only specifiers
                if (import.NamedImports != null)
                {
                    foreach (var spec in import.NamedImports.Where(s => !s.IsTypeOnly))
                    {
                        string importedName = spec.Imported.Lexeme;
                        string localName = spec.LocalName?.Lexeme ?? importedName;
                        var value = importedModuleInstance.GetExport(importedName);
                        env.Define(localName, value);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Executes an export statement, registering exports in the current module.
    /// </summary>
    private ExecutionResult ExecuteExport(Stmt.Export export)
    {
        // Handle export = assignment (CommonJS-style)
        if (export.ExportAssignment != null)
        {
            var value = Evaluate(export.ExportAssignment);
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
                var result = Execute(export.Declaration);
                if (result.IsAbrupt) return result;

                if (_currentModuleInstance != null)
                {
                    _currentModuleInstance.DefaultExport = GetDeclaredValue(export.Declaration);
                }
            }
            else if (export.DefaultExpr != null)
            {
                var value = Evaluate(export.DefaultExpr);
                if (_currentModuleInstance != null)
                {
                    _currentModuleInstance.DefaultExport = value;
                }
            }
        }
        else if (export.Declaration != null)
        {
            var result = Execute(export.Declaration);
            if (result.IsAbrupt) return result;

            // Skip type-only declarations (interface, type alias) - they have no runtime value
            if (_currentModuleInstance != null && !IsTypeOnlyDeclaration(export.Declaration))
            {
                string name = GetDeclaredName(export.Declaration);
                _currentModuleInstance.SetExport(name, GetDeclaredValue(export.Declaration));
            }
        }
        else if (export.NamedExports != null && export.FromModulePath == null)
        {
            // export { x, y }
            foreach (var spec in export.NamedExports)
            {
                string localName = spec.LocalName.Lexeme;
                string exportedName = spec.ExportedName?.Lexeme ?? localName;
                var value = _environment.Get(spec.LocalName);
                if (_currentModuleInstance != null)
                {
                    _currentModuleInstance.SetExport(exportedName, value);
                }
            }
        }
        else if (export.FromModulePath != null)
        {
            // Re-export: export { x } from './module' or export * from './module'
            string sourcePath = _moduleResolver!.ResolveModulePath(export.FromModulePath, _currentModule!.Path);
            var sourceModuleInstance = _loadedModules.GetValueOrDefault(sourcePath);

            if (sourceModuleInstance != null && _currentModuleInstance != null)
            {
                if (export.NamedExports != null)
                {
                    // Re-export specific names
                    foreach (var spec in export.NamedExports)
                    {
                        string importedName = spec.LocalName.Lexeme;
                        string exportedName = spec.ExportedName?.Lexeme ?? importedName;
                        var value = sourceModuleInstance.GetExport(importedName);
                        _currentModuleInstance.SetExport(exportedName, value);
                    }
                }
                else
                {
                    // Re-export all: export * from './module'
                    foreach (var (name, value) in sourceModuleInstance.Exports)
                    {
                        _currentModuleInstance.SetExport(name, value);
                    }
                }
            }
        }

        return ExecutionResult.Success();
    }

    /// <summary>
    /// Checks if a declaration is type-only (interface or type alias) with no runtime value.
    /// </summary>
    private bool IsTypeOnlyDeclaration(Stmt decl) =>
        decl is Stmt.Interface or Stmt.TypeAlias;

    /// <summary>
    /// Executes a CommonJS-style require import: import x = require('path')
    /// </summary>
    private ExecutionResult ExecuteImportRequire(Stmt.ImportRequire importReq)
    {
        // Check if it's a built-in module (fs, path, os, etc.)
        string? builtInModuleName = BuiltInModuleRegistry.GetModuleName(importReq.ModulePath);
        if (builtInModuleName != null && BuiltInModuleValues.HasInterpreterSupport(builtInModuleName))
        {
            // Get the built-in module exports and create a namespace object
            var exports = BuiltInModuleValues.GetModuleExports(builtInModuleName);
            var builtInModule = new SharpTSObject(exports);
            _environment.Define(importReq.AliasName.Lexeme, builtInModule);

            // If this is a re-export, register the export
            if (importReq.IsExported && _currentModuleInstance != null)
            {
                _currentModuleInstance.SetExport(importReq.AliasName.Lexeme, builtInModule);
            }
            return ExecutionResult.Success();
        }

        // Not in module context - define as null
        if (_currentModule == null || _moduleResolver == null)
        {
            _environment.Define(importReq.AliasName.Lexeme, null);
            return ExecutionResult.Success();
        }

        // Resolve the module path
        string resolvedPath = _moduleResolver.ResolveModulePath(importReq.ModulePath, _currentModule.Path);

        // Find the loaded module instance
        var moduleInstance = _loadedModules.GetValueOrDefault(resolvedPath);
        var importedModule = _moduleResolver.GetCachedModule(resolvedPath);

        object? importedValue;
        if (importedModule?.HasExportAssignment == true)
        {
            // Module uses export = value - import the assignment value directly
            importedValue = importedModule.ExportAssignmentValue;
        }
        else if (moduleInstance != null)
        {
            // ES6-style module - create a namespace object with all exports
            var exports = new Dictionary<string, object?>(moduleInstance.Exports);
            importedValue = new SharpTSObject(exports);
        }
        else
        {
            // Module not found - define as null
            importedValue = null;
        }

        _environment.Define(importReq.AliasName.Lexeme, importedValue);

        // If this is a re-export, register the export
        if (importReq.IsExported && _currentModuleInstance != null)
        {
            _currentModuleInstance.SetExport(importReq.AliasName.Lexeme, importedValue);
        }

        return ExecutionResult.Success();
    }

    /// <summary>
    /// Checks if the statements begin with a "use strict" directive.
    /// </summary>
    /// <param name="statements">The list of statements to check.</param>
    /// <returns>True if "use strict" directive is found at the beginning.</returns>
    private static bool CheckForUseStrict(List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Directive directive)
            {
                if (directive.Value == "use strict")
                {
                    return true;
                }
                // Continue checking other directives at the start
            }
            else
            {
                // Non-directive statement encountered, stop checking
                break;
            }
        }
        return false;
    }

    /// <summary>
    /// Hoists function declarations by defining them before other statements execute.
    /// This enables functions to call each other regardless of declaration order.
    /// </summary>
    private void HoistFunctionDeclarations(IEnumerable<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            Stmt.Function? funcStmt = null;

            // Handle top-level functions
            if (stmt is Stmt.Function f && f.Body != null)
            {
                funcStmt = f;
            }
            // Handle exported functions
            else if (stmt is Stmt.Export export && export.Declaration is Stmt.Function ef && ef.Body != null)
            {
                funcStmt = ef;
            }

            if (funcStmt != null)
            {
                // Skip if already defined
                if (_environment.IsDefinedLocally(funcStmt.Name.Lexeme))
                    continue;

                // Create the appropriate function type and define it
                if (funcStmt.IsGenerator && funcStmt.IsAsync)
                {
                    _environment.Define(funcStmt.Name.Lexeme, new SharpTSAsyncGeneratorFunction(funcStmt, _environment));
                }
                else if (funcStmt.IsGenerator)
                {
                    _environment.Define(funcStmt.Name.Lexeme, new SharpTSGeneratorFunction(funcStmt, _environment));
                }
                else if (funcStmt.IsAsync)
                {
                    _environment.Define(funcStmt.Name.Lexeme, new SharpTSAsyncFunction(funcStmt, _environment));
                }
                else
                {
                    _environment.Define(funcStmt.Name.Lexeme, new SharpTSFunction(funcStmt, _environment));
                }
            }
        }
    }

    /// <summary>
    /// Gets the name of a declaration.
    /// </summary>
    private string GetDeclaredName(Stmt decl)
    {
        return decl switch
        {
            Stmt.Function f => f.Name.Lexeme,
            Stmt.Class c => c.Name.Lexeme,
            Stmt.Var v => v.Name.Lexeme,
            Stmt.Enum e => e.Name.Lexeme,
            _ => throw new InterpreterException($"Cannot get name of declaration type {decl.GetType().Name}")
        };
    }

    /// <summary>
    /// Gets the value of a declaration from the environment.
    /// </summary>
    private object? GetDeclaredValue(Stmt decl)
    {
        string name = GetDeclaredName(decl);
        var token = decl switch
        {
            Stmt.Function f => f.Name,
            Stmt.Class c => c.Name,
            Stmt.Var v => v.Name,
            Stmt.Enum e => e.Name,
            _ => throw new InterpreterException($"Cannot get value of declaration type {decl.GetType().Name}")
        };
        return _environment.Get(token);
    }

    /// <summary>
    /// Internal wrapper for Execute that allows evaluation contexts to dispatch statements.
    /// </summary>
    /// <param name="stmt">The statement to execute.</param>
    /// <returns>The execution result.</returns>
    internal ExecutionResult ExecuteStatement(Stmt stmt) => Execute(stmt);

    /// <summary>
    /// Internal async wrapper for ExecuteAsync that allows evaluation contexts to dispatch statements.
    /// </summary>
    /// <param name="stmt">The statement to execute.</param>
    /// <returns>A task containing the execution result.</returns>
    internal Task<ExecutionResult> ExecuteStatementAsync(Stmt stmt) => ExecuteAsync(stmt);

    /// <summary>
    /// Dispatches a statement to the appropriate execution handler using the registry.
    /// </summary>
    /// <param name="stmt">The statement AST node to execute.</param>
    /// <remarks>
    /// Handles all statement types including control flow (if, while, for, switch),
    /// declarations (var, function, class, enum), and control transfer (return, break, continue, throw).
    /// Control flow uses <see cref="ExecutionResult"/> for non-local jumps.
    /// </remarks>
    private ExecutionResult Execute(Stmt stmt)
    {
        return _registry.DispatchStmt(stmt, this);
    }

    // Statement handlers - called by the registry

    internal ExecutionResult VisitBlock(Stmt.Block block) =>
        ExecuteBlock(block.Statements, new RuntimeEnvironment(_environment));

    internal ExecutionResult VisitLabeledStatement(Stmt.LabeledStatement labeledStmt) =>
        ExecuteLabeledStatement(labeledStmt);

    internal ExecutionResult VisitSequence(Stmt.Sequence seq)
    {
        // Execute in current scope (no new environment)
        foreach (var s in seq.Statements)
        {
            var result = Execute(s);
            if (result.IsAbrupt) return result;
        }
        return ExecutionResult.Success();
    }

    internal ExecutionResult VisitExpression(Stmt.Expression exprStmt)
    {
        Evaluate(exprStmt.Expr);
        return ExecutionResult.Success();
    }

    internal ExecutionResult VisitIf(Stmt.If ifStmt)
    {
        if (IsTruthy(Evaluate(ifStmt.Condition)))
        {
            return Execute(ifStmt.ThenBranch);
        }
        else if (ifStmt.ElseBranch != null)
        {
            return Execute(ifStmt.ElseBranch);
        }
        return ExecutionResult.Success();
    }

    internal ExecutionResult VisitWhile(Stmt.While whileStmt) =>
        ExecuteWhileCore(
            () => IsTruthy(Evaluate(whileStmt.Condition)),
            () => Execute(whileStmt.Body));

    internal ExecutionResult VisitDoWhile(Stmt.DoWhile doWhileStmt)
    {
        do
        {
            var result = Execute(doWhileStmt.Body);
            var (shouldBreak, shouldContinue, abruptResult) = HandleLoopResult(result, null);
            if (shouldBreak) return ExecutionResult.Success();
            if (shouldContinue) continue;
            if (abruptResult.HasValue) return abruptResult.Value;
            // Process any pending timer callbacks
            ProcessPendingCallbacks();
        } while (IsTruthy(Evaluate(doWhileStmt.Condition)));
        return ExecutionResult.Success();
    }

    internal ExecutionResult VisitFor(Stmt.For forStmt)
    {
        // Create scope for loop variables (ES6 let/const block scoping)
        // Variables declared with let/const in the initializer are scoped to the loop
        RuntimeEnvironment loopEnv = new(_environment);
        using (PushScope(loopEnv))
        {
            // Execute initializer once (defines loop variable in loopEnv)
            if (forStmt.Initializer != null)
                Execute(forStmt.Initializer);
            // Loop with proper continue handling - increment always runs
            while (forStmt.Condition == null || IsTruthy(Evaluate(forStmt.Condition)))
            {
                var result = Execute(forStmt.Body);
                if (result.Type == ExecutionResult.ResultType.Break && result.TargetLabel == null) break;
                // On continue, execute increment then continue the loop
                if (result.Type == ExecutionResult.ResultType.Continue && result.TargetLabel == null)
                {
                    if (forStmt.Increment != null)
                        Evaluate(forStmt.Increment);
                    // Yield to allow timer callbacks and other threads to execute
                    Thread.Sleep(0);
                    continue;
                }
                if (result.IsAbrupt) return result;
                // Normal completion: execute increment
                if (forStmt.Increment != null)
                    Evaluate(forStmt.Increment);
                // Process any pending timer callbacks
                ProcessPendingCallbacks();
            }
            return ExecutionResult.Success();
        }
    }

    internal ExecutionResult VisitForOf(Stmt.ForOf forOf) => ExecuteForOf(forOf);

    internal ExecutionResult VisitForIn(Stmt.ForIn forIn) => ExecuteForIn(forIn);

    internal ExecutionResult VisitBreak(Stmt.Break breakStmt) =>
        ExecutionResult.Break(breakStmt.Label?.Lexeme);

    internal ExecutionResult VisitContinue(Stmt.Continue continueStmt) =>
        ExecutionResult.Continue(continueStmt.Label?.Lexeme);

    internal ExecutionResult VisitSwitch(Stmt.Switch switchStmt) => ExecuteSwitch(switchStmt);

    internal ExecutionResult VisitTryCatch(Stmt.TryCatch tryCatch) => ExecuteTryCatch(tryCatch);

    internal ExecutionResult VisitThrow(Stmt.Throw throwStmt) =>
        ExecutionResult.Throw(Evaluate(throwStmt.Value));

    internal ExecutionResult VisitVar(Stmt.Var varStmt)
    {
        object? value = null;
        if (varStmt.Initializer != null)
        {
            value = Evaluate(varStmt.Initializer);
        }
        _environment.Define(varStmt.Name.Lexeme, value);
        return ExecutionResult.Success();
    }

    internal ExecutionResult VisitConst(Stmt.Const constStmt)
    {
        // Const declarations always have an initializer (enforced by parser)
        object? constValue = Evaluate(constStmt.Initializer);
        _environment.Define(constStmt.Name.Lexeme, constValue);
        return ExecutionResult.Success();
    }

    internal ExecutionResult VisitFunction(Stmt.Function functionStmt)
    {
        // Skip overload signatures (no body) - they're type-checking only
        if (functionStmt.Body == null) return ExecutionResult.Success();
        // Skip if already hoisted
        if (_environment.IsDefinedLocally(functionStmt.Name.Lexeme)) return ExecutionResult.Success();
        if (functionStmt.IsGenerator && functionStmt.IsAsync)
        {
            // Async generator: async function* foo() { yield await ... }
            SharpTSAsyncGeneratorFunction asyncGenFunction = new(functionStmt, _environment);
            _environment.Define(functionStmt.Name.Lexeme, asyncGenFunction);
        }
        else if (functionStmt.IsGenerator)
        {
            SharpTSGeneratorFunction generatorFunction = new(functionStmt, _environment);
            _environment.Define(functionStmt.Name.Lexeme, generatorFunction);
        }
        else if (functionStmt.IsAsync)
        {
            SharpTSAsyncFunction asyncFunction = new(functionStmt, _environment);
            _environment.Define(functionStmt.Name.Lexeme, asyncFunction);
        }
        else
        {
            SharpTSFunction function = new(functionStmt, _environment);
            _environment.Define(functionStmt.Name.Lexeme, function);
        }
        return ExecutionResult.Success();
    }

    internal ExecutionResult VisitClass(Stmt.Class classStmt)
    {
        object? superclass = null;
        if (classStmt.Superclass != null)
        {
            superclass = _environment.Get(classStmt.Superclass);
            if (superclass is not SharpTSClass)
            {
                throw new InterpreterException("Superclass must be a class.");
            }
        }

        _environment.Define(classStmt.Name.Lexeme, null);

        if (classStmt.Superclass != null)
        {
            _environment = new RuntimeEnvironment(_environment);
            _environment.Define("super", superclass);
        }

        Dictionary<string, ISharpTSCallable> methods = [];
        Dictionary<string, ISharpTSCallable> staticMethods = [];
        Dictionary<string, object?> staticProperties = [];
        List<Stmt.Field> instanceFields = [];
        // ES2022 private class elements
        List<Stmt.Field> instancePrivateFields = [];
        Dictionary<string, ISharpTSCallable> privateMethods = [];
        Dictionary<string, object?> staticPrivateFields = [];
        Dictionary<string, ISharpTSCallable> staticPrivateMethods = [];

        // Process fields: collect instance fields, defer static field initialization if using StaticInitializers
        // Note: Declare fields are processed normally - they can't have initializers (enforced by parser),
        // so they'll be added with null/undefined values and can be set externally later.
        bool hasStaticInitializers = classStmt.StaticInitializers != null && classStmt.StaticInitializers.Count > 0;

        foreach (Stmt.Field field in classStmt.Fields)
        {
            if (field.IsPrivate)
            {
                // ES2022 private fields
                if (field.IsStatic)
                {
                    if (!hasStaticInitializers)
                    {
                        // Old behavior: evaluate immediately
                        object? fieldValue = field.Initializer != null
                            ? Evaluate(field.Initializer)
                            : null;
                        staticPrivateFields[field.Name.Lexeme] = fieldValue;
                    }
                    // else: will be evaluated via StaticInitializers with proper 'this' binding
                }
                else
                {
                    // Collect instance private fields - they'll be initialized when instances are created
                    instancePrivateFields.Add(field);
                }
            }
            else if (field.IsStatic)
            {
                if (!hasStaticInitializers)
                {
                    // Old behavior: evaluate immediately
                    object? fieldValue = field.Initializer != null
                        ? Evaluate(field.Initializer)
                        : null;
                    staticProperties[field.Name.Lexeme] = fieldValue;
                }
                // else: will be evaluated via StaticInitializers with proper 'this' binding
            }
            else
            {
                // Collect instance fields - they'll be initialized when instances are created
                instanceFields.Add(field);
            }
        }

        // Separate static and instance methods (skip overload signatures with no body)
        foreach (Stmt.Function method in classStmt.Methods.Where(m => m.Body != null))
        {
            // Create the appropriate function type based on async/generator flags
            ISharpTSCallable func;
            if (method.IsAsync)
                func = new SharpTSAsyncFunction(method, _environment);
            else if (method.IsGenerator)
                func = new SharpTSGeneratorFunction(method, _environment);
            else
                func = new SharpTSFunction(method, _environment);

            if (method.IsPrivate)
            {
                // ES2022 private methods
                if (method.IsStatic)
                {
                    staticPrivateMethods[method.Name.Lexeme] = func;
                }
                else
                {
                    privateMethods[method.Name.Lexeme] = func;
                }
            }
            else if (method.IsStatic)
            {
                staticMethods[method.Name.Lexeme] = func;
            }
            else
            {
                methods[method.Name.Lexeme] = func;
            }
        }

        // Create accessor functions
        Dictionary<string, SharpTSFunction> getters = [];
        Dictionary<string, SharpTSFunction> setters = [];

        if (classStmt.Accessors != null)
        {
            foreach (var accessor in classStmt.Accessors)
            {
                // Create a synthetic function for the accessor
                var funcStmt = new Stmt.Function(
                    accessor.Name,
                    null,  // No type parameters for accessor
                    null,  // No this type annotation
                    accessor.SetterParam != null ? [accessor.SetterParam] : [],
                    accessor.Body,
                    accessor.ReturnType);

                SharpTSFunction func = new(funcStmt, _environment);

                if (accessor.Kind.Type == TokenType.GET)
                {
                    getters[accessor.Name.Lexeme] = func;
                }
                else
                {
                    setters[accessor.Name.Lexeme] = func;
                }
            }
        }

        // Process auto-accessors (TypeScript 4.9+)
        List<Stmt.AutoAccessor> instanceAutoAccessors = [];
        Dictionary<string, object?> staticAutoAccessors = [];

        if (classStmt.AutoAccessors != null)
        {
            foreach (var autoAccessor in classStmt.AutoAccessors)
            {
                if (autoAccessor.IsStatic)
                {
                    // Evaluate static auto-accessor initializer now
                    object? initValue = autoAccessor.Initializer != null
                        ? Evaluate(autoAccessor.Initializer)
                        : null;
                    staticAutoAccessors[autoAccessor.Name.Lexeme] = initValue;
                }
                else
                {
                    // Collect instance auto-accessors for later initialization
                    instanceAutoAccessors.Add(autoAccessor);
                }
            }
        }

        SharpTSClass klass = new(
            classStmt.Name.Lexeme,
            (SharpTSClass?)superclass,
            methods,
            staticMethods,
            staticProperties,
            getters,
            setters,
            classStmt.IsAbstract,
            instanceFields,
            instancePrivateFields,
            privateMethods,
            staticPrivateFields,
            staticPrivateMethods,
            instanceAutoAccessors.Count > 0 ? instanceAutoAccessors : null,
            staticAutoAccessors.Count > 0 ? staticAutoAccessors : null);

        // Execute static initializers in declaration order (if present)
        if (hasStaticInitializers)
        {
            // Create temporary environment with 'this' bound to the class
            // Also make the class name available so code like Foo.x works
            var staticEnv = new RuntimeEnvironment(_environment);
            staticEnv.Define("this", klass);
            staticEnv.Define(classStmt.Name.Lexeme, klass);

            var prevEnv = _environment;
            _environment = staticEnv;

            try
            {
                foreach (var initializer in classStmt.StaticInitializers!)
                {
                    switch (initializer)
                    {
                        case Stmt.Field field when field.IsStatic:
                            object? fieldValue = field.Initializer != null
                                ? Evaluate(field.Initializer)
                                : null;
                            if (field.IsPrivate)
                                klass.SetStaticPrivateField(field.Name.Lexeme, fieldValue);
                            else
                                klass.SetStaticProperty(field.Name.Lexeme, fieldValue);
                            break;

                        case Stmt.StaticBlock block:
                            foreach (var blockStmt in block.Body)
                            {
                                var result = Execute(blockStmt);
                                if (result.IsAbrupt)
                                {
                                    // Handle throw from static block
                                    if (result.Type == ExecutionResult.ResultType.Throw)
                                    {
                                        throw new InterpreterException($"Error in static block: {Stringify(result.Value)}");
                                    }
                                    // Return, break, continue are not allowed (validated by type checker)
                                }
                            }
                            break;
                    }
                }
            }
            finally
            {
                _environment = prevEnv;
            }
        }

        // Apply decorators in the correct order
        klass = ApplyAllDecorators(classStmt, klass, methods, staticMethods, getters, setters);

        if (classStmt.Superclass != null)
        {
            _environment = _environment.Enclosing!;
        }

        _environment.Assign(classStmt.Name, klass);
        return ExecutionResult.Success();
    }

    internal ExecutionResult VisitTypeAlias(Stmt.TypeAlias typeAlias) =>
        // Type-only declarations - compile-time only, no runtime effect
        ExecutionResult.Success();

    internal ExecutionResult VisitInterface(Stmt.Interface iface) =>
        // Type-only declarations - compile-time only, no runtime effect
        ExecutionResult.Success();

    internal ExecutionResult VisitFileDirective(Stmt.FileDirective fileDirective) =>
        // Type-only declarations - compile-time only, no runtime effect
        ExecutionResult.Success();

    internal ExecutionResult VisitField(Stmt.Field field) =>
        // Class member declarations - handled within class processing, not executed directly
        ExecutionResult.Success();

    internal ExecutionResult VisitAccessor(Stmt.Accessor accessor) =>
        // Class member declarations - handled within class processing, not executed directly
        ExecutionResult.Success();

    internal ExecutionResult VisitAutoAccessor(Stmt.AutoAccessor autoAccessor) =>
        // Class member declarations - handled within class processing, not executed directly
        ExecutionResult.Success();

    internal ExecutionResult VisitStaticBlock(Stmt.StaticBlock staticBlock) =>
        // Class member declarations - handled within class processing, not executed directly
        ExecutionResult.Success();

    internal ExecutionResult VisitEnum(Stmt.Enum enumStmt)
    {
        ExecuteEnumDeclaration(enumStmt);
        return ExecutionResult.Success();
    }

    internal ExecutionResult VisitNamespace(Stmt.Namespace ns) => ExecuteNamespace(ns);

    internal ExecutionResult VisitImportAlias(Stmt.ImportAlias importAlias) => ExecuteImportAlias(importAlias);

    internal ExecutionResult VisitReturn(Stmt.Return returnStmt)
    {
        object? returnValue = null;
        if (returnStmt.Value != null) returnValue = Evaluate(returnStmt.Value);
        return ExecutionResult.Return(returnValue);
    }

    internal ExecutionResult VisitPrint(Stmt.Print printStmt)
    {
        Console.WriteLine(Stringify(Evaluate(printStmt.Expr)));
        return ExecutionResult.Success();
    }

    internal ExecutionResult VisitImport(Stmt.Import import) =>
        // Imports are handled in BindModuleImports before execution
        // In single-file mode, imports are a no-op (type checker would have errored)
        ExecutionResult.Success();

    internal ExecutionResult VisitImportRequire(Stmt.ImportRequire importReq) => ExecuteImportRequire(importReq);

    internal ExecutionResult VisitExport(Stmt.Export exportStmt) => ExecuteExport(exportStmt);

    internal ExecutionResult VisitDirective(Stmt.Directive directive) =>
        // Directives are processed at the start of interpretation for their side effects (strict mode)
        // When encountered during execution, they are a no-op
        ExecutionResult.Success();

    internal ExecutionResult VisitDeclareModule(Stmt.DeclareModule declareModule) =>
        // Module/global augmentations and ambient declarations are type-only
        // No runtime effect - types were merged during type checking
        ExecutionResult.Success();

    internal ExecutionResult VisitDeclareGlobal(Stmt.DeclareGlobal declareGlobal) =>
        // Module/global augmentations and ambient declarations are type-only
        // No runtime effect - types were merged during type checking
        ExecutionResult.Success();

    internal ExecutionResult VisitUsing(Stmt.Using usingStmt) => ExecuteUsingDeclaration(usingStmt);

}
