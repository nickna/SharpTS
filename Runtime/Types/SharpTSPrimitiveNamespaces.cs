using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Singleton representing the String namespace/constructor.
/// Callable as String(value) for type conversion, and provides static methods.
/// </summary>
public class SharpTSStringNamespace : ISharpTSCallable
{
    public static readonly SharpTSStringNamespace Instance = new();
    private SharpTSStringNamespace() { }

    public int Arity() => 0;

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        if (arguments.Count == 0) return "";
        var arg = arguments[0];
        if (arg is SharpTSUndefined) return "undefined";
        if (arg == null) return "null";
        if (arg is bool b) return b ? "true" : "false";
        if (arg is double d) return Compilation.RuntimeTypes.FormatNumber(d);
        // ECMA-262 7.1.17 ToString(bigint) = BigInt::toString = bare numeric form
        // ("42"), NOT the "42n" debug form used by console.log / util.inspect.
        if (arg is SharpTSBigInt bi) return bi.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (arg is SharpTSArray arr) return ArrayBuiltIns.ToJsString(interpreter, arr);
        // A boxed wrapper / plain object goes through ToString = ToPrimitive
        // (string hint) then stringify, honoring an own toString override and
        // unwrapping a bare wrapper to its primitive (#574). A raw Symbol is
        // exempt from the ToString TypeError per §22.1.1.1 — fall through to its
        // descriptive string.
        if (arg is SharpTSObject) return interpreter.ToStringForStringCall(arg);
        // A class instance (incl. Error subclasses) resolves toString through its
        // class chain; route it through ToString so String(new TypeError("x"))
        // yields "TypeError: x" rather than the C# "TypeError instance" form
        // (#921/#922 follow-up). ToStringForStringCall stringifies via the
        // instance's own toString.
        if (arg is SharpTSInstance) return interpreter.ToStringForStringCall(arg);
        return arg.ToString() ?? "";
    }

    /// <summary>
    /// Returns <c>String.prototype</c> so real-world patterns like
    /// <c>String.prototype.trim.call(x)</c> resolve correctly; built-in static
    /// methods (String.raw, String.fromCharCode, ...) fall through to the
    /// registry lookup.
    /// </summary>
    public object? GetMember(string name)
    {
        if (name == "prototype") return SharpTSStringPrototype.Instance;
        return StringBuiltIns.GetStaticMember(name);
    }

    public override string ToString() => "function String() { [native code] }";
}

/// <summary>
/// <c>String.prototype</c>. Exposes every registered String method as an
/// unbound <see cref="BuiltInMethod"/> via <see cref="StringBuiltIns"/>,
/// wrapped so <c>String.prototype.trim.call(value)</c> throws a proper
/// TypeError on null/undefined receivers and ToString-coerces every other
/// receiver per ECMA-262 before dispatch. Also accepts arbitrary user-assigned
/// properties (ECMA-262: String.prototype is an ordinary object).
/// </summary>
public sealed class SharpTSStringPrototype
{
    /// <summary>
    /// Process-wide template instance. Retained as a fallback, but guest reads
    /// of <c>String.prototype</c> resolve to a per-realm instance (see
    /// <c>Interpreter.GetStringPrototype</c>) so user-added properties stay
    /// realm-local and don't race across worker threads. Mirrors the per-realm
    /// RegExp.prototype (#101).
    /// </summary>
    public static readonly SharpTSStringPrototype Instance = new();
    // internal (not private) so each Interpreter can construct its own realm
    // instance; only the _extras overlay differs between instances.
    internal SharpTSStringPrototype() { }

    private readonly SharpTSObject _extras = new([]);
    private readonly HashSet<string> _deletedBuiltIns = [];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, StringPrototypeMethodWrapper>
        _methodCache = new(StringComparer.Ordinal);
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

    private bool IsBuiltIn(string name)
        => name == "constructor" || StringBuiltIns.GetPrototypeMethod(name) != null;

    public bool HasOwnProperty(string name)
        => HasExtra(name) || (!_deletedBuiltIns.Contains(name) && IsBuiltIn(name));

    public bool DeleteProperty(string name)
    {
        bool hadExtra = HasExtra(name);
        if (hadExtra && !_extras.DeleteProperty(name)) return false;
        if (IsBuiltIn(name)) _deletedBuiltIns.Add(name);
        return true;
    }

    public IEnumerable<string> OwnEnumerableKeys() => _extras.OwnEnumerableKeys();

    public object? GetMember(string name)
    {
        if (HasExtra(name)) return TryGetExtra(name);
        if (_deletedBuiltIns.Contains(name)) return null;
        if (name == "constructor") return SharpTSStringNamespace.Instance;
        var method = StringBuiltIns.GetPrototypeMethod(name);
        if (method is null) return null;
        return _methodCache.GetOrAdd(name, _ => new StringPrototypeMethodWrapper(name, method));
    }

    public override string ToString() => "[object String]";
}

/// <summary>
/// Adapter around a String <see cref="BuiltInMethod"/>. Throws TypeError for
/// null/undefined receivers and otherwise coerces the receiver to a string
/// (ToString — the abstract operation, not the method) before binding and
/// dispatching.
/// </summary>
internal sealed class StringPrototypeMethodWrapper : ISharpTSCallable
{
    private readonly string _name;
    private readonly BuiltInMethod _inner;
    private readonly HashSet<string> _deletedMetadataProperties;
    private readonly object? _receiver;
    private readonly bool _hasReceiver;

    public StringPrototypeMethodWrapper(string name, BuiltInMethod inner)
    {
        _name = name;
        _inner = inner;
        _deletedMetadataProperties = [];
    }

    private StringPrototypeMethodWrapper(
        string name,
        BuiltInMethod inner,
        HashSet<string> deletedMetadataProperties,
        object? receiver)
    {
        _name = name;
        _inner = inner;
        _deletedMetadataProperties = deletedMetadataProperties;
        _receiver = receiver;
        _hasReceiver = true;
    }

    public int Arity() => _inner.SpecLength;

    public StringPrototypeMethodWrapper Bind(object? receiver)
        => new(_name, _inner, _deletedMetadataProperties, receiver);

    internal string FunctionName => _name;

    internal bool HasMetadataProperty(string name)
        => name is "name" or "length"
            && !_deletedMetadataProperties.Contains(name);

    internal bool DeleteMetadataProperty(string name)
    {
        if (name is not ("name" or "length"))
            return true;
        _deletedMetadataProperties.Add(name);
        return true;
    }

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        if (!_hasReceiver || _receiver is null or SharpTSUndefined)
        {
            throw new ThrowException(new SharpTSTypeError(
                $"String.prototype.{_name} called on null or undefined"));
        }

        var coerced = interpreter.ToStringForBuiltInArgument(_receiver);
        return _inner.Bind(coerced).Call(interpreter, arguments);
    }

    public override string ToString() => $"function {_name}() {{ [native code] }}";
}

/// <summary>
/// Singleton representing the Number namespace/constructor.
/// Callable as Number(value) for type conversion, and provides static methods.
/// </summary>
public class SharpTSNumberNamespace : ISharpTSCallable
{
    public static readonly SharpTSNumberNamespace Instance = new();
    private SharpTSNumberNamespace() { }

    public int Arity() => 0;

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        if (arguments.Count == 0) return 0.0;
        var arg = arguments[0];
        if (arg is double d) return d;
        if (arg is SharpTSUndefined) return double.NaN;
        if (arg == null) return 0.0;
        if (arg is bool b) return b ? 1.0 : 0.0;
        // Number(bigint) is an explicit, allowed conversion (ECMA-262 21.1.1.1
        // step 3): it returns the numeric value, even though *implicit* ToNumber
        // on a bigint throws a TypeError. The radix-free decimal magnitude maps
        // to the nearest double.
        if (arg is SharpTSBigInt bi) return (double)bi.Value;
        if (arg is string s)
        {
            s = s.Trim();
            if (s.Length == 0) return 0.0;
            // ECMA-262 7.1.4: only the case-sensitive "Infinity"/"+Infinity"/
            // "-Infinity" forms are valid Infinity literals. Double.TryParse
            // (NumberStyles.Float) would otherwise accept "infinity"/"INFINITY"
            // case-insensitively.
            if (s == "Infinity" || s == "+Infinity") return double.PositiveInfinity;
            if (s == "-Infinity") return double.NegativeInfinity;
            if (s.Contains("infinity", StringComparison.OrdinalIgnoreCase))
                return double.NaN;
            if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double result))
                return result;
            return double.NaN;
        }
        return double.NaN;
    }

    /// <summary>
    /// Returns <c>Number.prototype</c> (so <c>Number.prototype.toString.call(x)</c>
    /// resolves), with built-in static members (Number.MAX_VALUE, isNaN, etc.)
    /// falling through to the registry.
    /// </summary>
    public object? GetMember(string name)
    {
        if (name == "prototype") return SharpTSNumberPrototype.Instance;
        return NumberBuiltIns.GetStaticMember(name);
    }

    public override string ToString() => "function Number() { [native code] }";
}

/// <summary>
/// <c>Number.prototype</c>. Exposes registered Number instance methods
/// (toFixed, toPrecision, toExponential, toString) as unbound callables
/// wrapped to coerce the receiver to a number per ECMA-262.
/// Also accepts arbitrary user-assigned properties (ECMA-262: Number.prototype is
/// an ordinary object — Test262 sets indexed elements and <c>length</c> on it
/// before invoking Array.prototype.* with a number primitive as the receiver).
/// </summary>
public sealed class SharpTSNumberPrototype
{
    /// <summary>
    /// Process-wide template instance. Retained as a fallback, but guest reads
    /// of <c>Number.prototype</c> resolve to a per-realm instance (see
    /// <c>Interpreter.GetNumberPrototype</c>) so user-added properties stay
    /// realm-local and don't race across worker threads. Mirrors the per-realm
    /// RegExp.prototype (#101).
    /// </summary>
    public static readonly SharpTSNumberPrototype Instance = new();
    // internal (not private) so each Interpreter can construct its own realm
    // instance; only the _extras overlay differs between instances.
    internal SharpTSNumberPrototype() { }

    private readonly SharpTSObject _extras = new([]);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, NumberPrototypeMethodWrapper>
        _methodCache = new(StringComparer.Ordinal);
    public bool HasExtra(string name) => _extras.HasProperty(name) || _extras.HasSetter(name);
    public object? TryGetExtra(string name) => _extras.GetProperty(name);
    public void SetExtra(string name, object? value) => _extras.SetProperty(name, value);
    public bool DefineExtraProperty(string name, SharpTSPropertyDescriptor descriptor)
        => _extras.DefineProperty(name, descriptor);
    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _extras.GetOwnPropertyDescriptor(name);
    public ISharpTSCallable? GetExtraGetter(string name) => _extras.GetGetter(name);
    public ISharpTSCallable? GetExtraSetter(string name) => _extras.GetSetter(name);
    public object? GetMember(string name)
    {
        if (HasExtra(name)) return TryGetExtra(name);
        if (name == "constructor") return SharpTSNumberNamespace.Instance;
        var method = NumberBuiltIns.GetPrototypeMethod(name);
        if (method is null) return null;
        return _methodCache.GetOrAdd(name, _ => new NumberPrototypeMethodWrapper(name, method));
    }

    public override string ToString() => "[object Number]";
}

/// <summary>
/// Adapter around a Number <see cref="BuiltInMethod"/>. Throws TypeError on
/// non-number receivers per ECMA-262 (Number.prototype.toString and friends
/// require <c>thisNumberValue</c>). Accepts boxed Number wrappers produced by
/// <c>new Number(x)</c> by extracting their <c>__primitiveValue</c>.
/// </summary>
internal sealed class NumberPrototypeMethodWrapper : ISharpTSCallable
{
    private readonly string _name;
    private readonly BuiltInMethod _inner;
    private readonly object? _receiver;
    private readonly bool _hasReceiver;

    public NumberPrototypeMethodWrapper(string name, BuiltInMethod inner)
    {
        _name = name;
        _inner = inner;
    }

    private NumberPrototypeMethodWrapper(string name, BuiltInMethod inner, object? receiver)
    {
        _name = name;
        _inner = inner;
        _receiver = receiver;
        _hasReceiver = true;
    }

    public int Arity() => _inner.SpecLength;

    public NumberPrototypeMethodWrapper Bind(object? receiver)
        => new(_name, _inner, receiver);

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        if (!_hasReceiver)
        {
            throw new ThrowException(new SharpTSTypeError(
                $"Number.prototype.{_name} requires that 'this' be a Number"));
        }
        // Unwrap boxed Number wrapper produced by `new Number(x)`.
        var numValue = _receiver is SharpTSObject obj
            && obj.GetProperty("__primitiveType") is string pt && pt == "Number"
            && obj.GetProperty("__primitiveValue") is double wv
            ? (double?)wv : null;
        if (numValue is not null)
            return _inner.Bind(numValue.Value).Call(interpreter, arguments);
        if (_receiver is not double d)
        {
            throw new ThrowException(new SharpTSTypeError(
                $"Number.prototype.{_name} requires that 'this' be a Number"));
        }
        return _inner.Bind(d).Call(interpreter, arguments);
    }

    public override string ToString() => $"function {_name}() {{ [native code] }}";
}

/// <summary>
/// Singleton representing the Boolean namespace/constructor.
/// Callable as Boolean(value) for type conversion.
/// </summary>
public class SharpTSBooleanNamespace : ISharpTSCallable
{
    public static readonly SharpTSBooleanNamespace Instance = new();
    private SharpTSBooleanNamespace() { }

    public int Arity() => 0;

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        if (arguments.Count == 0) return false;
        var arg = arguments[0];
        return SharpTS.Compilation.RuntimeTypes.IsTruthy(arg);
    }

    /// <summary>
    /// Returns <c>Boolean.prototype</c>. Boolean has no static members worth
    /// exposing here, so an unknown name returns null (= undefined to user).
    /// </summary>
    public object? GetMember(string name)
    {
        if (name == "prototype") return SharpTSBooleanPrototype.Instance;
        return null;
    }

    public override string ToString() => "function Boolean() { [native code] }";
}

/// <summary>
/// <c>Boolean.prototype</c>. Exposes <c>toString</c> and <c>valueOf</c>
/// per ECMA-262 as wrapper callables that throw TypeError on non-boolean
/// receivers. Also accepts arbitrary user-assigned properties — Test262 sets
/// indexed elements and <c>length</c> before calling Array.prototype.* with a
/// boolean primitive as the receiver.
/// </summary>
public sealed class SharpTSBooleanPrototype
{
    /// <summary>
    /// Process-wide template instance. Retained as a fallback, but guest reads
    /// of <c>Boolean.prototype</c> resolve to a per-realm instance (see
    /// <c>Interpreter.GetBooleanPrototype</c>) so user-added properties stay
    /// realm-local and don't race across worker threads. Mirrors the per-realm
    /// RegExp.prototype (#101).
    /// </summary>
    public static readonly SharpTSBooleanPrototype Instance = new();
    // internal (not private) so each Interpreter can construct its own realm
    // instance; only the _extras overlay differs between instances.
    internal SharpTSBooleanPrototype() { }

    private Dictionary<string, object?>? _extras;
    public bool HasExtra(string name) => _extras is not null && _extras.ContainsKey(name);
    public object? TryGetExtra(string name) =>
        _extras is not null && _extras.TryGetValue(name, out var v) ? v : null;
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
            "constructor" => SharpTSBooleanNamespace.Instance,
            "toString" => BooleanPrototypeMethodWrapper.ToStringInstance,
            "valueOf" => BooleanPrototypeMethodWrapper.ValueOfInstance,
            _ => null,
        };
    }

    public override string ToString() => "[object Boolean]";
}

/// <summary>
/// Adapter for Boolean.prototype.toString/valueOf. Throws TypeError on non-
/// boolean receivers per ECMA-262 (<c>thisBooleanValue</c>); returns the JS
/// string form ("true"/"false") or the primitive otherwise.
/// </summary>
internal sealed class BooleanPrototypeMethodWrapper : ISharpTSCallable
{
    public static readonly BooleanPrototypeMethodWrapper ToStringInstance = new("toString", isToString: true);
    public static readonly BooleanPrototypeMethodWrapper ValueOfInstance = new("valueOf", isToString: false);

    private readonly string _name;
    private readonly bool _isToString;
    private readonly object? _receiver;
    private readonly bool _hasReceiver;

    private BooleanPrototypeMethodWrapper(string name, bool isToString)
    {
        _name = name;
        _isToString = isToString;
    }

    private BooleanPrototypeMethodWrapper(string name, bool isToString, object? receiver)
    {
        _name = name;
        _isToString = isToString;
        _receiver = receiver;
        _hasReceiver = true;
    }

    public int Arity() => 0;

    public BooleanPrototypeMethodWrapper Bind(object? receiver)
        => new(_name, _isToString, receiver);

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        if (!_hasReceiver)
        {
            throw new ThrowException(new SharpTSTypeError(
                $"Boolean.prototype.{_name} requires that 'this' be a Boolean"));
        }
        // Unwrap boxed Boolean wrapper produced by `new Boolean(x)`.
        bool boolValue;
        if (_receiver is SharpTSObject obj
            && obj.GetProperty("__primitiveType") is string pt && pt == "Boolean"
            && obj.GetProperty("__primitiveValue") is bool wv)
            boolValue = wv;
        else if (_receiver is bool b)
            boolValue = b;
        else
            throw new ThrowException(new SharpTSTypeError(
                $"Boolean.prototype.{_name} requires that 'this' be a Boolean"));
        return _isToString ? (boolValue ? "true" : "false") : (object)boolValue;
    }

    public override string ToString() => $"function {_name}() {{ [native code] }}";
}
