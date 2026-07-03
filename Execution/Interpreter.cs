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
    /// The %Promise% constructor sentinel registered when no Promise singleton
    /// claimed the global first. MUST be declared before
    /// <see cref="_globalConstants"/> — static initializers run in textual
    /// order and CreateGlobalsLookup reads this field.
    /// </summary>
    internal static readonly SharpTSBuiltInConstructor PromiseConstructorSentinel = new(
        BuiltInNames.Promise,
        _ => throw new Exception("Runtime Error: Use 'new Promise(executor)' syntax."));

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
            // WebCrypto global (#1063): bare `crypto` — an import binding shadows it.
            "crypto",
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

        // Bind value-position globals for built-ins that were previously only
        // reachable through special-cased `new` expressions or member access
        // (#208): bare `AbortSignal`/`Intl`/`ReadableStream`/... otherwise
        // throw "Undefined variable".
        //
        // AbortSignal and Intl are namespace-style globals: member access on
        // SharpTSBuiltInConstructor routes through the namespace registry
        // (AbortSignal.abort/timeout/any, Intl.NumberFormat/...), while
        // direct construction throws per spec (AbortSignal has no public
        // constructor; Intl is not a constructor).
        globals["AbortSignal"] = new SharpTSBuiltInConstructor("AbortSignal",
            _ => throw new Exception("Runtime Error: TypeError: AbortSignal cannot be constructed directly. Use AbortSignal.abort(), AbortSignal.timeout(), or AbortController."));
        globals["Intl"] = new SharpTSBuiltInConstructor("Intl",
            _ => throw new Exception("Runtime Error: TypeError: Intl is not a constructor."));

        // Web-streams constructors: the same singletons stream/web exports,
        // so `new ReadableStream(...)`, `ReadableStream.from(...)`, and
        // value-position references all share one identity.
        globals[BuiltInNames.ReadableStream] = Runtime.Types.SharpTSReadableStreamConstructor.Instance;
        globals[BuiltInNames.WritableStream] = Runtime.Types.SharpTSWritableStreamConstructor.Instance;
        globals[BuiltInNames.TransformStream] = Runtime.Types.SharpTSTransformStreamConstructor.Instance;

        // MessageChannel as a value (construction already worked by name).
        globals[BuiltInNames.MessageChannel] = WorkerBuiltIns.MessageChannelConstructor;

        // Symbol as a value-position global (#234): `typeof Symbol`,
        // `const f = Symbol`, and `(Symbol as any).species` need a real
        // binding. Its namespace is registered as non-singleton (member
        // access routes through SymbolBuiltIns via GetMember), so the
        // singleton loop above didn't bind it. The factory implements the
        // call form Symbol(description); JS has no `new Symbol()`.
        globals[BuiltInNames.Symbol] = new SharpTSBuiltInConstructor(
            BuiltInNames.Symbol,
            args => new SharpTSSymbol(args.Count > 0 && args[0] is not SharpTSUndefined
                ? args[0]?.ToString()
                : null));

        // Promise needs a bare-reference global so `x instanceof Promise`,
        // `typeof Promise === 'function'`, and stdlib modules that carry
        // Promise as a value can type-check/run. Its namespace is registered
        // as non-singleton (to preserve special `new Promise(executor)`
        // handling), so it wasn't picked up by the loops above. Register a
        // minimal constructor sentinel — `new Promise(executor)` has its
        // own dedicated path and does not route through this factory.
        if (!globals.ContainsKey(BuiltInNames.Promise))
        {
            globals[BuiltInNames.Promise] = PromiseConstructorSentinel;
        }

        return globals.ToFrozenDictionary();
    }

    /// <summary>
    /// The value bare <c>Promise</c> resolves to — whatever the global table
    /// actually holds (a registry singleton when one exists, otherwise
    /// <see cref="PromiseConstructorSentinel"/>). Surfaced as
    /// <c>promise.constructor</c> by PromiseBuiltIns.GetMember so the
    /// ECMA-262 §27.2.5.1 identity holds:
    /// <c>Promise.resolve(1).constructor === Promise</c> (#221).
    /// </summary>
    internal static object PromiseGlobalValue => _globalConstants[BuiltInNames.Promise];

    private RuntimeEnvironment _environment = new();
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

    // Per-realm RegExp.prototype. Held on the Interpreter (not on the
    // process-wide SharpTSBuiltInConstructor singleton) so user mutations
    // — `delete RegExp.prototype[Symbol.split]`, `Object.defineProperty`,
    // etc. — stay scoped to this realm. Lazily populated on first read of
    // `RegExp.prototype`.
    private Runtime.Types.SharpTSObject? _regExpPrototype;
    internal Runtime.Types.SharpTSObject GetRegExpPrototype()
        => _regExpPrototype ??= Runtime.BuiltIns.RegExpBuiltIns.BuildPrototype();

    // Per-realm Symbol.for registry. Held on the Interpreter (not as a
    // process-wide static on SharpTSSymbol) so `Symbol.for(k)` returns a
    // symbol unique to this realm and `Symbol.keyFor` cannot leak
    // registrations across Interpreter instances. Each realm is its own agent
    // per ECMA-262, so a separate registry is the correct semantics — and it
    // removes a cross-thread data race: the old static was a plain Dictionary
    // mutated by every realm in the process, including concurrent worker
    // threads. Mirrors the per-realm RegExp.prototype (#101). Well-known
    // symbols (Symbol.iterator, …) are NOT in this registry; they remain
    // process-wide singletons. Lazily allocated; thread-confined to this
    // realm's execution thread, so no lock is needed.
    private Dictionary<string, Runtime.Types.SharpTSSymbol>? _symbolRegistry;
    private Dictionary<Runtime.Types.SharpTSSymbol, string>? _symbolReverseRegistry;

    /// <summary>
    /// Returns this realm's registered symbol for <paramref name="key"/>,
    /// creating and registering one on first use (ECMA-262 <c>Symbol.for</c>).
    /// </summary>
    internal Runtime.Types.SharpTSSymbol SymbolFor(string key)
    {
        _symbolRegistry ??= [];
        if (_symbolRegistry.TryGetValue(key, out var existing))
            return existing;

        var symbol = new Runtime.Types.SharpTSSymbol(key);
        _symbolRegistry[key] = symbol;
        (_symbolReverseRegistry ??= [])[symbol] = key;
        return symbol;
    }

    /// <summary>
    /// Returns the registry key for <paramref name="symbol"/> in this realm, or
    /// <c>null</c> if it was not produced by this realm's <c>Symbol.for</c>
    /// (ECMA-262 <c>Symbol.keyFor</c>).
    /// </summary>
    internal string? SymbolKeyFor(Runtime.Types.SharpTSSymbol symbol)
        => _symbolReverseRegistry is not null
            && _symbolReverseRegistry.TryGetValue(symbol, out var key)
            ? key
            : null;

    // Per-realm Math. Math is an extensible ECMA-262 object: guest code may add
    // properties (`Math.x = 1`), which must not leak across realms or race
    // across worker threads. Held per-Interpreter, mirroring RegExp.prototype
    // (#101). The base members (PI, sqrt, …) are stateless and resolved the
    // same way for every instance; only the per-instance `_extras` overlay
    // differs. Within a realm both the bare `Math` global and `globalThis.Math`
    // resolve to this one instance, so `Math === globalThis.Math` holds.
    private Runtime.Types.SharpTSMath? _math;
    internal Runtime.Types.SharpTSMath GetMath() => _math ??= new Runtime.Types.SharpTSMath();

    /// <summary>
    /// Resolves a per-realm mutable built-in intrinsic by its global name
    /// (currently <c>Math</c>). These are the built-ins moved off process-global
    /// singletons so a realm's guest mutations stay realm-local. Returns
    /// <c>false</c> for every other name, leaving normal global resolution
    /// unchanged.
    /// </summary>
    internal bool TryGetRealmIntrinsic(string name, out object? value)
    {
        if (IsRealmIntrinsicName(name))
        {
            value = GetMath();
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Names of the per-realm mutable built-ins resolved off the Interpreter
    /// rather than the shared global-constants table or the namespace
    /// fast-path. Used to keep all resolution routes (bare global, namespace
    /// member access, <c>globalThis</c>) pointing at the one realm instance so
    /// method identity holds (<c>Math.max === Math.max</c>).
    /// </summary>
    internal static bool IsRealmIntrinsicName(string name) => name == "Math";

    // Per-realm String/Number/Boolean.prototype. Each is an extensible ECMA-262
    // object carrying a guest-writable _extras bag, so — like Math and
    // RegExp.prototype (#101) — it is held per-Interpreter: guest writes
    // (`String.prototype.x = …`, indexed/`length` assignments Test262 makes
    // before calling Array.prototype.* on a primitive) stay realm-local and
    // don't race across worker threads. The namespace objects
    // (String/Number/Boolean themselves) are immutable and stay shared
    // singletons; only the mutable prototypes are per-realm.
    private Runtime.Types.SharpTSStringPrototype? _stringPrototype;
    private Runtime.Types.SharpTSNumberPrototype? _numberPrototype;
    private Runtime.Types.SharpTSBooleanPrototype? _booleanPrototype;
    internal Runtime.Types.SharpTSStringPrototype GetStringPrototype() => _stringPrototype ??= new();
    internal Runtime.Types.SharpTSNumberPrototype GetNumberPrototype() => _numberPrototype ??= new();
    internal Runtime.Types.SharpTSBooleanPrototype GetBooleanPrototype() => _booleanPrototype ??= new();

    // Per-realm globalThis. The global object holds guest-assigned properties
    // (`globalThis.x = …`), which must stay realm-local and not race across
    // worker threads, so each Interpreter owns its own — like RegExp.prototype
    // (#101), Math, and the primitive prototypes. Built-in namespaces (Math,
    // JSON, …) are still resolved live through the shared BuiltInRegistry, so
    // `globalThis.JSON === JSON` and `globalThis.Math === Math` still hold; only
    // the user-property bag is per-realm. Bare `globalThis` and the Node
    // `global` alias resolve here (see LookupVariableRV), and sloppy-mode `this`
    // binds to it.
    private Runtime.Types.SharpTSGlobalThis? _globalThis;
    internal Runtime.Types.SharpTSGlobalThis GlobalThis => _globalThis ??= new Runtime.Types.SharpTSGlobalThis();

    /// <summary>
    /// Resolves <c>String</c>/<c>Number</c>/<c>Boolean</c><c>.prototype</c> to
    /// this realm's prototype instance when <paramref name="obj"/> is the
    /// corresponding built-in namespace, so the read is realm-local rather than
    /// the shared singleton. Returns <c>false</c> for any other receiver,
    /// leaving normal member resolution unchanged.
    /// </summary>
    private bool TryGetRealmPrototypeForNamespace(object? obj, out object? prototype)
    {
        switch (obj)
        {
            case Runtime.Types.SharpTSStringNamespace:
                prototype = GetStringPrototype();
                return true;
            case Runtime.Types.SharpTSNumberNamespace:
                prototype = GetNumberPrototype();
                return true;
            case Runtime.Types.SharpTSBooleanNamespace:
                prototype = GetBooleanPrototype();
                return true;
            default:
                prototype = null;
                return false;
        }
    }

    /// <summary>
    /// Reads a property off <c>globalThis</c> honoring per-realm intrinsics: a
    /// guest own-assignment (<c>globalThis.Math = x</c>) wins, then the realm
    /// intrinsic (so <c>globalThis.Math === Math</c> within a realm), then the
    /// normal built-in/global resolution. Behaviour is identical to
    /// <c>globalThis.GetProperty</c> for every non-intrinsic name.
    /// </summary>
    private object? ResolveGlobalThisRead(Runtime.Types.SharpTSGlobalThis globalThis, string key)
        => !globalThis.HasUserProperty(key) && TryGetRealmIntrinsic(key, out var intrinsic)
            ? intrinsic
            : globalThis.GetProperty(key);

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
    internal VirtualTimer ScheduleTimer(int delayMs, int intervalMs, Action callback, bool isInterval)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var fireTime = now + delayMs;
        var timer = new VirtualTimer(fireTime, intervalMs, callback, isInterval);
        lock (_virtualTimersLock)
        {
            _virtualTimerQueue.Enqueue(timer, (fireTime, _timerSequence++));
            _hasScheduledTimers = true;
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

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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
            if (!hadListeners || (!HasActiveHandles && _callbackQueue.Count == 0))
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
        var previousSyncContext = InstallEventLoopSyncContext();
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
        catch (Runtime.Exceptions.WorkerTerminatedException)
        {
            // worker.terminate() unwound this worker thread — propagate silently (not a
            // guest error) so SharpTSWorker.WorkerThreadMain emits exit, not error.
            throw;
        }
        catch (Exception error)
        {
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
            Out.WriteLine($"Runtime Error: {Stringify(tex.Value)}");
            throw;
        }
        catch (Exception error)
        {
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
                    // dotnet: modules are dependency-free and idempotent — execute on demand.
                    // Covers modules reached outside the static InterpretModules order
                    // (e.g. a dynamically imported file that itself imports a dotnet: module).
                    else if (importedParsed?.IsDotNetModule == true)
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

    internal ExecutionResult VisitWhile(Stmt.While whileStmt)
    {
        var labels = TakePendingLoopLabels();
        return ExecuteWhileCore(_syncContext, whileStmt, labels).GetAwaiter().GetResult();
    }

    internal ExecutionResult VisitDoWhile(Stmt.DoWhile doWhileStmt)
    {
        var labels = TakePendingLoopLabels();
        return ExecuteDoWhileCore(_syncContext, doWhileStmt, labels).GetAwaiter().GetResult();
    }

    internal ExecutionResult VisitFor(Stmt.For forStmt)
    {
        // Drain labels parked by an enclosing labeled statement before running the initializer,
        // so `continue <label>`/`break <label>` resolve to this loop (#558).
        var labels = TakePendingLoopLabels();
        return ExecuteForCore(_syncContext, forStmt, labels).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Core C-style for-loop execution logic shared by the sync and async evaluators. The evaluation
    /// context supplies the per-clause evaluation strategy and the between-iteration scheduler yield
    /// (<see cref="IEvaluationContext.YieldToSchedulerAsync"/>), so a single body serves both paths.
    /// </summary>
    private async ValueTask<ExecutionResult> ExecuteForCore(IEvaluationContext ctx, Stmt.For forStmt, IReadOnlyList<string>? labels)
    {
        // Create scope for loop variables (ES6 let/const block scoping)
        // Variables declared with let/const in the initializer are scoped to the loop
        RuntimeEnvironment loopEnv = new(_environment);
        using (PushScope(loopEnv))
        {
            // Execute initializer once (defines loop variable in loopEnv)
            if (forStmt.Initializer != null)
                await ctx.ExecuteStmtAsync(forStmt.Initializer);
            // ECMA-262 13.7.4: a `for (let/const …)` loop gives each iteration its
            // own bindings for the loop variables, so closures created in different
            // iterations capture distinct values (#633). `var`/expression
            // initializers share a single binding and keep the no-copy fast path.
            var perIterationNames = CollectPerIterationBindings(forStmt.Initializer);
            if (perIterationNames != null)
                CreatePerIterationEnvironment(loopEnv, perIterationNames);
            // Loop with proper continue handling - increment always runs
            while (forStmt.Condition == null || (await ctx.EvaluateExprAsync(forStmt.Condition)).IsTruthy())
            {
                var result = await ctx.ExecuteStmtAsync(forStmt.Body);
                var (shouldBreak, shouldContinue, abruptResult) = HandleLoopResult(result, labels);
                if (shouldBreak) break;
                // On continue (unlabeled or to this loop), execute increment then re-test
                if (shouldContinue)
                {
                    if (perIterationNames != null)
                        CreatePerIterationEnvironment(loopEnv, perIterationNames);
                    if (forStmt.Increment != null)
                        await ctx.EvaluateExprAsync(forStmt.Increment);
                    // Yield to allow timer callbacks and other threads to execute
                    await ctx.YieldToSchedulerAsync();
                    continue;
                }
                if (abruptResult.HasValue) return abruptResult.Value;
                // Normal completion: fresh per-iteration binding, then increment
                if (perIterationNames != null)
                    CreatePerIterationEnvironment(loopEnv, perIterationNames);
                if (forStmt.Increment != null)
                    await ctx.EvaluateExprAsync(forStmt.Increment);
                // Process any pending timer callbacks
                ProcessPendingCallbacks();
            }
            return ExecutionResult.Success();
        }
    }

    /// <summary>
    /// Returns the variable names a <c>for</c> initializer binds that require a
    /// fresh binding per iteration (ECMA-262 13.7.4): <c>let</c>/<c>const</c>
    /// declarations. Returns <c>null</c> for <c>var</c> or expression
    /// initializers, which share a single binding across the whole loop.
    /// </summary>
    private static List<string>? CollectPerIterationBindings(Stmt? initializer)
    {
        switch (initializer)
        {
            // `let`/`const` in a for-initializer parse to Stmt.Var with IsVar=false.
            case Stmt.Var v when !v.IsVar:
                return [v.Name.Lexeme];
            case Stmt.Const c:
                return [c.Name.Lexeme];
            // Multi-declarator initializers (`for (let i = 0, j = 10; …)`).
            case Stmt.Sequence seq:
                List<string>? names = null;
                foreach (var s in seq.Statements)
                {
                    var sub = CollectPerIterationBindings(s);
                    if (sub != null) (names ??= []).AddRange(sub);
                }
                return names;
            default:
                return null;
        }
    }

    /// <summary>
    /// ECMA-262 13.7.4.8 CreatePerIterationEnvironment: copies the current value
    /// of each loop variable into a fresh environment that is a sibling of the
    /// loop environment (same enclosing scope, so the resolver's static scope
    /// distances stay valid) and makes it the active scope. Closures created in
    /// the next iteration capture this distinct binding rather than a shared slot.
    /// </summary>
    private void CreatePerIterationEnvironment(RuntimeEnvironment loopEnv, List<string> names)
    {
        var iterationEnv = new RuntimeEnvironment(loopEnv.Enclosing);
        foreach (var name in names)
            iterationEnv.Define(name, _environment.GetAt(0, name));
        _environment = iterationEnv;
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

            // `extends Array` (#233): the Array global is a constructor
            // singleton, not a SharpTSClass — substitute the SharpTSArrayClass
            // bridge so the class machinery (super(), method lookup,
            // instanceof) sees a real superclass.
            if (superclass is SharpTSArrayGlobal)
            {
                superclass = SharpTSArrayClass.ArrayBase;
            }

            // `extends Promise` (#242): same substitution for the Promise
            // constructor sentinel.
            if (superclass is SharpTSBuiltInConstructor { Name: BuiltInNames.Promise })
            {
                superclass = SharpTSPromiseClass.PromiseBase;
            }

            if (superclass is not SharpTSClass)
            {
                // Built-in constructors that don't have a class bridge yet
                // get a precise error instead of the generic
                // "Superclass must be a class".
                if (superclass is SharpTSBuiltInConstructor builtInCtor)
                {
                    throw new InterpreterException(
                        $"Class '{classStmt.Name.Lexeme}' cannot extend built-in '{builtInCtor.Name}': subclassing this built-in is not supported yet.");
                }
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

        // Symbol-keyed computed methods (`[Symbol.iterator]() {...}`) can't go into the string
        // dictionaries; collected here and attached to the class after construction.
        List<(SharpTSSymbol Symbol, ISharpTSCallable Func, bool IsStatic)>? symbolMethods = null;

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

            // Computed method keys (`[Symbol.iterator]()`, `[expr]()`) are evaluated at
            // class-definition time, like computed field keys and accessors. Symbol keys land
            // in the symbol-method table; other keys fold to a string-named method.
            if (method.ComputedKey != null)
            {
                object? key = Evaluate(method.ComputedKey);
                if (key is SharpTSSymbol symbolKey)
                {
                    (symbolMethods ??= []).Add((symbolKey, func, method.IsStatic));
                    continue;
                }
                string keyStr = PropertyKeyConverter.ToPropertyKeyString(key);
                (method.IsStatic ? staticMethods : methods)[keyStr] = func;
                continue;
            }

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

        // Symbol-keyed accessors can't go into the string dictionaries; collected
        // here and attached to the class after construction.
        List<(SharpTSSymbol Symbol, SharpTSFunction Func, bool IsStatic, bool IsGetter)>? symbolAccessors = null;

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
                bool isGetter = accessor.Kind.Type == TokenType.GET;

                // Computed accessor names (`get [Symbol.toStringTag]()`,
                // `static get [Symbol.species]()`) are evaluated at
                // class-definition time, like computed field keys.
                if (accessor.ComputedKey != null)
                {
                    object? key = Evaluate(accessor.ComputedKey);
                    if (key is SharpTSSymbol symbolKey)
                    {
                        (symbolAccessors ??= []).Add((symbolKey, func, accessor.IsStatic, isGetter));
                        continue;
                    }
                    string keyStr = PropertyKeyConverter.ToPropertyKeyString(key);
                    if (isGetter) (accessor.IsStatic ? staticGetters : getters)[keyStr] = func;
                    else (accessor.IsStatic ? staticSetters : setters)[keyStr] = func;
                    continue;
                }

                var targetGet = accessor.IsStatic ? staticGetters : getters;
                var targetSet = accessor.IsStatic ? staticSetters : setters;

                if (isGetter)
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
        // Likewise an Array superclass produces a SharpTSArrayClass whose
        // instances are real arrays (#233), and a Promise superclass produces a
        // SharpTSPromiseClass whose instances are real promises (#242).
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
            : superclass is SharpTSArrayClass arraySuper
            ? new SharpTSArrayClass(
                classStmt.Name.Lexeme,
                arraySuper,
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
            : superclass is SharpTSPromiseClass promiseSuper
            ? new SharpTSPromiseClass(
                classStmt.Name.Lexeme,
                promiseSuper,
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

        if (symbolAccessors != null)
        {
            foreach (var (symbol, func, isStatic, isGetter) in symbolAccessors)
            {
                klass.AddSymbolAccessor(symbol, func, isStatic, isGetter);
            }
        }

        if (symbolMethods != null)
        {
            foreach (var (symbol, func, isStatic) in symbolMethods)
            {
                klass.AddSymbolMethod(symbol, func, isStatic);
            }
        }

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
        // A bare `return;` completes with `undefined` — distinct from `return null;`. Emitting the
        // undefined sentinel here (rather than C# null, which represents JS null) is what makes a
        // generator's completion value and a plain function's return value `undefined` instead of
        // conflating them with null (#480). `return <expr>` preserves whatever the expression
        // evaluates to, so an explicit `return null;` still yields null.
        if (returnStmt.Value == null)
            return ExecutionResult.Return(RuntimeValue.Undefined);
        return ExecutionResult.Return(Evaluate(returnStmt.Value));
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
