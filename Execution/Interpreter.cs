using SharpTS.Modules;
using SharpTS.Modules.Stdlib;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.DotNet;
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
    private static readonly NodeRegistry<Interpreter, RuntimeValue, ExecutionResult> _registry =
        InterpreterRegistry.Create();

    /// <summary>
    /// Frozen dictionary of global constants and built-in singletons for fast lookup.
    /// Combines global constants (NaN, Infinity, undefined) with built-in namespaces
    /// (Math, JSON, Object, etc.) into a single lookup to minimize dictionary operations.
    /// </summary>
    private static readonly FrozenDictionary<string, object> _globalConstants = CreateGlobalsLookup();

    // The process-wide RegExp constructor singleton (a SharpTSBuiltInConstructor),
    // resolved once from the static globals table. ECMA-262 §22.2.6.1 requires
    // `RegExp.prototype.constructor === RegExp` and, by inheritance,
    // `(/x/).constructor === RegExp` — both must reference this exact instance
    // for strict-equality identity to hold. Cached so the regex property hot
    // path returns it without a dictionary probe. Mirrors the compiled side,
    // where the `$RegExp` Type token plays the same role.
    internal static readonly object? RegExpConstructorObject =
        _globalConstants.TryGetValue(BuiltInNames.RegExp, out var rxCtor) ? rxCtor : null;

    private static FrozenDictionary<string, object> CreateGlobalsLookup()
    {
        var globals = new Dictionary<string, object>
        {
            [BuiltInNames.NaN] = double.NaN,
            [BuiltInNames.Infinity] = double.PositiveInfinity,
            [BuiltInNames.Undefined] = Runtime.Types.SharpTSUndefined.Instance,
            [BuiltInNames.Fetch] = Runtime.Types.SharpTSFetchGlobal.Instance,

            // SharedArrayBuffer constructor
            [BuiltInNames.SharedArrayBuffer] = WorkerBuiltIns.SharedArrayBufferConstructor,

            // ArrayBuffer constructor
            [BuiltInNames.ArrayBuffer] = WorkerBuiltIns.ArrayBufferConstructor,

            // DataView constructor
            [BuiltInNames.DataView] = WorkerBuiltIns.DataViewConstructor,
        };

        // Add TypedArray constructors using centralized names
        foreach (var typedArrayName in BuiltInNames.TypedArrayNames)
        {
            globals[typedArrayName] = WorkerBuiltIns.GetTypedArrayConstructor(typedArrayName);
        }

        // Add Error constructors as global class variables
        // This enables typeof Error, class MyError extends Error, const E = Error, etc.
        var errorClass = new Runtime.Types.SharpTSErrorClass("Error", null);
        globals[BuiltInNames.Error] = errorClass;
        foreach (var errorTypeName in BuiltInNames.ErrorTypeNames)
        {
            if (errorTypeName != "Error")
                globals[errorTypeName] = new Runtime.Types.SharpTSErrorClass(errorTypeName, errorClass);
        }

        // Bare `Array` reference — needed for Array.prototype.X.apply() patterns
        // that real-world CJS packages (yaml, lodash internals) rely on.
        globals[BuiltInNames.Array] = Runtime.Types.SharpTSArrayGlobal.Instance;

        // Bare `Function` reference — required for `Function.prototype.call.bind(...)`
        // patterns used by test262 propertyHelper.js (and many libraries' native-
        // detection paths). Without this, the harness fails at load before any
        // test body runs.
        globals[BuiltInNames.Function] = Runtime.Types.SharpTSFunctionGlobal.Instance;

        // Node-style `global` alias for globalThis. CJS packages (lodash)
        // detect the global object via `typeof global == 'object'` and alias
        // its Array/Object/Date/etc. into a local scope.
        var gtSingleton = BuiltInRegistry.Instance.GetSingleton(BuiltInNames.GlobalThis);
        if (gtSingleton != null)
        {
            globals["global"] = gtSingleton;
        }

        // Add built-in singletons (Math, JSON, Object, etc.)
        // These are namespaces that resolve to singleton instances when accessed as variables
        string[] singletonNames =
        [
            BuiltInNames.Math, BuiltInNames.JSON, BuiltInNames.Object,
            BuiltInNames.Number, BuiltInNames.String, BuiltInNames.Boolean, BuiltInNames.Symbol,
            BuiltInNames.Console, BuiltInNames.Process, BuiltInNames.GlobalThis,
            BuiltInNames.Reflect, BuiltInNames.Promise, BuiltInNames.Atomics,
            "Buffer",
        ];
        foreach (var name in singletonNames)
        {
            var singleton = BuiltInRegistry.Instance.GetSingleton(name);
            if (singleton != null)
            {
                globals[name] = singleton;
            }
        }

        // Add built-in constructors as global variables (Map, Set, Date, RegExp, etc.)
        // Enables typeof Map, val instanceof Map, passing Map as value, Map.groupBy(), etc.
        foreach (var (name, factory) in BuiltInConstructorFactory.GetConstructors())
        {
            if (!globals.ContainsKey(name))
                globals[name] = new SharpTSBuiltInConstructor(name, factory);
        }

        // Expose global functions (parseFloat, parseInt, isNaN, isFinite,
        // structuredClone, setTimeout/clearTimeout, etc.) as first-class
        // callable values so they can be referenced by name — not just
        // invoked directly. CommonJS packages (lodash) alias `var
        // freeParseFloat = parseFloat`, and user code may do
        // `typeof parseFloat === 'function'`.
        string[] globalFunctionNames =
        [
            BuiltInNames.ParseInt, BuiltInNames.ParseFloat,
            BuiltInNames.IsNaN, BuiltInNames.IsFinite,
            BuiltInNames.StructuredClone,
            BuiltInNames.EncodeURIComponent, BuiltInNames.DecodeURIComponent,
            BuiltInNames.SetTimeout, BuiltInNames.ClearTimeout,
            BuiltInNames.SetInterval, BuiltInNames.ClearInterval,
            BuiltInNames.QueueMicrotask,
        ];
        foreach (var name in globalFunctionNames)
        {
            if (!globals.ContainsKey(name))
                globals[name] = new SharpTSGlobalFunction(name);
        }

        // Promise needs a bare-reference global so `x instanceof Promise`,
        // `typeof Promise === 'function'`, and stdlib modules that carry
        // Promise as a value can type-check/run. Its namespace is registered
        // as non-singleton (to preserve special `new Promise(executor)`
        // handling), so it wasn't picked up by the loops above. Register a
        // minimal constructor sentinel — `new Promise(executor)` has its
        // own dedicated path and does not route through this factory.
        if (!globals.ContainsKey(BuiltInNames.Promise))
        {
            globals[BuiltInNames.Promise] = new SharpTSBuiltInConstructor(
                BuiltInNames.Promise,
                _ => throw new Exception("Runtime Error: Use 'new Promise(executor)' syntax."));
        }

        return globals.ToFrozenDictionary();
    }

    private RuntimeEnvironment _environment = new();
    private readonly Dictionary<Expr, int> _locals = []; // Depth for resolved variables
    private TypeMap? _typeMap;

    // Evaluation contexts for unified sync/async handling
    private readonly SyncEvaluationContext _syncContext;
    private readonly AsyncEvaluationContext _asyncContext;

    // Cached wrappers for GlobalFunctionHandler delegate compatibility
    private Func<Expr, ValueTask<object?>>? _syncEvalWrapperCached;
    private Func<Expr, ValueTask<RuntimeValue>>? _syncEvalWrapperV2Cached;

    /// <summary>
    /// The TextWriter used for stdout output (console.log, process.stdout.write, etc.).
    /// Defaults to Console.Out when not explicitly provided.
    /// </summary>
    internal TextWriter Out { get; }

    /// <summary>
    /// The TextWriter used for stderr output (console.error, console.warn, etc.).
    /// Defaults to Console.Error when not explicitly provided.
    /// </summary>
    internal TextWriter Error { get; }

    /// <summary>
    /// The last uncaught top-level error swallowed by <see cref="Interpret"/>.
    /// <see cref="Interpret"/> intentionally catches a top-level guest
    /// <c>throw</c>, prints "Runtime Error: …" to <see cref="Out"/>, and returns
    /// normally (so the CLI prints the error without a .NET stack trace). That
    /// swallow hides the failure from hosts that bucket on a propagated
    /// exception — notably the Test262 runner, which would otherwise score a
    /// thrown assertion (or TypeError) as a Pass. Hosts that need to observe the
    /// failure read this after <see cref="Interpret"/> returns; it is reset to
    /// null at the start of each <see cref="Interpret"/> call.
    /// </summary>
    public Exception? LastUncaughtError { get; private set; }

    /// <summary>
    /// Gets the sync evaluation context for use in unified core methods.
    /// </summary>
    internal SyncEvaluationContext SyncContext => _syncContext;

    /// <summary>
    /// Gets the async evaluation context for use in unified core methods.
    /// </summary>
    internal AsyncEvaluationContext AsyncContext => _asyncContext;

    /// <summary>
    /// Returns the current <c>this</c> binding from the environment, or <c>null</c> if none is in scope.
    /// Used by built-in callables (e.g. Error constructor) that need access to the bound instance.
    /// </summary>
    internal object? GetCurrentThis()
    {
        if (_environment.TryGet("this", out var value))
            return value.ToObject();
        return null;
    }

    /// <summary>
    /// Initializes a new instance of the Interpreter with default Console output.
    /// </summary>
    public Interpreter() : this(Console.Out, Console.Error)
    {
    }

    /// <summary>
    /// Initializes a new instance of the Interpreter with custom output writers.
    /// </summary>
    /// <param name="stdout">TextWriter for stdout output. Used by console.log, process.stdout.write, etc.</param>
    /// <param name="stderr">TextWriter for stderr output. Used by console.error, console.warn, etc.</param>
    public Interpreter(TextWriter stdout, TextWriter stderr)
    {
        Out = stdout ?? throw new ArgumentNullException(nameof(stdout));
        Error = stderr ?? throw new ArgumentNullException(nameof(stderr));
        _syncContext = new SyncEvaluationContext(this);
        _asyncContext = new AsyncEvaluationContext(this);
    }

    // Per-realm RegExp.prototype. Held on the Interpreter (not on the
    // process-wide SharpTSBuiltInConstructor singleton) so user mutations
    // — `delete RegExp.prototype[Symbol.split]`, `Object.defineProperty`,
    // etc. — stay scoped to this realm. Lazily populated on first read of
    // `RegExp.prototype`.
    private Runtime.Types.SharpTSObject? _regExpPrototype;
    internal Runtime.Types.SharpTSObject GetRegExpPrototype()
        => _regExpPrototype ??= Runtime.BuiltIns.RegExpBuiltIns.BuildPrototype();

    // Module support
    private readonly Dictionary<string, ModuleInstance> _loadedModules = [];
    private ModuleResolver? _moduleResolver;
    private ParsedModule? _currentModule;
    private ModuleInstance? _currentModuleInstance;

    /// <summary>
    /// Gets or sets the path of the entry module (first module in InterpretModules).
    /// Used by the cluster module to re-execute the same script in worker threads.
    /// </summary>
    public string? EntryModulePath { get; set; }

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

    // Microtask queue - FIFO queue for microtasks (queueMicrotask callbacks, Promise callbacks).
    // Microtasks execute before any macrotasks (setTimeout/setInterval) - this is the JavaScript spec behavior.
    // Processed after each top-level statement and in the event loop before processing timers.
    private readonly Queue<Action> _microtaskQueue = new();
    private readonly object _microtaskQueueLock = new();

    // Active handles counter - keeps the event loop alive while there are active operations.
    // Uses Interlocked operations for thread-safe lock-free access, consistent with _hasScheduledTimers.
    // Synchronization strategy: all counters/flags use lock-free atomic operations for reads/writes,
    // while the timer queue itself uses _virtualTimersLock for compound operations.
    private volatile int _activeHandles;

    // Event loop infrastructure - BlockingCollection for efficient waiting (no polling)
    // SynchronizationContext routes async/await continuations back to the main thread
    private readonly System.Collections.Concurrent.BlockingCollection<Action> _callbackQueue = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private InterpreterSynchronizationContext? _eventLoopSyncContext;

    // VM timeout support — checked during statement execution to enforce script timeout
    private CancellationToken _vmTimeoutToken;

    /// <summary>
    /// Sets a cancellation token that will be checked during statement execution.
    /// Used by the vm module to enforce script execution timeouts.
    /// </summary>
    public void SetVmTimeoutToken(CancellationToken token) => _vmTimeoutToken = token;

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
    /// Registers a host-provided value as a global binding. Must be called
    /// before <see cref="Interpret"/> so the binding is visible at the
    /// outermost scope. Used by Test262 to inject the <c>$DONE</c>
    /// async-completion callback into <c>flags: [async]</c> tests.
    /// </summary>
    public void RegisterGlobal(string name, object? value) => _environment.Define(name, value);

    /// <summary>
    /// When set, yield expressions call this delegate instead of throwing YieldException.
    /// Used by the coroutine-based generator to suspend the worker thread at yield points
    /// without unwinding the call stack.
    /// Returns the value of the yield expression: for plain <c>yield</c>, the value sent
    /// via <c>g.next(v)</c> (currently always undefined); for <c>yield*</c>, the delegated
    /// iterator's return value per ECMA-262 §14.4.14.
    /// </summary>
    internal Func<object?, bool, object?>? YieldCallback { get; set; }

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
            catch (InvalidOperationException)
            {
                // Queue was completed between our check and the Add call - this is expected
                // during shutdown when multiple threads may be cleaning up concurrently.
                System.Diagnostics.Debug.WriteLine("WakeEventLoop: Queue already completed, ignoring wake request.");
            }
        }
    }

    /// <summary>
    /// Queues a microtask to be executed at the end of the current task.
    /// Microtasks execute before any macrotasks (setTimeout/setInterval callbacks).
    /// This is the JavaScript spec behavior for queueMicrotask() and Promise callbacks.
    /// </summary>
    /// <param name="callback">The callback function to execute as a microtask.</param>
    internal void QueueMicrotask(ISharpTSCallable callback)
    {
        lock (_microtaskQueueLock)
        {
            _microtaskQueue.Enqueue(() =>
            {
                if (!_isDisposed)
                {
                    try
                    {
                        callback.Call(this, []);
                    }
                    catch (Exception ex)
                    {
                        // Log uncaught exceptions from microtasks but don't crash
                        Error.WriteLine($"Uncaught exception in microtask: {ex.Message}");
                    }
                }
            });
        }
        // Wake the event loop to process microtasks promptly
        WakeEventLoop();
    }

    /// <summary>
    /// Processes all pending microtasks. Microtasks can queue more microtasks,
    /// which will be processed in the same flush (until the queue is empty).
    /// This ensures JavaScript-compliant microtask semantics.
    /// </summary>
    internal void ProcessMicrotasks()
    {
        while (true)
        {
            Action? microtask;
            lock (_microtaskQueueLock)
            {
                if (_microtaskQueue.Count == 0 || _isDisposed)
                    return;
                microtask = _microtaskQueue.Dequeue();
            }
            microtask();
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
            catch (InvalidOperationException)
            {
                // Queue was completed between our check and the Add call - this is expected
                // during shutdown. The callback will not be executed.
                System.Diagnostics.Debug.WriteLine("EnqueueCallback: Queue already completed, callback will not be executed.");
            }
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
    /// Thread-safe using lock-free atomic increment.
    /// </summary>
    internal void Ref()
    {
        Interlocked.Increment(ref _activeHandles);
    }

    /// <summary>
    /// Decrements the active handles count. When count reaches zero, the event loop can exit.
    /// Thread-safe using lock-free atomic decrement.
    /// </summary>
    internal void Unref()
    {
        int newValue = Interlocked.Decrement(ref _activeHandles);

        // Wake the event loop when count reaches zero so it can check exit conditions
        if (newValue == 0)
        {
            WakeEventLoop();
        }
    }

    /// <summary>
    /// Signals the event loop to shut down promptly.
    /// Unlike Dispose, this uses cooperative cancellation — the event loop exits
    /// at its next blocking point (TryTake) rather than waiting for handles to drain.
    /// Used by cluster worker Kill/Disconnect to implement Node.js-style prompt termination.
    /// </summary>
    internal void Shutdown()
    {
        try { _shutdownCts.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// Gets whether there are active handles keeping the event loop alive.
    /// Thread-safe - reads volatile int which is atomic on all .NET platforms.
    /// </summary>
    internal bool HasActiveHandles => _activeHandles > 0;

    /// <summary>
    /// Non-blocking event loop tick: drains pending microtasks, due timers, and queued callbacks.
    /// Used by the REPL to process async work between input lines without blocking.
    /// </summary>
    public void TickEventLoop()
    {
        // Process microtasks (Promise callbacks, queueMicrotask)
        ProcessMicrotasks();

        // Process due timers (setTimeout/setInterval)
        ProcessPendingCallbacks();

        // Drain any queued callbacks (async I/O completions, etc.)
        while (_callbackQueue.TryTake(out var action, TimeSpan.Zero))
        {
            try { action(); }
            catch (Exception ex)
            {
                Error.WriteLine($"Uncaught exception in callback: {ex.Message}");
            }
            ProcessMicrotasks();
        }
    }

    /// <summary>
    /// Waits for a promise to complete while processing timers and callbacks.
    /// Avoids a deadlock where GetResult() blocks the main thread but timers
    /// (which resolve the promise) only fire during event loop processing.
    /// Returns (without throwing) while the promise is still pending when the
    /// event loop has been provably quiescent — no active handles, scheduled
    /// timers, or queued callbacks — for a sustained window: nothing can ever
    /// settle the promise, and a forever-pending promise must not block exit
    /// (matches Node). Also honors the VM timeout token so a runaway wait is
    /// cancellable mid-loop, not just between statements.
    /// </summary>
    private void WaitForPromise(SharpTSPromise promise)
    {
        // Continuous quiescent time before concluding the promise can never
        // settle. Time-based, not iteration-based: a loaded thread pool can
        // delay an awaited continuation tens of ms with nothing visible to
        // HasPendingEventLoopWork, and Sleep(1) granularity differs by
        // platform (~15ms Windows, ~1ms Linux), so an iteration count meant
        // ~300ms on Windows but ~20ms on Linux — flaky under CI load.
        const long QuiescentMsBeforeGiveUp = 250;
        var quiescentTimer = new System.Diagnostics.Stopwatch();

        while (!promise.Task.IsCompleted)
        {
            if (_vmTimeoutToken.IsCancellationRequested)
                throw new Runtime.Exceptions.ThrowException(
                    new Runtime.Types.SharpTSError("Script execution timed out."));

            TickEventLoop();
            if (promise.Task.IsCompleted) break;

            if (HasPendingEventLoopWork())
            {
                quiescentTimer.Reset();
            }
            else
            {
                quiescentTimer.Start();
                if (quiescentTimer.ElapsedMilliseconds >= QuiescentMsBeforeGiveUp)
                    return; // never-settling — leave it pending rather than hang
            }

            Thread.Sleep(1);
        }
        // Rethrow if the promise was rejected
        promise.Task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// True when the event loop has work that could still settle a pending
    /// promise: an active handle (server, socket, in-flight I/O), a queued
    /// callback, or a scheduled non-cancelled timer.
    /// </summary>
    private bool HasPendingEventLoopWork()
    {
        if (HasActiveHandles) return true;
        if (_callbackQueue.Count > 0) return true;
        lock (_virtualTimersLock)
        {
            while (_virtualTimerQueue.TryPeek(out var timer, out _))
            {
                if (!timer.IsCancelled) return true;
                _virtualTimerQueue.Dequeue();
            }
        }
        return false;
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
        // Set up SynchronizationContext so async/await continuations come back to this thread.
        // InterpretModules also sets this up earlier so module-init awaits have the correct
        // context; this assignment is idempotent for the nested case.
        _eventLoopSyncContext ??= new InterpreterSynchronizationContext(EnqueueCallback);
        var previousSyncContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(_eventLoopSyncContext);

        try
        {
            var shutdownToken = _shutdownCts.Token;

            while (!_isDisposed)
            {
                // Exit immediately if there's no work keeping the loop alive
                if (!HasActiveHandles && _callbackQueue.Count == 0)
                {
                    break;
                }

                // Calculate timeout until next timer fires
                var timeout = GetNextTimerTimeout();

                // Efficient wait: blocks until callback arrives, timeout expires,
                // or shutdown is requested (via CancellationToken from Shutdown())
                if (_callbackQueue.TryTake(out var action, (int)timeout.TotalMilliseconds, shutdownToken))
                {
                    // Execute the queued callback (HTTP request handler, async continuation, etc.)
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        // Log uncaught exceptions but don't crash the event loop
                        Error.WriteLine($"Uncaught exception in event loop callback: {ex.Message}");
                    }
                }

                // Process microtasks first (queueMicrotask, Promise callbacks)
                // Microtasks always run before any macrotasks (timers)
                ProcessMicrotasks();

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
        catch (OperationCanceledException)
        {
            // Shutdown() was called — exit the event loop promptly.
            // This is the cooperative cancellation path used by cluster worker Kill/Disconnect.
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
            catch (ObjectDisposedException)
            {
                // Queue was already disposed by another thread (e.g., Dispose() called concurrently).
                // This is expected during forced shutdown scenarios.
                System.Diagnostics.Debug.WriteLine("RunEventLoop: Queue already disposed during cleanup.");
            }
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
                Error.WriteLine($"Uncaught exception during event loop drain: {ex.Message}");
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
        // Process microtasks first - they always run before any macrotask (timers)
        // This ensures correct JavaScript event loop semantics during busy-wait loops
        ProcessMicrotasks();

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
        if (_isDisposed)
        {
            // Already disposed - idempotent disposal pattern
            return;
        }

        _isDisposed = true;

        // Signal the event loop to exit via cooperative cancellation
        try { _shutdownCts.Cancel(); }
        catch (ObjectDisposedException) { }

        // Complete the callback queue to unblock any waiting TryTake
        try { _callbackQueue.CompleteAdding(); }
        catch (ObjectDisposedException)
        {
            // Queue was already disposed - can happen if Dispose is called from multiple threads
            // or if RunEventLoop's finally block ran first.
            System.Diagnostics.Debug.WriteLine("Dispose: Queue already disposed during CompleteAdding.");
        }

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
        catch (ObjectDisposedException)
        {
            // Queue was already disposed - safe to ignore as we're cleaning up anyway.
            System.Diagnostics.Debug.WriteLine("Dispose: Queue already disposed during Dispose call.");
        }

        try { _shutdownCts.Dispose(); }
        catch (ObjectDisposedException) { }

        // Reset singletons to prevent listener/state leakage
        // across interpreter runs (e.g., in test suites or REPL restarts).
        Runtime.Types.SharpTSStdin.Instance.ResetReadableState();
        Runtime.Types.SharpTSStdout.Instance.ResetWritableState();
        Runtime.Types.SharpTSStderr.Instance.ResetWritableState();
        Runtime.Types.SharpTSAgent.ResetGlobalAgent();

        GC.SuppressFinalize(this);
    }

    public void Resolve(Expr expr, int depth)
    {
        _locals[expr] = depth;
    }

    private object? LookupVariable(Token name, Expr expr) => LookupVariableRV(name, expr).ToObject();

    /// <summary>
    /// Looks up a variable and returns its value as RuntimeValue without boxing.
    /// This is the fast path for variable access in expressions.
    /// </summary>
    private RuntimeValue LookupVariableRV(Token name, Expr expr)
    {
        // Fast path: resolved locals with known depth
        if (_locals.TryGetValue(expr, out int distance))
        {
            return _environment.GetAt(distance, name.Lexeme);
        }

        // Scope chain traversal for user-defined variables
        // User variables can shadow built-in globals, so check environment first
        if (_environment.TryGet(name.Lexeme, out RuntimeValue rv))
        {
            return rv;
        }

        // Check global constants and built-in singletons (single frozen dictionary lookup)
        // This handles: NaN, Infinity, undefined, Math, JSON, Object, console, process, etc.
        if (_globalConstants.TryGetValue(name.Lexeme, out var constant))
        {
            return RuntimeValue.FromBoxed(constant);
        }

        // Check for Node.js module globals (__dirname, __filename)
        if (name.Lexeme == "__filename") return RuntimeValue.FromString(_currentModule?.Path ?? "");
        if (name.Lexeme == "__dirname") return RuntimeValue.FromString(Path.GetDirectoryName(_currentModule?.Path) ?? "");

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
        LastUncaughtError = null;
        ProcessBuiltIns.ResetScriptStartTime();
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
                    try
                    {
                        object? result = Evaluate(exprStmt.Expr);
                        // Wait for top-level Promises to complete before continuing
                        if (result is SharpTSPromise promise)
                        {
                            WaitForPromise(promise);
                        }
                    }
                    catch (ThrowException tex)
                    {
                        LastUncaughtError = tex;
                        Out.WriteLine($"Runtime Error: {Stringify(tex.Value)}");
                        return;
                    }
                }
                else
                {
                    var result = Execute(statement);
                    if (result.Type == ExecutionResult.ResultType.Throw)
                    {
                        LastUncaughtError = ThrowException.FromResult(result.Value.ToObject());
                        Out.WriteLine($"Runtime Error: {Stringify(result.Value.ToObject())}");
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
            Out.WriteLine($"Runtime Error: {error.Message}");
            throw;
        }
    }

    /// <summary>
    /// Interprets statements and returns the value of the last expression statement.
    /// Used by the REPL to auto-display expression results.
    /// </summary>
    /// <returns>The value of the last expression statement, or null for declarations.</returns>
    public object? InterpretRepl(List<Stmt> statements, TypeMap? typeMap = null)
    {
        _typeMap = typeMap;
        object? lastExprValue = null;

        // Hoist function declarations first
        HoistFunctionDeclarations(statements);

        foreach (Stmt statement in statements)
        {
            // Check vm timeout token before each statement
            if (_vmTimeoutToken.IsCancellationRequested)
                throw new Runtime.Exceptions.ThrowException(
                    new Runtime.Types.SharpTSError("Script execution timed out."));

            if (statement is Stmt.Expression exprStmt)
            {
                lastExprValue = Evaluate(exprStmt.Expr);
                if (lastExprValue is SharpTSPromise promise)
                {
                    WaitForPromise(promise);
                    // WaitForPromise escapes on a never-settling promise; only
                    // unwrap when it actually completed (Result would deadlock).
                    if (promise.Task.IsCompleted)
                        lastExprValue = promise.Task.Result;
                }
            }
            else
            {
                lastExprValue = null;
                var result = Execute(statement);
                if (result.Type == ExecutionResult.ResultType.Throw)
                {
                    throw new InvalidOperationException($"Runtime Error: {Stringify(result.Value.ToObject())}");
                }
                if (result.IsAbrupt)
                {
                    return null;
                }
            }
        }

        return lastExprValue;
    }

    /// <summary>
    /// Implements the global <c>eval(source)</c> function. Lexes, parses, and interprets
    /// <paramref name="source"/> in the interpreter's current environment, returning the
    /// completion value (the value of the last expression statement, or <c>undefined</c>).
    /// </summary>
    /// <remarks>
    /// This is "direct eval" semantics: the evaluated code runs against the current scope
    /// chain. It is intentionally NOT type-checked — <c>eval</c> is typed as
    /// <c>(s: string) =&gt; any</c>, matching tsc, so the string body is dynamic. The
    /// variable resolver is also skipped so identifier lookups fall back to runtime
    /// scope-chain traversal (<see cref="LookupVariableRV"/>), which resolves names against
    /// the live caller environment rather than a from-scratch resolution that would compute
    /// wrong scope depths. A parse failure throws a <c>SyntaxError</c>.
    /// </remarks>
    public object? Eval(string source)
    {
        var lexer = new Lexer(source);
        List<Token> tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        var parseResult = parser.Parse();
        if (!parseResult.IsSuccess)
        {
            var detail = parseResult.Diagnostics.Count > 0
                ? parseResult.Diagnostics[0].ToString()
                : "invalid syntax";
            throw new ThrowException(new SharpTSError($"SyntaxError: {detail}"));
        }

        // Preserve the outer type map: InterpretRepl assigns _typeMap, and passing null
        // would clobber type-aware dispatch for the remainder of the outer program.
        return InterpretRepl(parseResult.Statements, _typeMap);
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

        // Capture entry module path for cluster.fork() support
        if (modules.Count > 0 && EntryModulePath == null)
        {
            EntryModulePath = modules[^1].Path;
        }

        try
        {
            // Create a shared script environment for script files (they share global scope)
            var scriptEnv = new RuntimeEnvironment(_environment);

            // Determine the entry module — the last one in topological order — so we can run
            // CJS modules lazily. Pre-emptive init of CJS modules would change visible execution
            // order in circular-require scenarios (a non-entry CJS file would run before the
            // entry, inverting the require()-trigger semantics that real Node packages depend on).
            ParsedModule? entryModule = modules.Count > 0 ? modules[^1] : null;

            foreach (var module in modules)
            {
                if (module.IsScript)
                {
                    ExecuteScriptFile(module, scriptEnv);
                }
                else if (module.IsCommonJs)
                {
                    // Only the entry CJS module is initialized eagerly. Non-entry CJS modules
                    // wait for require() to trigger them.
                    if (module == entryModule)
                    {
                        ExecuteModule(module);
                    }
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
        catch (ThrowException tex)
        {
            Out.WriteLine($"Runtime Error: {Stringify(tex.Value)}");
            throw;
        }
        catch (Exception error)
        {
            Out.WriteLine($"Runtime Error: {error.Message}");
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
                        WaitForPromise(promise);
                    }
                }
                else
                {
                    var result = Execute(stmt);
                    if (result.Type == ExecutionResult.ResultType.Throw)
                    {
                        throw new InterpreterException(Stringify(result.Value.ToObject()));
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
        if (!_environment.TryGet(mainFunc.Name.Lexeme, out RuntimeValue mainRV))
            return;

        if (mainRV.ToObject() is not SharpTSFunction mainFn)
            return;

        // Call main with process.argv (pass args even if main() doesn't take them - JS allows this)
        var argv = ProcessBuiltIns.GetArgv();
        // Pass argv only if main expects it
        object? result = mainFunc.Parameters.Count == 0
            ? mainFn.Call(this, [])
            : mainFn.Call(this, [argv]);

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
        // CommonJS modules go through their own execution path which sets up the
        // synthetic require/module/exports scope.
        if (module.IsCommonJs)
        {
            if (!_loadedModules.ContainsKey(module.Path))
            {
                ExecuteCommonJsModule(module);
            }
            return;
        }

        // Create module instance to track exports (TryAdd returns false if already executed)
        var moduleInstance = new ModuleInstance();
        if (!_loadedModules.TryAdd(module.Path, moduleInstance))
        {
            return;
        }

        // Handle built-in modules specially - populate exports from interpreter implementations
        if (module.IsBuiltIn)
        {
            // Primitive modules (primitive:os, etc.) share dispatch with the C# built-ins
            // but live in a separate registry that user code cannot import.
            var primitiveName = PrimitiveRegistry.GetPrimitiveName(module.Path);
            if (primitiveName != null && PrimitiveModuleValues.HasInterpreterSupport(primitiveName))
            {
                var primitiveExports = PrimitiveModuleValues.GetPrimitiveExports(primitiveName);
                foreach (var (name, value) in primitiveExports)
                {
                    moduleInstance.SetExport(name, value);
                }
                moduleInstance.DefaultExport = moduleInstance.ExportsAsObject();
                moduleInstance.IsExecuted = true;
                return;
            }

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
                        WaitForPromise(promise);
                    }
                }
                else
                {
                    var result = Execute(stmt);
                    if (result.Type == ExecutionResult.ResultType.Throw)
                    {
                        throw new InterpreterException(Stringify(result.Value.ToObject()));
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

                // CJS modules are lazy-initialized — ESM imports of CJS need to trigger init now
                // because BindModuleImports runs BEFORE the body executes (so we can't rely on a
                // later require() call to set things up).
                if (importedModuleInstance == null)
                {
                    var importedParsed = _moduleResolver.GetCachedModule(importedPath);
                    if (importedParsed?.IsCommonJs == true)
                    {
                        ExecuteCommonJsModule(importedParsed);
                        importedModuleInstance = _loadedModules.GetValueOrDefault(importedPath);
                    }
                }

                if (importedModuleInstance == null)
                {
                    throw new InterpreterException($"Module '{import.ModulePath}' not loaded.");
                }

                // For CJS imports, the exports live on the live `module.exports` object.
                // Resolve once and reuse for all import forms in this statement.
                bool isCjsSource = importedModuleInstance.CommonJsModuleObject != null;
                object? cjsExports = isCjsSource
                    ? importedModuleInstance.CommonJsModuleObject!.GetProperty("exports")
                    : null;

                // Default import
                if (import.DefaultImport != null)
                {
                    if (isCjsSource)
                    {
                        env.Define(import.DefaultImport.Lexeme, cjsExports);
                    }
                    else
                    {
                        env.Define(import.DefaultImport.Lexeme, importedModuleInstance.DefaultExport);
                    }
                }

                // Namespace import: import * as Module from './file'
                if (import.NamespaceImport != null)
                {
                    if (isCjsSource)
                    {
                        env.Define(import.NamespaceImport.Lexeme, cjsExports);
                    }
                    else
                    {
                        env.Define(import.NamespaceImport.Lexeme, importedModuleInstance.ExportsAsObject());
                    }
                }

                // Named imports: import { x, y as z } from './file'
                // Skip individual type-only specifiers
                if (import.NamedImports != null)
                {
                    foreach (var spec in import.NamedImports.Where(s => !s.IsTypeOnly))
                    {
                        string importedName = spec.Imported.Lexeme;
                        string localName = spec.LocalName?.Lexeme ?? importedName;
                        object? value;
                        if (isCjsSource)
                        {
                            // Named CJS exports can be either plain fields (`exports.foo = ...`)
                            // or accessor properties (Babel's transpiled `export { foo }` emits
                            // `Object.defineProperty(exports, "foo", { get() { return _m.default; } })`).
                            // Route through the full property-access path so getters are invoked;
                            // a direct _fields read would skip them and bind undefined.
                            value = cjsExports is SharpTSObject so
                                ? EvaluateGetOnRecord(so, importedName)
                                : null;
                        }
                        else
                        {
                            value = importedModuleInstance.GetExport(importedName);
                        }
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
                var value = _environment.Get(spec.LocalName).ToObject();
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

            // CJS sources are lazy-initialized; trigger init so we have exports to read.
            // Mirrors the import-side trigger in BindModuleImports.
            if (sourceModuleInstance == null)
            {
                var sourceParsed = _moduleResolver.GetCachedModule(sourcePath);
                if (sourceParsed?.IsCommonJs == true)
                {
                    ExecuteCommonJsModule(sourceParsed);
                    sourceModuleInstance = _loadedModules.GetValueOrDefault(sourcePath);
                }
            }

            if (sourceModuleInstance != null && _currentModuleInstance != null)
            {
                // For CJS sources, read from the live module.exports object via the full
                // property-access path so accessor-defined named exports work (matching the
                // import-side fix). ESM sources use the static Exports dictionary as before.
                SharpTSObject? cjsExports = sourceModuleInstance.CommonJsModuleObject != null
                    ? sourceModuleInstance.CommonJsModuleObject.GetProperty("exports") as SharpTSObject
                    : null;

                if (export.NamedExports != null)
                {
                    // Re-export specific names
                    foreach (var spec in export.NamedExports)
                    {
                        string importedName = spec.LocalName.Lexeme;
                        string exportedName = spec.ExportedName?.Lexeme ?? importedName;
                        object? value = cjsExports != null
                            ? EvaluateGetOnRecord(cjsExports, importedName)
                            : sourceModuleInstance.GetExport(importedName);
                        _currentModuleInstance.SetExport(exportedName, value);
                    }
                }
                else if (cjsExports != null)
                {
                    // Re-export all from a CJS source: enumerate both data fields and accessor
                    // properties. Skip the __esModule interop marker (Babel-style CJS emits it
                    // to signal "this is an ES-module-shaped object" — it should not leak as a
                    // named export of the re-exporting ESM module).
                    foreach (var name in cjsExports.Fields.Keys)
                    {
                        if (name == "__esModule") continue;
                        _currentModuleInstance.SetExport(name, EvaluateGetOnRecord(cjsExports, name));
                    }
                    foreach (var name in cjsExports.AccessorPropertyNames)
                    {
                        if (name == "__esModule") continue;
                        if (cjsExports.Fields.ContainsKey(name)) continue;
                        _currentModuleInstance.SetExport(name, EvaluateGetOnRecord(cjsExports, name));
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
        return _environment.Get(token).ToObject();
    }

    /// <summary>
    /// Internal wrapper for Execute that allows evaluation contexts to dispatch statements.
    /// </summary>
    /// <param name="stmt">The statement to execute.</param>
    /// <returns>The execution result.</returns>
    internal ExecutionResult ExecuteStatement(Stmt stmt) => Execute(stmt);

    /// <summary>
    /// Internal async wrapper for statement execution using registry-based dispatch.
    /// Uses DispatchStmtAsync which falls back to sync handlers when no async handler exists.
    /// </summary>
    /// <param name="stmt">The statement to execute.</param>
    /// <returns>A task containing the execution result.</returns>
    internal async Task<ExecutionResult> ExecuteStatementAsync(Stmt stmt)
    {
        return await _registry.DispatchStmtAsync(stmt, this);
    }

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
        object? value = SharpTSUndefined.Instance;
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
        // @DotNetType declare class: bind a DotNet wrapper into the environment
        // instead of creating an empty SharpTSClass. Non-DotNet declare classes still
        // fall through and produce an empty SharpTSClass for type-only compatibility.
        if (classStmt.IsDeclare)
        {
            if (TryRegisterDotNetType(classStmt)) return ExecutionResult.Success();
        }

        object? superclass = null;
        if (classStmt.SuperclassExpr != null)
        {
            superclass = Evaluate(classStmt.SuperclassExpr);

            if (superclass is not SharpTSClass)
            {
                throw new InterpreterException("Superclass must be a class.");
            }
        }

        _environment.Define(classStmt.Name.Lexeme, null);

        if (classStmt.SuperclassExpr != null)
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
            if (method.IsGenerator && method.IsAsync)
                func = new SharpTSAsyncGeneratorFunction(method, _environment);
            else if (method.IsAsync)
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
        Dictionary<string, SharpTSFunction> staticGetters = [];
        Dictionary<string, SharpTSFunction> staticSetters = [];

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

                var targetGet = accessor.IsStatic ? staticGetters : getters;
                var targetSet = accessor.IsStatic ? staticSetters : setters;

                if (accessor.Kind.Type == TokenType.GET)
                {
                    targetGet[accessor.Name.Lexeme] = func;
                }
                else
                {
                    targetSet[accessor.Name.Lexeme] = func;
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

        // If the superclass is an Error type, create a SharpTSErrorClass so that
        // instances carry error fields (name, message, stack) and instanceof works.
        SharpTSClass klass = superclass is SharpTSErrorClass errorSuper
            ? new SharpTSErrorClass(
                classStmt.Name.Lexeme,
                errorSuper,
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
                staticAutoAccessors.Count > 0 ? staticAutoAccessors : null,
                staticGetters.Count > 0 ? staticGetters : null,
                staticSetters.Count > 0 ? staticSetters : null)
            : new SharpTSClass(
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
                staticAutoAccessors.Count > 0 ? staticAutoAccessors : null,
                staticGetters.Count > 0 ? staticGetters : null,
                staticSetters.Count > 0 ? staticSetters : null);

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
                                        throw new InterpreterException($"Error in static block: {Stringify(result.Value.ToObject())}");
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

        if (classStmt.SuperclassExpr != null)
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
        Out.WriteLine(Stringify(Evaluate(printStmt.Expr)));
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
