using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// <c>Object.prototype</c>. Exposes the classic object methods as unbound
/// callables: each expects to receive the target object via
/// <c>Function.prototype.apply/call</c>. Added so that real-world CJS packages
/// (lodash's <c>hasOwnProperty.call(obj, key)</c>, Intl <c>toString</c>
/// detection, etc.) can resolve these names.
/// </summary>
public sealed class SharpTSObjectPrototype : ISharpTSMutableBuiltIn
{
    public static readonly SharpTSObjectPrototype Instance = new();
    internal SharpTSObjectPrototype() { }

    // Object.prototype is an ordinary mutable object. Reuse SharpTSObject's
    // descriptor-aware storage — as Array/String/Number.prototype already do — so
    // `Object.defineProperty(Object.prototype, …)` can install accessors, `delete`
    // takes, and for-in / getOwnPropertyDescriptor see the same keys. The previous
    // value-only Dictionary supported none of that: every one of those operations
    // either threw or silently no-oped, and Test262 leans on patching
    // Object.prototype constantly to exercise inherited-property paths.
    private readonly SharpTSObject _extras = new([]);
    private readonly HashSet<string> _deletedBuiltIns = [];
    internal SharpTSObjectNamespace? RealmConstructor { get; set; }

    public bool HasExtra(string name) => _extras.HasProperty(name) || _extras.HasSetter(name);
    public object? TryGetExtra(string name) => _extras.GetProperty(name);
    public void SetExtra(string name, object? value)
    {
        _deletedBuiltIns.Remove(name);
        _extras.SetProperty(name, value);
    }
    public bool DefineExtraProperty(string name, SharpTSPropertyDescriptor descriptor)
    {
        _deletedBuiltIns.Remove(name);
        return _extras.DefineProperty(name, descriptor);
    }
    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _extras.GetOwnPropertyDescriptor(name);
    public ISharpTSCallable? GetExtraGetter(string name) => _extras.GetGetter(name);
    public ISharpTSCallable? GetExtraSetter(string name) => _extras.GetSetter(name);

    // Per-realm copies of the unbound methods. The templates below are process-wide statics,
    // but each carries mutable ECMA-262 §17 metadata (a `delete fn.length` is observable), so
    // handing the template itself to guest code leaks that deletion into every later program
    // sharing the process — which showed up as order-dependent Test262 results. This
    // prototype object is already per-realm, so caching a copy here scopes the metadata to
    // the realm.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SharpTSObjectUnboundMethod>
        _methodCache = new(StringComparer.Ordinal);

    private static bool IsBuiltIn(string name) => BuiltInMemberTemplate(name) != null;

    public bool HasOwnProperty(string name)
        => HasExtra(name) || (!_deletedBuiltIns.Contains(name) && IsBuiltIn(name));

    public bool DeleteProperty(string name)
    {
        bool hadExtra = HasExtra(name);
        if (hadExtra && !_extras.DeleteProperty(name)) return false;
        if (IsBuiltIn(name)) _deletedBuiltIns.Add(name);
        return true;
    }

    /// <summary>Own enumerable string keys — the for-in / Object.keys surface.</summary>
    public IEnumerable<string> OwnEnumerableKeys() => _extras.OwnEnumerableKeys();

    public object? GetMember(string name)
    {
        if (HasExtra(name)) return TryGetExtra(name);
        if (_deletedBuiltIns.Contains(name)) return null;
        if (name == "constructor")
            return RealmConstructor ?? SharpTSObjectNamespace.Instance;
        var template = BuiltInMemberTemplate(name);
        return template is SharpTSObjectUnboundMethod unbound
            ? _methodCache.GetOrAdd(name, _ => unbound.CloneUnbound())
            : template;
    }

    private static object? BuiltInMemberTemplate(string name)
    {
        return name switch
        {
            "constructor" => SharpTSObjectNamespace.Instance,
            "hasOwnProperty" => SharpTSObjectUnboundMethod.HasOwnProperty,
            "toString" => SharpTSObjectUnboundMethod.ToString_,
            "toLocaleString" => SharpTSObjectUnboundMethod.ToLocaleString,
            "valueOf" => SharpTSObjectUnboundMethod.ValueOf,
            "isPrototypeOf" => SharpTSObjectUnboundMethod.IsPrototypeOf,
            "propertyIsEnumerable" => SharpTSObjectUnboundMethod.PropertyIsEnumerable,
            // Annex B §B.2.2.2–5. The compiled backend has wired these since
            // RuntimeEmitter.ObjectPrototypePopulate; the interpreter reported them
            // `undefined`.
            "__defineGetter__" => SharpTSObjectUnboundMethod.DefineGetter,
            "__defineSetter__" => SharpTSObjectUnboundMethod.DefineSetter,
            "__lookupGetter__" => SharpTSObjectUnboundMethod.LookupGetter,
            "__lookupSetter__" => SharpTSObjectUnboundMethod.LookupSetter,
            _ => null,
        };
    }

    public override string ToString() => "[object Object]";
}

/// <summary>
/// An unbound method on <c>Object.prototype</c>. Invoked via
/// <c>Function.prototype.call/apply</c> with the target object supplied as
/// <c>this</c>, or directly with the target as the first argument.
/// </summary>
public sealed class SharpTSObjectUnboundMethod : ISharpTSCallable, IBuiltInFunctionMetadata
{
    // The trailing int is the ECMA-262 §17 `length` — the spec'd named-argument count,
    // which is what `Object.prototype.<m>.length` must report.
    public static readonly SharpTSObjectUnboundMethod HasOwnProperty = new("hasOwnProperty", HasOwnPropertyImpl, 1);
    public static readonly SharpTSObjectUnboundMethod ToString_ = new("toString", ToStringImpl, 0);
    public static readonly SharpTSObjectUnboundMethod ToLocaleString = new("toLocaleString", ToLocaleStringImpl, 0);
    public static readonly SharpTSObjectUnboundMethod ValueOf = new("valueOf", ValueOfImpl, 0);
    public static readonly SharpTSObjectUnboundMethod IsPrototypeOf = new("isPrototypeOf", IsPrototypeOfImpl, 1);
    public static readonly SharpTSObjectUnboundMethod PropertyIsEnumerable = new("propertyIsEnumerable", PropertyIsEnumerableImpl, 1);
    public static readonly SharpTSObjectUnboundMethod DefineGetter =
        new("__defineGetter__", (i, t, a) => DefineAccessorImpl(i, t, a, isGetter: true), 2);
    public static readonly SharpTSObjectUnboundMethod DefineSetter =
        new("__defineSetter__", (i, t, a) => DefineAccessorImpl(i, t, a, isGetter: false), 2);
    public static readonly SharpTSObjectUnboundMethod LookupGetter =
        new("__lookupGetter__", (i, t, a) => LookupAccessorImpl(i, t, a, isGetter: true), 1);
    public static readonly SharpTSObjectUnboundMethod LookupSetter =
        new("__lookupSetter__", (i, t, a) => LookupAccessorImpl(i, t, a, isGetter: false), 1);

    private readonly string _name;
    private readonly Func<Interp?, object?, List<object?>, object?> _impl;
    private readonly BuiltInFunctionMetadata _metadata;
    private readonly int _jsLength;
    private readonly object? _boundThis;
    private readonly bool _hasBoundThis;

    private SharpTSObjectUnboundMethod(
        string name, Func<Interp?, object?, List<object?>, object?> impl, int jsLength)
    {
        _name = name;
        _impl = impl;
        _jsLength = jsLength;
        _metadata = new BuiltInFunctionMetadata();
        _boundThis = null;
        _hasBoundThis = false;
    }

    private SharpTSObjectUnboundMethod(
        string name,
        Func<Interp?, object?, List<object?>, object?> impl,
        int jsLength,
        BuiltInFunctionMetadata metadata,
        object? boundThis)
    {
        _name = name;
        _impl = impl;
        _jsLength = jsLength;
        _metadata = metadata;
        _boundThis = boundThis;
        _hasBoundThis = true;
    }

    public string FunctionName => _name;

    public bool HasMetadataProperty(string name) => _metadata.Has(name);

    public bool DeleteMetadataProperty(string name) => _metadata.Delete(name);

    public int Arity() => _jsLength;

    /// <summary>
    /// True once <see cref="BindTo"/> has supplied a receiver. Callers use this to avoid
    /// re-binding an already-bound method (a member call on the result of
    /// <c>Object.prototype.toString.bind(x)</c> must keep <c>x</c>).
    /// </summary>
    public bool HasBoundThis => _hasBoundThis;

    public object? Call(Interp interpreter, List<object?> arguments)
    {
        object? target;
        List<object?> rest;
        if (_hasBoundThis)
        {
            target = _boundThis;
            rest = arguments;
        }
        else
        {
            if (arguments.Count == 0)
                throw new ThrowException(new SharpTSTypeError(
                    $"Object.prototype.{_name} called on null or undefined"));
            target = arguments[0];
            rest = arguments.Count > 1 ? arguments.GetRange(1, arguments.Count - 1) : new List<object?>();
        }
        return _impl(interpreter, target, rest);
    }

    public SharpTSObjectUnboundMethod BindTo(object? thisArg)
        => new(_name, _impl, _jsLength, _metadata, thisArg);

    /// <summary>
    /// A receiverless copy with its own §17 metadata. Lets a realm hand guest code an
    /// instance whose <c>delete fn.length</c> can't leak into another realm's view of the
    /// same built-in.
    /// </summary>
    internal SharpTSObjectUnboundMethod CloneUnbound() => new(_name, _impl, _jsLength);

    public override string ToString() => $"function {_name}() {{ [native code] }}";

    /// <summary>
    /// ECMA-262 §7.3.13 HasOwnProperty, shared with <c>Object.hasOwn</c> (§20.1.2.13 is
    /// defined as exactly this operation) so the two can't drift — they previously did,
    /// with <c>Object.hasOwn</c> seeing only data fields and missing accessors entirely.
    /// </summary>
    public static bool HasOwn(Interp? interpreter, object? target, object? key)
        => HasOwnPropertyImpl(interpreter, target, [key]) is true;

    private static object? HasOwnPropertyImpl(Interp? interpreter, object? target, List<object?> args)
    {
        if (target == null || args.Count == 0) return false;
        // ECMA-262 §19.1.3.2 ToPropertyKey: symbol args route through the
        // symbol-keyed dispatch instead of being stringified.
        if (args[0] is SharpTSSymbol sym)
        {
            return target switch
            {
                SharpTSObject obj => obj.HasSymbolProperty(sym),
                SharpTSInstance inst => inst.HasSymbolProperty(sym),
                SharpTSMath math => math.GetOwnPropertyDescriptor(sym) is not null,
                _ => false,
            };
        }
        var key = args[0]?.ToString() ?? "";
        return target switch
        {
            SharpTSObject obj => obj.HasProperty(key) || obj.HasSetter(key),
            // Own properties only: a class method lives on the prototype, so
            // `Object.hasOwn(new C(), "someMethod")` is false. HasProperty would resolve
            // it through the class chain and answer true.
            SharpTSInstance inst => inst.HasField(key) || inst.GetOwnPropertyDescriptor(key) is not null,
            SharpTSArray array => array.HasOwnProperty(key),
            SharpTSMath math => math.HasExtra(key)
                || (!math.IsBuiltInDeleted(key) && MathBuiltIns.GetMember(key) is not null),
            SharpTSJSON json => json.HasOwnProperty(key),
            SharpTSDate date => date.HasExtra(key)
                || (date.IsPrototype
                    && !date.IsBuiltInDeleted(key)
                    && DateBuiltIns.GetMember(date, key) is not null),
            SharpTSRegExp regex => regex.HasOwnProperty(key),
            SharpTSArrayGlobal arrayGlobal => arrayGlobal.HasOwnProperty(key),
            SharpTSObjectNamespace objectNamespace => objectNamespace.HasOwnProperty(key),
            SharpTSStringNamespace stringNamespace => stringNamespace.HasOwnProperty(key),
            SharpTSNumberNamespace numberNamespace => numberNamespace.HasOwnProperty(key),
            SharpTSBooleanNamespace booleanNamespace => booleanNamespace.HasOwnProperty(key),
            SharpTSFunctionPrototype functionPrototype => functionPrototype.HasOwnProperty(key),
            SharpTSArrayPrototype arrayPrototype => arrayPrototype.HasOwnProperty(key),
            SharpTSStringPrototype stringPrototype => stringPrototype.HasOwnProperty(key),
            SharpTSNumberPrototype numberPrototype => numberPrototype.GetMember(key) != null,
            SharpTSBigIntPrototype bigIntPrototype => bigIntPrototype.HasOwnProperty(key),
            SharpTSSymbolPrototype symbolPrototype => symbolPrototype.HasOwnProperty(key),
            SharpTSObjectPrototype objectPrototype => objectPrototype.HasOwnProperty(key),
            SharpTSClassPrototype classPrototype => classPrototype.HasOwnProperty(key),
            SharpTSPromisePrototype promisePrototype => promisePrototype.HasOwnProperty(key),
            SharpTSClass klass => key is "name" or "length" or "prototype"
                || klass.HasOwnStaticMember(key),
            SharpTSFunction function => function.HasProperty(key) || key is "name" or "length",
            SharpTSArrowFunction arrow => arrow.HasProperty(key) || key is "name" or "length",
            SharpTSGlobalThis globalThis => globalThis.HasProperty(key),
            IDictionary<string, object?> dict => dict.ContainsKey(key),
            // Built-in functions expose `name` and `length` as own properties
            // per ECMA-262 §17. test262's verifyProperty calls
            // hasOwnProperty(fn, "name") before reading the descriptor — without
            // this branch the assertion fails before we ever see the descriptor.
            // Wrappers that track deletion answer for themselves, so a preceding
            // `delete fn.length` (how propertyHelper.js proves configurability)
            // is observable here.
            IBuiltInFunctionMetadata meta when key is "name" or "length"
                => meta.HasMetadataProperty(key),
            SharpTSBuiltInConstructor constructor
                => interpreter?.HasBuiltInConstructorOwnProperty(constructor, key)
                    ?? constructor.GetMember(key) is not null,
            ISharpTSCallable when key is "name" or "length" => true,
            _ => false,
        };
    }

    private static object? ToStringImpl(Interp? interpreter, object? target, List<object?> args)
    {
        // ECMA-262 20.1.3.6: toString uses the @@toStringTag tag of the
        // receiver. Kept conservative — extending this to every built-in tag
        // broke Lodash's typeof detection (it uses `Object.prototype.toString.call`
        // on functions and expected "[object Object]" back). Add new tags
        // only when a specific spec test needs them.
        if (target == null) return "[object Null]";
        if (target is SharpTSUndefined) return "[object Undefined]";
        if (target is string) return "[object String]";
        if (target is double or int) return "[object Number]";
        if (target is bool) return "[object Boolean]";
        if (target is SharpTSArguments) return "[object Arguments]";
        if (target is SharpTSArray) return "[object Array]";
        if (target is SharpTSDate) return "[object Date]";
        if (target is SharpTSRegExp) return "[object RegExp]";
        if (target is SharpTSError
            || target is SharpTSInstance { RuntimeClass: SharpTSErrorClass })
            return "[object Error]";
        if (target is SharpTSObject boxed
            && boxed.GetProperty("__primitiveType") is string primitiveType)
        {
            return primitiveType switch
            {
                "Number" => "[object Number]",
                "String" => "[object String]",
                "Boolean" => "[object Boolean]",
                _ => "[object Object]",
            };
        }
        if (target is SharpTSMath) return "[object Math]";
        if (target is SharpTSJSON) return "[object JSON]";
        // The primitive prototype objects each carry the matching internal slot
        // (§21.1.3 / §22.1.3 / §20.3.3), so their class tag is the wrapped type — not
        // "[object Object]". Observable once a test deletes the type's own toString.
        if (target is SharpTSNumberPrototype) return "[object Number]";
        if (target is SharpTSStringPrototype) return "[object String]";
        if (target is SharpTSBooleanPrototype) return "[object Boolean]";
        if (target is SharpTSArrayPrototype) return "[object Array]";
        // Function classification — any value `typeof` reports as "function"
        // must tag "[object Function]" (ECMA-262 20.1.3.6 step 7, IsCallable):
        // lodash's baseGetTag/isFunction classifies built-in constructors held
        // as values (`var O = Object`) by this tag (#314). Mirrors
        // GetTypeofString in Interpreter.Operators.cs.
        if (target is SharpTSProxy proxy) return proxy.IsCallable ? "[object Function]" : "[object Object]";
        if (target is SharpTSFunction or SharpTSArrowFunction or SharpTSClass
            or BuiltInMethod or ISharpTSCallable or SharpTSBufferConstructor)
            return "[object Function]";
        return "[object Object]";
    }

    private static object? ToLocaleStringImpl(Interp? interpreter, object? target, List<object?> args)
    {
        if (target is null or SharpTSUndefined)
            throw new ThrowException(new SharpTSTypeError(
                "Cannot convert undefined or null to object"));
        return ToStringImpl(interpreter, target, args);
    }

    private static object? ValueOfImpl(Interp? interpreter, object? target, List<object?> args)
    {
        RequireObjectCoercible(target, "valueOf");
        return target;
    }

    private static void RequireObjectCoercible(object? target, string methodName)
    {
        if (target is null or SharpTSUndefined)
        {
            throw new ThrowException(new SharpTSTypeError(
                $"Object.prototype.{methodName} called on null or undefined"));
        }
    }

    private static object? DefineAccessorImpl(
        Interp? interpreter, object? target, List<object?> args, bool isGetter)
        => ObjectBuiltIns.DefineAccessorProperty(
            interpreter ?? throw new InvalidOperationException(
                "Object.prototype accessor definition requires an interpreter"),
            target,
            args.Count > 0 ? args[0] : SharpTSUndefined.Instance,
            args.Count > 1 ? args[1] : SharpTSUndefined.Instance,
            isGetter);

    private static object? LookupAccessorImpl(
        Interp? interpreter, object? target, List<object?> args, bool isGetter)
        => ObjectBuiltIns.LookupAccessorProperty(
            interpreter ?? throw new InvalidOperationException(
                "Object.prototype accessor lookup requires an interpreter"),
            target,
            args.Count > 0 ? args[0] : SharpTSUndefined.Instance,
            isGetter);

    private static object? IsPrototypeOfImpl(Interp? interpreter, object? target, List<object?> args)
    {
        // ECMA-262 §20.1.3.4 Object.prototype.isPrototypeOf(V): walk V's
        // prototype chain and return true if `this` (target) appears in it.
        // (Was stubbed to always return false.)
        if (args.Count == 0) return false;
        var v = args[0];
        // Step 1: if Type(V) is not Object, return false — primitives have no chain.
        if (v is null or SharpTSUndefined or double or int or long or bool or string
            or System.Numerics.BigInteger or SharpTSSymbol)
            return false;
        RequireObjectCoercible(target, "isPrototypeOf");

        // Bounded to keep a cyclic __proto__ chain from spinning; 64 matches the
        // interpreter's other prototype walks.
        for (int i = 0; i < 64; i++)
        {
            object? proto;
            try { proto = ObjectBuiltIns.PrototypeOf(interpreter, v); }
            catch { return false; }
            if (proto is null or SharpTSUndefined) return false;
            if (ReferenceEquals(proto, target)) return true;
            v = proto;
        }
        return false;
    }

    private static object? PropertyIsEnumerableImpl(Interp? interpreter, object? target, List<object?> args)
    {
        RequireObjectCoercible(target, "propertyIsEnumerable");
        if (args.Count == 0) return false;
        if (args[0] is SharpTSSymbol symbol)
        {
            return target switch
            {
                SharpTSObject obj
                    => obj.GetOwnPropertyDescriptor(symbol) is { Enumerable: true },
                SharpTSInstance instance => instance.HasSymbolProperty(symbol),
                SharpTSMath math
                    => math.GetOwnPropertyDescriptor(symbol) is { Enumerable: true },
                _ => false,
            };
        }
        var key = args[0]?.ToString() ?? "";
        return target switch
        {
            // ECMA-262 §20.1.3.4: O.[[GetOwnProperty]](P) then return its
            // [[Enumerable]] — a non-enumerable own property (e.g. the
            // RegExp.prototype flag accessors) must return false, not just
            // "is present".
            SharpTSObject obj => obj.GetOwnPropertyDescriptor(key) is { Enumerable: true },
            SharpTSMath math => math.GetOwnPropertyDescriptor(key) is { Enumerable: true },
            SharpTSJSON json => json.GetOwnPropertyDescriptor(key) is { Enumerable: true },
            SharpTSDate date => date.GetOwnPropertyDescriptor(key) is { Enumerable: true },
            SharpTSRegExp regex
                => regex.GetOwnPropertyDescriptor(key) is { Enumerable: true },
            SharpTSObjectNamespace objectNamespace
                => objectNamespace.GetOwnPropertyDescriptor(key) is { Enumerable: true },
            SharpTSFunctionPrototype functionPrototype
                => functionPrototype.GetOwnPropertyDescriptor(key) is { Enumerable: true },
            SharpTSArrayPrototype arrayPrototype
                => arrayPrototype.GetOwnPropertyDescriptor(key) is { Enumerable: true },
            SharpTSStringPrototype stringPrototype
                => stringPrototype.GetOwnPropertyDescriptor(key) is { Enumerable: true },
            SharpTSFunction function => function.IsPropertyEnumerable(key),
            SharpTSArrowFunction arrow => arrow.IsPropertyEnumerable(key),
            SharpTSArray array => array.IsPropertyEnumerable(key),
            IDictionary<string, object?> dict => dict.ContainsKey(key),
            _ => false,
        };
    }
}
