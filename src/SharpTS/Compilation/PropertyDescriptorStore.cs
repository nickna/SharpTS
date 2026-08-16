using System.Runtime.CompilerServices;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

/// <summary>
/// Stores property descriptor information for compiled objects (dictionaries).
/// Uses ConditionalWeakTable to avoid memory leaks - descriptors are automatically
/// cleaned up when the associated object is garbage collected.
/// </summary>
public static class PropertyDescriptorStore
{
    /// <summary>
    /// Stores property descriptors for each object, keyed by property name.
    /// </summary>
    private static readonly ConditionalWeakTable<object, Dictionary<string, CompiledPropertyDescriptor>> _descriptors = new();

    /// <summary>
    /// Stores the frozen state of objects.
    /// </summary>
    private static readonly ConditionalWeakTable<object, FrozenSealedState> _frozenSealedState = new();

    /// <summary>
    /// Gets or creates the descriptor dictionary for an object.
    /// </summary>
    private static Dictionary<string, CompiledPropertyDescriptor> GetOrCreateDescriptors(object obj)
    {
        return _descriptors.GetOrCreateValue(obj);
    }

    /// <summary>
    /// Defines a property with the given descriptor.
    /// </summary>
    /// <returns>True if the property was defined, false if blocked by frozen/sealed state.</returns>
    public static bool DefineProperty(object obj, string propertyKey, CompiledPropertyDescriptor descriptor)
    {
        // Check if object is frozen
        if (_frozenSealedState.TryGetValue(obj, out var state) && state.IsFrozen)
        {
            return false;
        }

        var descriptors = GetOrCreateDescriptors(obj);

        // Check if object is sealed and property doesn't exist
        if (state?.IsSealed == true && !descriptors.ContainsKey(propertyKey))
        {
            // Sealed objects can't add new properties
            // But we need to check if the property exists in the underlying dictionary
            if (obj is Dictionary<string, object?> dict && !dict.ContainsKey(propertyKey))
            {
                return false;
            }
        }

        // Store the descriptor
        descriptors[propertyKey] = descriptor;

        // If this is a data property (no accessors), also set the value in the dictionary
        if (descriptor.Getter == null && descriptor.Setter == null)
        {
            if (obj is Dictionary<string, object?> dict)
            {
                dict[propertyKey] = descriptor.Value;
            }
            else if (obj is System.Collections.IDictionary idict)
            {
                idict[propertyKey] = descriptor.Value;
            }
        }

        return true;
    }

    /// <summary>
    /// Gets the property descriptor for a property.
    /// </summary>
    /// <returns>The descriptor if one was explicitly defined, null otherwise.</returns>
    public static CompiledPropertyDescriptor? GetPropertyDescriptor(object obj, string propertyKey)
    {
        if (_descriptors.TryGetValue(obj, out var descriptors) && descriptors.TryGetValue(propertyKey, out var descriptor))
        {
            return descriptor;
        }
        return null;
    }

    /// <summary>
    /// Checks if a property has a getter accessor defined.
    /// </summary>
    public static bool TryGetGetter(object obj, string propertyKey, out object? getter)
    {
        if (_descriptors.TryGetValue(obj, out var descriptors) &&
            descriptors.TryGetValue(propertyKey, out var descriptor) &&
            descriptor.Getter != null)
        {
            getter = descriptor.Getter;
            return true;
        }
        getter = null;
        return false;
    }

    /// <summary>
    /// Checks if a property has a setter accessor defined.
    /// </summary>
    public static bool TryGetSetter(object obj, string propertyKey, out object? setter)
    {
        if (_descriptors.TryGetValue(obj, out var descriptors) &&
            descriptors.TryGetValue(propertyKey, out var descriptor) &&
            descriptor.Setter != null)
        {
            setter = descriptor.Setter;
            return true;
        }
        setter = null;
        return false;
    }

    /// <summary>
    /// Checks if a property is writable.
    /// </summary>
    /// <returns>True if writable (or no descriptor exists), false if explicitly non-writable.</returns>
    public static bool IsWritable(object obj, string propertyKey)
    {
        // Frozen objects are never writable
        if (_frozenSealedState.TryGetValue(obj, out var state) && state.IsFrozen)
        {
            return false;
        }

        if (_descriptors.TryGetValue(obj, out var descriptors) && descriptors.TryGetValue(propertyKey, out var descriptor))
        {
            return descriptor.Writable;
        }
        // Default is writable
        return true;
    }

    /// <summary>
    /// Freezes an object, preventing any modifications.
    /// </summary>
    public static void Freeze(object obj)
    {
        var state = _frozenSealedState.GetOrCreateValue(obj);
        state.IsFrozen = true;
        state.IsSealed = true;
        state.IsExtensible = false;
    }

    /// <summary>
    /// Seals an object, preventing adding/removing properties but allowing modification.
    /// </summary>
    public static void Seal(object obj)
    {
        var state = _frozenSealedState.GetOrCreateValue(obj);
        state.IsSealed = true;
        state.IsExtensible = false;
    }

    /// <summary>
    /// Prevents adding new properties to an object.
    /// </summary>
    public static void PreventExtensions(object obj)
    {
        var state = _frozenSealedState.GetOrCreateValue(obj);
        state.IsExtensible = false;
    }

    /// <summary>
    /// Checks if an object is extensible (can have new properties added).
    /// </summary>
    public static bool IsExtensible(object obj)
    {
        if (_frozenSealedState.TryGetValue(obj, out var state))
            return state.IsExtensible;
        return true; // Objects are extensible by default
    }

    /// <summary>
    /// Checks if an object is frozen.
    /// </summary>
    public static bool IsFrozen(object obj)
    {
        return _frozenSealedState.TryGetValue(obj, out var state) && state.IsFrozen;
    }

    /// <summary>
    /// Checks if an object is sealed.
    /// </summary>
    public static bool IsSealed(object obj)
    {
        return _frozenSealedState.TryGetValue(obj, out var state) && state.IsSealed;
    }

    /// <summary>
    /// Checks if adding a new property is allowed.
    /// </summary>
    public static bool CanAddProperty(object obj, string propertyKey)
    {
        if (_frozenSealedState.TryGetValue(obj, out var state))
        {
            if (!state.IsExtensible)
            {
                // Check if property already exists
                if (obj is Dictionary<string, object?> dict)
                {
                    return dict.ContainsKey(propertyKey);
                }
                if (obj is System.Collections.IDictionary idict)
                {
                    return idict.Contains(propertyKey);
                }
                if (obj is List<object?> list)
                {
                    // For arrays, only numeric indices can be accessed
                    if (int.TryParse(propertyKey, out int index))
                    {
                        return index >= 0 && index < list.Count;
                    }
                    return false;
                }
                if (_descriptors.TryGetValue(obj, out var descriptors) && descriptors.ContainsKey(propertyKey))
                {
                    return true;
                }
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Stores symbol-keyed properties for compiled objects.
    /// Uses object as key type to avoid compile-time dependency on SharpTSSymbol.
    /// </summary>
    private static readonly ConditionalWeakTable<object, Dictionary<object, object?>> _symbolStorage = new();

    /// <summary>
    /// Stores prototype references for compiled objects.
    /// </summary>
    private static readonly ConditionalWeakTable<object, PrototypeInfo> _prototypeStore = new();

    /// <summary>
    /// Gets all symbol keys from an object.
    /// Returns objects that are SharpTSSymbol instances (checked via reflection at runtime).
    /// </summary>
    public static IEnumerable<object> GetSymbolKeys(object obj)
    {
        if (_symbolStorage.TryGetValue(obj, out var symbols))
            return symbols.Keys;
        return [];
    }

    /// <summary>
    /// Sets the prototype of an object.
    /// </summary>
    public static void SetPrototype(object obj, object? proto)
    {
        var info = _prototypeStore.GetOrCreateValue(obj);
        info.Prototype = proto;
    }

    /// <summary>
    /// Gets the prototype of an object.
    /// </summary>
    public static object? GetPrototype(object obj)
    {
        return _prototypeStore.TryGetValue(obj, out var info) ? info.Prototype : null;
    }
}

/// <summary>
/// Holds the prototype reference for an object.
/// </summary>
internal class PrototypeInfo
{
    public object? Prototype { get; set; }
}

/// <summary>
/// Represents a property descriptor for compiled objects.
/// </summary>
public class CompiledPropertyDescriptor
{
    /// <summary>Value for data properties.</summary>
    public object? Value { get; set; }

    /// <summary>Getter function for accessor properties.</summary>
    public object? Getter { get; set; }

    /// <summary>Setter function for accessor properties.</summary>
    public object? Setter { get; set; }

    /// <summary>Whether the property value can be changed.</summary>
    public bool Writable { get; set; } = true;

    /// <summary>Whether the property shows up in enumeration.</summary>
    public bool Enumerable { get; set; } = true;

    /// <summary>Whether the property can be deleted or reconfigured.</summary>
    public bool Configurable { get; set; } = true;

    /// <summary>
    /// Creates a descriptor from a SharpTSPropertyDescriptor or similar object.
    /// </summary>
    public static CompiledPropertyDescriptor FromAny(object? descriptorObj)
    {
        var result = new CompiledPropertyDescriptor();

        if (descriptorObj is SharpTSPropertyDescriptor descriptor)
        {
            result.Value = descriptor.Value;
            result.Getter = descriptor.Get;
            result.Setter = descriptor.Set;
            result.Writable = descriptor.Writable;
            result.Enumerable = descriptor.Enumerable;
            result.Configurable = descriptor.Configurable;
            return result;
        }

        // Handle Dictionary<string, object?>
        if (descriptorObj is Dictionary<string, object?> dict)
        {
            if (dict.TryGetValue("value", out var value))
                result.Value = value;
            if (dict.TryGetValue("get", out var getter))
                result.Getter = getter;
            if (dict.TryGetValue("set", out var setter))
                result.Setter = setter;
            if (dict.TryGetValue("writable", out var w) && w is bool writable)
                result.Writable = writable;
            if (dict.TryGetValue("enumerable", out var e) && e is bool enumerable)
                result.Enumerable = enumerable;
            if (dict.TryGetValue("configurable", out var c) && c is bool configurable)
                result.Configurable = configurable;
            return result;
        }

        // Handle IDictionary
        if (descriptorObj is System.Collections.IDictionary idict)
        {
            if (idict.Contains("value"))
                result.Value = idict["value"];
            if (idict.Contains("get"))
                result.Getter = idict["get"];
            if (idict.Contains("set"))
                result.Setter = idict["set"];
            if (idict.Contains("writable") && idict["writable"] is bool writable)
                result.Writable = writable;
            if (idict.Contains("enumerable") && idict["enumerable"] is bool enumerable)
                result.Enumerable = enumerable;
            if (idict.Contains("configurable") && idict["configurable"] is bool configurable)
                result.Configurable = configurable;
            return result;
        }

        return result;
    }

    /// <summary>
    /// Converts to a SharpTSObject for returning from getOwnPropertyDescriptor.
    /// </summary>
    public object ToObject()
    {
        var fields = new Dictionary<string, object?>();

        if (Getter != null || Setter != null)
        {
            // Accessor descriptor
            if (Getter != null)
                fields["get"] = Getter;
            if (Setter != null)
                fields["set"] = Setter;
        }
        else
        {
            // Data descriptor
            fields["value"] = Value;
            fields["writable"] = Writable;
        }

        fields["enumerable"] = Enumerable;
        fields["configurable"] = Configurable;

        return new SharpTSObject(fields);
    }
}

/// <summary>
/// Tracks frozen/sealed/extensible state for compiled objects.
/// </summary>
internal class FrozenSealedState
{
    public bool IsFrozen { get; set; }
    public bool IsSealed { get; set; }
    public bool IsExtensible { get; set; } = true;
}
