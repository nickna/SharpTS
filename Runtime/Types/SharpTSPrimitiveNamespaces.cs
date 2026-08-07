using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Singleton representing the String namespace/constructor.
/// Callable as String(value) for type conversion, and provides static methods.
/// </summary>
public class SharpTSStringNamespace : ISharpTSCallable, ISharpTSMutableBuiltIn
{
    /// <summary>
    /// Process-wide template instance. Retained as the BuiltInRegistry singleton (existence
    /// checks like <c>"String" in globalThis</c>), but guest reads resolve to a per-realm
    /// instance (see <c>Interpreter.GetStringNamespace</c>) so expando writes stay
    /// realm-local rather than leaking between programs sharing a process. Mirrors Math /
    /// JSON / Object (#101).
    /// </summary>
    public static readonly SharpTSStringNamespace Instance = new();
    // internal (not private) so each Interpreter can construct its own realm instance;
    // the built-in members are stateless, only the _extras overlay differs.
    internal SharpTSStringNamespace() { }

    // ECMA-262 makes a constructor object ordinary and extensible, so `String.foo = 1`
    // must take. Descriptor-aware storage keeps defineProperty attributes intact.
    private readonly SharpTSObject _extras = new([]);
    private readonly HashSet<string> _deletedBuiltIns = [];

    public bool HasExtra(string name) => _extras.HasProperty(name) || _extras.HasSetter(name);
    public object? TryGetExtra(string name) => _extras.GetProperty(name);

    /// <summary>
    /// Assigns an expando. String static methods are writable/configurable
    /// non-enumerable data properties, so an assignment replaces the intrinsic
    /// with a realm-local own value while preserving those attributes.
    /// </summary>
    public void SetExtra(string name, object? value)
    {
        if (IsReadOnlyBuiltIn(name)) return;
        _deletedBuiltIns.Remove(name);
        if (IsBuiltInMethod(name) && !HasExtra(name))
        {
            _extras.DefineProperty(name, new SharpTSPropertyDescriptor
            {
                Value = value,
                HasValue = true,
                Writable = true,
                HasWritable = true,
                Enumerable = false,
                HasEnumerable = true,
                Configurable = true,
                HasConfigurable = true,
            });
            return;
        }
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
    public bool DeleteProperty(string name)
    {
        if (IsReadOnlyBuiltIn(name)) return false;
        bool hadExtra = HasExtra(name);
        if (hadExtra && !_extras.DeleteProperty(name)) return false;
        if (IsBuiltInMethod(name)) _deletedBuiltIns.Add(name);
        return true;
    }
    public IEnumerable<string> OwnEnumerableKeys() => _extras.OwnEnumerableKeys();

    private static bool IsReadOnlyBuiltIn(string name) => name == "prototype";

    private static bool IsBuiltInMethod(string name)
        => StringBuiltIns.GetStaticMember(name) is not null;

    public bool HasOwnProperty(string name)
        => name is "name" or "length"
            || HasExtra(name)
            || (!_deletedBuiltIns.Contains(name)
                && (name == "prototype" || StringBuiltIns.GetStaticMember(name) != null));

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
        if (HasExtra(name)) return TryGetExtra(name);
        if (name == "name") return "String";
        if (name == "length") return 1.0;
        if (name == "prototype") return SharpTSStringPrototype.Instance;
        if (_deletedBuiltIns.Contains(name)) return null;
        // Materialize constant-wrapping members (MAX_VALUE, EPSILON, …) here rather than
        // leaving the wrapper for each read path to unwrap — a per-realm intrinsic bypasses
        // the namespace static fast-path in EvaluateGet that used to do it.
        var member = StringBuiltIns.GetStaticMember(name);
        return member is BuiltInMethod { IsConstant: true } constant ? constant.ConstantValue : member;
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
public sealed class SharpTSStringPrototype : ISharpTSMutableBuiltIn
{
    /// <summary>
    /// The constructor this prototype reports as its <c>constructor</c> property. Set by the
    /// Interpreter to its per-realm SharpTSStringNamespace instance so
    /// <c>String.prototype.constructor === String</c> holds — the bare global resolves
    /// per-realm, so pointing at the process-wide singleton here would break that identity.
    /// </summary>
    internal SharpTSStringNamespace? RealmConstructor { get; set; }

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
        if (name == "constructor") return RealmConstructor ?? (object)SharpTSStringNamespace.Instance;
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
internal sealed class StringPrototypeMethodWrapper : ISharpTSCallable, IBuiltInFunctionMetadata
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

    public string FunctionName => _name;

    public bool HasMetadataProperty(string name)
        => name is "name" or "length"
            && !_deletedMetadataProperties.Contains(name);

    public bool DeleteMetadataProperty(string name)
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

        if (_name is "replace" or "replaceAll"
            && StringBuiltIns.TryInvokeCustomReplace(
                interpreter, _receiver, arguments,
                requireGlobalRegExp: _name == "replaceAll",
                out object? customResult))
        {
            return customResult;
        }

        if (_name == "match"
            && StringBuiltIns.TryInvokeCustomMatch(
                interpreter, _receiver, arguments, out object? matchResult))
        {
            return matchResult;
        }

        if (_name == "search"
            && StringBuiltIns.TryInvokeCustomSearch(
                interpreter, _receiver, arguments, out object? searchResult))
        {
            return searchResult;
        }

        if (_name == "split"
            && StringBuiltIns.TryInvokeCustomSplit(
                interpreter, _receiver, arguments, out object? splitResult))
        {
            return splitResult;
        }

        if (_name == "matchAll"
            && StringBuiltIns.TryInvokeCustomMatchAll(
                interpreter, _receiver, arguments, out object? matchAllResult))
        {
            return matchAllResult;
        }

        if (_name is "toString" or "valueOf")
        {
            bool isStringReceiver = _receiver is string or SharpTSStringPrototype
                || _receiver is SharpTSObject boxed
                    && boxed.GetProperty("__primitiveType") is "String";
            if (!isStringReceiver)
            {
                throw new ThrowException(new SharpTSTypeError(
                    $"String.prototype.{_name} called on incompatible receiver"));
            }
        }

        // ECMA-262 §22.1.3: String.prototype is itself a String object whose
        // [[StringData]] is "", so `String.prototype.toString()` is "" rather than
        // the object's "[object String]" stringification.
        var coerced = _receiver is SharpTSStringPrototype
            ? ""
            : interpreter.ToStringForBuiltInArgument(_receiver);
        return _inner.Bind(coerced).Call(interpreter, arguments);
    }

    public override string ToString() => $"function {_name}() {{ [native code] }}";
}

/// <summary>
/// Realm-local <c>BigInt.prototype</c> object. BigInt conversion is exposed by
/// a first-class global function, while this ordinary mutable object carries
/// its constructor back-reference and guest-defined properties.
/// </summary>
public sealed class SharpTSBigIntPrototype : ISharpTSMutableBuiltIn
{
    internal object? RealmConstructor { get; set; }
    private readonly SharpTSObject _extras = new([]);
    private bool _constructorDeleted;
    private readonly Dictionary<string, BigIntPrototypeMethodWrapper> _methodCache = [];
    private readonly HashSet<string> _deletedMethods = [];

    internal SharpTSBigIntPrototype() { }

    public bool HasExtra(string name) => _extras.HasProperty(name) || _extras.HasSetter(name);
    public object? TryGetExtra(string name) => _extras.GetProperty(name);
    public void SetExtra(string name, object? value)
    {
        _deletedMethods.Remove(name);
        if (name == "constructor") _constructorDeleted = false;
        if (name is "constructor" or "valueOf" or "toString" or "toLocaleString" && !HasExtra(name))
        {
            _extras.DefineProperty(name, new SharpTSPropertyDescriptor
            {
                Value = value,
                HasValue = true,
                Writable = true,
                HasWritable = true,
                Enumerable = false,
                HasEnumerable = true,
                Configurable = true,
                HasConfigurable = true,
            });
            return;
        }
        _extras.SetProperty(name, value);
    }
    public bool DefineExtraProperty(string name, SharpTSPropertyDescriptor descriptor)
    {
        _deletedMethods.Remove(name);
        if (name == "constructor") _constructorDeleted = false;
        return _extras.DefineProperty(name, descriptor);
    }
    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _extras.GetOwnPropertyDescriptor(name);
    public ISharpTSCallable? GetExtraGetter(string name) => _extras.GetGetter(name);
    public ISharpTSCallable? GetExtraSetter(string name) => _extras.GetSetter(name);
    public bool HasOwnProperty(string name)
        => HasExtra(name)
            || name == "constructor" && !_constructorDeleted
            || name is "valueOf" or "toString" or "toLocaleString" && !_deletedMethods.Contains(name);
    public bool DeleteProperty(string name)
    {
        if (HasExtra(name))
        {
            bool deleted = _extras.DeleteProperty(name);
            if (deleted && name == "constructor") _constructorDeleted = true;
            if (deleted && name is "valueOf" or "toString" or "toLocaleString") _deletedMethods.Add(name);
            return deleted;
        }
        if (name == "constructor") _constructorDeleted = true;
        if (name is "valueOf" or "toString" or "toLocaleString") _deletedMethods.Add(name);
        return true;
    }
    public IEnumerable<string> OwnEnumerableKeys() => _extras.OwnEnumerableKeys();
    public object? GetMember(string name)
        => HasExtra(name) ? TryGetExtra(name)
            : name == "constructor" && !_constructorDeleted ? RealmConstructor
            : name is "valueOf" or "toString" or "toLocaleString" && !_deletedMethods.Contains(name)
                ? _methodCache.GetValueOrDefault(name)
                    ?? (_methodCache[name] = new BigIntPrototypeMethodWrapper(name))
            : null;
    public override string ToString() => "[object BigInt]";
}

internal sealed class BigIntPrototypeMethodWrapper : ISharpTSCallable, IBuiltInFunctionMetadata
{
    private readonly string _name;
    private readonly BuiltInFunctionMetadata _metadata;
    private readonly object? _receiver;
    private readonly bool _hasReceiver;

    public BigIntPrototypeMethodWrapper(string name)
    {
        _name = name;
        _metadata = new BuiltInFunctionMetadata();
    }

    private BigIntPrototypeMethodWrapper(
        string name, BuiltInFunctionMetadata metadata, object? receiver)
    {
        _name = name;
        _metadata = metadata;
        _receiver = receiver;
        _hasReceiver = true;
    }

    public BigIntPrototypeMethodWrapper Bind(object? receiver)
        => new(_name, _metadata, receiver);
    public int Arity() => 0;
    public string FunctionName => _name;
    public bool HasMetadataProperty(string name) => _metadata.Has(name);
    public bool DeleteMetadataProperty(string name) => _metadata.Delete(name);

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        SharpTSBigInt? value = _receiver switch
        {
            SharpTSBigInt primitive => primitive,
            SharpTSObject boxed when boxed.GetProperty("__primitiveType") is "BigInt"
                => boxed.GetProperty("__primitiveValue") as SharpTSBigInt,
            _ => null,
        };
        if (!_hasReceiver || value is null)
            throw new ThrowException(new SharpTSTypeError(
                $"BigInt.prototype.{_name} called on incompatible receiver"));
        if (_name == "valueOf") return value;
        var method = BigIntBuiltIns.GetInstanceMember(value, _name) as ISharpTSCallable
            ?? throw new ThrowException(new SharpTSTypeError(
                $"BigInt.prototype.{_name} is unavailable"));
        return method.Call(interpreter, arguments);
    }

    public override string ToString() => $"function {_name}() {{ [native code] }}";
}

/// <summary>Realm-local ordinary object backing <c>Symbol.prototype</c>.</summary>
public sealed class SharpTSSymbolPrototype : ISharpTSMutableBuiltIn
{
    internal object? RealmConstructor { get; set; }
    private readonly SharpTSObject _extras = new([]);
    private bool _constructorDeleted;
    private readonly Dictionary<string, SymbolPrototypeMethodWrapper> _methodCache = [];
    private readonly HashSet<string> _deletedMethods = [];

    internal SharpTSSymbolPrototype() { }

    public bool HasExtra(string name) => _extras.HasProperty(name) || _extras.HasSetter(name);
    public object? TryGetExtra(string name) => _extras.GetProperty(name);
    public void SetExtra(string name, object? value)
    {
        _deletedMethods.Remove(name);
        if (name == "constructor") _constructorDeleted = false;
        if (name is "constructor" or "toString" or "valueOf" && !HasExtra(name))
        {
            _extras.DefineProperty(name, new SharpTSPropertyDescriptor
            {
                Value = value,
                HasValue = true,
                Writable = true,
                HasWritable = true,
                Enumerable = false,
                HasEnumerable = true,
                Configurable = true,
                HasConfigurable = true,
            });
            return;
        }
        _extras.SetProperty(name, value);
    }
    public bool DefineExtraProperty(string name, SharpTSPropertyDescriptor descriptor)
    {
        _deletedMethods.Remove(name);
        if (name == "constructor") _constructorDeleted = false;
        return _extras.DefineProperty(name, descriptor);
    }
    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _extras.GetOwnPropertyDescriptor(name);
    public ISharpTSCallable? GetExtraGetter(string name) => _extras.GetGetter(name);
    public ISharpTSCallable? GetExtraSetter(string name) => _extras.GetSetter(name);
    public bool HasOwnProperty(string name)
        => HasExtra(name)
            || name == "constructor" && !_constructorDeleted
            || name is "toString" or "valueOf" && !_deletedMethods.Contains(name);
    public bool DeleteProperty(string name)
    {
        if (HasExtra(name))
        {
            bool deleted = _extras.DeleteProperty(name);
            if (deleted && name == "constructor") _constructorDeleted = true;
            if (deleted && name is "toString" or "valueOf") _deletedMethods.Add(name);
            return deleted;
        }
        if (name == "constructor") _constructorDeleted = true;
        if (name is "toString" or "valueOf") _deletedMethods.Add(name);
        return true;
    }
    public IEnumerable<string> OwnEnumerableKeys() => _extras.OwnEnumerableKeys();
    public object? GetMember(string name)
        => HasExtra(name) ? TryGetExtra(name)
            : name == "constructor" && !_constructorDeleted ? RealmConstructor
            : name is "toString" or "valueOf" && !_deletedMethods.Contains(name)
                ? _methodCache.GetValueOrDefault(name)
                    ?? (_methodCache[name] = new SymbolPrototypeMethodWrapper(name))
            : null;
    public override string ToString() => "[object Symbol]";
}

internal sealed class SymbolPrototypeMethodWrapper : ISharpTSCallable, IBuiltInFunctionMetadata
{
    private readonly string _name;
    private readonly BuiltInFunctionMetadata _metadata;
    private readonly object? _receiver;
    private readonly bool _hasReceiver;

    public SymbolPrototypeMethodWrapper(string name)
    {
        _name = name;
        _metadata = new BuiltInFunctionMetadata();
    }

    private SymbolPrototypeMethodWrapper(
        string name, BuiltInFunctionMetadata metadata, object? receiver)
    {
        _name = name;
        _metadata = metadata;
        _receiver = receiver;
        _hasReceiver = true;
    }

    public SymbolPrototypeMethodWrapper Bind(object? receiver)
        => new(_name, _metadata, receiver);
    public int Arity() => 0;
    public string FunctionName => _name;
    public bool HasMetadataProperty(string name) => _metadata.Has(name);
    public bool DeleteMetadataProperty(string name) => _metadata.Delete(name);

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        SharpTSSymbol? symbol = _receiver switch
        {
            SharpTSSymbol primitive => primitive,
            SharpTSObject boxed when boxed.GetProperty("__primitiveType") is "Symbol"
                => boxed.GetProperty("__primitiveValue") as SharpTSSymbol,
            _ => null,
        };
        if (!_hasReceiver || symbol is null)
            throw new ThrowException(new SharpTSTypeError(
                $"Symbol.prototype.{_name} called on incompatible receiver"));
        return _name == "toString" ? symbol.ToString() : symbol;
    }

    public override string ToString() => $"function {_name}() {{ [native code] }}";
}

/// <summary>
/// Singleton representing the Number namespace/constructor.
/// Callable as Number(value) for type conversion, and provides static methods.
/// </summary>
public class SharpTSNumberNamespace : ISharpTSCallable, ISharpTSMutableBuiltIn
{
    /// <summary>
    /// Process-wide template instance. Retained as the BuiltInRegistry singleton (existence
    /// checks like <c>"Number" in globalThis</c>), but guest reads resolve to a per-realm
    /// instance (see <c>Interpreter.GetNumberNamespace</c>) so expando writes stay
    /// realm-local rather than leaking between programs sharing a process. Mirrors Math /
    /// JSON / Object (#101).
    /// </summary>
    public static readonly SharpTSNumberNamespace Instance = new();
    // internal (not private) so each Interpreter can construct its own realm instance;
    // the built-in members are stateless, only the _extras overlay differs.
    internal SharpTSNumberNamespace() { }

    // ECMA-262 makes a constructor object ordinary and extensible, so `Number.foo = 1`
    // must take. Descriptor-aware storage keeps defineProperty attributes intact.
    private readonly SharpTSObject _extras = new([]);
    private readonly HashSet<string> _deletedBuiltIns = [];
    private readonly Dictionary<string, object?> _realmBuiltIns = [];

    internal void SetRealmBuiltInAlias(string name, object value)
        => _realmBuiltIns[name] = value;

    public bool HasExtra(string name) => _extras.HasProperty(name) || _extras.HasSetter(name);
    public object? TryGetExtra(string name) => _extras.GetProperty(name);

    /// <summary>
    /// Assigns an expando. A write targeting a numeric constant is dropped because those
    /// slots are non-writable; built-in methods are ordinary writable/configurable data
    /// properties and therefore may be replaced.
    /// </summary>
    public void SetExtra(string name, object? value)
    {
        if (IsReadOnlyBuiltIn(name)) return;
        _deletedBuiltIns.Remove(name);
        if (IsBuiltInMethod(name) && !HasExtra(name))
        {
            _extras.DefineProperty(name, new SharpTSPropertyDescriptor
            {
                Value = value,
                HasValue = true,
                Writable = true,
                HasWritable = true,
                Enumerable = false,
                HasEnumerable = true,
                Configurable = true,
                HasConfigurable = true,
            });
            return;
        }
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
    public bool DeleteProperty(string name)
    {
        if (IsReadOnlyBuiltIn(name)) return false;
        bool hadExtra = HasExtra(name);
        if (hadExtra && !_extras.DeleteProperty(name)) return false;
        if (IsBuiltInMethod(name)) _deletedBuiltIns.Add(name);
        return true;
    }
    public IEnumerable<string> OwnEnumerableKeys() => _extras.OwnEnumerableKeys();

    private static bool IsReadOnlyBuiltIn(string name)
        => name == "prototype"
            || NumberBuiltIns.GetStaticMember(name) is BuiltInMethod { IsConstant: true };

    private static bool IsBuiltInMethod(string name)
        => NumberBuiltIns.GetStaticMember(name) is BuiltInMethod { IsConstant: false };

    public bool HasOwnProperty(string name)
        => name is "name" or "length"
            || HasExtra(name)
            || (!_deletedBuiltIns.Contains(name)
                && (name == "prototype" || NumberBuiltIns.GetStaticMember(name) != null));

    public int Arity() => 1;

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        if (arguments.Count == 0) return 0.0;
        var arg = arguments[0];
        // Number(bigint) is an explicit, allowed conversion (ECMA-262 21.1.1.1
        // step 3): it returns the numeric value, even though *implicit* ToNumber
        // on a bigint throws a TypeError. The radix-free decimal magnitude maps
        // to the nearest double.
        if (arg is SharpTSBigInt bi) return (double)bi.Value;
        return interpreter.ToNumberWithPrimitive(arg);
    }

    /// <summary>
    /// Returns <c>Number.prototype</c> (so <c>Number.prototype.toString.call(x)</c>
    /// resolves), with built-in static members (Number.MAX_VALUE, isNaN, etc.)
    /// falling through to the registry.
    /// </summary>
    public object? GetMember(string name)
    {
        if (HasExtra(name)) return TryGetExtra(name);
        if (name == "name") return "Number";
        if (name == "length") return 1.0;
        if (name == "prototype") return SharpTSNumberPrototype.Instance;
        if (_deletedBuiltIns.Contains(name)) return null;
        if (_realmBuiltIns.TryGetValue(name, out var cached)) return cached;
        // Materialize constant-wrapping members (MAX_VALUE, EPSILON, …) here rather than
        // leaving the wrapper for each read path to unwrap — a per-realm intrinsic bypasses
        // the namespace static fast-path in EvaluateGet that used to do it.
        var member = NumberBuiltIns.GetStaticMember(name);
        if (member is BuiltInMethod { IsConstant: true } constant) return constant.ConstantValue;
        // Built-in function metadata is configurable. Keep each realm's copy isolated so a
        // delete/redefinition of `name` or `length` cannot leak into another interpreter.
        if (member is BuiltInMethod method) member = method.Bind(null);
        if (member != null) _realmBuiltIns[name] = member;
        return member;
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
public sealed class SharpTSNumberPrototype : ISharpTSMutableBuiltIn
{
    /// <summary>
    /// The constructor this prototype reports as its <c>constructor</c> property. Set by the
    /// Interpreter to its per-realm SharpTSNumberNamespace instance so
    /// <c>Number.prototype.constructor === Number</c> holds — the bare global resolves
    /// per-realm, so pointing at the process-wide singleton here would break that identity.
    /// </summary>
    internal SharpTSNumberNamespace? RealmConstructor { get; set; }

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
    private readonly HashSet<string> _deletedBuiltIns = [];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, NumberPrototypeMethodWrapper>
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

    public IEnumerable<string> OwnEnumerableKeys() => _extras.OwnEnumerableKeys();

    private bool IsBuiltIn(string name)
        => name == "constructor" || NumberBuiltIns.GetPrototypeMethod(name) != null;

    public bool HasOwnProperty(string name)
        => HasExtra(name) || (!_deletedBuiltIns.Contains(name) && IsBuiltIn(name));

    /// <summary>
    /// Number.prototype is an ordinary object, so its built-in methods are configurable
    /// and `delete Number.prototype.toString` must take — after which the name resolves
    /// up the chain to Object.prototype.
    /// </summary>
    public bool DeleteProperty(string name)
    {
        bool hadExtra = HasExtra(name);
        if (hadExtra && !_extras.DeleteProperty(name)) return false;
        if (IsBuiltIn(name)) _deletedBuiltIns.Add(name);
        return true;
    }

    public object? GetMember(string name)
    {
        if (HasExtra(name)) return TryGetExtra(name);
        if (_deletedBuiltIns.Contains(name)) return null;
        if (name == "constructor") return RealmConstructor ?? (object)SharpTSNumberNamespace.Instance;
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
internal sealed class NumberPrototypeMethodWrapper : ISharpTSCallable, IBuiltInFunctionMetadata
{
    private readonly string _name;
    private readonly BuiltInMethod _inner;
    private readonly BuiltInFunctionMetadata _metadata;
    private readonly object? _receiver;
    private readonly bool _hasReceiver;

    public NumberPrototypeMethodWrapper(string name, BuiltInMethod inner)
    {
        _name = name;
        _inner = inner;
        _metadata = new BuiltInFunctionMetadata();
    }

    private NumberPrototypeMethodWrapper(
        string name, BuiltInMethod inner, BuiltInFunctionMetadata metadata, object? receiver)
    {
        _name = name;
        _inner = inner;
        _metadata = metadata;
        _receiver = receiver;
        _hasReceiver = true;
    }

    public int Arity() => _inner.SpecLength;

    // Bound copies share the metadata store: `delete Number.prototype.toFixed.length`
    // must stay observable through any later binding of the same method object.
    public NumberPrototypeMethodWrapper Bind(object? receiver)
        => new(_name, _inner, _metadata, receiver);

    public string FunctionName => _name;

    public bool HasMetadataProperty(string name) => _metadata.Has(name);

    public bool DeleteMetadataProperty(string name) => _metadata.Delete(name);

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        if (!_hasReceiver)
        {
            throw new ThrowException(new SharpTSTypeError(
                $"Number.prototype.{_name} requires that 'this' be a Number"));
        }
        // ECMA-262 §21.1.3: Number.prototype is itself a Number object whose
        // [[NumberData]] is +0, so `Number.prototype.toString()` is "0" rather
        // than a TypeError.
        if (_receiver is SharpTSNumberPrototype)
            return _inner.Bind(0.0).Call(interpreter, arguments);
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
public class SharpTSBooleanNamespace : ISharpTSCallable, ISharpTSMutableBuiltIn
{
    /// <summary>
    /// Process-wide template instance; guest reads resolve to a per-realm instance (see
    /// <c>Interpreter.GetBooleanNamespace</c>) so expando writes stay realm-local. Mirrors
    /// Math / JSON / Object (#101).
    /// </summary>
    public static readonly SharpTSBooleanNamespace Instance = new();
    internal SharpTSBooleanNamespace() { }

    // ECMA-262 §20.3.2 makes Boolean an ordinary, extensible constructor object.
    private readonly SharpTSObject _extras = new([]);

    public bool HasExtra(string name) => _extras.HasProperty(name) || _extras.HasSetter(name);
    public object? TryGetExtra(string name) => _extras.GetProperty(name);
    public void SetExtra(string name, object? value)
    {
        if (name == "prototype") return;
        _extras.SetProperty(name, value);
    }
    public bool DefineExtraProperty(string name, SharpTSPropertyDescriptor descriptor)
        => _extras.DefineProperty(name, descriptor);
    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _extras.GetOwnPropertyDescriptor(name);
    public ISharpTSCallable? GetExtraGetter(string name) => _extras.GetGetter(name);
    public ISharpTSCallable? GetExtraSetter(string name) => _extras.GetSetter(name);
    public bool DeleteProperty(string name) => name != "prototype" && _extras.DeleteProperty(name);
    public IEnumerable<string> OwnEnumerableKeys() => _extras.OwnEnumerableKeys();
    public bool HasOwnProperty(string name)
        => name is "name" or "length" or "prototype" || HasExtra(name);

    public int Arity() => 1;

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
        if (HasExtra(name)) return TryGetExtra(name);
        return name switch
        {
            "prototype" => SharpTSBooleanPrototype.Instance,
            "length" => 1.0,
            _ => null,
        };
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
public sealed class SharpTSBooleanPrototype : ISharpTSMutableBuiltIn
{
    /// <summary>
    /// The constructor this prototype reports as its <c>constructor</c> property. Set by the
    /// Interpreter to its per-realm SharpTSBooleanNamespace instance so
    /// <c>Boolean.prototype.constructor === Boolean</c> holds — the bare global resolves
    /// per-realm, so pointing at the process-wide singleton here would break that identity.
    /// </summary>
    internal SharpTSBooleanNamespace? RealmConstructor { get; set; }

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

    private readonly SharpTSObject _extras = new([]);
    private readonly HashSet<string> _deletedBuiltIns = [];
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
    private static bool IsBuiltIn(string name)
        => name is "constructor" or "toString" or "valueOf";

    public bool DeleteProperty(string name)
    {
        if (HasExtra(name) && !_extras.DeleteProperty(name)) return false;
        if (IsBuiltIn(name)) _deletedBuiltIns.Add(name);
        return true;
    }
    public bool HasOwnProperty(string name)
        => HasExtra(name) || (!_deletedBuiltIns.Contains(name) && IsBuiltIn(name));
    public IEnumerable<string> OwnEnumerableKeys() => _extras.OwnEnumerableKeys();
    public object? GetMember(string name)
    {
        if (HasExtra(name)) return TryGetExtra(name);
        if (_deletedBuiltIns.Contains(name)) return null;
        return name switch
        {
            "constructor" => RealmConstructor ?? (object)SharpTSBooleanNamespace.Instance,
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
internal sealed class BooleanPrototypeMethodWrapper : ISharpTSCallable, IBuiltInFunctionMetadata
{
    public static readonly BooleanPrototypeMethodWrapper ToStringInstance = new("toString", isToString: true);
    public static readonly BooleanPrototypeMethodWrapper ValueOfInstance = new("valueOf", isToString: false);

    private readonly string _name;
    private readonly bool _isToString;
    private readonly BuiltInFunctionMetadata _metadata;
    private readonly object? _receiver;
    private readonly bool _hasReceiver;

    private BooleanPrototypeMethodWrapper(string name, bool isToString)
    {
        _name = name;
        _isToString = isToString;
        _metadata = new BuiltInFunctionMetadata();
    }

    private BooleanPrototypeMethodWrapper(
        string name, bool isToString, BuiltInFunctionMetadata metadata, object? receiver)
    {
        _name = name;
        _isToString = isToString;
        _metadata = metadata;
        _receiver = receiver;
        _hasReceiver = true;
    }

    public int Arity() => 0;

    public BooleanPrototypeMethodWrapper Bind(object? receiver)
        => new(_name, _isToString, _metadata, receiver);

    public string FunctionName => _name;

    public bool HasMetadataProperty(string name) => _metadata.Has(name);

    public bool DeleteMetadataProperty(string name) => _metadata.Delete(name);

    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        if (!_hasReceiver)
        {
            throw new ThrowException(new SharpTSTypeError(
                $"Boolean.prototype.{_name} requires that 'this' be a Boolean"));
        }
        // Unwrap boxed Boolean wrapper produced by `new Boolean(x)`.
        bool boolValue;
        // ECMA-262 §20.3.3: Boolean.prototype is itself a Boolean object whose
        // [[BooleanData]] is false, so `Boolean.prototype.toString()` is "false".
        if (_receiver is SharpTSBooleanPrototype)
            boolValue = false;
        else if (_receiver is SharpTSObject obj
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
