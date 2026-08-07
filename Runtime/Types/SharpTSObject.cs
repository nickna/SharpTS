using SharpTS.Runtime;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of plain object literals.
/// </summary>
/// <remarks>
/// Represents <c>{ key: value }</c> object literals (not class instances).
/// Stores fields in a dictionary with dynamic Get/Set access. Used for structural
/// typing, object destructuring, and <c>Object.keys()</c> support.
/// Unlike <see cref="SharpTSInstance"/>, plain objects have no associated class or methods.
/// </remarks>
/// <seealso cref="SharpTSInstance"/>
/// <seealso cref="SharpTSArray"/>
public class SharpTSObject(Dictionary<string, object?> fields) : ISharpTSPropertyAccessor, ITypeCategorized
{
    private readonly Dictionary<string, object?> _fields = fields;
    private readonly Dictionary<SharpTSSymbol, object?> _symbolFields = new();
    private Dictionary<SharpTSSymbol, (ISharpTSCallable? Get, ISharpTSCallable? Set)>?
        _symbolAccessors;
    private Dictionary<string, ISharpTSCallable>? _getters;
    private Dictionary<string, ISharpTSCallable>? _setters;
    private HashSet<string>? _accessorProperties;
    private HashSet<string>? _callableIdentityProperties;
    private Dictionary<string, PropertyDescriptorFlags>? _descriptors;

    /// <inheritdoc />
    public virtual TypeCategory RuntimeCategory => TypeCategory.Record;

    /// <summary>
    /// Whether this object is frozen (no property additions, removals, or modifications).
    /// </summary>
    public bool IsFrozen { get; private set; }

    /// <summary>
    /// Whether this object is sealed (no property additions or removals, but modifications allowed).
    /// </summary>
    public bool IsSealed { get; private set; }

    /// <summary>
    /// Whether this object is extensible (can have new properties added).
    /// </summary>
    public bool IsExtensible { get; private set; } = true;

    /// <summary>
    /// The prototype object (set via Object.create or Object.setPrototypeOf).
    /// </summary>
    public object? Prototype { get; set; }

    /// <summary>
    /// True for a genuine null-prototype object (<c>Object.create(null)</c>,
    /// <c>Object.groupBy</c>/<c>Map.groupBy</c> results, etc.). Distinguishes
    /// these from an ordinary object whose <see cref="Prototype"/> is also null
    /// by default — an ordinary object inherits <c>Object.prototype</c>'s methods
    /// (hasOwnProperty, …), a null-prototype object does not.
    /// </summary>
    public bool IsNullPrototype { get; set; }

    /// <summary>
    /// Internal descriptor records returned by Object.getOwnPropertyDescriptor
    /// contain callable data values in their get/set fields. Reading those fields
    /// must preserve the original callable identity rather than treating them as
    /// object-literal methods and eagerly binding a receiver.
    /// </summary>
    internal bool PreserveCallableValueIdentity { get; init; }

    internal void PreserveCallableValueIdentityFor(string name)
    {
        _callableIdentityProperties ??= [];
        _callableIdentityProperties.Add(name);
    }

    internal bool ShouldPreserveCallableValueIdentity(string name)
        => PreserveCallableValueIdentity
            || (_callableIdentityProperties?.Contains(name) ?? false);

    /// <summary>
    /// Freezes this object, preventing any property changes.
    /// </summary>
    public void Freeze()
    {
        SetOwnPropertyIntegrityLevel(frozen: true);
        IsFrozen = true;
        IsSealed = true; // Frozen implies sealed
        IsExtensible = false; // Frozen implies non-extensible
    }

    /// <summary>
    /// Seals this object, preventing property additions/removals but allowing modifications.
    /// </summary>
    public void Seal()
    {
        SetOwnPropertyIntegrityLevel(frozen: false);
        IsSealed = true;
        IsExtensible = false;
    }

    /// <summary>
    /// Applies SetIntegrityLevel's descriptor changes to every own string-keyed
    /// property. Sealing clears configurability; freezing also clears
    /// writability for data properties while preserving enumerability.
    /// </summary>
    private void SetOwnPropertyIntegrityLevel(bool frozen)
    {
        _descriptors ??= [];

        foreach (var name in _fields.Keys)
        {
            var current = GetPropertyFlags(name);
            _descriptors[name] = PropertyDescriptorFlags.ForDefineProperty(
                writable: frozen ? false : current.Writable,
                enumerable: current.Enumerable,
                configurable: false);
        }

        if (_accessorProperties is null) return;
        foreach (var name in _accessorProperties)
        {
            var current = GetPropertyFlags(name);
            _descriptors[name] = PropertyDescriptorFlags.ForDefineProperty(
                writable: current.Writable,
                enumerable: current.Enumerable,
                configurable: false);
        }
    }

    /// <summary>
    /// Prevents adding new properties to this object.
    /// </summary>
    public void PreventExtensions()
    {
        IsExtensible = false;
    }

    /// <summary>
    /// Gets all symbol-keyed property names.
    /// </summary>
    public IEnumerable<SharpTSSymbol> GetSymbolPropertyNames()
    {
        return _symbolFields.Keys;
    }

    /// <summary>
    /// Expose fields for Object.keys() and object rest patterns
    /// </summary>
    public IReadOnlyDictionary<string, object?> Fields => _fields;

    /// <inheritdoc />
    public IEnumerable<string> PropertyNames => _fields.Keys;

    /// <summary>
    /// Names of accessor properties on this object. Disjoint from
    /// <see cref="Fields"/>.Keys because <see cref="DefineProperty"/> removes any
    /// data-property entry when installing an accessor. The explicit name set
    /// preserves setter-only and get/set-undefined accessors, which otherwise have
    /// no concrete callable to act as their existence marker.
    /// </summary>
    public IEnumerable<string> AccessorPropertyNames =>
        _accessorProperties ?? Enumerable.Empty<string>();

    /// <inheritdoc />
    public object? GetProperty(string name)
    {
        if (_fields.TryGetValue(name, out object? value))
        {
            return value;
        }
        // Map the implicit `__proto__` slot to the Prototype property so the
        // interpreter's prototype-chain walk (Interpreter.Properties.cs) reaches
        // inherited own properties without us mirroring Prototype into _fields
        // (which would leak __proto__ into Object.keys / for-in).
        if (name == "__proto__" && Prototype != null)
        {
            return Prototype;
        }
        // Non-existent properties return undefined, not null (JavaScript semantics)
        return SharpTSUndefined.Instance;
    }

    public RuntimeValue GetPropertyRV(string name) => RuntimeValue.FromBoxed(GetProperty(name));

    /// <summary>
    /// Reflection-friendly field accessor used by the compiled-mode
    /// <c>GetFieldsProperty</c> helper. Its <c>GetMember(string)</c> reflection
    /// fallback (<c>Compilation/RuntimeEmitter.Objects.Properties.cs</c>) calls
    /// this method by name on any object whose fields it can't otherwise
    /// resolve. Exposing it lets compiled code read properties off an
    /// interpreter-constructed <see cref="SharpTSObject"/> (e.g., iterator
    /// result objects like <c>{value, done}</c>) without a type-specific
    /// dispatch branch.
    /// </summary>
    public object? GetMember(string name)
    {
        return _fields.TryGetValue(name, out var value) ? value : SharpTSUndefined.Instance;
    }

    /// <inheritdoc />
    public void SetProperty(string name, object? value)
    {
        if (IsFrozen)
        {
            // Frozen objects silently ignore property modifications (JavaScript behavior in non-strict mode)
            SloppyModeWarnings.Warn("write to frozen", $"Assignment to frozen object property '{name}' ignored");
            return;
        }

        // An accessor without a setter rejects assignment, including a
        // get/set-undefined accessor whose existence is tracked separately.
        if (IsAccessorProperty(name) && !HasSetter(name))
        {
            SloppyModeWarnings.Warn("write to getter-only", $"Assignment to getter-only property '{name}' ignored");
            return;
        }

        bool exists = _fields.ContainsKey(name) || IsAccessorProperty(name);
        if (!IsExtensible && !exists)
        {
            // Non-extensible objects silently ignore new property additions
            SloppyModeWarnings.Warn("add to non-extensible", $"Property addition to non-extensible object '{name}' ignored");
            return;
        }

        // Check writable flag for properties defined via defineProperty
        if (exists && _descriptors?.TryGetValue(name, out var flags) == true && flags.HasExplicitDescriptor && !flags.Writable)
        {
            SloppyModeWarnings.Warn("write to non-writable", $"Assignment to non-writable property '{name}' ignored");
            return;
        }

        _fields[name] = value;
    }

    /// <summary>
    /// Sets a property value with strict mode behavior.
    /// In strict mode, throws TypeError for modifications to frozen objects, new properties on sealed objects,
    /// or assignments to getter-only properties.
    /// </summary>
    /// <param name="name">The property name to set.</param>
    /// <param name="value">The value to set.</param>
    /// <param name="strictMode">Whether strict mode is enabled.</param>
    public void SetPropertyStrict(string name, object? value, bool strictMode)
    {
        if (IsFrozen)
        {
            if (strictMode)
            {
                throw StrictModeErrors.TypeError($"Cannot assign to read only property '{name}' of object");
            }
            SloppyModeWarnings.Warn("write to frozen", $"Assignment to frozen object property '{name}' ignored");
            return;
        }

        // An accessor without a setter rejects assignment, including a
        // get/set-undefined accessor whose existence is tracked separately.
        if (IsAccessorProperty(name) && !HasSetter(name))
        {
            if (strictMode)
            {
                throw StrictModeErrors.TypeError($"Cannot set property '{name}' which has only a getter");
            }
            SloppyModeWarnings.Warn("write to getter-only", $"Assignment to getter-only property '{name}' ignored");
            return;
        }

        bool exists = _fields.ContainsKey(name) || IsAccessorProperty(name);
        if (!IsExtensible && !exists)
        {
            if (strictMode)
            {
                throw StrictModeErrors.TypeError($"Cannot add property '{name}' to a non-extensible object");
            }
            SloppyModeWarnings.Warn("add to non-extensible", $"Property addition to non-extensible object '{name}' ignored");
            return;
        }

        // Check writable flag for properties defined via defineProperty
        if (exists && _descriptors?.TryGetValue(name, out var flags) == true && flags.HasExplicitDescriptor && !flags.Writable)
        {
            if (strictMode)
            {
                throw StrictModeErrors.TypeError($"Cannot assign to read only property '{name}'");
            }
            SloppyModeWarnings.Warn("write to non-writable", $"Assignment to non-writable property '{name}' ignored");
            return;
        }

        _fields[name] = value;
    }

    /// <summary>
    /// Removes a property by name. Respects frozen/sealed state.
    /// </summary>
    public bool DeleteProperty(string name)
    {
        if (IsFrozen || IsSealed)
        {
            // Frozen and sealed objects silently ignore property deletions
            SloppyModeWarnings.Warn("delete from frozen/sealed", $"Delete from frozen/sealed object property '{name}' returns false");
            return false;
        }
        return RemoveOwnProperty(name);
    }

    /// <summary>
    /// Removes a property by name with strict mode behavior.
    /// In strict mode, throws TypeError for deletions on frozen/sealed objects.
    /// </summary>
    /// <param name="name">The property name to delete.</param>
    /// <param name="strictMode">Whether strict mode is enabled.</param>
    /// <returns>True if the property was deleted, false otherwise.</returns>
    public bool DeletePropertyStrict(string name, bool strictMode)
    {
        if (IsFrozen || IsSealed)
        {
            if (strictMode)
            {
                throw StrictModeErrors.TypeError($"Cannot delete property '{name}' of a frozen or sealed object");
            }
            SloppyModeWarnings.Warn("delete from frozen/sealed", $"Delete from frozen/sealed object property '{name}' returns false");
            return false;
        }
        return RemoveOwnProperty(name);
    }

    /// <summary>
    /// Removes an own property (data OR accessor) and its descriptor, honoring
    /// configurability. Returns true when something was removed, false when a
    /// present non-configurable property blocks the delete (or nothing matched,
    /// preserving the legacy absent → false result). Accessors live in
    /// <c>_getters</c>/<c>_setters</c>, so a getter-only property (e.g.
    /// RegExp.prototype.global) is now deletable. The configurability check
    /// relies on correct ToBoolean attribute coercion (interpreter-aware
    /// ApplyBooleanAttributes in ObjectBuiltIns).
    /// </summary>
    private bool RemoveOwnProperty(string name)
    {
        if (_descriptors != null && _descriptors.TryGetValue(name, out var flags)
            && flags.HasExplicitDescriptor && !flags.Configurable)
            return false;
        bool removed = _fields.Remove(name);
        if (_getters?.Remove(name) == true) removed = true;
        if (_setters?.Remove(name) == true) removed = true;
        if (_accessorProperties?.Remove(name) == true) removed = true;
        if (removed) _descriptors?.Remove(name);
        return removed;
    }

    public bool HasProperty(string name)
    {
        if (_fields.ContainsKey(name)) return true;
        if (IsAccessorProperty(name)) return true;
        // Treat __proto__ as a virtual own slot mirroring the Prototype
        // property — see GetProperty for the matching read.
        if (name == "__proto__" && Prototype != null) return true;
        return false;
    }

    /// <summary>
    /// Defines a getter for a property.
    /// </summary>
    public void DefineGetter(string name, ISharpTSCallable getter)
    {
        _accessorProperties ??= [];
        _accessorProperties.Add(name);
        _getters ??= new Dictionary<string, ISharpTSCallable>();
        _getters[name] = getter;
    }

    /// <summary>
    /// Defines a setter for a property.
    /// </summary>
    public void DefineSetter(string name, ISharpTSCallable setter)
    {
        _accessorProperties ??= [];
        _accessorProperties.Add(name);
        _setters ??= new Dictionary<string, ISharpTSCallable>();
        _setters[name] = setter;
    }

    private bool IsAccessorProperty(string name)
        => _accessorProperties?.Contains(name) ?? false;

    /// <summary>
    /// Checks if a property has a getter.
    /// </summary>
    public bool HasGetter(string name)
    {
        return _getters?.ContainsKey(name) ?? false;
    }

    /// <summary>
    /// Checks if a property has a setter.
    /// </summary>
    public bool HasSetter(string name)
    {
        return _setters?.ContainsKey(name) ?? false;
    }

    /// <summary>
    /// Gets the getter function for a property, or null if none.
    /// </summary>
    public ISharpTSCallable? GetGetter(string name)
    {
        return _getters?.GetValueOrDefault(name);
    }

    /// <summary>
    /// Gets the setter function for a property, or null if none.
    /// </summary>
    public ISharpTSCallable? GetSetter(string name)
    {
        return _setters?.GetValueOrDefault(name);
    }

    /// <summary>
    /// Gets a value by symbol key.
    /// </summary>
    public object? GetBySymbol(SharpTSSymbol symbol)
    {
        return _symbolFields.TryGetValue(symbol, out var value) ? value : null;
    }

    /// <summary>Installs a symbol-keyed accessor property.</summary>
    internal void DefineSymbolAccessor(
        SharpTSSymbol symbol, ISharpTSCallable? getter, ISharpTSCallable? setter)
    {
        _symbolAccessors ??= [];
        _symbolAccessors[symbol] = (getter, setter);
        _symbolFields.Remove(symbol);
    }

    /// <summary>Returns a symbol-keyed accessor pair when one is defined.</summary>
    internal bool TryGetSymbolAccessor(
        SharpTSSymbol symbol, out ISharpTSCallable? getter, out ISharpTSCallable? setter)
    {
        if (_symbolAccessors != null
            && _symbolAccessors.TryGetValue(symbol, out var pair))
        {
            getter = pair.Get;
            setter = pair.Set;
            return true;
        }

        getter = null;
        setter = null;
        return false;
    }

    /// <summary>
    /// Sets a value by symbol key.
    /// </summary>
    public void SetBySymbol(SharpTSSymbol symbol, object? value)
    {
        if (IsFrozen)
        {
            return;
        }

        bool exists = HasSymbolProperty(symbol);
        if (!IsExtensible && !exists)
        {
            return;
        }

        _symbolAccessors?.Remove(symbol);
        _symbolFields[symbol] = value;
    }

    /// <summary>
    /// Sets a value by symbol key with strict mode behavior.
    /// </summary>
    public void SetBySymbolStrict(SharpTSSymbol symbol, object? value, bool strictMode)
    {
        if (IsFrozen)
        {
            if (strictMode)
            {
                throw StrictModeErrors.TypeError("Cannot assign to read only symbol property of object");
            }
            return;
        }

        bool exists = HasSymbolProperty(symbol);
        if (!IsExtensible && !exists)
        {
            if (strictMode)
            {
                throw StrictModeErrors.TypeError("Cannot add symbol property to a non-extensible object");
            }
            return;
        }

        _symbolAccessors?.Remove(symbol);
        _symbolFields[symbol] = value;
    }

    /// <summary>
    /// Checks if the object has a property with the given symbol key.
    /// </summary>
    public bool HasSymbolProperty(SharpTSSymbol symbol)
    {
        return _symbolFields.ContainsKey(symbol)
            || (_symbolAccessors?.ContainsKey(symbol) ?? false);
    }

    /// <summary>
    /// Removes a property by symbol key. Respects frozen/sealed state.
    /// </summary>
    public bool DeleteBySymbol(SharpTSSymbol symbol)
    {
        if (IsFrozen || IsSealed)
        {
            // Frozen and sealed objects silently ignore property deletions
            return false;
        }
        bool removed = _symbolFields.Remove(symbol);
        return (_symbolAccessors?.Remove(symbol) ?? false) || removed;
    }

    /// <summary>
    /// Removes a property by symbol key with strict mode behavior.
    /// In strict mode, throws TypeError for deletions on frozen/sealed objects.
    /// </summary>
    public bool DeleteBySymbolStrict(SharpTSSymbol symbol, bool strictMode)
    {
        if (IsFrozen || IsSealed)
        {
            if (strictMode)
            {
                throw new Exception($"TypeError: Cannot delete symbol property of a frozen or sealed object");
            }
            return false;
        }
        bool removed = _symbolFields.Remove(symbol);
        return (_symbolAccessors?.Remove(symbol) ?? false) || removed;
    }

    /// <summary>
    /// Defines or modifies a property with the given descriptor.
    /// Returns true on success, false if the operation is not allowed.
    /// </summary>
    public bool DefineProperty(string name, SharpTSPropertyDescriptor descriptor)
    {
        // Get existing descriptor flags if any
        bool hasExisting = _fields.ContainsKey(name) || IsAccessorProperty(name);
        bool existingIsAccessor = IsAccessorProperty(name);
        bool descriptorIsAccessor = descriptor.HasGet || descriptor.HasSet;
        bool descriptorIsData = descriptor.HasValue || descriptor.HasWritable;
        PropertyDescriptorFlags existingFlags = default;

        if (hasExisting && _descriptors?.TryGetValue(name, out existingFlags) != true)
        {
            // Existing property without explicit descriptor - use defaults
            existingFlags = PropertyDescriptorFlags.Default;
        }

        // Check if we can modify the property
        if (hasExisting && existingFlags.HasExplicitDescriptor && !existingFlags.Configurable)
        {
            // Non-configurable properties cannot become configurable, change
            // enumerability, or switch between data/accessor kinds.
            if ((descriptor.HasConfigurable && descriptor.Configurable) ||
                (descriptor.HasEnumerable && descriptor.Enumerable != existingFlags.Enumerable))
            {
                return false;
            }

            if (existingIsAccessor)
            {
                if (descriptorIsData)
                    return false;
                if (descriptor.HasGet && !SameValue(descriptor.Get, GetGetter(name)))
                    return false;
                if (descriptor.HasSet && !SameValue(descriptor.Set, GetSetter(name)))
                    return false;
            }
            else
            {
                if (descriptorIsAccessor)
                    return false;
                if (!existingFlags.Writable)
                {
                    if (descriptor.HasWritable && descriptor.Writable)
                        return false;
                    var currentValue = _fields.TryGetValue(name, out var value) ? value : SharpTSUndefined.Instance;
                    if (descriptor.HasValue && !SameValue(descriptor.Value, currentValue))
                        return false;
                }
            }
        }

        // Check sealed/frozen/extensible state
        if (IsFrozen)
        {
            return false;
        }
        if (!IsExtensible && !hasExisting)
        {
            return false;
        }

        // Store the descriptor flags
        _descriptors ??= new Dictionary<string, PropertyDescriptorFlags>();
        _descriptors[name] = PropertyDescriptorFlags.ForDefineProperty(
            descriptor.Writable,
            descriptor.Enumerable,
            descriptor.Configurable
        );

        // ECMA-262 §10.1.6.3 classification. SharpTSObject represents an accessor
        // property only via its accessor-name marker plus optional concrete
        // getter/setter callables. Any descriptor that specifies get or set is
        // therefore classified as an accessor even when both are undefined; an
        // attribute-only redefine of an existing accessor also keeps its kind.
        bool descHasRealAccessor = descriptor.Get != null || descriptor.Set != null;
        bool descSpecifiesAccessor = descriptor.HasGet || descriptor.HasSet;
        // Apply the descriptor
        if (descHasRealAccessor
            || descSpecifiesAccessor
            || (!descSpecifiesAccessor && !descriptor.HasValue && existingIsAccessor))
        {
            // Accessor property - remove any data property value
            _fields.Remove(name);
            _accessorProperties ??= [];
            _accessorProperties.Add(name);

            // Install a concrete getter/setter; clear it only when the descriptor
            // EXPLICITLY specifies the half as undefined (HasGet/HasSet with a null
            // callable); otherwise (omitted) preserve the existing one. The non-null
            // check comes first so internally-built descriptors that set Get/Set
            // directly without the Has* flags (e.g. RegExp.prototype's accessor
            // slots) still register their getter.
            if (descriptor.Get != null) DefineGetter(name, descriptor.Get);
            else if (descriptor.HasGet) _getters?.Remove(name);

            if (descriptor.Set != null) DefineSetter(name, descriptor.Set);
            else if (descriptor.HasSet) _setters?.Remove(name);
        }
        else
        {
            // Data property - remove any accessor
            _accessorProperties?.Remove(name);
            _getters?.Remove(name);
            _setters?.Remove(name);

            // Only set value when the descriptor actually specifies `value`; an
            // attribute-only redefine of an existing data property preserves its
            // current value (ECMA-262 §10.1.6.3). Gating on HasValue rather than
            // `Value != null` avoids wiping the value to the undefined sentinel when
            // `value` was omitted (#801).
            if (descriptor.HasValue)
            {
                _fields[name] = descriptor.Value;
            }
            else if (!hasExisting)
            {
                // A brand-new data property whose descriptor omits `value` defaults
                // to undefined per spec — store the sentinel (not C# null, which
                // would read back as typeof "object").
                _fields[name] = SharpTSUndefined.Instance;
            }
        }

        return true;
    }

    /// <summary>
    /// ECMA-262 SameValue comparison used by ValidateAndApplyPropertyDescriptor.
    /// Object/callable identity is reference-based; numbers additionally keep
    /// NaN equal to itself and distinguish positive from negative zero.
    /// </summary>
    private static bool SameValue(object? left, object? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is double ld && right is double rd)
        {
            if (double.IsNaN(ld) && double.IsNaN(rd)) return true;
            if (ld == 0 && rd == 0)
                return BitConverter.DoubleToInt64Bits(ld) == BitConverter.DoubleToInt64Bits(rd);
            return ld.Equals(rd);
        }
        return left?.Equals(right) == true;
    }

    /// <summary>
    /// Gets the property descriptor for the given property name.
    /// Returns null if the property doesn't exist.
    /// </summary>
    public SharpTSPropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        bool hasDataProperty = _fields.TryGetValue(name, out var fieldValue);
        bool isAccessor = IsAccessorProperty(name);

        if (!hasDataProperty && !isAccessor)
        {
            return null;
        }

        // Get descriptor flags (defaults if not explicitly set)
        PropertyDescriptorFlags flags = default;
        if (_descriptors?.TryGetValue(name, out flags) != true)
        {
            flags = PropertyDescriptorFlags.Default;
        }

        if (isAccessor)
        {
            // Accessor property
            return new SharpTSPropertyDescriptor
            {
                Get = GetGetter(name),
                Set = GetSetter(name),
                HasGet = true,
                HasSet = true,
                Enumerable = flags.Enumerable,
                Configurable = flags.Configurable
            };
        }
        else
        {
            // Data property
            return new SharpTSPropertyDescriptor
            {
                Value = fieldValue,
                Writable = flags.Writable,
                Enumerable = flags.Enumerable,
                Configurable = flags.Configurable
            };
        }
    }

    /// <summary>
    /// Gets the descriptor flags for a property, or default flags if not explicitly set.
    /// </summary>
    public PropertyDescriptorFlags GetPropertyFlags(string name)
    {
        if (_descriptors?.TryGetValue(name, out var flags) == true)
        {
            return flags;
        }
        return PropertyDescriptorFlags.Default;
    }

    /// <summary>
    /// Marks an existing own data property as non-enumerable, leaving its other
    /// attributes unchanged. Used for a String exotic wrapper's <c>length</c>,
    /// which is non-enumerable per ECMA-262 §22.1.4.1 so it stays out of
    /// Object.keys/values/entries and for-in (#475).
    /// </summary>
    internal void MarkNonEnumerable(string name)
    {
        var cur = GetPropertyFlags(name);
        _descriptors ??= new Dictionary<string, PropertyDescriptorFlags>();
        _descriptors[name] = PropertyDescriptorFlags.ForDefineProperty(cur.Writable, enumerable: false, cur.Configurable);
    }

    /// <summary>
    /// True for the internal-slot field names that back boxed primitive wrappers
    /// (<c>new String/Number/Boolean</c>): they hold [[StringData]]/[[NumberData]]/
    /// [[BooleanData]] and the type tag, not real own properties, so enumeration
    /// must skip them (#475).
    /// </summary>
    internal static bool IsInternalSlot(string key) => key is "__primitiveType" or "__primitiveValue";

    /// <summary>
    /// Own enumerable string-keyed property names: data fields first, followed by
    /// accessor properties, honoring per-property enumerability.
    /// </summary>
    internal IEnumerable<string> OwnEnumerableKeys()
    {
        foreach (var key in _fields.Keys)
            if (!IsInternalSlot(key) && GetPropertyFlags(key).Enumerable)
                yield return key;
        if (_accessorProperties == null) yield break;
        foreach (var key in _accessorProperties)
            if (GetPropertyFlags(key).Enumerable)
                yield return key;
    }

    public override string ToString() => $"{{ {string.Join(", ", _fields.Select(f => $"{f.Key}: {f.Value}"))} }}";
}
