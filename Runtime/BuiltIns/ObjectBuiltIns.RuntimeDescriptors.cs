using SharpTS.Compilation;
using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

// Split out of ObjectBuiltIns.cs (#1143): the reflective runtime
// property-descriptor subsystem. These Runtime* helpers are invoked from
// compiled code (and the interpreter's runtime paths) to define/read property
// descriptors, prototypes, and extensibility on arbitrary CLR/guest objects
// via reflection. Kept separate from the JS Object.* statics.
public static partial class ObjectBuiltIns
{
    /// <summary>
    /// Runtime helper for Object.defineProperty called from compiled code.
    /// </summary>
    public static object? RuntimeDefineProperty(object? target, object? propertyKey, object? descriptorArg)
    {
        if (target == null)
        {
            throw new Exception("TypeError: Object.defineProperty called on null or undefined");
        }

        if (descriptorArg == null)
        {
            throw new Exception("TypeError: Property description must be an object");
        }

        // Parse descriptor from object - use FromAnyObject to handle both SharpTSObject and compiled $Object
        SharpTSPropertyDescriptor descriptor = SharpTSPropertyDescriptor.FromAnyObject(descriptorArg);

        // ECMA-262 §7.1.19 ToPropertyKey: Symbols pass through to the symbol-dict
        // path; everything else stringifies.
        if (propertyKey is SharpTSSymbol symKey)
        {
            switch (target)
            {
                case SharpTSObject symObj: symObj.SetBySymbol(symKey, descriptor.Value); return target;
                case SharpTSInstance symInst: symInst.SetBySymbol(symKey, descriptor.Value); return target;
            }
        }
        var propKey = PropertyKeyConverter.ToPropertyKeyString(propertyKey);

        bool success;
        switch (target)
        {
            case SharpTSObject obj:
                success = obj.DefineProperty(propKey, descriptor);
                break;
            case SharpTSInstance inst:
                success = inst.DefineProperty(propKey, descriptor);
                break;
            case SharpTSArray arr:
                success = arr.DefineProperty(propKey, descriptor);
                break;
            case Dictionary<string, object?> dict:
                // Handle compiled object literals (e.g., let obj: any = {})
                // Use PropertyDescriptorStore for full descriptor support
                // Parse directly from raw descriptor to preserve TSFunction getters/setters
                var compiledDesc = CompiledPropertyDescriptor.FromAny(descriptorArg);
                success = PropertyDescriptorStore.DefineProperty(target, propKey, compiledDesc);
                break;
            case System.Collections.IDictionary dict:
                // Handle other dictionary types
                // Parse directly from raw descriptor to preserve TSFunction getters/setters
                var compiledDesc2 = CompiledPropertyDescriptor.FromAny(descriptorArg);
                success = PropertyDescriptorStore.DefineProperty(target, propKey, compiledDesc2);
                break;
            case System.Collections.IList list:
                // Handle compiled arrays
                success = TryDefinePropertyOnList(list, propKey, descriptor);
                break;
            default:
                // Try to handle compiled $Object type using reflection
                success = TryDefinePropertyViaReflection(target, propKey, descriptor);
                break;
        }

        if (!success)
        {
            throw new Exception($"TypeError: Cannot define property '{propKey}': object is not extensible or property is not configurable");
        }

        return target;
    }
    /// <summary>
    /// Attempts to define a property on a compiled array (IList).
    /// </summary>
    private static bool TryDefinePropertyOnList(System.Collections.IList list, string propKey, SharpTSPropertyDescriptor descriptor)
    {
        // Only support numeric indices for arrays
        if (int.TryParse(propKey, out int index) && index >= 0)
        {
            // Expand list if needed
            while (list.Count <= index)
            {
                list.Add(null);
            }
            list[index] = descriptor.Value;
            return true;
        }
        return false;
    }
    /// <summary>
    /// Attempts to define a property on a compiled $Object using reflection.
    /// </summary>
    private static bool TryDefinePropertyViaReflection(object target, string propKey, SharpTSPropertyDescriptor descriptor)
    {
        var type = target.GetType();

        // Check if this looks like a compiled $Object (has SetProperty method)
        var methods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        System.Reflection.MethodInfo? setPropertyMethod = null;

        foreach (var m in methods)
        {
            if (m.Name == "SetProperty")
            {
                var parms = m.GetParameters();
                if (parms.Length == 2 && parms[0].ParameterType == typeof(string))
                {
                    setPropertyMethod = m;
                    break;
                }
            }
        }

        if (setPropertyMethod != null)
        {
            // For compiled objects, we just set the value directly
            // Full descriptor support would require modifying the compiled type
            setPropertyMethod.Invoke(target, [propKey, descriptor.Value]);
            return true;
        }

        // Fallback: check if the type has a _fields dictionary (compiled $Object)
        var fieldsField = type.GetField("_fields", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (fieldsField != null)
        {
            var fieldsValue = fieldsField.GetValue(target);
            if (fieldsValue is System.Collections.IDictionary dict)
            {
                dict[propKey] = descriptor.Value;
                return true;
            }
        }

        return false;
    }
    /// <summary>
    /// Runtime helper for Object.getOwnPropertyDescriptor called from compiled code.
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
    /// Runtime helper for Object.defineProperties called from compiled code.
    /// </summary>
    public static object? RuntimeDefineProperties(object? target, object? props)
    {
        if (target == null)
        {
            throw new Exception("TypeError: Object.defineProperties called on null or undefined");
        }

        if (props == null)
        {
            throw new Exception("TypeError: Cannot convert undefined or null to object");
        }

        // Get keys and values from the properties object
        IEnumerable<KeyValuePair<string, object?>> entries = props switch
        {
            SharpTSObject obj => obj.Fields,
            SharpTSInstance inst => inst.GetFieldNames().Select(k => new KeyValuePair<string, object?>(k, inst.GetRawField(k))),
            Dictionary<string, object?> dict => dict,
            _ => throw new Exception("TypeError: Property descriptions must be an object")
        };

        foreach (var entry in entries)
        {
            RuntimeDefineProperty(target, entry.Key, entry.Value);
        }

        return target;
    }
    /// <summary>
    /// Runtime helper for Object.getOwnPropertyDescriptors called from compiled code.
    /// </summary>
    public static object? RuntimeGetOwnPropertyDescriptors(object? target)
    {
        if (target == null)
        {
            throw new Exception("TypeError: Object.getOwnPropertyDescriptors called on null or undefined");
        }

        // Get all own property names
        List<string> names = target switch
        {
            SharpTSObject obj => GetAllOwnPropertyNames(obj),
            SharpTSInstance inst => inst.GetFieldNames().ToList(),
            SharpTSArray arr => GetOwnPropertyNamesFromArray(arr).Select(n => n!.ToString()!).ToList(),
            Dictionary<string, object?> dict => dict.Keys.ToList(),
            System.Collections.IList list => GetPropertyNamesFromList(list),
            _ => []
        };

        var result = new Dictionary<string, object?>();

        foreach (var name in names)
        {
            var descriptor = RuntimeGetOwnPropertyDescriptor(target, name);
            if (descriptor != null)
            {
                result[name] = descriptor;
            }
        }

        return result;
    }
    /// <summary>
    /// Gets property names from an IList (compiled arrays).
    /// </summary>
    private static List<string> GetPropertyNamesFromList(System.Collections.IList list)
    {
        var names = new List<string>();
        for (int i = 0; i < list.Count; i++)
        {
            names.Add(i.ToString());
        }
        names.Add("length");
        return names;
    }
    /// <summary>
    /// Gets a property descriptor from a Dictionary<string, object?>.
    /// </summary>
    private static SharpTSPropertyDescriptor? GetDescriptorFromDictionary(Dictionary<string, object?> dict, string propKey)
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
        if (!dict.TryGetValue(propKey, out var value))
        {
            return null;
        }
        return new SharpTSPropertyDescriptor
        {
            Value = value,
            Writable = true,
            Enumerable = true,
            Configurable = true
        };
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
    /// <summary>
    /// Runtime helper for Object.create called from compiled code.
    /// </summary>
    public static object? RuntimeCreate(object? proto, object? propertiesObject)
    {
        // Create a new object - for compiled mode, use Dictionary<string, object?>
        var result = new Dictionary<string, object?>();

        // Store the prototype reference
        PropertyDescriptorStore.SetPrototype(result, proto);

        // If proto is not null, copy its properties (simulating prototype inheritance)
        if (proto != null)
        {
            RuntimeCopyPropertiesFrom(proto, result);
        }

        // If propertiesObject is provided, define properties using defineProperty semantics
        if (propertiesObject != null)
        {
            RuntimeDefinePropertiesFromDescriptors(propertiesObject, result);
        }

        return result;
    }
    /// <summary>
    /// Copies properties from a source object to a target dictionary (compiled mode).
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
    /// Defines properties on target using property descriptors (compiled mode).
    /// </summary>
    private static void RuntimeDefinePropertiesFromDescriptors(object propertiesObject, Dictionary<string, object?> target)
    {
        IEnumerable<KeyValuePair<string, object?>>? entries = null;

        if (propertiesObject is Dictionary<string, object?> dict)
        {
            entries = dict;
        }
        else if (propertiesObject is SharpTSObject obj)
        {
            entries = obj.Fields;
        }
        else if (propertiesObject is System.Collections.IDictionary idict)
        {
            var list = new List<KeyValuePair<string, object?>>();
            foreach (System.Collections.DictionaryEntry entry in idict)
            {
                list.Add(new KeyValuePair<string, object?>(entry.Key?.ToString() ?? "", entry.Value));
            }
            entries = list;
        }

        if (entries == null) return;

        foreach (var kv in entries)
        {
            if (kv.Value == null) continue;

            // Parse the descriptor and extract value or getter/setter
            var compiledDesc = CompiledPropertyDescriptor.FromAny(kv.Value);
            PropertyDescriptorStore.DefineProperty(target, kv.Key, compiledDesc);
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
    /// Runtime helper for Object.preventExtensions called from compiled code.
    /// </summary>
    public static object? RuntimePreventExtensions(object? obj)
    {
        switch (obj)
        {
            case SharpTSObject tsObj:
                tsObj.PreventExtensions();
                return tsObj;
            case SharpTSInstance inst:
                inst.PreventExtensions();
                return inst;
            case SharpTSArray arr:
                arr.PreventExtensions();
                return arr;
            case Dictionary<string, object?> dict:
                PropertyDescriptorStore.PreventExtensions(dict);
                return dict;
            case List<object?> list:
                // Compiled arrays are List<object?>
                PropertyDescriptorStore.PreventExtensions(list);
                return list;
            case System.Collections.IDictionary idict:
                PropertyDescriptorStore.PreventExtensions(idict);
                return idict;
            case System.Collections.IList ilist:
                // Compiled arrays might also be IList
                PropertyDescriptorStore.PreventExtensions(ilist);
                return ilist;
            default:
                // For compiled class instances, use PropertyDescriptorStore
                if (obj != null && IsCompiledClassInstance(obj))
                {
                    PropertyDescriptorStore.PreventExtensions(obj);
                }
                return obj;
        }
    }
    /// <summary>
    /// Runtime helper for Object.isExtensible called from compiled code.
    /// </summary>
    public static bool RuntimeIsExtensible(object? obj)
    {
        return obj switch
        {
            SharpTSObject tsObj => tsObj.IsExtensible,
            SharpTSInstance inst => inst.IsExtensible,
            SharpTSArray arr => arr.IsExtensible,
            Dictionary<string, object?> dict => PropertyDescriptorStore.IsExtensible(dict),
            List<object?> list => PropertyDescriptorStore.IsExtensible(list),
            System.Collections.IDictionary idict => PropertyDescriptorStore.IsExtensible(idict),
            System.Collections.IList ilist => PropertyDescriptorStore.IsExtensible(ilist),
            null => false,
            _ when IsPrimitive(obj) => false,
            _ when IsCompiledClassInstance(obj) => PropertyDescriptorStore.IsExtensible(obj),
            _ => false
        };
    }
    /// <summary>
    /// Runtime helper for Object.getOwnPropertySymbols called from compiled code.
    /// Returns a List<object?> for compiled code compatibility (not SharpTSArray).
    /// </summary>
    public static object? RuntimeGetOwnPropertySymbols(object? obj)
    {
        if (obj == null)
            throw new Exception("TypeError: Cannot convert null to object");

        List<object?> symbols = obj switch
        {
            SharpTSObject tsObj => tsObj.GetSymbolPropertyNames().Select(s => (object?)s).ToList(),
            SharpTSInstance inst => inst.GetSymbolPropertyNames().Select(s => (object?)s).ToList(),
            // For compiled objects (Dictionary or other), check RuntimeTypes symbol storage first,
            // then fall back to PropertyDescriptorStore for interpreted objects
            Dictionary<string, object?> dict => GetCompiledSymbolKeys(dict),
            _ => GetCompiledSymbolKeys(obj)
        };
        // Return List<object?> for compiled code (not SharpTSArray which is for interpreted mode)
        return symbols;
    }
    /// <summary>
    /// Gets symbol keys from an object, checking RuntimeTypes first (for compiled code)
    /// then PropertyDescriptorStore (for interpreted objects).
    /// </summary>
    private static List<object?> GetCompiledSymbolKeys(object obj)
    {
        // Try RuntimeTypes._symbolStorage first (used by compiled code)
        var compiledSymbols = SharpTS.Compilation.RuntimeTypes.GetSymbolKeys(obj).ToList();
        if (compiledSymbols.Count > 0)
        {
            return compiledSymbols.Select(s => (object?)s).ToList();
        }

        // Fall back to PropertyDescriptorStore (used by interpreted code)
        return PropertyDescriptorStore.GetSymbolKeys(obj).Select(s => (object?)s).ToList();
    }
    /// <summary>
    /// Runtime helper for Object.getPrototypeOf called from compiled code.
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
    /// Runtime helper for Object.setPrototypeOf called from compiled code.
    /// </summary>
    public static object? RuntimeSetPrototypeOf(object? target, object? proto)
    {
        if (target == null)
            throw new Exception("TypeError: Cannot convert null to object");

        switch (target)
        {
            case SharpTSObject obj:
                if (!obj.IsExtensible)
                    throw new Exception("TypeError: Object is not extensible");
                obj.Prototype = proto;
                if (proto != null)
                    CopyPropertiesFrom(proto, obj);
                return obj;

            case SharpTSInstance:
                throw new Exception("TypeError: Cannot set prototype of class instance");

            case Dictionary<string, object?> dict:
                if (!PropertyDescriptorStore.IsExtensible(dict))
                    throw new Exception("TypeError: Object is not extensible");
                PropertyDescriptorStore.SetPrototype(dict, proto);
                if (proto != null)
                    RuntimeCopyPropertiesFrom(proto, dict);
                return dict;

            default:
                // Check for compiled class instances
                if (target != null && IsCompiledClassInstance(target))
                {
                    throw new Exception("TypeError: Cannot set prototype of class instance");
                }
                return target;
        }
    }
}
