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
    private readonly List<object> _ownKeyOrder = ["constructor"];
    // Newly created properties retain their live insertion position if the
    // declaration-order seed arrives after an early prototype exposure.
    private readonly HashSet<object> _createdPropertyKeys = [];

    public SharpTSClassPrototype(SharpTSClass klass)
    {
        _klass = klass;
        _overlay = new(IsBuiltIn);
        foreach (var name in klass.OwnInstancePropertyNames)
            RecordOwnKey(name);
        foreach (var (name, descriptor) in klass.OwnInstanceAccessorDescriptors())
        {
            _overlay.DefineProperty(name, descriptor);
            RecordOwnKey(name);
        }
        foreach (var (key, method) in klass.OwnInstanceSymbolMethods())
        {
            _overlay.DefineProperty(key, SharpTSPropertyDescriptor.ForMethod(method));
            RecordOwnKey(key);
        }
        foreach (var (key, descriptor) in klass.OwnInstanceSymbolAccessorDescriptors())
        {
            _overlay.DefineProperty(key, descriptor);
            RecordOwnKey(key);
        }
    }

    public SharpTSClass Class => _klass;

    internal bool HasExplicitPrototype { get; private set; }
    internal object? ExplicitPrototype { get; private set; }

    internal void SetExplicitPrototype(object? prototype)
    {
        ExplicitPrototype = prototype;
        HasExplicitPrototype = true;
    }

    public bool HasExtra(string name) => _overlay.HasExtra(name);
    public object? TryGetExtra(string name) => _overlay.GetProperty(name);
    public void SetExtra(string name, object? value)
    {
        bool existed = HasOwnProperty(name);
        // A refused addition must not clear a deleted virtual member's tombstone.
        if (!existed && !_overlay.IsExtensible) return;
        _overlay.SetProperty(name, value);
        if (HasOwnProperty(name)) RecordWrittenKey(name, existed);
    }
    public bool DefineExtraProperty(string name, SharpTSPropertyDescriptor descriptor)
    {
        bool existed = HasOwnProperty(name);
        if (!existed && !_overlay.IsExtensible) return false;
        // Declared string methods (and constructor) remain virtual until an
        // override is needed. Seed their complete descriptor so ordinary
        // partial-descriptor merging retains omitted values and attributes.
        if (existed) MaterializeOwnVirtualProperty(name);
        if (!_overlay.DefineProperty(name, descriptor)) return false;
        RecordWrittenKey(name, existed);
        return true;
    }
    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
        => _overlay.GetOwnPropertyDescriptor(name);
    public ISharpTSCallable? GetExtraGetter(string name) => _overlay.GetGetter(name);
    public ISharpTSCallable? GetExtraSetter(string name) => _overlay.GetSetter(name);
    public bool DeleteProperty(string name)
    {
        if (!_overlay.DeleteProperty(name)) return false;
        _ownKeyOrder.Remove(name);
        _createdPropertyKeys.Remove(name);
        return true;
    }

    private bool IsBuiltIn(string name) => name == "constructor" || _klass.FindOwnMethod(name) != null;

    public bool HasOwnProperty(string name) => _overlay.HasOwnProperty(name);
    public IEnumerable<string> OwnEnumerableKeys() => _overlay.OwnEnumerableKeys();

    internal void FreezeOwnProperties()
    {
        MaterializeOwnVirtualPropertiesForIntegrity();
        _overlay.Freeze();
    }

    internal void SealOwnProperties()
    {
        MaterializeOwnVirtualPropertiesForIntegrity();
        _overlay.Seal();
    }

    private void MaterializeOwnVirtualPropertiesForIntegrity()
    {
        // Only present own methods and constructor are virtual. Accessors and
        // symbols already have canonical descriptors; never evaluate a getter.
        foreach (var name in OwnStringKeys().ToArray())
            MaterializeOwnVirtualProperty(name);
    }

    private void MaterializeOwnVirtualProperty(string name)
    {
        if (_overlay.GetOwnPropertyDescriptor(name) == null
            && TryGetOwnMember(name, out var member))
            _overlay.DefineProperty(name, new SharpTSPropertyDescriptor(
                member, writable: true, enumerable: false, configurable: true));
    }

    internal void InitializeOwnKeyOrder(IEnumerable<object> keys)
    {
        var declaredKeys = keys.ToArray();
        var existingKeys = _ownKeyOrder.ToArray();
        _ownKeyOrder.Clear();
        if (!_createdPropertyKeys.Contains("constructor")) RecordOwnKey("constructor");
        foreach (var key in declaredKeys)
            if (!_createdPropertyKeys.Contains(key)) RecordOwnKey(key);
        foreach (var key in existingKeys) RecordOwnKey(key);
    }

    internal IEnumerable<string> OwnStringKeys()
    {
        List<(uint Index, string Key)> indices = [];
        List<string> names = [];
        foreach (var key in _ownKeyOrder.OfType<string>())
        {
            if (!HasOwnProperty(key)) continue;
            if (PropertySemantics.TryGetArrayIndex(key, out uint index)) indices.Add((index, key));
            else names.Add(key);
        }
        return indices.OrderBy(entry => entry.Index).Select(entry => entry.Key).Concat(names);
    }

    internal IEnumerable<SharpTSSymbol> GetSymbolPropertyNames()
        => _ownKeyOrder.OfType<SharpTSSymbol>().Where(_overlay.HasSymbolProperty).ToArray();

    private void RecordOwnKey(object key)
    {
        if (!_ownKeyOrder.Contains(key)) _ownKeyOrder.Add(key);
    }

    private void RecordWrittenKey(object key, bool existed)
    {
        if (!existed)
        {
            _createdPropertyKeys.Add(key);
            _ownKeyOrder.Remove(key);
        }
        RecordOwnKey(key);
    }

    /// <summary>Symbol-keyed own properties (<c>Error.prototype[Symbol.toStringTag]</c>).</summary>
    public bool HasSymbolProperty(SharpTSSymbol key) => _overlay.HasSymbolProperty(key);
    public object? GetBySymbol(SharpTSSymbol key) => _overlay.GetBySymbol(key);
    public void SetBySymbol(SharpTSSymbol key, object? value)
    {
        bool existed = HasSymbolProperty(key);
        _overlay.SetBySymbol(key, value);
        if (HasSymbolProperty(key)) RecordWrittenKey(key, existed);
    }
    internal void DefineDeclaredSymbolAccessor(SharpTSSymbol key, SharpTSPropertyDescriptor descriptor)
    {
        if (_overlay.DefineProperty(key, descriptor)) RecordOwnKey(key);
    }
    internal void DefineDeclaredSymbolMethod(SharpTSSymbol key, ISharpTSCallable method)
    {
        if (_overlay.DefineProperty(key, SharpTSPropertyDescriptor.ForMethod(method))) RecordOwnKey(key);
    }
    public bool DefineSymbolProperty(SharpTSSymbol key, SharpTSPropertyDescriptor descriptor)
    {
        bool existed = HasSymbolProperty(key);
        if (!_overlay.DefineProperty(key, descriptor)) return false;
        RecordWrittenKey(key, existed);
        return true;
    }
    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(SharpTSSymbol key)
        => _overlay.GetOwnPropertyDescriptor(key);
    public bool DeleteBySymbolStrict(SharpTSSymbol key, bool strictMode)
    {
        if (!_overlay.DeleteBySymbolStrict(key, strictMode)) return false;
        _ownKeyOrder.Remove(key);
        _createdPropertyKeys.Remove(key);
        return true;
    }

    public object? GetMember(string name)
    {
        if (_overlay.TryGetOverride(name, out var value)) return value;
        if (name == "constructor") return _klass;
        var method = _klass.FindMethod(name);
        if (method != null) return method;
        return null;
    }

    // A chain reader must check only this prototype's virtual members before
    // advancing, so an ancestor's live overlay can shadow its declared method.
    internal bool TryGetOwnMember(string name, out object? value)
    {
        if (_overlay.TryGetOverride(name, out value))
            return _overlay.HasExtra(name);
        if (name == "constructor")
        {
            value = _klass;
            return true;
        }
        value = _klass.FindOwnMethod(name);
        return value is not null;
    }

    public override string ToString() => $"[object {_klass.Name}]";
}
