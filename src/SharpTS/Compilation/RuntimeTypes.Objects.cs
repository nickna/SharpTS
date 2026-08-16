using System.Runtime.CompilerServices;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    /// <summary>Helper to check if value is a SharpTSNamespace and get a property.</summary>
    private static bool TryGetNamespaceProperty(object obj, string name, out object? result)
    {
        result = null;
        if (obj is not SharpTSNamespace ns)
            return false;
        result = ns.Get(name);
        return true;
    }

    /// <summary>Helper to check if value is a SharpTSArray and get element count.</summary>
    private static bool TryGetSharpTSArrayLength(object obj, out double length)
    {
        length = 0;
        if (obj is not SharpTSArray arr)
            return false;
        length = arr.Length;
        return true;
    }

    /// <summary>Helper to check if value is a SharpTSArray and get element at index.</summary>
    private static bool TryGetSharpTSArrayElement(object obj, int index, out object? result)
    {
        result = null;
        if (obj is not SharpTSArray arr)
            return false;
        if (index >= 0 && index < arr.Length)
        {
            result = arr[index];
            return true;
        }
        return false;
    }

    #region Objects

    // Helper to safely cast object to Dictionary<string, object?>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetDictionary(object? obj, out Dictionary<string, object?>? dict)
    {
        if (obj is Dictionary<string, object?> d)
        {
            dict = d;
            return true;
        }
        dict = null;
        return false;
    }

    // Helper to get _fields from a class instance using ReflectionCache
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Dictionary<string, object?>? TryGetFields(object obj)
    {
        // Try to get cached field info
        var field = ReflectionCache.GetField(obj.GetType(), "_fields");
        if (field != null)
        {
            var val = field.GetValue(obj);
            // Safe unsafe cast because we only read/write compatible types (object)
            // _fields is emitted as Dictionary<string, object>, we treat it as Dictionary<string, object?>
            // effectively the same at runtime for reference types
            return Unsafe.As<Dictionary<string, object?>>(val);
        }
        return null;
    }

    // Helper to check if object is a Map (Dictionary<object, object>)
    // Maps use object keys, while regular JS objects use string keys (Dictionary<string, object?>)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsMapDictionary(object obj)
    {
        var type = obj.GetType();
        if (!type.IsGenericType) return false;
        if (type.GetGenericTypeDefinition() != typeof(Dictionary<,>)) return false;
        var args = type.GetGenericArguments();
        // Map: Dictionary<object, object> (key is object, not string)
        return args[0] == typeof(object) && args[1] == typeof(object);
    }

    public static void MergeIntoObject(Dictionary<string, object?> target, object? source)
    {
        if (TryGetDictionary(source, out var dict))
        {
            foreach (var kv in dict!)
            {
                target[kv.Key] = kv.Value;
            }
        }
        else if (source != null)
        {
            // For class instances, get their fields
            if (TryGetFields(source) is { } fields)
            {
                foreach (var kv in fields)
                {
                    target[kv.Key] = kv.Value;
                }
            }
        }
    }

    public static Dictionary<string, object?> CreateObject(Dictionary<string, object> fields)
    {
        Dictionary<string, object?> result = [];
        foreach (var kv in fields)
        {
            result[kv.Key] = kv.Value;
        }
        return result;
    }

    public static object? GetProperty(object? obj, string name)
    {
        if (obj == null) return null;

        // Proxy interception
        if (obj is SharpTSProxy proxy)
            return proxy.TrapGet(name, null);

        // Namespace (TypeScript namespace object) - checked via reflection
        if (TryGetNamespaceProperty(obj, name, out var nsResult))
        {
            return nsResult;
        }

        // Map (Dictionary<object, object>) - check before Dictionary<string, object?>
        // This handles cases where Map is accessed through 'any' typed variable
        // Note: Use runtime type check since pattern matching with nullable types can be tricky
        if (IsMapDictionary(obj))
        {
            var mapDict = (System.Collections.IDictionary)obj;
            return name switch
            {
                "size" => (double)mapDict.Count,
                _ => null // Map methods (get, set, has, etc.) are handled via method calls
            };
        }

        // Dictionary (object literal)
        if (TryGetDictionary(obj, out var dict))
        {
            // Check for accessor property (getter) defined via Object.defineProperty
            if (PropertyDescriptorStore.TryGetGetter(obj, name, out var getter))
            {
                return InvokeAccessor(getter, obj);
            }

            if (dict!.TryGetValue(name, out var value))
            {
                // If it's a TSFunction with a display class, bind 'this' to the dictionary
                // This handles object method shorthand: { fn() { return this.x; } }
                if (value is TSFunction func)
                {
                    func.BindThis(dict);
                }
                return value;
            }
            return null;
        }

        // List (array)
        if (obj is List<object?> list)
        {
            return name == "length" ? (double)list.Count : null;
        }

        // SharpTSArray (interpreter array type used by Map/Set entries) - checked via reflection
        if (TryGetSharpTSArrayLength(obj, out var arrLength))
        {
            return name == "length" ? arrLength : null;
        }

        // KeyValuePair<object, object> (compiled Map entries) - treat as [key, value] tuple
        if (obj is KeyValuePair<object, object?> kvp)
        {
            return name == "length" ? 2.0 : null;
        }

        // KeyValuePair<object, object> non-nullable variant
        if (obj is KeyValuePair<object, object> kvpNonNull)
        {
            return name == "length" ? 2.0 : null;
        }

        // String
        if (obj is string s)
        {
            return name == "length" ? (double)s.Length : null;
        }

        // Class reference used as a value (e.g. `const Alias = Foo; Alias.bar()` or
        // `require('./mod').Foo.bar()`). Compiled classes are emitted as System.Type
        // tokens, so property access must look up STATIC members on that Type —
        // otherwise obj.GetType() below returns System.RuntimeType and reflection
        // searches for instance members of Type itself, missing every user-declared
        // static on the target class.
        if (obj is Type classType)
        {
            const System.Reflection.BindingFlags staticPublic =
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;

            // Static getter first (auto-accessors and explicit static getters compile to get_Xxx).
            var pascalName = name.Length > 0
                ? char.ToUpperInvariant(name[0]) + name[1..]
                : name;
            var staticGetter = ManagedOutputRuntimeReflection.GetMethodByName(
                classType, "get_" + pascalName, staticPublic);
            if (staticGetter != null)
            {
                return ReflectionCache.GetInvoker(staticGetter).Invoke(null, default(Span<object?>));
            }

            // Static field with the user-declared name.
            var staticField = ManagedOutputRuntimeReflection.GetFieldByName(
                classType, name, staticPublic);
            if (staticField != null)
            {
                return staticField.GetValue(null);
            }

            // Static method — bind with null receiver so InvokeValue forwards args as-is.
            var staticMethod = ManagedOutputRuntimeReflection.GetMethodByName(
                classType, name, staticPublic);
            if (staticMethod != null)
            {
                return CreateBoundMethod(null!, staticMethod);
            }

            // Node/JS spec: Function.prototype.name holds the class name.
            if (name == "name")
            {
                return classType.Name;
            }

            return null;
        }

        // Class instance - use reflection
        var type = obj.GetType();

        // Check for getter method first (get_<PascalCaseName>)
        var getterMethod = ReflectionCache.GetGetter(type, name);
        if (getterMethod != null)
        {
            var invoker = ReflectionCache.GetInvoker(getterMethod);
            return invoker.Invoke(obj, default(Span<object?>));
        }

        // Check _fields dictionary
        if (TryGetFields(obj) is { } fields && fields.TryGetValue(name, out var val))
        {
            return val;
        }

        // Try method
        var method = ReflectionCache.GetMethod(type, name);
        if (method != null)
        {
            return CreateBoundMethod(obj, method);
        }

        return null;
    }

    public static void SetProperty(object? obj, string name, object? value)
    {
        if (obj == null) return;

        // Proxy interception
        if (obj is SharpTSProxy proxy)
        {
            proxy.TrapSet(name, value, null);
            return;
        }

        // Dictionary
        if (TryGetDictionary(obj, out var dict))
        {
            // Check for accessor property (setter) defined via Object.defineProperty
            if (PropertyDescriptorStore.TryGetSetter(obj, name, out var setter))
            {
                InvokeAccessor(setter, obj, value);
                return;
            }

            // Check if property is writable
            if (!PropertyDescriptorStore.IsWritable(obj, name))
            {
                // In non-strict mode, silently fail
                return;
            }

            dict![name] = value;
            return;
        }

        // Class instance
        var type = obj.GetType();

        // Check for setter method first (set_<PascalCaseName>)
        var setterMethod = ReflectionCache.GetSetter(type, name);
        if (setterMethod != null)
        {
            var invoker = ReflectionCache.GetInvoker(setterMethod);
            invoker.Invoke(obj, new Span<object?>([value]));
            return;
        }

        // Check _fields dictionary
        if (TryGetFields(obj) is { } fields)
        {
            fields[name] = value!;
        }
    }

    public static object? GetIndex(object? obj, object? index)
    {
        if (obj == null) return null;

        // Proxy interception
        if (obj is SharpTSProxy proxy)
        {
            string proxyKey = index?.ToString() ?? "";
            return proxy.TrapGet(proxyKey, null);
        }

        // Object/Dictionary with string key
        if (TryGetDictionary(obj, out var dict) && index is string key)
        {
            // Check for accessor property (getter) defined via Object.defineProperty
            if (PropertyDescriptorStore.TryGetGetter(obj, key, out var getter))
            {
                return InvokeAccessor(getter, obj);
            }

            if (dict!.TryGetValue(key, out var value))
            {
                // Bind 'this' for object method shorthand functions
                if (value is TSFunction func)
                {
                    func.BindThis(dict);
                }
                return value;
            }
            return null;
        }

        // Number key on dictionary (convert to string)
        if (TryGetDictionary(obj, out var numDict) && index is double numKey)
        {
            return numDict!.TryGetValue(numKey.ToString(), out var numValue) ? numValue : null;
        }

        // Symbol key - use separate storage
        if (index != null && IsSymbol(index))
        {
            var symbolDict = GetSymbolDict(obj);
            return symbolDict.TryGetValue(index, out var symValue) ? symValue : null;
        }

        // Numeric index for arrays/strings
        if (index is double or int or long)
        {
            int idx = (int)ToNumber(index);

            if (obj is List<object?> list && idx >= 0 && idx < list.Count)
            {
                return list[idx];
            }

            // SharpTSArray (interpreter array type used by Map/Set entries) - checked via reflection
            if (TryGetSharpTSArrayElement(obj, idx, out var arrElement))
            {
                return arrElement;
            }

            // KeyValuePair<object, object> (compiled Map entries) - treat as [key, value] tuple
            if (obj is KeyValuePair<object, object?> kvp)
            {
                return idx switch
                {
                    0 => kvp.Key,
                    1 => kvp.Value,
                    _ => null
                };
            }

            // KeyValuePair<object, object> non-nullable variant
            if (obj is KeyValuePair<object, object> kvpNonNull)
            {
                return idx switch
                {
                    0 => kvpNonNull.Key,
                    1 => kvpNonNull.Value,
                    _ => null
                };
            }

            // Native .NET arrays (e.g., string[] from command line args)
            if (obj is Array arr && idx >= 0 && idx < arr.Length)
            {
                return arr.GetValue(idx);
            }

            if (obj is string s && idx >= 0 && idx < s.Length)
            {
                return s[idx].ToString();
            }
        }

        return null;
    }

    private static bool IsSymbol(object obj)
    {
        // Optimized check
        var name = obj.GetType().Name;
        return name is "TSSymbol" or "$TSSymbol";
    }

    public static void SetIndex(object? obj, object? index, object? value)
    {
        if (obj == null) return;

        // Proxy interception
        if (obj is SharpTSProxy proxy)
        {
            string proxyKey = index?.ToString() ?? "";
            proxy.TrapSet(proxyKey, value, null);
            return;
        }

        // Object/Dictionary with string key
        if (TryGetDictionary(obj, out var dict) && index is string key)
        {
            // Check for accessor property (setter) defined via Object.defineProperty
            if (PropertyDescriptorStore.TryGetSetter(obj, key, out var setter))
            {
                InvokeAccessor(setter, obj, value);
                return;
            }

            // Check if property is writable
            if (!PropertyDescriptorStore.IsWritable(obj, key))
            {
                // In non-strict mode, silently fail
                return;
            }

            dict![key] = value;
            return;
        }

        // Number key on dictionary (convert to string)
        if (TryGetDictionary(obj, out var numDict) && index is double numKey)
        {
            numDict![numKey.ToString()] = value;
            return;
        }

        // Symbol key - use separate storage
        if (index != null && IsSymbol(index))
        {
            var symbolDict = GetSymbolDict(obj);
            symbolDict[index] = value;
            return;
        }

        // Numeric index for arrays
        if (index is double or int or long)
        {
            int idx = (int)ToNumber(index);

            if (obj is List<object?> list && idx >= 0 && idx < list.Count)
            {
                list[idx] = value;
            }
        }
    }

    /// <summary>
    /// Invokes a getter accessor function with the given 'this' binding.
    /// </summary>
    private static object? InvokeAccessor(object? accessor, object? thisArg)
    {
        if (accessor == null) return null;

        // TSFunction (compiled arrow/function)
        if (accessor is TSFunction tsFunc)
        {
            tsFunc.BindThis(thisArg);
            return tsFunc.Invoke();
        }

        // Bound method delegate (from CreateBoundMethod in RuntimeTypes.Methods)
        if (accessor is Func<object?[], object?> boundMethod)
        {
            return boundMethod(Array.Empty<object?>());
        }

        // Generic delegate fallback
        if (accessor is Delegate del)
        {
            return del.DynamicInvoke(new object[] { Array.Empty<object?>() });
        }

        return null;
    }

    /// <summary>
    /// Invokes a setter accessor function with the given 'this' binding and value.
    /// </summary>
    private static void InvokeAccessor(object? accessor, object? thisArg, object? value)
    {
        if (accessor == null) return;

        // TSFunction (compiled arrow/function)
        if (accessor is TSFunction tsFunc)
        {
            tsFunc.BindThis(thisArg);
            tsFunc.Invoke(value);
            return;
        }

        // Bound method delegate (from CreateBoundMethod in RuntimeTypes.Methods)
        if (accessor is Func<object?[], object?> boundMethod)
        {
            boundMethod(new object?[] { value });
            return;
        }

        // Generic delegate fallback
        if (accessor is Delegate del)
        {
            del.DynamicInvoke(new object[] { new object?[] { value } });
            return;
        }
    }

    #endregion

    #region Object Methods

    public static List<object?> GetValues(object? obj)
    {
        if (TryGetDictionary(obj, out var dict))
        {
            return dict!.Values.ToList();
        }
        // For compiled class instances, get values from typed backing fields AND _fields dictionary
        if (obj != null)
        {
            List<object?> values = [];
            var type = obj.GetType();
            HashSet<string> seenKeys = [];

            // Get values from typed backing fields (fields starting with __)
            foreach (var backingField in ReflectionCache.GetBackingFields(type))
            {
                string propName = backingField.Name[2..];
                seenKeys.Add(propName);
                values.Add(backingField.GetValue(obj));
            }

            // Also get values from _fields dictionary (for dynamic properties and generic type fields)
            if (TryGetFields(obj) is { } fields)
            {
                foreach (var kv in fields)
                {
                    if (!seenKeys.Contains(kv.Key))
                    {
                        values.Add(kv.Value);
                    }
                }
            }

            return values;
        }
        return [];
    }

    public static List<object?> GetEntries(object? obj)
    {
        if (TryGetDictionary(obj, out var dict))
        {
            return dict!.Select(kv => (object?)new List<object?> { kv.Key, kv.Value }).ToList();
        }
        // For compiled class instances, get entries from typed backing fields AND _fields dictionary
        if (obj != null)
        {
            List<object?> entries = [];
            var type = obj.GetType();
            HashSet<string> seenKeys = [];

            // Get entries from typed backing fields (fields starting with __)
            foreach (var backingField in ReflectionCache.GetBackingFields(type))
            {
                string propName = backingField.Name[2..];
                seenKeys.Add(propName);
                entries.Add(new List<object?> { propName, backingField.GetValue(obj) });
            }

            // Also get entries from _fields dictionary (for dynamic properties and generic type fields)
            if (TryGetFields(obj) is { } fields)
            {
                foreach (var kv in fields)
                {
                    if (!seenKeys.Contains(kv.Key))
                    {
                        entries.Add(new List<object?> { kv.Key, kv.Value });
                    }
                }
            }

            return entries;
        }
        return [];
    }

    #endregion
}
