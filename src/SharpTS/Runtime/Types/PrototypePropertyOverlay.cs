namespace SharpTS.Runtime.Types;

// Owned by one interpreter prototype. Built-in lookup, method caches and constructor
// identity stay on that prototype; this object only shadows or suppresses its members.
internal sealed class PrototypePropertyOverlay(
    Func<string, bool> isBuiltIn,
    bool preserveBuiltInAssignmentAttributes = false,
    IEnumerable<string>? initialStringKeys = null)
{
    private readonly SharpTSObject _extras = new([]);
    private readonly HashSet<string> _deletedBuiltIns = [];
    // Opt-in order for prototypes whose original members are virtual. Values
    // and presence remain authoritative in the existing overlay/tombstones.
    private readonly List<string>? _stringKeyOrder = initialStringKeys?.Distinct(StringComparer.Ordinal).ToList();

    public bool IsExtensible => _extras.IsExtensible;
    public void Freeze() => _extras.Freeze();
    public void Seal() => _extras.Seal();
    public void PreventExtensions() => _extras.PreventExtensions();

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
        // A live virtual constructor retains its intrinsic attributes. Once
        // deleted, assignment creates an ordinary property instead. Existing
        // explicit descriptors remain authoritative in the extra store.
        bool preserveAttributes = (name == "constructor" || preserveBuiltInAssignmentAttributes)
            && !HasExtra(name)
            && !_deletedBuiltIns.Contains(name)
            && isBuiltIn(name);
        _deletedBuiltIns.Remove(name);
        if (preserveAttributes)
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
            TrackOwnStringKey(name);
            return;
        }
        _extras.SetProperty(name, value);
        TrackOwnStringKey(name);
    }

    public bool DefineProperty(string name, SharpTSPropertyDescriptor descriptor)
    {
        bool defined = _extras.DefineProperty(name, descriptor);
        if (defined)
        {
            _deletedBuiltIns.Remove(name);
            TrackOwnStringKey(name);
        }
        return defined;
    }

    internal bool DefinePropertyPreservingVirtualData(
        string name, SharpTSPropertyDescriptor descriptor, Func<string, object?> getMember,
        bool configurable = true)
    {
        if (!HasExtra(name) && HasOwnProperty(name))
        {
            // A virtual own data slot already exists even before its overlay is
            // materialized. Preserve its value and omitted attributes on update.
            var current = GetVirtualDataDescriptor(name, getMember, configurable);
            return DefineExistingBuiltInDataProperty(name, current, descriptor);
        }
        return DefineProperty(name, descriptor);
    }

    internal bool DefineExistingBuiltInDataProperty(
        string name, SharpTSPropertyDescriptor current, SharpTSPropertyDescriptor change)
    {
        if (!current.HasValue || current.HasGet || current.HasSet || HasExtra(name)
            || _deletedBuiltIns.Contains(name) || !isBuiltIn(name))
            return false;

        // Validate against the actual existing descriptor before materializing
        // its virtual slot. Nonextensibility forbids new own properties, not
        // changes to an already present built-in data property.
        var validation = new SharpTSObject(new Dictionary<string, object?>());
        if (!validation.DefineProperty(name, current)
            || !validation.DefineProperty(name, change))
            return false;
        _extras.MirrorCompletedPropertyDefinition(name, validation.GetOwnPropertyDescriptor(name)!);
        TrackOwnStringKey(name);
        return true;
    }

    public bool DeleteProperty(string name)
    {
        if (!HasExtra(name) && HasOwnProperty(name) && _extras.IsSealed) return false;
        if (HasExtra(name) && !_extras.DeleteProperty(name)) return false;
        if (isBuiltIn(name)) _deletedBuiltIns.Add(name);
        _stringKeyOrder?.Remove(name);
        return true;
    }

    public bool HasOwnProperty(string name)
        => HasExtra(name) || (!_deletedBuiltIns.Contains(name) && isBuiltIn(name));

    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _extras.GetOwnPropertyDescriptor(name);

    internal SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(
        string name, Func<string, object?> getMember, bool configurable = true)
        => _extras.GetOwnPropertyDescriptor(name)
            ?? (HasOwnProperty(name) ? GetVirtualDataDescriptor(name, getMember, configurable) : null);

    private SharpTSPropertyDescriptor GetVirtualDataDescriptor(
        string name, Func<string, object?> getMember, bool configurable)
        => new(getMember(name), writable: !_extras.IsFrozen, enumerable: false,
            configurable: configurable && !_extras.IsSealed);
    public ISharpTSCallable? GetGetter(string name) => _extras.GetGetter(name);
    public ISharpTSCallable? GetSetter(string name) => _extras.GetSetter(name);
    public IEnumerable<string> OwnEnumerableKeys() => _extras.OwnEnumerableKeys();
    public IEnumerable<string> OwnVisibleStringKeys()
        => _stringKeyOrder == null
            ? _extras.OwnVisibleStringKeys()
            : _extras.OwnVisibleStringKeys().Where(key => PropertySemantics.TryGetArrayIndex(key, out _))
                .Concat(_stringKeyOrder.Where(key => HasOwnProperty(key)
                    && !PropertySemantics.TryGetArrayIndex(key, out _)));

    private void TrackOwnStringKey(string name)
    {
        if (_stringKeyOrder != null && _extras.HasOwnStringProperty(name) && !_stringKeyOrder.Contains(name))
            _stringKeyOrder.Add(name);
    }
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
