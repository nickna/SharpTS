using SharpTS.Modules;
using SharpTS.Modules.Stdlib;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.Execution.Debugging;
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
///   <item><description>Interpreter.cs - Core infrastructure, event loop, module execution</description></item>
///   <item><description>Interpreter.Realm.cs - Realm/global state (globals table, symbol registry, primitive prototypes, globalThis routing)</description></item>
///   <item><description>Interpreter.Statements.cs - Statement dispatch, Visit* handlers, and execution helpers (block, switch, try/catch, loops)</description></item>
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
    private RuntimeEnvironment _environment = new();
    internal InterpreterDebugHost? DebugHost { get; set; }
    internal InterpreterDebugController? DebugController { get; set; }
    // Keyed by AST-node identity, not structural value. The resolver stores and reads the
    // same Expr instance, so reference identity is the intent. Expr is a record, so the
    // default comparer would recursively hash an assignment's RHS subtree (Assign/
    // CompoundAssign/LogicalAssign nest an Expr Value) on every probe — e.g. `sum += arr[i]`
    // would re-hash the whole RHS each loop iteration. ReferenceEqualityComparer reduces every
    // probe to GetHashCode(object)+ReferenceEquals with no recursion. Same pattern as TypeMap.
    private readonly Dictionary<Expr, int> _locals = new(Runtime.Types.ReferenceEqualityComparer.Instance); // Depth for resolved variables
    private TypeMap? _typeMap;

    // Evaluation contexts for unified sync/async handling
    private readonly SyncEvaluationContext _syncContext;
    private readonly AsyncEvaluationContext _asyncContext;

    // Cached wrapper for GlobalFunctionHandlerV2 delegate compatibility
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

    /// <summary>
    /// Worker-thread bindings for the <c>worker_threads</c> built-in module. Non-null
    /// only on the isolated interpreter that runs a Worker's script. It makes
    /// <c>import { workerData, parentPort, threadId, isMainThread } from "worker_threads"</c>
    /// resolve to this worker's live values instead of the main-thread <c>null</c>
    /// placeholders, so a worker can read its inputs via the canonical import form and
    /// not only the bare worker-context globals (#410). <see cref="WorkerThreadsBindings.ParentPort"/>
    /// is the same port instance bound as the bare <c>parentPort</c> global, so a
    /// <c>message</c> listener attached through the import receives the messages the
    /// worker's message loop delivers to that instance.
    /// </summary>
    internal WorkerThreadsBindings? WorkerThreadsContext { get; set; }

    /// <summary>Live <c>worker_threads</c> values for the running Worker (see <see cref="WorkerThreadsContext"/>).</summary>
    internal sealed record WorkerThreadsBindings(object? WorkerData, object? ParentPort, double ThreadId);

    // Flag to indicate interpreter has been disposed - timer callbacks should not execute
    private volatile bool _isDisposed;

    // Console execution waits for promise-valued top-level expression statements.
    // Hosted scripts/CommonJS disable that legacy convention; hosted ESM uses the
    // resumable async statement path in Interpreter.Hosting.cs instead.
    private bool _waitForTopLevelPromises = true;

    /// <summary>
    /// Controls the legacy console convention that awaits promise-valued
    /// expression statements. Script hosts such as Test262 disable this for
    /// ordinary scripts because ECMAScript does not implicitly await them.
    /// </summary>
    public bool WaitForTopLevelPromises
    {
        get => _waitForTopLevelPromises;
        set => _waitForTopLevelPromises = value;
    }

    // Track all pending timers for cleanup on disposal
    private readonly System.Collections.Concurrent.ConcurrentBag<Runtime.Types.SharpTSTimeout> _pendingTimers = new();

    // Virtual timer system - timers are checked and executed on the main thread during loop iterations.
    // This avoids thread scheduling issues on macOS where background threads may not get CPU time.
    // Uses PriorityQueue for O(log n) insert and O(log n) extraction of due timers.
    // Priority is (fireTime, sequence): PriorityQueue is not FIFO-stable for equal
    // priorities, and two 0-ms timers scheduled within the same millisecond MUST fire
    // in schedule order (e.g. a socket's 'data' before its read-EOF 'end'/'close').
    private readonly PriorityQueue<VirtualTimer, (long FireTime, long Seq)> _virtualTimerQueue = new();
    private long _timerSequence;
    private readonly object _virtualTimersLock = new();
    // Volatile flag for O(1) "queue empty" check without acquiring lock
    private volatile bool _hasScheduledTimers;

    // Microtask queue - FIFO queue for microtasks (queueMicrotask callbacks, Promise callbacks).
    // Microtasks execute before any macrotasks (setTimeout/setInterval) - this is the JavaScript spec behavior.
    // Processed after each top-level statement and in the event loop before processing timers.
    private readonly Queue<Action> _microtaskQueue = new();
    private readonly object _microtaskQueueLock = new();
    // Producers publish this flag while holding the queue lock. Empty checks on
    // ordinary loop iterations can then avoid acquiring that lock altogether.
    private int _hasMicrotasks;

    // Active handles counter - keeps the event loop alive while there are active operations.
    // Uses Interlocked operations for thread-safe lock-free access, consistent with _hasScheduledTimers.
    // Synchronization strategy: all counters/flags use lock-free atomic operations for reads/writes
    // (Interlocked/Volatile, so the field itself is deliberately not `volatile`),
    // while the timer queue itself uses _virtualTimersLock for compound operations.
    private int _activeHandles;

    // Event loop infrastructure - BlockingCollection for efficient waiting (no polling)
    // SynchronizationContext routes async/await continuations back to the main thread
    private readonly System.Collections.Concurrent.BlockingCollection<Action> _callbackQueue = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly OwnedProcessRegistry _ownedProcesses = new();
    private InterpreterSynchronizationContext? _eventLoopSyncContext;

    // VM timeout support — checked during statement execution to enforce script timeout
    private CancellationToken _vmTimeoutToken;

    /// <summary>
    /// Sets a cancellation token that will be checked during statement execution.
    /// Used by the vm module to enforce script execution timeouts.
    /// </summary>
    public void SetVmTimeoutToken(CancellationToken token) => _vmTimeoutToken = token;

    // vm codeGeneration.strings:false support — when true, eval()/new Function() throw
    // an EvalError in this (sub-)interpreter, matching a vm context created with
    // codeGeneration:{ strings:false }.
    private bool _vmCodeGenerationStringsDisabled;

    /// <summary>
    /// Disables runtime code generation from strings (eval / new Function) in this
    /// interpreter. Used by the vm module to honor codeGeneration:{ strings:false }.
    /// </summary>
    public void DisableCodeGenerationFromStrings() => _vmCodeGenerationStringsDisabled = true;

    /// <summary>Whether eval/new Function are disabled (codeGeneration.strings:false).</summary>
    public bool IsCodeGenerationFromStringsDisabled => _vmCodeGenerationStringsDisabled;

    // vm importModuleDynamically support — when set, a dynamic import() inside vm-executed
    // code is resolved through this hook (specifier → module namespace) instead of the
    // normal module resolver. See vm #1156.
    private Func<string, object?>? _vmDynamicImportHook;

    /// <summary>
    /// Routes dynamic import() in this interpreter through a user hook (the vm
    /// importModuleDynamically option). The hook maps a specifier to a module namespace.
    /// </summary>
    public void SetVmDynamicImportHook(Func<string, object?>? hook) => _vmDynamicImportHook = hook;

    // Worker-termination support — a worker sets this to its CancellationToken so a
    // synchronous runtime op that observes it (Atomics.wait) can unwind the worker thread
    // when worker.terminate() cancels it. Distinct from _vmTimeoutToken so the worker abort
    // raises a non-catchable WorkerTerminatedException rather than a guest "timed out" throw.
    private CancellationToken _workerTerminationToken;

    /// <summary>
    /// Associates this interpreter with the owning worker's cancellation token so blocking
    /// runtime operations (notably <c>Atomics.wait</c>) can be woken by <c>worker.terminate()</c>.
    /// </summary>
    public void SetWorkerTerminationToken(CancellationToken token) => _workerTerminationToken = token;

    /// <summary>
    /// The owning worker's termination token, or a non-cancelable default on the main thread.
    /// </summary>
    public CancellationToken WorkerTerminationToken => _workerTerminationToken;

    /// <summary>
    /// Checks host-controlled execution cancellation at interpreter safe points. Worker
    /// termination is deliberately not a guest exception and therefore bypasses guest catch
    /// blocks; VM timeouts retain their existing catchable Error value.
    /// </summary>
    private void ThrowIfExecutionCancelled()
    {
        if (_workerTerminationToken.IsCancellationRequested)
            throw new Runtime.Exceptions.WorkerTerminatedException();
        if (_vmTimeoutToken.IsCancellationRequested)
        {
            throw new Runtime.Exceptions.ThrowException(
                new Runtime.Types.SharpTSError("Script execution timed out."));
        }
    }

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

    internal IDisposable EnterDebugFrame(string name, RuntimeEnvironment environment, object declaration) =>
        DebugController?.EnterFrame(name, environment, declaration) ?? NoopDisposable.Instance;

    private sealed class NoopDisposable : IDisposable
    {
        internal static NoopDisposable Instance { get; } = new();
        public void Dispose() { }
    }

    /// <summary>
    /// Registers a host-provided value as a global binding. Must be called
    /// before <see cref="Interpret"/> so the binding is visible at the
    /// outermost scope. Used by Test262 to inject the <c>$DONE</c>
    /// async-completion callback into <c>flags: [async]</c> tests.
    /// </summary>
    public void RegisterGlobal(string name, object? value)
    {
        _environment.Define(name, value);
        // Host-injected globals are properties of the realm's global object,
        // not lexical-only bindings. Test262's asyncHelpers intentionally checks
        // hasOwnProperty(globalThis, "$DONE") before using the callback.
        GlobalThis.SetProperty(name, value);
    }

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
    /// The async generator whose body is currently executing, if any. An async generator body runs as
    /// an ordinary interpreter async execution on the single event-loop thread; this binding lets the
    /// async <c>yield</c> evaluator (<see cref="EvaluateYieldAsync"/>) suspend through the right
    /// generator. It is re-asserted across every suspension the body crosses — guest awaits restore it
    /// via <see cref="AwaitPreservingEnvironment"/>, and the generator re-asserts it itself at each
    /// <c>yield</c> resume — so interleaved async generators never observe each other's binding.
    /// </summary>
    internal Runtime.Types.SharpTSAsyncGenerator? CurrentAsyncGenerator { get; set; }

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
    // Monotonic clock for the virtual-timer queue. FireTime values are compared
    // only against this same clock, never against wall time, so an NTP step or a
    // manual clock change cannot fire every pending setTimeout early or stall it
    // for the offset (same non-monotonic-clock bug class as the process.uptime()
    // Stopwatch fix).
    private static readonly System.Diagnostics.Stopwatch _timerClock = System.Diagnostics.Stopwatch.StartNew();
    private static long TimerNowMs => _timerClock.ElapsedMilliseconds;

    internal VirtualTimer ScheduleTimer(int delayMs, int intervalMs, Action callback, bool isInterval)
    {
        var now = TimerNowMs;
        var fireTime = now + delayMs;
        var timer = new VirtualTimer(fireTime, intervalMs, callback, isInterval);
        lock (_virtualTimersLock)
        {
            _virtualTimerQueue.Enqueue(timer, (fireTime, _timerSequence++));
            _hasScheduledTimers = true;
        }
        if (_hostedTimerChanged != null)
        {
            _hostedTimerChanged();
            return timer;
        }
        // Always wake the event loop: it may be blocked in a wait whose timeout was
        // computed before this timer existed (up to 60s when the queue was empty), so a
        // cross-thread schedule with any delay must force a timeout recomputation.
        WakeEventLoop();
        return timer;
    }

    /// <summary>
    /// Wakes the event loop by enqueueing a no-op action.
    /// Used when a timer or other operation needs prompt processing.
    /// </summary>
    private void WakeEventLoop()
    {
        if (_hostedWorkAvailable != null)
        {
            _hostedWorkAvailable();
            return;
        }

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

    /// <summary>Wakes an idle interpreter so a cooperative debugger pause can converge.</summary>
    internal void WakeDebugger() => WakeEventLoop();

    private void DebuggerIdleCheckpoint() => DebugController?.OnIdleSafePoint(this);

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
                        if (_hostedUnhandledError != null)
                            _hostedUnhandledError(ex);
                        else
                            Error.WriteLine($"Uncaught exception in microtask: {ex.Message}");
                    }
                }
            });
            Volatile.Write(ref _hasMicrotasks, 1);
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
        if (Volatile.Read(ref _hasMicrotasks) == 0)
            return;

        while (true)
        {
            Action? microtask;
            lock (_microtaskQueueLock)
            {
                if (_microtaskQueue.Count == 0 || _isDisposed)
                {
                    Volatile.Write(ref _hasMicrotasks, 0);
                    return;
                }
                microtask = _microtaskQueue.Dequeue();
            }
            microtask();
        }
    }

    private bool HasMicrotasks()
    {
        return Volatile.Read(ref _hasMicrotasks) != 0;
    }

    /// <summary>
    /// Enqueues a callback to be executed on the main event loop thread.
    /// Thread-safe - can be called from any thread (HTTP accept loop, async I/O, etc).
    /// </summary>
    /// <param name="action">The callback action to execute on the main thread.</param>
    internal void EnqueueCallback(Action action)
    {
        if (_hostedWorkAvailable != null && !_hostedAcceptingWork)
            return;

        if (!_isDisposed && !_callbackQueue.IsAddingCompleted)
        {
            try
            {
                _callbackQueue.Add(action);
                _hostedWorkAvailable?.Invoke();
            }
            catch (InvalidOperationException)
            {
                // Queue was completed between our check and the Add call - this is expected
                // during shutdown. The callback will not be executed.
                System.Diagnostics.Debug.WriteLine("EnqueueCallback: Queue already completed, callback will not be executed.");
            }
        }
    }

    /// <summary>
    /// Adopts an inner promise's settled state into <paramref name="tcs"/> — the
    /// <c>resolve(thenable)</c> / await-thenable flatten step where the resolution
    /// value is itself a <see cref="Runtime.Types.SharpTSPromise"/>. The settle is
    /// delivered as an event-loop callback so it (a) runs on the event-loop thread
    /// and (b) is visible to the loop's exit check (<c>_callbackQueue</c>).
    /// </summary>
    /// <remarks>
    /// The previous implementation used <c>innerTask.ContinueWith(…, TaskScheduler.Default)</c>,
    /// which settled <paramref name="tcs"/> on a thread-pool thread with nothing on
    /// the callback queue. When the inner task was already settled (e.g. resolving a
    /// pending promise with an already-resolved one — Test262
    /// <c>Promise/resolve-thenable-deferred</c>), the event loop could observe
    /// "no active handles AND empty callback queue" and exit before the thread-pool
    /// continuation settled the outer promise, so its <c>.then</c> reaction never
    /// ran. Whether the loop won that race depended on scheduling, so the test
    /// flipped Pass/Fail under machine load.
    /// <para>
    /// <see cref="TaskContinuationOptions.ExecuteSynchronously"/> means an
    /// already-settled inner enqueues the settle callback inline (during
    /// <c>resolve</c>, before the loop starts), so the loop never starts idle; a
    /// never-settling inner enqueues nothing and the loop exits normally — matching
    /// Node, where a pending adoption does not by itself keep the program alive.
    /// Settling the outer task here synchronously posts any downstream <c>.then</c>
    /// continuations onto this loop via the interpreter SynchronizationContext.
    /// </para>
    /// </remarks>
    internal void AdoptInnerPromise(Task<object?> innerTask, TaskCompletionSource<object?> tcs)
    {
        innerTask.ContinueWith(
            t => EnqueueCallback(() =>
            {
                if (t.IsFaulted)
                    tcs.TrySetException(t.Exception!.InnerException ?? t.Exception);
                else if (t.IsCanceled)
                    tcs.TrySetCanceled();
                else
                    tcs.TrySetResult(t.Result);
            }),
            TaskContinuationOptions.ExecuteSynchronously);
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

            if (!_virtualTimerQueue.TryPeek(out _, out var priority))
            {
                _hasScheduledTimers = false;
                return TimeSpan.FromSeconds(60);
            }

            var now = TimerNowMs;
            var ms = priority.FireTime - now;

            // Clamp to reasonable range: 0ms to 60 seconds
            if (ms <= 0) return TimeSpan.Zero;
            if (ms > 60000) return TimeSpan.FromSeconds(60);
            return TimeSpan.FromMilliseconds(ms);
        }
    }

    /// <summary>
    /// Current count of active handles keeping the event loop alive. Surfaced
    /// (approximately) through process.getActiveResourcesInfo().
    /// </summary>
    internal int ActiveHandleCount => Volatile.Read(ref _activeHandles);

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
        _ownedProcesses.TerminateAll();
    }

    /// <summary>
    /// Registers a successfully started OS process as owned by this interpreter.
    /// No other interpreter, test host, worktree, or build-server process is visible here.
    /// </summary>
    internal void RegisterOwnedProcess(System.Diagnostics.Process process) => _ownedProcesses.Register(process);

    /// <summary>Releases ownership after the child has exited and its streams have drained.</summary>
    internal void UnregisterOwnedProcess(System.Diagnostics.Process process) => _ownedProcesses.Unregister(process);

    internal int OwnedProcessCount => _ownedProcesses.Count;

    /// <summary>
    /// Allows runtime resources to stop pending asynchronous I/O when their owning interpreter
    /// shuts down. This is intentionally internal: guest code observes shutdown through normal
    /// resource events, while hosts retain control of interpreter lifetime.
    /// </summary>
    internal CancellationToken ShutdownToken => _shutdownCts.Token;

    /// <summary>
    /// Gets whether there are active handles keeping the event loop alive.
    /// Thread-safe - volatile read of an int, which is atomic on all .NET platforms.
    /// </summary>
    internal bool HasActiveHandles => Volatile.Read(ref _activeHandles) > 0;

    /// <summary>
    /// Non-blocking event loop tick: drains pending microtasks, due timers, and queued callbacks.
    /// Used by the REPL to process async work between input lines without blocking.
    /// </summary>
    public void TickEventLoop()
    {
        DebuggerIdleCheckpoint();

        // Process microtasks (Promise callbacks, queueMicrotask)
        ProcessMicrotasks();

        // Process due timers (setTimeout/setInterval)
        ProcessPendingCallbacks();

        // Drain any queued callbacks (async I/O completions, etc.)
        while (_callbackQueue.TryTake(out var action, TimeSpan.Zero))
        {
            DebuggerIdleCheckpoint();
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
            ThrowIfExecutionCancelled();

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
        if (HasMicrotasks()) return true;
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
    /// <summary>
    /// Installs the interpreter's SynchronizationContext on the current thread so
    /// async/await continuations post back to the event loop queue instead of
    /// resuming on thread-pool threads. Continuations that escape to the thread
    /// pool race the main thread over the ambient environment, which surfaces as
    /// spurious "Undefined variable" errors in then-callbacks. Must be installed
    /// before the FIRST top-level statement runs — promise chains started at module
    /// top level capture whatever context is current at their first await.
    /// Returns the previous context; callers restore it when execution finishes.
    /// </summary>
    private SynchronizationContext? InstallEventLoopSyncContext()
    {
        _eventLoopSyncContext ??= new InterpreterSynchronizationContext(EnqueueCallback);
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(_eventLoopSyncContext);
        return previous;
    }

    public void RunEventLoop()
    {
        // Set up SynchronizationContext so async/await continuations come back to this
        // thread. Interpret/InterpretModules also set this up earlier so top-level
        // awaits have the correct context; this assignment is idempotent for that case.
        var previousSyncContext = InstallEventLoopSyncContext();

        try
        {
            var shutdownToken = _shutdownCts.Token;

            RunEventLoopCore(shutdownToken);

            // Node lifecycle at natural drain: 'beforeExit' (re-entering the
            // loop when a listener schedules new work), then a final 'exit'.
            // Only the program's main interpreter opts in (see
            // EmitProcessLifecycleEvents) — worker/vm/nested interpreters
            // share the process singleton and must not fire process events.
            EmitProcessLifecycleAtDrain(shutdownToken);
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
    /// The event loop's drain loop: runs callbacks/microtasks/timers until no
    /// active handles remain and the queue is empty (or shutdown is requested).
    /// </summary>
    private void RunEventLoopCore(CancellationToken shutdownToken)
    {
        while (!_isDisposed)
        {
            DebuggerIdleCheckpoint();

            // Exit immediately if there's no work keeping the loop alive
            if (!HasActiveHandles && _callbackQueue.Count == 0 && !HasMicrotasks())
            {
                break;
            }

            // Calculate timeout until next timer fires
            var timeout = GetNextTimerTimeout();

            // Efficient wait: blocks until callback arrives, timeout expires,
            // or shutdown is requested (via CancellationToken from Shutdown())
            if (_callbackQueue.TryTake(out var action, (int)timeout.TotalMilliseconds, shutdownToken))
            {
                DebuggerIdleCheckpoint();
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

            DebuggerIdleCheckpoint();

            // Process microtasks first (queueMicrotask, Promise callbacks)
            // Microtasks always run before any macrotasks (timers)
            ProcessMicrotasks();

            // Process any due timers (setTimeout, setInterval callbacks)
            ProcessPendingCallbacks();

            // Exit condition: no active handles AND queue is empty
            // This ensures all queued callbacks are processed before exiting (like Node.js)
            if (!HasActiveHandles && _callbackQueue.Count == 0 && !HasMicrotasks())
            {
                break;
            }
        }
    }

    private bool _emitProcessLifecycleEvents;
    private bool _exitEventEmitted;

    /// <summary>
    /// When true, this interpreter fires the Node process lifecycle events
    /// ('beforeExit' at loop drain, 'exit' at the end) and receives process
    /// events delivered from foreign threads (signals). Set by the CLI and the
    /// test harness on the program's main interpreter only — workers, vm
    /// contexts and nested interpreters share the process singleton and must
    /// not fire process-level events.
    /// </summary>
    public bool EmitProcessLifecycleEvents
    {
        get => _emitProcessLifecycleEvents;
        set
        {
            _emitProcessLifecycleEvents = value;
            if (value) Runtime.BuiltIns.ProcessBuiltIns.DispatchInterpreter = this;
        }
    }

    /// <summary>
    /// Fires 'beforeExit' when the loop drains (re-entering the loop while
    /// listeners schedule new work), then 'exit' exactly once. Mirrors Node:
    /// beforeExit is skipped for explicit process.exit() (which never returns),
    /// and 'exit' listeners can only do synchronous work.
    /// </summary>
    private void EmitProcessLifecycleAtDrain(CancellationToken shutdownToken)
    {
        if (!_emitProcessLifecycleEvents || _isDisposed || _exitEventEmitted)
            return;

        var process = Runtime.Types.SharpTSProcess.Instance;
        while (!_isDisposed && !shutdownToken.IsCancellationRequested)
        {
            bool hadListeners;
            try
            {
                hadListeners = process.EmitWith(this, "beforeExit", (double)System.Environment.ExitCode);
            }
            catch (Exception ex)
            {
                Error.WriteLine($"Uncaught exception in beforeExit listener: {ex.Message}");
                break;
            }

            ProcessMicrotasks();
            if (!hadListeners ||
                (!HasActiveHandles && _callbackQueue.Count == 0 && !HasMicrotasks()))
                break; // no listeners, or listeners scheduled nothing new

            RunEventLoopCore(shutdownToken);
        }

        _exitEventEmitted = true;
        Runtime.BuiltIns.ProcessBuiltIns.EmitExitEvent(
            this, HadUnhandledRejection ? 1 : System.Environment.ExitCode);
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
    // FinalizationRegistry instances with at least one registration, drained on
    // event-loop ticks. Weak: tracking must not keep a guest registry alive.
    private readonly List<WeakReference<SharpTSFinalizationRegistry>> _finalizationRegistries = [];
    private readonly object _finalizationRegistriesLock = new();
    private volatile bool _hasFinalizationRegistries;

    private bool HasPendingCallbacks =>
        !_isDisposed
        && (HasMicrotasks()
            || _hasFinalizationRegistries
            || _hasScheduledTimers);

    /// <summary>
    /// Enrolls a FinalizationRegistry so its GC-enqueued cleanups are drained on
    /// event-loop ticks. Without this, registered cleanup callbacks never fired.
    /// </summary>
    internal void TrackFinalizationRegistry(SharpTSFinalizationRegistry registry)
    {
        lock (_finalizationRegistriesLock)
        {
            foreach (var wr in _finalizationRegistries)
                if (wr.TryGetTarget(out var existing) && ReferenceEquals(existing, registry))
                    return;
            _finalizationRegistries.Add(new WeakReference<SharpTSFinalizationRegistry>(registry));
            _hasFinalizationRegistries = true;
        }
    }

    private void DrainFinalizationRegistries()
    {
        if (_finalizationRegistries.Count == 0) return;
        List<SharpTSFinalizationRegistry>? due = null;
        lock (_finalizationRegistriesLock)
        {
            for (int i = _finalizationRegistries.Count - 1; i >= 0; i--)
            {
                if (!_finalizationRegistries[i].TryGetTarget(out var registry))
                {
                    _finalizationRegistries.RemoveAt(i);
                    continue;
                }
                if (registry.HasPendingCleanups) (due ??= []).Add(registry);
            }
        }
        // Invoke outside the lock: cleanup callbacks are arbitrary guest code.
        if (due != null)
            foreach (var registry in due)
                registry.DrainCleanups(this);
    }

    internal void ProcessPendingCallbacks()
    {
        // Process microtasks first - they always run before any macrotask (timers)
        // This ensures correct JavaScript event loop semantics during busy-wait loops
        ProcessMicrotasks();

        // FinalizationRegistry cleanups ride event-loop ticks (Node runs them as
        // ordinary tasks after GC observes a target collection).
        if (_hasFinalizationRegistries)
            DrainFinalizationRegistries();

        // Quick checks before acquiring lock
        if (_isDisposed || !_hasScheduledTimers) return;

        var now = TimerNowMs;
        List<VirtualTimer>? toExecute = null;
        List<VirtualTimer>? toReschedule = null;

        lock (_virtualTimersLock)
        {
            // Dequeue all due timers - PriorityQueue is min-heap, so lowest fireTime comes first
            // (ties broken by schedule order via the sequence component)
            while (_virtualTimerQueue.TryPeek(out var timer, out var priority))
            {
                // If the next timer isn't due yet, stop processing
                if (priority.FireTime > now) break;

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
                    _virtualTimerQueue.Enqueue(timer, (timer.FireTimeMs, _timerSequence++));
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

        // Child processes are external resources: cancellation alone cannot stop them.
        // Terminate only the processes explicitly registered by this interpreter.
        _ownedProcesses.TerminateAll();

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

        // Per-realm globalThis and its Node `global` alias: each realm has its
        // own global object so guest `globalThis.x = …` stays realm-local. A user
        // `let globalThis`/`let global` already won via the environment check above.
        if (name.Lexeme == "globalThis" || name.Lexeme == "global")
        {
            return RuntimeValue.FromBoxed(GlobalThis);
        }

        // Per-realm mutable built-ins (Math, …) shadow the shared global-constants
        // table so guest mutations stay realm-local. A user `let Math`/`var Math`
        // already won via the environment check above.
        if (TryGetRealmIntrinsic(name.Lexeme, out var realmIntrinsic))
        {
            return RuntimeValue.FromBoxed(realmIntrinsic);
        }

        // Check global constants and built-in singletons (single frozen dictionary lookup)
        // This handles: NaN, Infinity, undefined, JSON, Object, console, process, etc.
        if (_globalConstants.TryGetValue(name.Lexeme, out var constant))
        {
            return RuntimeValue.FromBoxed(constant);
        }

        // Check for Node.js module globals (__dirname, __filename)
        if (name.Lexeme == "__filename") return RuntimeValue.FromString(_currentModule?.Path ?? "");
        if (name.Lexeme == "__dirname") return RuntimeValue.FromString(Path.GetDirectoryName(_currentModule?.Path) ?? "");

        // ECMA-262 §9.4.2: resolving an unbound name is a ReferenceError. The name prefix
        // is what routes this to a guest ReferenceError at the catch binding
        // (Interpreter.Statements.cs TryCreateGuestErrorFromMessage).
        throw new InterpreterException($"ReferenceError: Undefined variable '{name.Lexeme}'.");
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
        var previousSyncContext = InstallEventLoopSyncContext();
        try
        {
            // Check for "use strict" directive at file level
            bool isStrict = Parsing.DirectivePrologue.HasUseStrict(statements);
            if (isStrict)
            {
                // Wrap the current environment with strict mode enabled
                _environment = new RuntimeEnvironment(_environment, strictMode: true);
            }

            // `var` declarations are instantiated before any top-level code runs;
            // their initializer still executes in source order.
            HoistTopLevelVarDeclarations(statements);

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
                        DebugController?.OnSafePoint(this, statement, _environment, _currentModule);
                        object? result = Evaluate(exprStmt.Expr);
                        // Wait for top-level Promises to complete before continuing
                        if (_waitForTopLevelPromises
                            && result is SharpTSPromise promise
                            && !promise.SuppressImplicitTopLevelWait)
                        {
                            WaitForPromise(promise);
                        }
                    }
                    catch (ThrowException tex)
                    {
                        NotifyDebuggerUnhandledException(tex.Value);
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
                        object? thrown = result.Value.ToObject();
                        NotifyDebuggerUnhandledException(thrown);
                        LastUncaughtError = ThrowException.FromResult(thrown);
                        Out.WriteLine($"Runtime Error: {Stringify(thrown)}");
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
        catch (Runtime.Exceptions.WorkerTerminatedException)
        {
            // worker.terminate() unwound this worker thread — propagate silently (not a
            // guest error) so SharpTSWorker.WorkerThreadMain emits exit, not error.
            throw;
        }
        catch (Exception error)
        {
            NotifyDebuggerUnhandledException(TranslateException(error));
            Out.WriteLine($"Runtime Error: {error.Message}");
            throw;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousSyncContext);
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
            ThrowIfExecutionCancelled();

            if (statement is Stmt.Expression exprStmt)
            {
                DebugController?.OnSafePoint(this, statement, _environment, _currentModule);
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
        if (_vmCodeGenerationStringsDisabled)
            throw new ThrowException(new SharpTSError(
                "EvalError: Code generation from strings disallowed for this context"));

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
        var completion = InterpretRepl(parseResult.Statements, _typeMap);
        return parseResult.Statements.Count > 0
            && parseResult.Statements[^1] is Stmt.Expression
                ? completion
                : SharpTSUndefined.Instance;
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

        var previousSyncContext = InstallEventLoopSyncContext();
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
        catch (Runtime.Exceptions.WorkerTerminatedException)
        {
            // worker.terminate() unwound this worker thread — propagate silently (see Interpret).
            throw;
        }
        catch (ThrowException tex)
        {
            NotifyDebuggerUnhandledException(tex.Value);
            Out.WriteLine($"Runtime Error: {Stringify(tex.Value)}");
            throw;
        }
        catch (Exception error)
        {
            NotifyDebuggerUnhandledException(TranslateException(error));
            Out.WriteLine($"Runtime Error: {error.Message}");
            throw;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousSyncContext);
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
            bool isStrict = Parsing.DirectivePrologue.HasUseStrict(script.Statements);
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
                    DebugController?.OnSafePoint(this, stmt, _environment, _currentModule);
                    object? result = Evaluate(exprStmt.Expr);
                    if (_waitForTopLevelPromises && result is SharpTSPromise promise)
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

        // Get the main function from the environment (single scope traversal).
        // Deliberately narrowed to SharpTSFunction: `async function main()` is a
        // SharpTSAsyncFunction and is NOT auto-invoked — hundreds of existing
        // programs (and this repo's own test corpus) follow the pattern
        // `async function main() {...} main();`, and auto-invoking would run
        // them twice. The Promise<...> return types in the gate above exist for
        // a *sync* main that returns a promise chain; WaitForPromise below
        // pumps the event loop while that settles.
        if (!_environment.TryGet(mainFunc.Name.Lexeme, out RuntimeValue mainRV))
            return;

        if (mainRV.ToObject() is not SharpTSFunction mainFn)
            return;

        // Call main with process.argv (pass args even if main() doesn't take them - JS allows this)
        var argv = ProcessBuiltIns.GetArgv();
        // Pass argv only if main expects it
        object? result = mainFunc.Parameters.Count == 0
            ? mainFn.CallBoxed(this, [])
            : mainFn.CallBoxed(this, [argv]);

        // If result is a Promise, await it — pumping the event loop, because an
        // async main() typically awaits timers/I-O whose continuations only run
        // on this thread, and RunEventLoop starts only after this method returns.
        // A bare GetResult() here deadlocked. If the promise can provably never
        // settle, WaitForPromise returns with it still pending → no exit code.
        if (result is SharpTSPromise promise)
        {
            WaitForPromise(promise);
            result = promise.Task.IsCompletedSuccessfully ? promise.Task.Result : null;
        }

        // If result is a number, use it as exit code
        if (result is double exitCode)
        {
            ProcessControl.Exit((int)exitCode);
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

        // dotnet: interop modules export a DotNetClass wrapper per imported .NET type —
        // the same runtime binding an @DotNetType declare class produces (Interpreter.DotNet.cs),
        // registered at import-resolution time instead of class-declaration time.
        if (module.IsDotNetModule)
        {
            foreach (var (name, clrType) in module.DotNetExports!)
            {
                moduleInstance.SetExport(name, new DotNetClass(name, clrType));
            }
            moduleInstance.IsExecuted = true;
            return;
        }

        // Extension imports only modify member lookup in the importing module. Their virtual
        // modules have no runtime exports or initialization side effects of their own.
        if (module.IsDotNetExtensionModule)
        {
            moduleInstance.IsExecuted = true;
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
                // On a worker thread, rebind the worker_threads identity exports to this
                // worker's live values so `import { workerData, parentPort } from
                // "worker_threads"` sees the same inputs as the bare worker-context
                // globals instead of the main-thread null placeholders (#410).
                if (moduleName == "worker_threads" && WorkerThreadsContext is { } wtc)
                {
                    exports["workerData"] = wtc.WorkerData;
                    exports["parentPort"] = wtc.ParentPort;
                    exports["threadId"] = wtc.ThreadId;
                    exports["isMainThread"] = false;
                }
                foreach (var (name, value) in exports)
                {
                    moduleInstance.SetExport(name, value);
                }
                // Modules with live namespace semantics (cluster) install a stable
                // accessor-backed namespace object; ExportsAsObject() then returns it
                // for `import * as x` and the default export alike.
                moduleInstance.NamespaceObject = BuiltInModuleValues.TryCreateNamespaceOverride(moduleName, exports);
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
                    DebugController?.OnSafePoint(this, stmt, _environment, _currentModule);
                    object? result = Evaluate(exprStmt.Expr);
                    // Wait for top-level Promises to complete before continuing
                    if (_waitForTopLevelPromises && result is SharpTSPromise promise)
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

                string importedPath = _moduleResolver!.ResolveRuntimeModulePath(
                    import.ModulePath, module.Path);
                var importedModuleInstance = _loadedModules.GetValueOrDefault(importedPath);

                // Lazily-reached dependency: the static InterpretModules order only executes
                // modules it was handed up front, so anything first reached at runtime — a CJS
                // require() of a stdlib facade (whose primitive:* imports were loaded but never
                // executed, #1210), a dynamically imported subtree, or an ESM import of a
                // lazy-initialized CJS module — must be executed on demand here, BEFORE the
                // importer's body runs. ExecuteModule routes every module kind (CJS, dotnet:,
                // builtin:/primitive: placeholders, plain ESM) and registers the instance
                // before binding its own imports, so circular graphs terminate.
                if (importedModuleInstance == null)
                {
                    var importedParsed = _moduleResolver.GetCachedModule(importedPath);
                    if (importedParsed != null)
                    {
                        ExecuteModule(importedParsed);
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
        if (export.IsTypeOnly)
            return ExecutionResult.Success();

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
                if (spec.IsTypeOnly)
                    continue;
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
            string sourcePath = _moduleResolver!.ResolveRuntimeModulePath(
                export.FromModulePath, _currentModule!.Path);
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
                        if (spec.IsTypeOnly)
                            continue;
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
            var builtInModule = BuiltInModuleValues.TryCreateNamespaceOverride(builtInModuleName, exports)
                ?? new SharpTSObject(exports);
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
        string resolvedPath = _moduleResolver.ResolveRuntimeModulePath(
            importReq.ModulePath, _currentModule.Path, ResolutionKind.Cjs);

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
    /// Creates top-level <c>var</c> bindings with the initial value undefined before
    /// evaluation begins. A later declaration overwrites the slot when its initializer
    /// executes; an earlier read therefore observes undefined rather than ReferenceError.
    /// </summary>
    private void HoistTopLevelVarDeclarations(IEnumerable<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Var { IsVar: true } variable
                && !_environment.IsDefinedLocally(variable.Name.Lexeme))
            {
                _environment.Define(variable.Name.Lexeme, SharpTSUndefined.Instance);
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
            // `export const x = …` now parses as Stmt.Const (was Stmt.Var before #428).
            Stmt.Const c => c.Name.Lexeme,
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
            // `export const x = …` now parses as Stmt.Const (was Stmt.Var before #428).
            Stmt.Const c => c.Name,
            Stmt.Enum e => e.Name,
            _ => throw new InterpreterException($"Cannot get value of declaration type {decl.GetType().Name}")
        };
        return _environment.Get(token).ToObject();
    }

}
