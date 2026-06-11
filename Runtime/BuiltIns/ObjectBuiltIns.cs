using SharpTS.Compilation;
using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

public static class ObjectBuiltIns
{
    private static readonly BuiltInStaticMemberLookup _staticLookup =
        BuiltInStaticBuilder.Create()
            .MethodV2("keys", 1, KeysV2)
            .MethodV2("values", 1, ValuesV2)
            .MethodV2("entries", 1, EntriesV2)
            .MethodV2("fromEntries", 1, FromEntriesV2)
            .MethodV2("hasOwn", 2, HasOwnV2)
            .MethodV2("is", 2, IsV2)
            .MethodV2("assign", 1, int.MaxValue, AssignV2)
            .MethodV2("freeze", 1, FreezeV2)
            .MethodV2("seal", 1, SealV2)
            .MethodV2("isFrozen", 1, IsFrozenV2)
            .MethodV2("isSealed", 1, IsSealedV2)
            .MethodV2("defineProperty", 3, DefinePropertyV2)
            .MethodV2("getOwnPropertyDescriptor", 2, GetOwnPropertyDescriptorV2)
            .MethodV2("getOwnPropertyNames", 1, GetOwnPropertyNamesV2)
            .MethodV2("create", 1, 2, CreateV2)
            .MethodV2("preventExtensions", 1, PreventExtensionsV2)
            .MethodV2("isExtensible", 1, IsExtensibleMethodV2)
            .MethodV2("getOwnPropertySymbols", 1, GetOwnPropertySymbolsV2)
            .MethodV2("getPrototypeOf", 1, GetPrototypeOfV2)
            .MethodV2("setPrototypeOf", 2, SetPrototypeOfV2)
            .MethodV2("groupBy", 2, GroupByV2)
            .MethodV2("defineProperties", 2, DefinePropertiesV2)
            .MethodV2("getOwnPropertyDescriptors", 1, GetOwnPropertyDescriptorsV2)
            .Build();

    /// <summary>
    /// Get static methods on the Object namespace (e.g., Object.keys())
    /// </summary>
    public static object? GetStaticMethod(string name)
        => _staticLookup.GetMember(name);

    private static RuntimeValue KeysV2(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var arg = args[0].ToObject();
        if (arg is SharpTSObject obj)
        {
            var keys = obj.Fields.Keys.Select(k => (object?)k).ToList();
            return RuntimeValue.FromObject(new SharpTSArray(keys));
        }
        if (arg is SharpTSArray arr)
        {
            // ECMA-262: only present (non-hole) indices are own enumerable
            // properties, so Object.keys skips holes.
            var keys = new List<object?>();
            for (int i = 0; i < arr.Length; i++)
                if (arr.HasIndex(i)) keys.Add(i.ToString());
            return RuntimeValue.FromObject(new SharpTSArray(keys));
        }
        if (arg is SharpTSInstance inst)
        {
            var keys = inst.GetFieldNames().Select(k => (object?)k).ToList();
            return RuntimeValue.FromObject(new SharpTSArray(keys));
        }
        // Plain Dictionary<string, object?> — used by runtime helpers (e.g.,
        // Web Streams iterator results) that produce JS-object-shaped data
        // without going through SharpTSObject. Compiled mode already has the
        // matching dict branch in $Runtime.GetKeys; this branch keeps the
        // interpreter at parity.
        if (arg is IDictionary<string, object?> dict)
        {
            var keys = dict.Keys.Select(k => (object?)k).ToList();
            return RuntimeValue.FromObject(new SharpTSArray(keys));
        }
        // Math singleton: built-in members are non-enumerable, so the own
        // enumerable keys are exactly the user-assigned extras (empty unless the
        // program assigned to Math). Matches compiled mode (#288).
        if (arg is SharpTSMath math)
        {
            var keys = math.OwnEnumerableProperties.Select(kv => (object?)kv.Key).ToList();
            return RuntimeValue.FromObject(new SharpTSArray(keys));
        }
        throw new Exception("Object.keys() requires an object argument");
    }

    private static RuntimeValue ValuesV2(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var arg = args[0].ToObject();
        if (arg is SharpTSObject obj)
        {
            var values = obj.Fields.Values.ToList();
            return RuntimeValue.FromObject(new SharpTSArray(values));
        }
        if (arg is SharpTSArray arr)
        {
            var values = new List<object?>();
            for (int i = 0; i < arr.Length; i++)
                if (arr.HasIndex(i)) values.Add(arr[i]);
            return RuntimeValue.FromObject(new SharpTSArray(values));
        }
        if (arg is SharpTSInstance inst)
        {
            var values = inst.GetFieldNames().Select(n => inst.GetRawField(n)).ToList();
            return RuntimeValue.FromObject(new SharpTSArray(values));
        }
        if (arg is IDictionary<string, object?> dict)
        {
            return RuntimeValue.FromObject(new SharpTSArray(dict.Values.ToList()));
        }
        // Math singleton — see Object.keys (#288).
        if (arg is SharpTSMath math)
        {
            var values = math.OwnEnumerableProperties.Select(kv => kv.Value).ToList();
            return RuntimeValue.FromObject(new SharpTSArray(values));
        }
        throw new Exception("Object.values() requires an object argument");
    }

    private static RuntimeValue EntriesV2(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var arg = args[0].ToObject();
        if (arg is SharpTSObject obj)
        {
            var entries = obj.Fields.Select(kv =>
                (object?)new SharpTSArray([(object?)kv.Key, kv.Value])).ToList();
            return RuntimeValue.FromObject(new SharpTSArray(entries));
        }
        if (arg is SharpTSArray arr)
        {
            var entries = new List<object?>();
            for (int i = 0; i < arr.Length; i++)
            {
                if (!arr.HasIndex(i)) continue;
                entries.Add(new SharpTSArray([(object?)i.ToString(), arr[i]]));
            }
            return RuntimeValue.FromObject(new SharpTSArray(entries));
        }
        if (arg is SharpTSInstance inst)
        {
            var entries = inst.GetFieldNames().Select(n =>
                (object?)new SharpTSArray([(object?)n, inst.GetRawField(n)])).ToList();
            return RuntimeValue.FromObject(new SharpTSArray(entries));
        }
        if (arg is IDictionary<string, object?> dict)
        {
            var entries = dict.Select(kv =>
                (object?)new SharpTSArray([(object?)kv.Key, kv.Value])).ToList();
            return RuntimeValue.FromObject(new SharpTSArray(entries));
        }
        // Math singleton — see Object.keys (#288).
        if (arg is SharpTSMath math)
        {
            var entries = math.OwnEnumerableProperties.Select(kv =>
                (object?)new SharpTSArray([(object?)kv.Key, kv.Value])).ToList();
            return RuntimeValue.FromObject(new SharpTSArray(entries));
        }
        throw new Exception("Object.entries() requires an object argument");
    }

    private static object? FromEntries(Interpreter interpreter, List<object?> args)
    {
        if (args[0] == null)
            throw new Exception("Runtime Error: Object.fromEntries() requires an iterable argument");

        var elements = interpreter.GetIterableElements(args[0]);
        Dictionary<string, object?> result = [];

        foreach (var element in elements)
        {
            if (element is SharpTSArray pair && pair.Length >= 2)
            {
                string key = pair.Get(0)?.ToString() ?? "";
                result[key] = pair.Get(1);
            }
            else if (element is List<object?> listPair && listPair.Count >= 2)
            {
                string key = listPair[0]?.ToString() ?? "";
                result[key] = listPair[1];
            }
            else
            {
                throw new Exception("Runtime Error: Object.fromEntries() requires [key, value] pairs");
            }
        }
        return new SharpTSObject(result);
    }

    private static RuntimeValue HasOwnV2(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var obj = args[0].ToObject();
        var key = args[1].AsString() ?? args[1].ToObject()?.ToString() ?? "";
        return RuntimeValue.FromBoolean(obj switch
        {
            SharpTSObject tsObj => tsObj.Fields.ContainsKey(key),
            SharpTSInstance inst => inst.HasField(key),
            IDictionary<string, object?> dict => dict.ContainsKey(key),
            _ => false
        });
    }

    private static RuntimeValue IsV2(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var value1 = args[0];
        var value2 = args[1];

        // Handle null/undefined cases
        if (value1.Kind is ValueKind.Null or ValueKind.Undefined
            && value2.Kind is ValueKind.Null or ValueKind.Undefined)
        {
            return RuntimeValue.FromBoolean(value1.Kind == value2.Kind);
        }
        if (value1.Kind is ValueKind.Null or ValueKind.Undefined
            || value2.Kind is ValueKind.Null or ValueKind.Undefined)
        {
            return RuntimeValue.False;
        }

        // Handle number cases (NaN and -0/+0)
        if (value1.Kind == ValueKind.Number && value2.Kind == ValueKind.Number)
        {
            var d1 = value1.AsNumber();
            var d2 = value2.AsNumber();
            if (double.IsNaN(d1) && double.IsNaN(d2))
                return RuntimeValue.True;
            if (d1 == 0.0 && d2 == 0.0)
                return RuntimeValue.FromBoolean(1.0 / d1 == 1.0 / d2);
            return RuntimeValue.FromBoolean(d1 == d2);
        }

        // Handle boolean cases
        if (value1.Kind == ValueKind.Boolean && value2.Kind == ValueKind.Boolean)
            return RuntimeValue.FromBoolean(value1.AsBoolean() == value2.AsBoolean());

        // Handle string cases
        if (value1.Kind == ValueKind.String && value2.Kind == ValueKind.String)
            return RuntimeValue.FromBoolean(value1.AsString() == value2.AsString());

        // Fall back to boxed comparison for objects, bigints, symbols
        var obj1 = value1.ToObject();
        var obj2 = value2.ToObject();

        if (obj1 is System.Numerics.BigInteger bi1 && obj2 is System.Numerics.BigInteger bi2)
            return RuntimeValue.FromBoolean(bi1 == bi2);

        return RuntimeValue.FromBoolean(ReferenceEquals(obj1, obj2));
    }

    private static object? Assign(Interpreter _, List<object?> args)
    {
        // Object.assign(target, ...sources)
        if (args.Count == 0 || args[0] == null)
            throw new Exception("Runtime Error: Object.assign() requires a target object");

        // Handle SharpTSObject target
        if (args[0] is SharpTSObject targetObj)
        {
            for (int i = 1; i < args.Count; i++)
            {
                if (args[i] == null) continue;

                if (args[i] is SharpTSObject srcObj)
                {
                    foreach (var kv in srcObj.Fields)
                        targetObj.SetProperty(kv.Key, kv.Value);
                }
                else if (args[i] is SharpTSInstance srcInst)
                {
                    foreach (var key in srcInst.GetFieldNames())
                        targetObj.SetProperty(key, srcInst.GetRawField(key));
                }
            }
            return args[0];
        }

        // Handle SharpTSInstance target
        if (args[0] is SharpTSInstance targetInst)
        {
            for (int i = 1; i < args.Count; i++)
            {
                if (args[i] == null) continue;

                if (args[i] is SharpTSObject srcObj)
                {
                    foreach (var kv in srcObj.Fields)
                        targetInst.SetRawField(kv.Key, kv.Value);
                }
                else if (args[i] is SharpTSInstance srcInst)
                {
                    foreach (var key in srcInst.GetFieldNames())
                        targetInst.SetRawField(key, srcInst.GetRawField(key));
                }
            }
            return args[0];
        }

        // JS functions are objects — Object.assign(fn, {...}) should copy props onto the function.
        if (args[0] is SharpTSFunction targetFn)
        {
            CopySourcesOntoFunction(args, (name, value) => targetFn.SetProperty(name, value));
            return args[0];
        }
        if (args[0] is SharpTSArrowFunction targetArrowFn)
        {
            CopySourcesOntoFunction(args, (name, value) => targetArrowFn.SetProperty(name, value));
            return args[0];
        }
        if (args[0] is SharpTSAsyncFunction targetAsyncFn)
        {
            CopySourcesOntoFunction(args, (name, value) => targetAsyncFn.SetProperty(name, value));
            return args[0];
        }
        if (args[0] is SharpTSAsyncArrowFunction targetAsyncArrowFn)
        {
            CopySourcesOntoFunction(args, (name, value) => targetAsyncArrowFn.SetProperty(name, value));
            return args[0];
        }
        // RegExp instances accept arbitrary property assignment in JS.
        if (args[0] is SharpTSRegExp targetRegExp)
        {
            CopySourcesOntoFunction(args, (name, value) => targetRegExp.SetProperty(name, value));
            return args[0];
        }

        throw new Exception($"Runtime Error: Object.assign() target must be an object (got {args[0]?.GetType().Name ?? "null"})");
    }

    private static void CopySourcesOntoFunction(List<object?> args, Action<string, object?> set)
    {
        for (int i = 1; i < args.Count; i++)
        {
            if (args[i] == null) continue;
            if (args[i] is SharpTSObject srcObj)
            {
                foreach (var kv in srcObj.Fields)
                    set(kv.Key, kv.Value);
            }
            else if (args[i] is SharpTSInstance srcInst)
            {
                foreach (var key in srcInst.GetFieldNames())
                    set(key, srcInst.GetRawField(key));
            }
        }
    }

    private static RuntimeValue FreezeV2(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var arg = args[0].ToObject();
        switch (arg)
        {
            case SharpTSObject obj:
                obj.Freeze();
                return RuntimeValue.FromObject(obj);
            case SharpTSInstance inst:
                inst.Freeze();
                return RuntimeValue.FromObject(inst);
            case SharpTSArray arr:
                arr.Freeze();
                return RuntimeValue.FromObject(arr);
            case Dictionary<string, object?> dict:
                PropertyDescriptorStore.Freeze(dict);
                return RuntimeValue.FromObject(dict);
            case System.Collections.IDictionary idict:
                PropertyDescriptorStore.Freeze(idict);
                return RuntimeValue.FromObject(idict);
            default:
                return args[0];
        }
    }

    private static RuntimeValue SealV2(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var arg = args[0].ToObject();
        switch (arg)
        {
            case SharpTSObject obj:
                obj.Seal();
                return RuntimeValue.FromObject(obj);
            case SharpTSInstance inst:
                inst.Seal();
                return RuntimeValue.FromObject(inst);
            case SharpTSArray arr:
                arr.Seal();
                return RuntimeValue.FromObject(arr);
            case Dictionary<string, object?> dict:
                PropertyDescriptorStore.Seal(dict);
                return RuntimeValue.FromObject(dict);
            case System.Collections.IDictionary idict:
                PropertyDescriptorStore.Seal(idict);
                return RuntimeValue.FromObject(idict);
            default:
                return args[0];
        }
    }

    private static RuntimeValue IsFrozenV2(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var arg = args[0].ToObject();
        return RuntimeValue.FromBoolean(arg switch
        {
            SharpTSObject obj => obj.IsFrozen,
            SharpTSInstance inst => inst.IsFrozen,
            SharpTSArray arr => arr.IsFrozen,
            Dictionary<string, object?> dict => PropertyDescriptorStore.IsFrozen(dict),
            System.Collections.IDictionary idict => PropertyDescriptorStore.IsFrozen(idict),
            _ => true
        });
    }

    private static RuntimeValue IsSealedV2(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var arg = args[0].ToObject();
        return RuntimeValue.FromBoolean(arg switch
        {
            SharpTSObject obj => obj.IsSealed,
            SharpTSInstance inst => inst.IsSealed,
            SharpTSArray arr => arr.IsSealed,
            Dictionary<string, object?> dict => PropertyDescriptorStore.IsSealed(dict),
            System.Collections.IDictionary idict => PropertyDescriptorStore.IsSealed(idict),
            _ => true
        });
    }

    /// <summary>
    /// Object.defineProperty(obj, prop, descriptor) - defines a new property or modifies an existing one.
    /// </summary>
    private static object? DefineProperty(Interpreter interpreter, List<object?> args)
    {
        var target = args[0];
        var descriptorArg = args[2];

        if (target == null)
        {
            throw new Exception("TypeError: Object.defineProperty called on null or undefined");
        }

        if (descriptorArg == null)
        {
            throw new Exception("TypeError: Property description must be an object");
        }

        // Parse descriptor from object - use FromAnyObject to handle any object type
        SharpTSPropertyDescriptor descriptor = SharpTSPropertyDescriptor.FromAnyObject(descriptorArg);
        // ECMA-262 §6.2.5.5 ToPropertyDescriptor: the boolean attributes are read
        // via Get (walking the prototype chain and invoking getters) and
        // ToBoolean-coerced. FromAnyObject only handles own `is bool` values, so
        // re-derive them with interpreter access — covers truthy non-booleans
        // (e.g. the string "false"), inherited attributes, and accessor-sourced
        // attributes. Correct flags are required for the delete configurability
        // check in SharpTSObject.
        ApplyBooleanAttributes(descriptor, descriptorArg, interpreter);

        // Handle Symbol-keyed property definition — route through Symbol storage.
        // Per ECMA-262 §10.1.6 / §6.2.5.6, a descriptor that omits `value` (only
        // sets writable/enumerable/configurable) preserves the existing value.
        // SharpTSPropertyDescriptor flattens "omitted" and "value: null/undefined"
        // into the same Value=null state, so we recognise an attribute-only
        // descriptor by absence of `value` AND `get`/`set` keys on the source
        // descriptor object — propertyHelper.js's verifyProperty hits exactly
        // this path against RegExp.prototype[Symbol.split].
        if (args[1] is SharpTSSymbol symKey)
        {
            bool descriptorHasValue = DescriptorHasValueOrAccessor(descriptorArg);
            bool isAccessor = descriptor.Get != null || descriptor.Set != null;
            switch (target)
            {
                case SharpTSObject symObj:
                    if (descriptorHasValue)
                        symObj.SetBySymbol(symKey, descriptor.Value);
                    return target;
                case SharpTSInstance symInst:
                    if (descriptorHasValue)
                        symInst.SetBySymbol(symKey, descriptor.Value);
                    return target;
                case SharpTSFunction symFn:
                    if (isAccessor)
                        symFn.DefineSymbolAccessor(symKey, descriptor.Get, descriptor.Set);
                    else if (descriptorHasValue)
                        symFn.SetBySymbol(symKey, descriptor.Value);
                    return target;
                case SharpTSArrowFunction symArrow:
                    if (isAccessor)
                        symArrow.DefineSymbolAccessor(symKey, descriptor.Get, descriptor.Set);
                    else if (descriptorHasValue)
                        symArrow.SetBySymbol(symKey, descriptor.Value);
                    return target;
                default:
                    break;
            }
        }

        // ECMA-262 §7.1.19: coerce non-Symbol property keys via ToPropertyKey
        // (undefined → "undefined", null → "null", -0 → "0", booleans lowercase).
        var propertyKey = PropertyKeyConverter.ToPropertyKeyString(args[1]);

        bool success;
        switch (target)
        {
            case SharpTSObject obj:
                success = obj.DefineProperty(propertyKey, descriptor);
                break;
            case SharpTSInstance inst:
                success = inst.DefineProperty(propertyKey, descriptor);
                break;
            case SharpTSArray arr:
                // Arrays can have properties defined on them
                success = arr.DefineProperty(propertyKey, descriptor);
                break;
            case Dictionary<string, object?> dict:
                // Compiled mode: Dictionary<string, object?> for any-typed object literals
                var compiledDesc = CompiledPropertyDescriptor.FromAny(descriptorArg);
                success = PropertyDescriptorStore.DefineProperty(dict, propertyKey, compiledDesc);
                break;
            case SharpTSFunction fn:
                // JS functions are objects — store accessor (get/set) or value
                // descriptors directly on the function. Attribute-only
                // descriptors (no value/get/set) preserve the existing value
                // per ECMA-262 §10.1.6.3.
                if (descriptor.Get != null || descriptor.Set != null)
                {
                    fn.DefineAccessor(propertyKey, descriptor.Get, descriptor.Set);
                }
                else if (DescriptorHasValueOrAccessor(descriptorArg))
                {
                    fn.SetProperty(propertyKey, descriptor.Value);
                }
                success = true;
                break;
            case SharpTSRegExp rx:
                // RegExp instances are objects; ECMA-262 §22.2 declares
                // `flags`/`global`/`unicode`/`lastIndex` as configurable
                // accessors that user code can override via
                // Object.defineProperty. Without this branch the descriptor
                // is silently dropped on the floor, so test262 patterns that
                // install throwing getters (.../coerce-global.js, etc.)
                // never see the override fire. Per ECMA-262 §10.1.6.3, an
                // attribute-only descriptor (just writable/enumerable/etc.,
                // no value/get/set) preserves the existing value — we mirror
                // that for the user-property dictionary so
                // `Object.defineProperty(r, 'global', {writable:true})`
                // followed by `r.global = false` reads back `false`, not null.
                if (descriptor.Get != null || descriptor.Set != null)
                {
                    rx.DefineAccessor(propertyKey, descriptor.Get, descriptor.Set);
                }
                else if (DescriptorHasValueOrAccessor(descriptorArg))
                {
                    rx.SetProperty(propertyKey, descriptor.Value);
                }
                success = true;
                break;
            default:
                throw new Exception("TypeError: Object.defineProperty called on non-object");
        }

        if (!success)
        {
            throw new Exception($"TypeError: Cannot define property '{propertyKey}': object is not extensible or property is not configurable");
        }

        return target;
    }

    /// <summary>
    /// Returns true if the descriptor object explicitly contains a `value`,
    /// `get`, or `set` key — i.e. it is a data or accessor descriptor, not an
    /// attribute-only descriptor. ECMA-262 §6.2.5.6 distinguishes "field
    /// absent" (preserve existing) from "field present" (overwrite). The
    /// flattened SharpTSPropertyDescriptor record loses that distinction, so
    /// we re-derive it from the source descriptor object's own keys.
    /// </summary>
    private static bool DescriptorHasValueOrAccessor(object descriptorArg)
    {
        switch (descriptorArg)
        {
            case SharpTSObject so:
                return so.HasProperty("value") || so.HasProperty("get") || so.HasProperty("set");
            case Dictionary<string, object?> d:
                return d.ContainsKey("value") || d.ContainsKey("get") || d.ContainsKey("set");
            case System.Collections.IDictionary id:
                return id.Contains("value") || id.Contains("get") || id.Contains("set");
            default:
                // Compiled $Object: probe via reflection on HasProperty(string).
                var t = descriptorArg.GetType();
                var has = t.GetMethod("HasProperty",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                    null, [typeof(string)], null);
                if (has != null)
                {
                    bool Has(string n) => has.Invoke(descriptorArg, [n]) is bool b && b;
                    return Has("value") || Has("get") || Has("set");
                }
                // Conservative fallback: assume value-bearing so we don't
                // silently drop user descriptors on unknown types.
                return true;
        }
    }

    /// <summary>
    /// Object.getOwnPropertyDescriptor(obj, prop) - returns the property descriptor for an own property.
    /// </summary>
    private static object? GetOwnPropertyDescriptor(Interpreter _, List<object?> args)
    {
        var target = args[0];

        if (target == null)
        {
            throw new Exception("TypeError: Object.getOwnPropertyDescriptor called on null or undefined");
        }

        // Symbol-keyed lookup goes through the symbol-dict path; the spec keeps
        // symbols distinct from string keys, and SharpTSObject/Instance store
        // them in a separate map.
        if (args[1] is SharpTSSymbol symKey)
        {
            return GetOwnPropertyDescriptorBySymbol(target, symKey);
        }

        // ECMA-262 §7.1.19: ToPropertyKey on the name argument.
        var propertyKey = PropertyKeyConverter.ToPropertyKeyString(args[1]);

        SharpTSPropertyDescriptor? descriptor = target switch
        {
            SharpTSObject obj => obj.GetOwnPropertyDescriptor(propertyKey),
            SharpTSInstance inst => inst.GetOwnPropertyDescriptor(propertyKey),
            SharpTSArray arr => arr.GetOwnPropertyDescriptor(propertyKey),
            Dictionary<string, object?> dict => GetDictionaryPropertyDescriptor(dict, propertyKey),
            // Function metadata: ECMA-262 §17 — built-in functions expose `name`
            // and `length` as { writable: false, enumerable: false, configurable: true }
            // data properties. test262's verifyProperty() checks introspect these
            // via getOwnPropertyDescriptor; without this branch the descriptor
            // lookup returns null and the assertion fails.
            ISharpTSCallable callable when propertyKey is "name" or "length"
                => GetCallableMetaDescriptor(callable, propertyKey),
            _ => null
        };

        if (descriptor == null)
        {
            return null;
        }

        // Return as an object
        return descriptor.ToObject();
    }

    /// <summary>
    /// Returns ECMA-262 §17 spec descriptor for a callable's `name` or
    /// `length` introspection. Both are { writable: false, enumerable: false,
    /// configurable: true } data properties on built-in functions.
    /// </summary>
    private static SharpTSPropertyDescriptor GetCallableMetaDescriptor(ISharpTSCallable callable, string propertyKey)
    {
        var value = FunctionBuiltIns.GetMember(callable, propertyKey);
        return new SharpTSPropertyDescriptor
        {
            Value = value,
            Writable = false,
            Enumerable = false,
            Configurable = true,
        };
    }

    /// <summary>
    /// Returns a data descriptor reflecting a Symbol-keyed property if one
    /// exists on the target, or null. Symbol-keyed properties are always
    /// writable/configurable but not enumerable per <c>SharpTSObject</c>'s
    /// internal storage.
    /// </summary>
    private static object? GetOwnPropertyDescriptorBySymbol(object target, SharpTSSymbol key)
    {
        switch (target)
        {
            case SharpTSObject obj when obj.HasSymbolProperty(key):
                return DescriptorObjectFor(obj.GetBySymbol(key));
            case SharpTSInstance inst when inst.HasSymbolProperty(key):
                return DescriptorObjectFor(inst.GetBySymbol(key));
            default:
                return null;
        }

        static object? DescriptorObjectFor(object? value) => new SharpTSPropertyDescriptor
        {
            Value = value,
            Writable = true,
            Enumerable = false,
            Configurable = true,
        }.ToObject();
    }

    /// <summary>
    /// Gets property descriptor for a compiled Dictionary<string, object?>.
    /// </summary>
    private static SharpTSPropertyDescriptor? GetDictionaryPropertyDescriptor(Dictionary<string, object?> dict, string propertyKey)
    {
        // First check PropertyDescriptorStore for explicitly defined descriptors
        var compiledDesc = PropertyDescriptorStore.GetPropertyDescriptor(dict, propertyKey);
        if (compiledDesc != null)
        {
            return new SharpTSPropertyDescriptor
            {
                Value = compiledDesc.Value,
                Get = compiledDesc.Getter as ISharpTSCallable,
                Set = compiledDesc.Setter as ISharpTSCallable,
                Writable = compiledDesc.Writable,
                Enumerable = compiledDesc.Enumerable,
                Configurable = compiledDesc.Configurable
            };
        }

        // Fall back to checking if property exists in dictionary (default data descriptor)
        if (dict.TryGetValue(propertyKey, out var value))
        {
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
    /// Object.defineProperties(obj, props) - defines new or modifies existing properties on an object.
    /// Iterates over all own enumerable properties of props and calls defineProperty for each.
    /// </summary>
    private static object? DefineProperties(Interpreter interpreter, List<object?> args)
    {
        var target = args[0];
        var props = args[1];

        if (target == null)
        {
            throw new Exception("TypeError: Object.defineProperties called on null or undefined");
        }

        if (props == null)
        {
            throw new Exception("TypeError: Cannot convert undefined or null to object");
        }

        // Get keys from the properties object
        IEnumerable<string> keys = props switch
        {
            SharpTSObject obj => obj.Fields.Keys,
            SharpTSInstance inst => inst.GetFieldNames(),
            Dictionary<string, object?> dict => dict.Keys,
            _ => throw new Exception("TypeError: Property descriptions must be an object")
        };

        foreach (var key in keys)
        {
            // Get the descriptor for this key
            object? descriptor = props switch
            {
                SharpTSObject obj => obj.Fields.TryGetValue(key, out var v) ? v : null,
                SharpTSInstance inst => inst.GetRawField(key),
                Dictionary<string, object?> dict => dict.TryGetValue(key, out var v) ? v : null,
                _ => null
            };

            // Delegate to defineProperty
            DefineProperty(interpreter, [target, key, descriptor]);
        }

        return target;
    }

    /// <summary>
    /// Object.getOwnPropertyDescriptors(obj) - returns all own property descriptors.
    /// Returns an object whose keys are the property names and values are the corresponding descriptors.
    /// </summary>
    private static object? GetOwnPropertyDescriptors(Interpreter interpreter, List<object?> args)
    {
        var target = args[0];

        if (target == null)
        {
            throw new Exception("TypeError: Object.getOwnPropertyDescriptors called on null or undefined");
        }

        // Get all own property names (including non-enumerable ones from defineProperty)
        List<string> names = target switch
        {
            SharpTSObject obj => GetAllOwnPropertyNames(obj),
            SharpTSInstance inst => inst.GetFieldNames().ToList(),
            SharpTSArray arr => GetOwnPropertyNamesFromArray(arr).Select(n => n!.ToString()!).ToList(),
            Dictionary<string, object?> dict => dict.Keys.ToList(),
            _ => []
        };

        var result = new Dictionary<string, object?>();

        foreach (var name in names)
        {
            var descriptor = GetOwnPropertyDescriptor(interpreter, [target, name]);
            if (descriptor != null)
            {
                result[name] = descriptor;
            }
        }

        return new SharpTSObject(result);
    }

    /// <summary>
    /// Gets all own property names from a SharpTSObject, including accessor-only properties.
    /// </summary>
    private static List<string> GetAllOwnPropertyNames(SharpTSObject obj)
    {
        HashSet<string> names = new(obj.Fields.Keys);
        foreach (var key in obj.PropertyNames)
        {
            names.Add(key);
        }
        return names.ToList();
    }

    /// <summary>
    /// Object.getOwnPropertyNames(obj) - returns an array of all own property names (including non-enumerable).
    /// </summary>
    private static object? GetOwnPropertyNames(Interpreter _, List<object?> args)
    {
        var target = args[0];

        if (target == null)
        {
            throw new Exception("TypeError: Object.getOwnPropertyNames called on null or undefined");
        }

        List<object?> names = target switch
        {
            SharpTSObject obj => GetOwnPropertyNamesFromObject(obj),
            SharpTSInstance inst => inst.GetFieldNames().Select(k => (object?)k).ToList(),
            SharpTSArray arr => GetOwnPropertyNamesFromArray(arr),
            Dictionary<string, object?> dict => dict.Keys.Select(k => (object?)k).ToList(),
            _ => []
        };

        return new SharpTSArray(names);
    }

    /// <summary>
    /// Gets all own property names from a SharpTSObject (including accessor properties).
    /// </summary>
    private static List<object?> GetOwnPropertyNamesFromObject(SharpTSObject obj)
    {
        HashSet<string> names = new(obj.Fields.Keys);

        // Add accessor property names (getters define properties even without data)
        foreach (var key in obj.PropertyNames)
        {
            names.Add(key);
        }

        return names.Select(k => (object?)k).ToList();
    }

    /// <summary>
    /// Gets all own property names from a SharpTSArray (indices + length + any custom properties).
    /// </summary>
    private static List<object?> GetOwnPropertyNamesFromArray(SharpTSArray arr)
    {
        List<object?> names = [];

        // Add numeric indices
        for (int i = 0; i < arr.Length; i++)
        {
            names.Add(i.ToString());
        }

        // Add "length"
        names.Add("length");

        return names;
    }

    /// <summary>
    /// Creates a new object with all properties from source except those in excludeKeys.
    /// Used for object rest patterns: const { x, ...rest } = obj;
    /// </summary>
    public static SharpTSObject ObjectRest(object? source, IEnumerable<object?> excludeKeys)
    {
        HashSet<string> excludeSet = new(excludeKeys.Where(k => k != null).Select(k => k!.ToString()!));
        Dictionary<string, object?> result = [];

        if (source is SharpTSObject obj)
        {
            foreach (var key in obj.Fields.Keys)
            {
                if (!excludeSet.Contains(key))
                    result[key] = obj.Fields[key];
            }
        }
        else if (source is SharpTSInstance inst)
        {
            foreach (var key in inst.GetFieldNames())
            {
                if (!excludeSet.Contains(key))
                    result[key] = inst.GetRawField(key);
            }
        }

        return new SharpTSObject(result);
    }

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
    /// Object.create(proto, propertiesObject?) - creates a new object with the specified prototype.
    /// Since SharpTS doesn't have a true prototype chain, this copies properties from proto
    /// to simulate inheritance.
    /// </summary>
    private static object? Create(Interpreter interpreter, List<object?> args)
    {
        var proto = args[0];
        var propertiesObject = args.Count > 1 ? args[1] : null;

        // ECMA-262 §20.1.2.2 step 2: Let obj be OrdinaryObjectCreate(O).
        // OrdinaryObjectCreate creates a FRESH object whose [[Prototype]] is
        // O — it does NOT copy O's own properties. Inherited properties are
        // reached via prototype-chain walk at property-access time.
        // SharpTSObject.GetProperty/HasProperty map the virtual `__proto__`
        // slot to the Prototype property so Interpreter.Properties.cs's
        // chain walker (which queries `__proto__`) reaches the prototype
        // without needing a real _fields entry that would leak via
        // Object.keys.
        var result = new SharpTSObject([]);
        result.Prototype = proto;
        // Object.create(null) → a null-prototype object that inherits nothing
        // (not even Object.prototype's methods). Distinguishes it from an
        // ordinary object, whose Prototype is also null by default.
        if (proto is null or SharpTSUndefined)
            result.IsNullPrototype = true;

        // If propertiesObject is provided, define properties using defineProperty semantics
        if (propertiesObject != null)
        {
            DefinePropertiesFromDescriptors(propertiesObject, result, interpreter);
        }

        return result;
    }

    /// <summary>
    /// Copies properties from a source object to a target SharpTSObject.
    /// </summary>
    private static void CopyPropertiesFrom(object source, SharpTSObject target)
    {
        switch (source)
        {
            case SharpTSObject srcObj:
                foreach (var kv in srcObj.Fields)
                {
                    target.SetProperty(kv.Key, kv.Value);
                }
                // Copy getters and setters
                foreach (var propName in srcObj.PropertyNames)
                {
                    var getter = srcObj.GetGetter(propName);
                    var setter = srcObj.GetSetter(propName);
                    if (getter != null)
                        target.DefineGetter(propName, getter);
                    if (setter != null)
                        target.DefineSetter(propName, setter);
                }
                break;

            case SharpTSInstance srcInst:
                foreach (var key in srcInst.GetFieldNames())
                {
                    target.SetProperty(key, srcInst.GetRawField(key));
                }
                break;

            case Dictionary<string, object?> dict:
                foreach (var kv in dict)
                {
                    target.SetProperty(kv.Key, kv.Value);
                }
                break;
        }
    }

    /// <summary>
    /// Defines properties on target using property descriptors from propertiesObject.
    /// Each property in propertiesObject should be a descriptor object.
    /// </summary>
    private static void DefinePropertiesFromDescriptors(object propertiesObject, SharpTSObject target, Interpreter interpreter)
    {
        IEnumerable<KeyValuePair<string, object?>>? entries = propertiesObject switch
        {
            SharpTSObject obj => obj.Fields,
            Dictionary<string, object?> dict => dict,
            _ => null
        };

        if (entries == null) return;

        foreach (var kv in entries)
        {
            if (kv.Value == null) continue;

            var descriptor = SharpTSPropertyDescriptor.FromAnyObject(kv.Value);
            ApplyBooleanAttributes(descriptor, kv.Value, interpreter);
            target.DefineProperty(kv.Key, descriptor);
        }
    }

    /// <summary>
    /// ECMA-262 §6.2.5.5 ToPropertyDescriptor boolean-attribute coercion done
    /// with interpreter access: each of writable/enumerable/configurable, when
    /// resolvable via <c>Get</c> (own or inherited, data or accessor), is
    /// ToBoolean-coerced. A non-undefined result means the attribute is present
    /// (for these three, "absent" and "present-but-undefined" both yield the
    /// false default, so this matches spec). Overrides the own-only <c>is bool</c>
    /// values from <see cref="SharpTSPropertyDescriptor.FromAnyObject"/>.
    /// </summary>
    private static void ApplyBooleanAttributes(SharpTSPropertyDescriptor descriptor, object? descObj, Interpreter interpreter)
    {
        if (descObj is null) return;
        var w = interpreter.GetProperty(descObj, "writable");
        if (w is not (null or SharpTSUndefined)) descriptor.Writable = Compilation.RuntimeTypes.IsTruthy(w);
        var e = interpreter.GetProperty(descObj, "enumerable");
        if (e is not (null or SharpTSUndefined)) descriptor.Enumerable = Compilation.RuntimeTypes.IsTruthy(e);
        var c = interpreter.GetProperty(descObj, "configurable");
        if (c is not (null or SharpTSUndefined)) descriptor.Configurable = Compilation.RuntimeTypes.IsTruthy(c);
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
    /// Object.preventExtensions(obj) - prevents new properties from being added to an object.
    /// Unlike freeze/seal, existing properties can still be modified and deleted.
    /// </summary>
    private static object? PreventExtensions(Interpreter _, List<object?> args)
    {
        switch (args[0])
        {
            case SharpTSObject obj:
                obj.PreventExtensions();
                return obj;
            case SharpTSInstance inst:
                inst.PreventExtensions();
                return inst;
            case SharpTSArray arr:
                arr.PreventExtensions();
                return arr;
            case Dictionary<string, object?> dict:
                PropertyDescriptorStore.PreventExtensions(dict);
                return dict;
            case System.Collections.IDictionary idict:
                PropertyDescriptorStore.PreventExtensions(idict);
                return idict;
            default:
                // Non-objects are returned unchanged (JavaScript behavior)
                return args[0];
        }
    }

    /// <summary>
    /// Object.isExtensible(obj) - returns whether new properties can be added to an object.
    /// </summary>
    private static object? IsExtensibleMethod(Interpreter _, List<object?> args)
    {
        return args[0] switch
        {
            SharpTSObject obj => obj.IsExtensible,
            SharpTSInstance inst => inst.IsExtensible,
            SharpTSArray arr => arr.IsExtensible,
            Dictionary<string, object?> dict => PropertyDescriptorStore.IsExtensible(dict),
            System.Collections.IDictionary idict => PropertyDescriptorStore.IsExtensible(idict),
            // Primitives are not extensible
            _ => false
        };
    }

    /// <summary>
    /// Object.getOwnPropertySymbols(obj) - returns an array of symbol-keyed properties.
    /// </summary>
    private static object? GetOwnPropertySymbols(Interpreter _, List<object?> args)
    {
        if (args[0] == null)
            throw new Exception("TypeError: Cannot convert null to object");

        List<object?> symbols = args[0] switch
        {
            SharpTSObject obj => obj.GetSymbolPropertyNames().Select(s => (object?)s).ToList(),
            SharpTSInstance inst => inst.GetSymbolPropertyNames().Select(s => (object?)s).ToList(),
            Dictionary<string, object?> dict => PropertyDescriptorStore.GetSymbolKeys(dict)
                                                  .Select(s => (object?)s).ToList(),
            _ => []
        };
        return new SharpTSArray(symbols);
    }

    private static RuntimeValue GetPrototypeOfV2(Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var target = args[0].ToObject()
            ?? throw new Exception("TypeError: Cannot convert null to object");

        var proto = target switch
        {
            SharpTSObject obj => obj.Prototype,
            SharpTSInstance inst => inst.Prototype,
            SharpTSArray => null,
            // ECMA-262 §22.2.6: a RegExp instance's [[Prototype]] is the
            // per-realm RegExp.prototype object, so `Object.getPrototypeOf(/x/)
            // === RegExp.prototype` (the from-regexp-like tests assert this).
            SharpTSRegExp => interp.GetRegExpPrototype(),
            Dictionary<string, object?> dict => PropertyDescriptorStore.GetPrototype(dict),
            _ => null
        };

        return proto != null ? RuntimeValue.FromObject(proto) : RuntimeValue.Null;
    }

    /// <summary>
    /// Object.setPrototypeOf(obj, proto) - sets the prototype of an object.
    /// </summary>
    private static object? SetPrototypeOf(Interpreter _, List<object?> args)
    {
        var target = args[0];
        var proto = args.Count > 1 ? args[1] : null;

        if (target == null)
            throw new Exception("TypeError: Cannot convert null to object");

        switch (target)
        {
            case SharpTSObject obj:
                if (!obj.IsExtensible)
                    throw new Exception("TypeError: Object is not extensible");
                obj.Prototype = proto;
                // Copy properties from new prototype if non-null
                if (proto != null)
                    CopyPropertiesFrom(proto, obj);
                return obj;

            case SharpTSInstance:
                // Cannot change prototype of class instances
                throw new Exception("TypeError: Cannot set prototype of class instance");

            case Dictionary<string, object?> dict:
                if (!PropertyDescriptorStore.IsExtensible(dict))
                    throw new Exception("TypeError: Object is not extensible");
                PropertyDescriptorStore.SetPrototype(dict, proto);
                if (proto != null)
                    RuntimeCopyPropertiesFrom(proto, dict);
                return dict;

            default:
                // Non-objects return unchanged (JavaScript behavior)
                return target;
        }
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
    /// Checks if an object is a primitive value (not extensible).
    /// </summary>
    private static bool IsPrimitive(object? obj)
    {
        return obj is double or int or float or decimal or string or bool or char;
    }

    /// <summary>
    /// Checks if an object is a compiled class instance (has _fields dictionary).
    /// </summary>
    private static bool IsCompiledClassInstance(object obj)
    {
        var type = obj.GetType();
        // Compiled class instances typically have a _fields field
        var fieldsField = type.GetField("_fields", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return fieldsField != null;
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

    private static object? GroupBy(Interpreter interp, List<object?> args)
    {
        var iterable = args[0] as SharpTSArray
            ?? throw new Exception("TypeError: Object.groupBy requires an iterable as first argument");
        var callback = args[1] as ISharpTSCallable
            ?? throw new Exception("TypeError: Object.groupBy requires a function as second argument");

        var groups = new Dictionary<string, object?>();
        var callbackArgs = new List<object?> { null, null };

        for (int i = 0; i < iterable.Length; i++)
        {
            var element = iterable[i];
            callbackArgs[0] = element;
            callbackArgs[1] = (double)i;
            var key = callback.Call(interp, callbackArgs);
            var keyStr = key?.ToString() ?? "undefined";

            if (!groups.TryGetValue(keyStr, out var existing))
            {
                existing = new SharpTSArray([]);
                groups[keyStr] = existing;
            }
            ((SharpTSArray)existing!).Add(element);
        }

        // ECMA-262 §20.1.2.13: the result is OrdinaryObjectCreate(null) — a
        // null-prototype object, so it does not inherit Object.prototype.
        return new SharpTSObject(groups) { IsNullPrototype = true };
    }

    // ===================== V2 Wrappers (RuntimeValue boundary — delegates to internal logic) =====================

    private static RuntimeValue FromEntriesV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(FromEntries(interp, SpanToList(args)));

    private static RuntimeValue AssignV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(Assign(interp, SpanToList(args)));

    private static RuntimeValue DefinePropertyV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(DefineProperty(interp, SpanToList(args)));

    private static RuntimeValue GetOwnPropertyDescriptorV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(GetOwnPropertyDescriptor(interp, SpanToList(args)));

    private static RuntimeValue GetOwnPropertyNamesV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(GetOwnPropertyNames(interp, SpanToList(args)));

    private static RuntimeValue CreateV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(Create(interp, SpanToList(args)));

    private static RuntimeValue PreventExtensionsV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(PreventExtensions(interp, SpanToList(args)));

    private static RuntimeValue IsExtensibleMethodV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(IsExtensibleMethod(interp, SpanToList(args)));

    private static RuntimeValue GetOwnPropertySymbolsV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(GetOwnPropertySymbols(interp, SpanToList(args)));

    private static RuntimeValue SetPrototypeOfV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(SetPrototypeOf(interp, SpanToList(args)));

    private static RuntimeValue GroupByV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(GroupBy(interp, SpanToList(args)));

    private static RuntimeValue DefinePropertiesV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(DefineProperties(interp, SpanToList(args)));

    private static RuntimeValue GetOwnPropertyDescriptorsV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(GetOwnPropertyDescriptors(interp, SpanToList(args)));

    private static List<object?> SpanToList(ReadOnlySpan<RuntimeValue> args)
    {
        var list = new List<object?>(args.Length);
        foreach (var arg in args)
            list.Add(arg.ToObject());
        return list;
    }
}
