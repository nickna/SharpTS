using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// <c>Promise.prototype</c> (ECMA-262 §27.2.5). Exposes the reaction methods as *unbound*
/// callables — <c>Promise.prototype.then</c> read off the prototype has no receiver until a
/// member call or an explicit <c>.call</c>/<c>.apply</c> supplies one.
/// <para>
/// Before this existed <c>Promise.prototype</c> read as <c>undefined</c>, so every
/// <c>Promise/prototype/*</c> conformance test died dereferencing it — even though the
/// per-instance dispatch in <see cref="PromiseBuiltIns.GetMember"/> worked fine. This object
/// is the prototype *as a value*; instance property reads still go through that dispatch.
/// </para>
/// </summary>
public sealed class SharpTSPromisePrototype
{
    /// <summary>
    /// Process-wide instance. Promise.prototype carries no per-realm mutable state here
    /// (guest writes land on <see cref="_extras"/>, which is per-instance), matching how the
    /// other built-in prototypes start out.
    /// </summary>
    public static readonly SharpTSPromisePrototype Instance = new();

    internal SharpTSPromisePrototype() { }

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

    private static bool IsBuiltIn(string name)
        => name is "then" or "catch" or "finally" or "constructor";

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
        // The unbound form: PromiseBuiltIns.GetMember binds each method to a concrete
        // promise, which is wrong for a read off the prototype itself.
        return PromiseBuiltIns.GetPrototypeMethod(name);
    }

    public override string ToString() => "[object Promise]";
}
