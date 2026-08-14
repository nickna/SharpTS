namespace SharpTS.Runtime.Types;

/// <summary>
/// Singleton marker for the JavaScript JSON object.
/// </summary>
/// <remarks>
/// Per ECMA-262 25.5: JSON is an ordinary object (not callable, not
/// constructable). Wiring this as a singleton lets bare references like
/// <c>var x = JSON</c> resolve, while <c>JSON()</c> and <c>new JSON()</c>
/// flow through the interpreter's "non-callable" / "non-constructable"
/// dispatch and surface the spec-mandated TypeError.
///
/// Method/property dispatch (<c>JSON.parse</c>, <c>JSON.stringify</c>) is
/// handled by <see cref="BuiltIns.JSONBuiltIns.GetStaticMethod"/> via the
/// registry's instance-type lookup.
/// </remarks>
public class SharpTSJSON : ISharpTSSymbolPropertyBag
{
    public static readonly SharpTSJSON Instance = new();
    private readonly SharpTSObject _extras = new([]);
    private readonly HashSet<string> _deletedBuiltIns = [];
    internal SharpTSJSON()
    {
        _extras.DefineProperty(SharpTSSymbol.ToStringTag, new SharpTSPropertyDescriptor
        {
            Value = "JSON",
            HasValue = true,
            Writable = false,
            HasWritable = true,
            Enumerable = false,
            HasEnumerable = true,
            Configurable = true,
            HasConfigurable = true,
        });
    }

    public bool HasExtra(string name) => _extras.HasProperty(name) || _extras.HasSetter(name);
    public object? TryGetExtra(string name) => _extras.GetProperty(name);
    public void SetExtra(string name, object? value)
    {
        _deletedBuiltIns.Remove(name);
        _extras.SetProperty(name, value);
    }
    public bool DefineExtraProperty(string name, SharpTSPropertyDescriptor descriptor)
    {
        bool defined = _extras.DefineProperty(name, descriptor);
        if (defined) _deletedBuiltIns.Remove(name);
        return defined;
    }
    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _extras.GetOwnPropertyDescriptor(name);
    public bool IsBuiltInDeleted(string name) => _deletedBuiltIns.Contains(name);
    public bool HasOwnProperty(string name)
        => HasExtra(name)
            || (!IsBuiltInDeleted(name)
                && BuiltIns.JSONBuiltIns.GetStaticMethod(name) is not null);
    public object? GetMember(string name)
    {
        if (HasExtra(name)) return TryGetExtra(name);
        if (IsBuiltInDeleted(name)) return null;
        return BuiltIns.JSONBuiltIns.GetStaticMethod(name);
    }
    public bool DeleteExtra(string name)
    {
        if (HasExtra(name) && !_extras.DeleteProperty(name)) return false;
        if (BuiltIns.JSONBuiltIns.GetStaticMethod(name) is not null)
            _deletedBuiltIns.Add(name);
        return true;
    }
    internal IEnumerable<string> OwnEnumerableKeys() => _extras.OwnEnumerableKeys();

    internal bool DefineProperty(SharpTSSymbol symbol, SharpTSPropertyDescriptor descriptor)
        => _extras.DefineProperty(symbol, descriptor);
    internal SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(SharpTSSymbol symbol)
        => _extras.GetOwnPropertyDescriptor(symbol);
    internal bool DeleteBySymbolStrict(SharpTSSymbol symbol, bool strictMode)
        => _extras.DeleteBySymbolStrict(symbol, strictMode);
    bool ISharpTSSymbolPropertyBag.HasSymbolProperty(SharpTSSymbol symbol)
        => _extras.HasSymbolProperty(symbol);
    object? ISharpTSSymbolPropertyBag.GetBySymbol(SharpTSSymbol symbol)
        => _extras.GetBySymbol(symbol);
    bool ISharpTSSymbolPropertyBag.TryGetSymbolAccessor(
        SharpTSSymbol symbol, out ISharpTSCallable? getter, out ISharpTSCallable? setter)
        => _extras.TryGetSymbolAccessor(symbol, out getter, out setter);
    void ISharpTSSymbolPropertyBag.SetBySymbolStrict(
        SharpTSSymbol symbol, object? value, bool strictMode)
        => _extras.SetBySymbolStrict(symbol, value, strictMode);

    public override string ToString() => "[object JSON]";
}
