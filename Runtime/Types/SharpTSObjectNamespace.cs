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
    // Each Interpreter owns a realm instance so guest mutations of Object's
    // configurable methods do not leak into other scripts or race in Test262.
    // The process-wide instance remains as a registry/template fallback.
    internal SharpTSObjectNamespace() { }

    public int Arity() => 0;

    private static bool IsBuiltIn(string name) => ObjectBuiltIns.GetStaticMethod(name) != null;

    public bool HasOwnProperty(string name)
        => _extras.HasProperty(name)
            || (!_deletedBuiltIns.Contains(name) && IsBuiltIn(name));

    public object? GetMember(string name)
    {
        if (_extras.HasProperty(name)) return _extras.GetProperty(name);
        if (_deletedBuiltIns.Contains(name)) return null;
        return ObjectBuiltIns.GetStaticMethod(name);
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
        // Primitives (string/number/bool) — wrap in a plain object holding the primitive.
        // Good enough for lodash's use case where the wrapper is iterated over, not read.
        // A fuller implementation would materialize String/Number/Boolean wrapper objects.
        if (value is string or double or int or long or bool)
            return new SharpTSObject(new Dictionary<string, object?> { ["valueOf"] = value });
        return value;
    }

    public override string ToString() => "function Object() { [native code] }";
}
