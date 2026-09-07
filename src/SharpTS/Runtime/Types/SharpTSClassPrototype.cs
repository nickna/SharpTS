namespace SharpTS.Runtime.Types;

/// <summary>
/// <c>Class.prototype</c> for a built-in or user-defined <see cref="SharpTSClass"/>.
/// Member access falls through to <see cref="SharpTSClass.FindMethod"/> so that
/// <c>Error.prototype.toString</c>, <c>RangeError.prototype.toString</c>, etc. resolve to the
/// class's instance methods. Spec-aligned: in JS, <c>Class.prototype</c> is a regular object
/// (typeof "object") whose properties are the instance methods plus a <c>constructor</c>
/// back-reference. One instance per class — see <see cref="SharpTSClass.Prototype"/> — so
/// <c>X.prototype === X.prototype</c> holds.
/// </summary>
/// <remarks>
/// Guest-added properties live in <see cref="_overlay"/>, backed by a descriptor-aware
/// <see cref="SharpTSObject"/>, so <c>Object.defineProperty(Error.prototype, …)</c> — including
/// Symbol-keyed writes like <c>@@toStringTag</c> — works and the attributes round-trip. The
/// class's own method table stays read-only: an assignment shadows a method for reads without
/// mutating the class.
/// </remarks>
public sealed class SharpTSClassPrototype : ISharpTSMutableBuiltIn
{
    private readonly SharpTSClass _klass;
    private readonly PrototypePropertyOverlay _overlay;

    public SharpTSClassPrototype(SharpTSClass klass)
    {
        _klass = klass;
        _overlay = new(IsBuiltIn);
    }

    public SharpTSClass Class => _klass;

    public bool HasExtra(string name) => _overlay.HasExtra(name);
    public object? TryGetExtra(string name) => _overlay.GetProperty(name);
    public void SetExtra(string name, object? value) => _overlay.SetProperty(name, value);
    public bool DefineExtraProperty(string name, SharpTSPropertyDescriptor descriptor)
        => _overlay.DefineProperty(name, descriptor);
    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _overlay.GetOwnPropertyDescriptor(name);
    public ISharpTSCallable? GetExtraGetter(string name) => _overlay.GetGetter(name);
    public ISharpTSCallable? GetExtraSetter(string name) => _overlay.GetSetter(name);
    public bool DeleteProperty(string name) => _overlay.DeleteProperty(name);

    private bool IsBuiltIn(string name) => name == "constructor" || _klass.FindMethod(name) != null;

    public bool HasOwnProperty(string name) => _overlay.HasOwnProperty(name);
    public IEnumerable<string> OwnEnumerableKeys() => _overlay.OwnEnumerableKeys();

    /// <summary>Symbol-keyed own properties (<c>Error.prototype[Symbol.toStringTag]</c>).</summary>
    public bool HasSymbolProperty(SharpTSSymbol key) => _overlay.HasSymbolProperty(key);
    public object? GetBySymbol(SharpTSSymbol key) => _overlay.GetBySymbol(key);
    public void SetBySymbol(SharpTSSymbol key, object? value) => _overlay.SetBySymbol(key, value);

    public object? GetMember(string name)
    {
        if (_overlay.TryGetOverride(name, out var value)) return value;
        if (name == "constructor") return _klass;
        var method = _klass.FindMethod(name);
        if (method != null) return method;
        return null;
    }

    public override string ToString() => $"[object {_klass.Name}]";
}
