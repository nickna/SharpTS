using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// <c>Object.prototype</c>. Exposes the classic object methods as unbound
/// callables: each expects to receive the target object via
/// <c>Function.prototype.apply/call</c>. Added so that real-world CJS packages
/// (lodash's <c>hasOwnProperty.call(obj, key)</c>, Intl <c>toString</c>
/// detection, etc.) can resolve these names.
/// </summary>
public sealed class SharpTSObjectPrototype
{
    public static readonly SharpTSObjectPrototype Instance = new();
    private Dictionary<string, object?>? _extras;
    internal SharpTSObjectPrototype() { }

    public bool HasExtra(string name) => _extras is not null && _extras.ContainsKey(name);
    public object? TryGetExtra(string name) =>
        _extras is not null && _extras.TryGetValue(name, out var value) ? value : null;
    public void SetExtra(string name, object? value)
    {
        _extras ??= new Dictionary<string, object?>();
        _extras[name] = value;
    }

    public object? GetMember(string name)
    {
        if (HasExtra(name)) return TryGetExtra(name);
        return name switch
        {
            "constructor" => SharpTSObjectNamespace.Instance,
            "hasOwnProperty" => SharpTSObjectUnboundMethod.HasOwnProperty,
            "toString" => SharpTSObjectUnboundMethod.ToString_,
            "toLocaleString" => SharpTSObjectUnboundMethod.ToLocaleString,
            "valueOf" => SharpTSObjectUnboundMethod.ValueOf,
            "isPrototypeOf" => SharpTSObjectUnboundMethod.IsPrototypeOf,
            "propertyIsEnumerable" => SharpTSObjectUnboundMethod.PropertyIsEnumerable,
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
public sealed class SharpTSObjectUnboundMethod : ISharpTSCallable
{
    public static readonly SharpTSObjectUnboundMethod HasOwnProperty = new("hasOwnProperty", HasOwnPropertyImpl);
    public static readonly SharpTSObjectUnboundMethod ToString_ = new("toString", ToStringImpl);
    public static readonly SharpTSObjectUnboundMethod ToLocaleString = new("toLocaleString", ToStringImpl);
    public static readonly SharpTSObjectUnboundMethod ValueOf = new("valueOf", ValueOfImpl);
    public static readonly SharpTSObjectUnboundMethod IsPrototypeOf = new("isPrototypeOf", IsPrototypeOfImpl);
    public static readonly SharpTSObjectUnboundMethod PropertyIsEnumerable = new("propertyIsEnumerable", PropertyIsEnumerableImpl);

    private readonly string _name;
    private readonly Func<object?, List<object?>, object?> _impl;
    private readonly object? _boundThis;
    private readonly bool _hasBoundThis;

    private SharpTSObjectUnboundMethod(string name, Func<object?, List<object?>, object?> impl)
    {
        _name = name;
        _impl = impl;
        _boundThis = null;
        _hasBoundThis = false;
    }

    private SharpTSObjectUnboundMethod(string name, Func<object?, List<object?>, object?> impl, object? boundThis)
    {
        _name = name;
        _impl = impl;
        _boundThis = boundThis;
        _hasBoundThis = true;
    }

    public int Arity() => 0;

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
                throw new Exception($"Runtime Error: Object.prototype.{_name} requires a receiver.");
            target = arguments[0];
            rest = arguments.Count > 1 ? arguments.GetRange(1, arguments.Count - 1) : new List<object?>();
        }
        return _impl(target, rest);
    }

    public SharpTSObjectUnboundMethod BindTo(object? thisArg) => new(_name, _impl, thisArg);

    public override string ToString() => $"function {_name}() {{ [native code] }}";

    private static object? HasOwnPropertyImpl(object? target, List<object?> args)
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
                _ => false,
            };
        }
        var key = args[0]?.ToString() ?? "";
        return target switch
        {
            SharpTSObject obj => obj.HasProperty(key) || obj.HasSetter(key),
            SharpTSInstance inst => inst.HasProperty(key),
            SharpTSArray array => array.HasOwnProperty(key),
            SharpTSMath math => math.HasExtra(key),
            SharpTSJSON json => json.HasExtra(key),
            SharpTSDate date => date.HasExtra(key),
            SharpTSObjectNamespace objectNamespace => objectNamespace.HasOwnProperty(key),
            SharpTSFunctionPrototype functionPrototype => functionPrototype.HasOwnProperty(key),
            SharpTSArrayPrototype arrayPrototype => arrayPrototype.HasOwnProperty(key),
            SharpTSStringPrototype stringPrototype => stringPrototype.HasOwnProperty(key),
            SharpTSNumberPrototype numberPrototype => numberPrototype.GetMember(key) != null,
            SharpTSFunction function => function.HasProperty(key) || key is "name" or "length",
            SharpTSArrowFunction arrow => arrow.HasProperty(key) || key is "name" or "length",
            IDictionary<string, object?> dict => dict.ContainsKey(key),
            // Built-in functions expose `name` and `length` as own properties
            // per ECMA-262 §17. test262's verifyProperty calls
            // hasOwnProperty(fn, "name") before reading the descriptor — without
            // this branch the assertion fails before we ever see the descriptor.
            ISharpTSCallable when key is "name" or "length" => true,
            _ => false,
        };
    }

    private static object? ToStringImpl(object? target, List<object?> args)
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
        if (target is SharpTSArray) return "[object Array]";
        if (target is SharpTSMath) return "[object Math]";
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

    private static object? ValueOfImpl(object? target, List<object?> args) => target;

    private static object? IsPrototypeOfImpl(object? target, List<object?> args)
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

        while (true)
        {
            object? proto;
            try { proto = ObjectBuiltIns.RuntimeGetPrototypeOf(v); }
            catch { return false; }
            if (proto is null or SharpTSUndefined) return false;
            if (ReferenceEquals(proto, target)) return true;
            v = proto;
        }
    }

    private static object? PropertyIsEnumerableImpl(object? target, List<object?> args)
    {
        if (target == null || args.Count == 0) return false;
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
