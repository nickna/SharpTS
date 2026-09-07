namespace SharpTS.Runtime.Types;

// Owned by one interpreter prototype. Built-in lookup, method caches and constructor
// identity stay on that prototype; this object only shadows or suppresses its members.
internal sealed class PrototypePropertyOverlay(
    Func<string, bool> isBuiltIn,
    bool preserveBuiltInAssignmentAttributes = false)
{
    private readonly SharpTSObject _extras = new([]);
    private readonly HashSet<string> _deletedBuiltIns = [];

    public bool HasExtra(string name) => _extras.HasProperty(name) || _extras.HasSetter(name);
    public object? GetProperty(string name) => _extras.GetProperty(name);

    // A present null/undefined value (or an accessor) must still shadow a built-in.
    // Accessors are retrieved below, then invoked by the interpreter with its receiver.
    public bool TryGetOverride(string name, out object? value)
    {
        if (HasExtra(name))
        {
            value = GetProperty(name);
            return true;
        }
        value = null;
        return _deletedBuiltIns.Contains(name);
    }

    public void SetProperty(string name, object? value)
    {
        _deletedBuiltIns.Remove(name);
        // BigInt/Symbol preserve these attributes even after delete + assignment.
        // Other prototypes retain their existing ordinary expando assignment policy.
        if (preserveBuiltInAssignmentAttributes && isBuiltIn(name) && !HasExtra(name))
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

    public bool DefineProperty(string name, SharpTSPropertyDescriptor descriptor)
    {
        _deletedBuiltIns.Remove(name);
        return _extras.DefineProperty(name, descriptor);
    }

    public bool DeleteProperty(string name)
    {
        if (HasExtra(name) && !_extras.DeleteProperty(name)) return false;
        if (isBuiltIn(name)) _deletedBuiltIns.Add(name);
        return true;
    }

    public bool HasOwnProperty(string name)
        => HasExtra(name) || (!_deletedBuiltIns.Contains(name) && isBuiltIn(name));

    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _extras.GetOwnPropertyDescriptor(name);
    public ISharpTSCallable? GetGetter(string name) => _extras.GetGetter(name);
    public ISharpTSCallable? GetSetter(string name) => _extras.GetSetter(name);
    public IEnumerable<string> OwnEnumerableKeys() => _extras.OwnEnumerableKeys();
    public bool HasIndexedOwnProperty(long exclusiveLength) => _extras.HasIndexedOwnProperty(exclusiveLength);

    // Symbol intrinsics are materialized in the descriptor store, so deleting them
    // needs no string-name tombstone. Keep each prototype's existing symbol surface.
    public bool HasSymbolProperty(SharpTSSymbol symbol) => _extras.HasSymbolProperty(symbol);
    public object? GetBySymbol(SharpTSSymbol symbol) => _extras.GetBySymbol(symbol);
    public void SetBySymbol(SharpTSSymbol symbol, object? value) => _extras.SetBySymbol(symbol, value);
    public void SetBySymbolStrict(SharpTSSymbol symbol, object? value, bool strictMode)
        => _extras.SetBySymbolStrict(symbol, value, strictMode);
    public bool TryGetSymbolAccessor(
        SharpTSSymbol symbol, out ISharpTSCallable? getter, out ISharpTSCallable? setter)
        => _extras.TryGetSymbolAccessor(symbol, out getter, out setter);
    public bool DefineProperty(SharpTSSymbol symbol, SharpTSPropertyDescriptor descriptor)
        => _extras.DefineProperty(symbol, descriptor);
    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(SharpTSSymbol symbol)
        => _extras.GetOwnPropertyDescriptor(symbol);
    public bool DeleteBySymbolStrict(SharpTSSymbol symbol, bool strictMode)
        => _extras.DeleteBySymbolStrict(symbol, strictMode);
    public IEnumerable<SharpTSSymbol> GetSymbolPropertyNames() => _extras.GetSymbolPropertyNames();
}
