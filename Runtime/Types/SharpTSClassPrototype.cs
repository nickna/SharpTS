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
/// Guest-added properties live in <see cref="_extras"/>, a descriptor-aware
/// <see cref="SharpTSObject"/>, so <c>Object.defineProperty(Error.prototype, …)</c> — including
/// Symbol-keyed writes like <c>@@toStringTag</c> — works and the attributes round-trip. The
/// class's own method table stays read-only: an assignment shadows a method for reads without
/// mutating the class.
/// </remarks>
public sealed class SharpTSClassPrototype : ISharpTSBuiltInPrototype
{
    private readonly SharpTSClass _klass;
    private readonly SharpTSObject _extras = new([]);

    public SharpTSClassPrototype(SharpTSClass klass)
    {
        _klass = klass;
    }

    public SharpTSClass Class => _klass;

    public bool HasExtra(string name) => _extras.HasProperty(name) || _extras.HasSetter(name);
    public object? TryGetExtra(string name) => _extras.GetProperty(name);
    public void SetExtra(string name, object? value) => _extras.SetProperty(name, value);
    public bool DefineExtraProperty(string name, SharpTSPropertyDescriptor descriptor)
        => _extras.DefineProperty(name, descriptor);
    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _extras.GetOwnPropertyDescriptor(name);
    public ISharpTSCallable? GetExtraGetter(string name) => _extras.GetGetter(name);
    public ISharpTSCallable? GetExtraSetter(string name) => _extras.GetSetter(name);
    public bool DeleteProperty(string name) => _extras.DeleteProperty(name);
    public IEnumerable<string> OwnEnumerableKeys() => _extras.OwnEnumerableKeys();

    /// <summary>Symbol-keyed own properties (<c>Error.prototype[Symbol.toStringTag]</c>).</summary>
    public bool HasSymbolProperty(SharpTSSymbol key) => _extras.HasSymbolProperty(key);
    public object? GetBySymbol(SharpTSSymbol key) => _extras.GetBySymbol(key);
    public void SetBySymbol(SharpTSSymbol key, object? value) => _extras.SetBySymbol(key, value);

    public object? GetMember(string name)
    {
        if (HasExtra(name)) return TryGetExtra(name);
        if (name == "constructor") return _klass;
        var method = _klass.FindMethod(name);
        if (method != null) return method;
        return SharpTSUndefined.Instance;
    }

    public override string ToString() => $"[object {_klass.Name}]";
}
