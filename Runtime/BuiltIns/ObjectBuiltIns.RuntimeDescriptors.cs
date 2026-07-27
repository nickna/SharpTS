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
    /// Copies properties from a source object to a target dictionary; used by
    /// the interpreter's Object.setPrototypeOf when re-pointing a dictionary
    /// object's prototype.
    /// </summary>
    private static void RuntimeCopyPropertiesFrom(object source, Dictionary<string, object?> target)
    {
        switch (source)
        {
            case SharpTSObject srcObj:
                foreach (var kv in srcObj.Fields)
                {
                    target[kv.Key] = kv.Value;
                }
                break;

            case SharpTSInstance srcInst:
                foreach (var key in srcInst.GetFieldNames())
                {
                    target[key] = srcInst.GetRawField(key);
                }
                break;

            case Dictionary<string, object?> dict:
                foreach (var kv in dict)
                {
                    target[kv.Key] = kv.Value;
                }
                break;

            case System.Collections.IDictionary idict:
                foreach (System.Collections.DictionaryEntry entry in idict)
                {
                    target[entry.Key?.ToString() ?? ""] = entry.Value;
                }
                break;

            default:
                // Try reflection for compiled class instances
                var type = source.GetType();

                // First, get typed backing fields (fields starting with __) for compiled class instances
                foreach (var backingField in type.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                {
                    if (backingField.Name.StartsWith("__"))
                    {
                        string pascalName = backingField.Name[2..]; // Remove __ prefix
                        // Convert PascalCase back to camelCase (how TypeScript originally named it)
                        string propName = ToCamelCase(pascalName);
                        target[propName] = backingField.GetValue(source);
                    }
                }

                // Also check for _fields dictionary (for dynamically added properties)
                var fieldsField = type.GetField("_fields", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (fieldsField != null)
                {
                    var fieldsValue = fieldsField.GetValue(source);
                    if (fieldsValue is System.Collections.IDictionary fieldsDict)
                    {
                        foreach (System.Collections.DictionaryEntry entry in fieldsDict)
                        {
                            target[entry.Key?.ToString() ?? ""] = entry.Value;
                        }
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Converts a PascalCase property name to camelCase.
    /// </summary>
    private static string ToCamelCase(string pascalCase)
    {
        if (string.IsNullOrEmpty(pascalCase))
            return pascalCase;
        if (char.IsLower(pascalCase[0]))
            return pascalCase;
        return char.ToLowerInvariant(pascalCase[0]) + pascalCase[1..];
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
        var methods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        // Find HasProperty and GetProperty methods
        System.Reflection.MethodInfo? hasPropertyMethod = null;
        System.Reflection.MethodInfo? getPropertyMethod = null;

        foreach (var m in methods)
        {
            if (m.Name == "HasProperty")
            {
                var parms = m.GetParameters();
                if (parms.Length == 1 && parms[0].ParameterType == typeof(string))
                {
                    hasPropertyMethod = m;
                }
            }
            else if (m.Name == "GetProperty")
            {
                var parms = m.GetParameters();
                if (parms.Length == 1 && parms[0].ParameterType == typeof(string))
                {
                    getPropertyMethod = m;
                }
            }
        }

        if (hasPropertyMethod != null && getPropertyMethod != null)
        {
            var hasProperty = (bool?)hasPropertyMethod.Invoke(target, [propKey]);
            if (hasProperty != true)
            {
                return null;
            }

            var value = getPropertyMethod.Invoke(target, [propKey]);
            return new SharpTSPropertyDescriptor
            {
                Value = value,
                Writable = true,
                Enumerable = true,
                Configurable = true
            };
        }

        return null;
    }
}
