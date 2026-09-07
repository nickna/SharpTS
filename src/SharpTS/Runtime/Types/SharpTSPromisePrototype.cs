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
public sealed class SharpTSPromisePrototype : ISharpTSMutableBuiltIn
{
    /// <summary>
    /// Process-wide template. Guest reads use the interpreter's per-realm prototype;
    /// its <see cref="_overlay"/> and method cache belong to that instance.
    /// </summary>
    public static readonly SharpTSPromisePrototype Instance = new();

    internal SharpTSPromisePrototype() { }

    private readonly PrototypePropertyOverlay _overlay = new(IsBuiltIn);
    private readonly Dictionary<string, ISharpTSCallable> _builtIns = [];

    public bool HasExtra(string name) => _overlay.HasExtra(name);
    public object? TryGetExtra(string name) => _overlay.GetProperty(name);
    public void SetExtra(string name, object? value) => _overlay.SetProperty(name, value);
    public bool DefineExtraProperty(string name, SharpTSPropertyDescriptor descriptor)
        => _overlay.DefineProperty(name, descriptor);
    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _overlay.GetOwnPropertyDescriptor(name);
    public ISharpTSCallable? GetExtraGetter(string name) => _overlay.GetGetter(name);

    private static bool IsBuiltIn(string name)
        => name is "then" or "catch" or "finally" or "constructor";

    public bool HasOwnProperty(string name) => _overlay.HasOwnProperty(name);

    public bool DeleteProperty(string name) => _overlay.DeleteProperty(name);

    public IEnumerable<string> OwnEnumerableKeys() => _overlay.OwnEnumerableKeys();

    public object? GetMember(string name)
    {
        if (_overlay.TryGetOverride(name, out var value)) return value;
        // The unbound form: PromiseBuiltIns.GetMember binds each method to a concrete
        // promise, which is wrong for a read off the prototype itself.
        if (_builtIns.TryGetValue(name, out var cached)) return cached;
        var member = PromiseBuiltIns.GetPrototypeMethod(name);
        if (member != null) _builtIns[name] = member;
        return member;
    }

    public override string ToString() => "[object Promise]";
}
