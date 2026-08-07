using SharpTS.Compilation;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

// Split out of ObjectBuiltIns.cs (#1143): the reflective runtime
// property-descriptor READ path. Live members: RuntimeGetOwnPropertyDescriptor
// (Reflect.getOwnPropertyDescriptor routes through it) and RuntimeGetPrototypeOf
// (SharpTSObjectPrototype). The write-side Runtime* twins that used to live here
// (defineProperty/-ies, create, preventExtensions, isExtensible,
// getOwnPropertySymbols, setPrototypeOf) were dead: compiled code dispatches to
// the emitted $Runtime methods (Compilation/Emitters/ObjectStaticEmitter), never
// to this class, and nothing else called them (2026-07 cleanup audit).
public static partial class ObjectBuiltIns
{
    /// <summary>
    /// Runtime helper for Object.getPrototypeOf; used by SharpTSObjectPrototype.
    /// </summary>
    public static object? RuntimeGetPrototypeOf(object? obj)
    {
        if (obj == null)
            throw new Exception("TypeError: Cannot convert null to object");

        return obj switch
        {
            SharpTSObject tsObj => tsObj.Prototype,
            SharpTSInstance inst => inst.Prototype,
            SharpTSArray => null,
            Dictionary<string, object?> dict => PropertyDescriptorStore.GetPrototype(dict),
            _ => null
        };
    }

    /// <summary>
    /// Runtime helper for Object.getOwnPropertyDescriptor; the live caller is
    /// Reflect.getOwnPropertyDescriptor (ReflectBuiltIns).
    /// </summary>
    public static object? RuntimeGetOwnPropertyDescriptor(object? target, object? propertyKey)
    {
        if (target == null)
        {
            throw new Exception("TypeError: Object.getOwnPropertyDescriptor called on null or undefined");
        }

        // ECMA-262 §7.1.19 ToPropertyKey: pass Symbols through to the symbol-dict
        // path, stringify everything else.
        if (propertyKey is SharpTSSymbol symKey)
        {
            return GetOwnPropertyDescriptorBySymbol(target, symKey);
        }
        var propKey = PropertyKeyConverter.ToPropertyKeyString(propertyKey);

        // Special handling for Dictionary<string, object?> to preserve $TSFunction getters/setters
        // (which don't implement ISharpTSCallable)
        if (target is Dictionary<string, object?> dict)
        {
            // Check PropertyDescriptorStore for explicitly defined descriptor
            var storedDesc = PropertyDescriptorStore.GetPropertyDescriptor(dict, propKey);
            if (storedDesc != null)
            {
                // Use CompiledPropertyDescriptor.ToObject() directly to preserve getter/setter types
                return storedDesc.ToObject();
            }

            // Fall back to checking the dictionary directly
            if (dict.TryGetValue(propKey, out var value))
            {
                var desc = new SharpTSPropertyDescriptor
                {
                    Value = value,
                    Writable = true,
                    Enumerable = true,
                    Configurable = true
                };
                return desc.ToObject();
            }
            return null;
        }

        SharpTSPropertyDescriptor? descriptor = target switch
        {
            SharpTSObject obj => obj.GetOwnPropertyDescriptor(propKey),
            SharpTSInstance inst => inst.GetOwnPropertyDescriptor(propKey),
            SharpTSArray arr => arr.GetOwnPropertyDescriptor(propKey),
            System.Collections.IDictionary idict => GetDescriptorFromIDictionary(idict, propKey),
            System.Collections.IList list => GetDescriptorFromList(list, propKey),
            _ => TryGetPropertyDescriptorViaReflection(target, propKey)
        };

        if (descriptor == null)
        {
            return null;
        }

        // Return as an object
        return descriptor.ToObject();
    }

    /// <summary>
    /// Gets a property descriptor from an IList (compiled arrays).
    /// </summary>
    private static SharpTSPropertyDescriptor? GetDescriptorFromList(System.Collections.IList list, string propKey)
    {
        // Handle "length" property
        if (propKey == "length")
        {
            return new SharpTSPropertyDescriptor
            {
                Value = (double)list.Count,
                Writable = true,
                Enumerable = false,
                Configurable = false
            };
        }

        // Handle numeric index
        if (int.TryParse(propKey, out int index) && index >= 0 && index < list.Count)
        {
            return new SharpTSPropertyDescriptor
            {
                Value = list[index],
                Writable = true,
                Enumerable = true,
                Configurable = true
            };
        }

        return null;
    }

    /// <summary>
    /// Gets a property descriptor from an IDictionary.
    /// </summary>
    private static SharpTSPropertyDescriptor? GetDescriptorFromIDictionary(System.Collections.IDictionary dict, string propKey)
    {
        // Check PropertyDescriptorStore for explicitly defined descriptor
        var storedDesc = PropertyDescriptorStore.GetPropertyDescriptor(dict, propKey);
        if (storedDesc != null)
        {
            return new SharpTSPropertyDescriptor
            {
                Value = storedDesc.Value,
                Get = storedDesc.Getter as ISharpTSCallable,
                Set = storedDesc.Setter as ISharpTSCallable,
                Writable = storedDesc.Writable,
                Enumerable = storedDesc.Enumerable,
                Configurable = storedDesc.Configurable
            };
        }

        // Fall back to checking the dictionary directly
        if (!dict.Contains(propKey))
        {
            return null;
        }
        return new SharpTSPropertyDescriptor
        {
            Value = dict[propKey],
            Writable = true,
            Enumerable = true,
            Configurable = true
        };
    }

    /// <summary>
    /// Attempts to get a property descriptor from a compiled $Object using reflection.
    /// </summary>
    private static SharpTSPropertyDescriptor? TryGetPropertyDescriptorViaReflection(object target, string propKey)
    {
        var type = target.GetType();
        if (!ManagedEmittedShapeReflection.IsShape(
                type, ManagedEmittedShape.HasFields))
        {
            return null;
        }

        var hasPropertyMethod = ManagedEmittedShapeReflection.GetPublicMethod(
            type, ManagedEmittedShape.HasFields, "HasProperty", [typeof(string)]);
        var getPropertyMethod = ManagedEmittedShapeReflection.GetPublicMethod(
            type, ManagedEmittedShape.HasFields, "GetProperty", [typeof(string)]);

        if (hasPropertyMethod?.Invoke(target, [propKey]) is not true ||
            getPropertyMethod == null)
        {
            return null;
        }

        return new SharpTSPropertyDescriptor
        {
            Value = getPropertyMethod.Invoke(target, [propKey]),
            Writable = true,
            Enumerable = true,
            Configurable = true
        };
    }
}
