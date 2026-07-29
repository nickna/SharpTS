namespace SharpTS.Runtime.Types;

/// <summary>
/// Minimal global <c>Function</c> constructor placeholder. Real-world CJS
/// packages (lodash) only use this indirectly for <c>Function.prototype</c>
/// and <c>funcProto.toString</c> introspection, so we expose a skeleton
/// sufficient for those lookups.
/// </summary>
public sealed class SharpTSFunctionGlobal : ISharpTSCallable
{
    public static readonly SharpTSFunctionGlobal Instance = new();
    private readonly SharpTSFunctionPrototype _prototype = new();
    private SharpTSFunctionGlobal() { }

    public int Arity() => 0;

    // Calling `new Function(body)` is not supported — lodash only dereferences
    // `.prototype`, never calls the constructor.
    public object? Call(Execution.Interpreter interpreter, List<object?> arguments)
        => throw new Exception("Runtime Error: Dynamic Function() construction is not supported.");

    public object? GetMember(string name)
    {
        if (name == "prototype") return _prototype;
        return null;
    }

    public override string ToString() => "function Function() { [native code] }";
}

/// <summary>
/// <c>Function.prototype</c> accessor. Returns the unbound <c>call</c>/
/// <c>apply</c>/<c>bind</c> singletons that <see cref="BuiltIns.FunctionBuiltIns"/>
/// also exposes for instance-level dispatch — so
/// <c>Function.prototype.call</c> and <c>fn.call</c> resolve to the same
/// callable, and <c>Function.prototype.call.bind(hasOwn)</c> composes with
/// the BuiltInMethod rebind path that real-world test262 harness code (e.g.
/// <c>propertyHelper.js</c>) relies on.
/// </summary>
public sealed class SharpTSFunctionPrototype : ISharpTSMutableBuiltIn
{
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
        => BuiltIns.FunctionBuiltIns.GetPrototypeMethod(name) != null
            || name is "toString" or "constructor";

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
        var method = BuiltIns.FunctionBuiltIns.GetPrototypeMethod(name);
        if (method != null) return method;
        if (name == "toString") return SharpTSFunctionProtoToString.Instance;
        if (name == "constructor") return SharpTSFunctionGlobal.Instance;
        return null;
    }

    public override string ToString() => "[object Function]";
}

/// <summary>
/// Unbound <c>Function.prototype.toString</c>. When invoked via
/// <c>.call(fn)</c> or <c>.apply(fn)</c>, returns a native-source-like string
/// for the bound function — enough to satisfy lodash's regex-based native
/// detection.
/// </summary>
public sealed class SharpTSFunctionProtoToString : ISharpTSCallable
{
    public static readonly SharpTSFunctionProtoToString Instance = new();
    private readonly object? _boundThis;
    private SharpTSFunctionProtoToString(object? boundThis = null) { _boundThis = boundThis; }

    public int Arity() => 0;

    public object? Call(Execution.Interpreter interpreter, List<object?> arguments)
    {
        var target = _boundThis ?? (arguments.Count > 0 ? arguments[0] : null);
        return target?.ToString() ?? "function () { [native code] }";
    }

    public SharpTSFunctionProtoToString BindTo(object? thisArg) => new(thisArg);

    public override string ToString() => "function toString() { [native code] }";
}
