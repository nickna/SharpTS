using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;
using System.Collections.Frozen;

namespace SharpTS.Execution;

/// <summary>
/// Realm / global-state partial of <see cref="Interpreter"/>: the process-wide
/// globals table (<c>CreateGlobalsLookup</c>) and the per-realm mutable
/// intrinsics that must not leak across realms or race across worker threads —
/// the <c>Symbol.for</c> registry, <c>Math</c>, the primitive prototypes,
/// <c>RegExp.prototype</c>, and <c>globalThis</c> routing. Extracted verbatim
/// from Interpreter.cs (#1142); no behaviour change.
/// </summary>
/// <remarks>
/// The three ordering-sensitive static fields
/// (<see cref="Interpreter.PromiseConstructorSentinel"/>,
/// <c>_globalConstants</c>, <see cref="Interpreter.RegExpConstructorObject"/>)
/// stay together and in textual order here: static field initializers run in
/// textual order within a single file, and <c>CreateGlobalsLookup</c> reads
/// the sentinel while <c>RegExpConstructorObject</c> reads the globals table.
/// </remarks>
public partial class Interpreter
{
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

    /// <summary>
    /// The names of every global constant and built-in singleton, for REPL autocomplete.
    /// </summary>
    internal static IEnumerable<string> GlobalNames
        => _globalConstants.Keys.Concat(BuiltInNames.ErrorTypeNames);

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
    private Runtime.Types.SharpTSJSON? _json;
    internal Runtime.Types.SharpTSJSON GetJSON() => _json ??= new Runtime.Types.SharpTSJSON();

    /// <summary>
    /// Resolves a per-realm mutable built-in intrinsic by its global name
    /// (currently <c>Math</c>). These are the built-ins moved off process-global
    /// singletons so a realm's guest mutations stay realm-local. Returns
    /// <c>false</c> for every other name, leaving normal global resolution
    /// unchanged.
    /// </summary>
    internal bool TryGetRealmIntrinsic(string name, out object? value)
    {
        if (BuiltInNames.IsErrorTypeName(name))
        {
            value = GetErrorClass(name);
            return true;
        }
        if (name == "Object")
        {
            value = GetObjectNamespace();
            return true;
        }
        if (name == "Math")
        {
            value = GetMath();
            return true;
        }
        if (name == "JSON")
        {
            value = GetJSON();
            return true;
        }
        if (name == BuiltInNames.String)
        {
            value = GetStringNamespace();
            return true;
        }
        if (name == BuiltInNames.Number)
        {
            value = GetNumberNamespace();
            return true;
        }
        if (name == BuiltInNames.Boolean)
        {
            value = GetBooleanNamespace();
            return true;
        }
        if (name == BuiltInNames.Array)
        {
            value = GetArrayGlobal();
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
    internal static bool IsRealmIntrinsicName(string name)
        => name is "Object" or "Math" or "JSON" or "String" or "Number" or "Boolean" or "Array"
            || BuiltInNames.IsErrorTypeName(name);

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
    private Runtime.Types.SharpTSArrayPrototype? _arrayPrototype;
    private Runtime.Types.SharpTSFunctionPrototype? _functionPrototype;
    private Runtime.Types.SharpTSObjectPrototype? _objectPrototype;
    private Runtime.Types.SharpTSObjectNamespace? _objectNamespace;
    // The String/Number/Boolean constructor objects. Ordinary and extensible per ECMA-262,
    // so they carry a guest-writable expando bag and — like Math/JSON/Object — are held
    // per-Interpreter: `Number.foo = 1` in one program must not be visible to the next one
    // sharing the process.
    private Runtime.Types.SharpTSStringNamespace? _stringNamespace;
    private Runtime.Types.SharpTSNumberNamespace? _numberNamespace;
    private Runtime.Types.SharpTSBooleanNamespace? _booleanNamespace;
    private Runtime.Types.SharpTSArrayGlobal? _arrayGlobal;
    private Runtime.Types.SharpTSPromisePrototype? _promisePrototype;
    private Dictionary<string, Runtime.Types.SharpTSErrorClass>? _errorClasses;
    // Each prototype is linked back to this realm's constructor object on creation, so
    // `String.prototype.constructor === String` holds — both sides resolve per-realm.
    internal Runtime.Types.SharpTSStringPrototype GetStringPrototype()
        => _stringPrototype ??= new() { RealmConstructor = GetStringNamespace() };
    internal Runtime.Types.SharpTSNumberPrototype GetNumberPrototype()
        => _numberPrototype ??= new() { RealmConstructor = GetNumberNamespace() };
    internal Runtime.Types.SharpTSBooleanPrototype GetBooleanPrototype()
        => _booleanPrototype ??= new() { RealmConstructor = GetBooleanNamespace() };
    internal Runtime.Types.SharpTSArrayPrototype GetArrayPrototype()
    {
        if (_arrayPrototype is null)
        {
            _arrayPrototype = new();
            _arrayPrototype.RealmConstructor = GetArrayGlobal();
            _arrayGlobal!.RealmPrototype = _arrayPrototype;
        }
        return _arrayPrototype;
    }
    internal Runtime.Types.SharpTSFunctionPrototype GetFunctionPrototype() => _functionPrototype ??= new();
    internal Runtime.Types.SharpTSObjectPrototype GetObjectPrototype() => _objectPrototype ??= new();
    internal Runtime.Types.SharpTSObjectNamespace GetObjectNamespace() => _objectNamespace ??= new();
    internal Runtime.Types.SharpTSStringNamespace GetStringNamespace() => _stringNamespace ??= new();
    internal Runtime.Types.SharpTSNumberNamespace GetNumberNamespace()
    {
        if (_numberNamespace is null)
        {
            _numberNamespace = new();
            // Number.parseInt / Number.parseFloat are aliases of the corresponding
            // global function objects, not merely equivalent implementations.
            _numberNamespace.SetRealmBuiltInAlias(
                BuiltInNames.ParseInt, _globalConstants[BuiltInNames.ParseInt]);
            _numberNamespace.SetRealmBuiltInAlias(
                BuiltInNames.ParseFloat, _globalConstants[BuiltInNames.ParseFloat]);
        }
        return _numberNamespace;
    }
    internal Runtime.Types.SharpTSBooleanNamespace GetBooleanNamespace() => _booleanNamespace ??= new();
    internal Runtime.Types.SharpTSArrayGlobal GetArrayGlobal()
    {
        if (_arrayGlobal is null)
        {
            _arrayGlobal = new();
            if (_arrayPrototype is null)
                _arrayPrototype = new() { RealmConstructor = _arrayGlobal };
            _arrayGlobal.RealmPrototype = _arrayPrototype;
        }
        return _arrayGlobal;
    }
    internal Runtime.Types.SharpTSPromisePrototype GetPromisePrototype() => _promisePrototype ??= new();

    /// <summary>
    /// Returns this realm's Error constructor. Error constructors and their prototype
    /// objects are ordinary mutable objects, so sharing them through the process-wide
    /// globals table lets one program's prototype writes leak into every later program
    /// hosted by the same Test262 worker.
    /// </summary>
    internal Runtime.Types.SharpTSErrorClass GetErrorClass(string errorTypeName)
    {
        if (_errorClasses is null)
        {
            var errorClass = new Runtime.Types.SharpTSErrorClass(BuiltInNames.Error, null);
            _errorClasses = new Dictionary<string, Runtime.Types.SharpTSErrorClass>(StringComparer.Ordinal)
            {
                [BuiltInNames.Error] = errorClass,
            };
            foreach (var name in BuiltInNames.ErrorTypeNames)
            {
                if (name != BuiltInNames.Error)
                    _errorClasses[name] = new Runtime.Types.SharpTSErrorClass(name, errorClass);
            }
        }

        return _errorClasses[errorTypeName];
    }

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
    internal Runtime.Types.SharpTSGlobalThis GlobalThis => _globalThis ??=
        new Runtime.Types.SharpTSGlobalThis(name =>
            TryGetRealmIntrinsic(name, out var intrinsic) ? intrinsic : null);

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
            case Runtime.Types.SharpTSArrayGlobal:
                prototype = GetArrayPrototype();
                return true;
            case Runtime.Types.SharpTSFunctionGlobal:
                prototype = GetFunctionPrototype();
                return true;
            case Runtime.Types.SharpTSObjectNamespace:
                prototype = GetObjectPrototype();
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
}
