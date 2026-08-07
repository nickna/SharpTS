using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Singleton representing the Object namespace.
/// Provides static methods like Object.keys, Object.values, etc.
/// Implements ISharpTSCallable so `Object(value)` coerces per ECMA-262 §19.1.1 —
/// lodash uses this idiom heavily (`Object(object)` to guarantee object-ness before
/// key iteration).
/// </summary>
public class SharpTSObjectNamespace : ISharpTSCallable
{
    public static readonly SharpTSObjectNamespace Instance = new();
    private readonly SharpTSObject _extras = new([]);
    private readonly HashSet<string> _deletedBuiltIns = [];
    private readonly Dictionary<string, object?> _realmBuiltIns = [];
    // Each Interpreter owns a realm instance so guest mutations of Object's
    // configurable methods do not leak into other scripts or race in Test262.
    // The process-wide instance remains as a registry/template fallback.
    internal SharpTSObjectNamespace() { }

    public int Arity() => 1;

    private static bool IsBuiltIn(string name) => ObjectBuiltIns.GetStaticMethod(name) != null;

    public bool HasOwnProperty(string name)
        => name is "length" or "name" or "prototype"
            || _extras.HasProperty(name)
            || (!_deletedBuiltIns.Contains(name) && IsBuiltIn(name));

    public object? GetMember(string name)
    {
        if (name == "length") return 1.0;
        if (name == "name") return "Object";
        if (_extras.HasProperty(name)) return _extras.GetProperty(name);
        if (_deletedBuiltIns.Contains(name)) return null;
        if (_realmBuiltIns.TryGetValue(name, out var cached)) return cached;
        var member = ObjectBuiltIns.GetStaticMethod(name);
        // Function metadata is mutable. Give every realm its own callable so
        // deleting/redefining `name` or `length` cannot leak across scripts.
        if (member is BuiltInMethod method) member = method.Bind(null);
        if (member != null) _realmBuiltIns[name] = member;
        return member;
    }

    public void SetProperty(string name, object? value)
    {
        _deletedBuiltIns.Remove(name);
        _extras.SetProperty(name, value);
    }

    public bool DefineProperty(string name, SharpTSPropertyDescriptor descriptor)
    {
        _deletedBuiltIns.Remove(name);
        return _extras.DefineProperty(name, descriptor);
    }

    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _extras.GetOwnPropertyDescriptor(name);

    public bool DeleteProperty(string name)
    {
        if (name == "prototype") return false;
        bool hadExtra = _extras.HasProperty(name);
        if (hadExtra && !_extras.DeleteProperty(name)) return false;
        if (IsBuiltIn(name)) _deletedBuiltIns.Add(name);
        return true;
    }

    public IEnumerable<string> OwnEnumerableKeys() => _extras.OwnEnumerableKeys();

    /// <summary>
    /// ECMA-262 §19.1.1 Object(value): if value is null/undefined, return a new empty object;
    /// otherwise return ToObject(value). For already-object values this is a pass-through.
    /// </summary>
    public object? Call(Interpreter interpreter, List<object?> arguments)
    {
        if (arguments.Count == 0) return new SharpTSObject(new Dictionary<string, object?>());
        var value = arguments[0];
        if (value == null || value is SharpTSUndefined)
            return new SharpTSObject(new Dictionary<string, object?>());
        // Symbol → boxed wrapper carrying the __primitiveType marker, matching the
        // compiled NewBoxedPrimitive("Symbol", …) shape so `Object(sym) instanceof
        // Symbol` is true (ECMA-262 §7.1.18 ToObject on a Symbol, #449).
        if (value is SharpTSSymbol)
            return new SharpTSObject(new Dictionary<string, object?>
            {
                ["__primitiveType"] = "Symbol",
                ["__primitiveValue"] = value,
            });
        if (value is SharpTSBigInt)
            return new SharpTSObject(new Dictionary<string, object?>
            {
                ["__primitiveType"] = "BigInt",
                ["__primitiveValue"] = value,
            })
            {
                Prototype = interpreter.GetBigIntPrototype(),
            };
        // Primitives use the same internal-slot wrappers as their dedicated
        // constructors. This preserves Object(value)'s primitive identity for
        // later ToPrimitive operations.
        if (value is string or double or bool)
            return BuiltInConstructorFactory.ToObject(value, interpreter);
        return value;
    }

    public override string ToString() => "function Object() { [native code] }";
}
