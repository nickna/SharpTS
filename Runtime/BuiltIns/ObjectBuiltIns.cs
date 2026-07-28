using SharpTS.Compilation;
using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

public static partial class ObjectBuiltIns
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

    /// <summary>Static member names for REPL autocomplete.</summary>
    public static IEnumerable<string> StaticMemberNames => _staticLookup.MemberNames;

    /// <summary>
    /// Enumerates the own enumerable (key, value) pairs of a receiver, encoding the
    /// Object.keys/values/entries receiver-type ladder once. Branch order and per-branch
    /// semantics are load-bearing:
    /// - SharpTSObject: own enumerable fields.
    /// - SharpTSArray: ECMA-262 — only present (non-hole) indices are own enumerable
    ///   properties, so holes are skipped; keys are the stringified indices.
    /// - SharpTSInstance: declared fields.
    /// - IDictionary&lt;string, object?&gt;: runtime helpers (e.g. Web Streams iterator results)
    ///   produce JS-object-shaped data without going through SharpTSObject; compiled mode has
    ///   the matching dict branch in $Runtime.GetKeys, this keeps the interpreter at parity.
    /// - SharpTSMath: built-in members are non-enumerable, so the own enumerable keys are
    ///   exactly the user-assigned extras. Matches compiled mode (#288).
    /// - Function/arrow function: functions are objects (ToObject is the identity), and their
    ///   own enumerable keys are the user-assigned expando properties — lodash mixes its
    ///   utility map onto the `lodash` function and enumerates it via keys() (#314); other
    ///   callables have none.
    /// Throws for non-object receivers; <paramref name="apiName"/> qualifies the message.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, object?>> EnumerateOwnEnumerable(object? arg, string apiName)
    {
        switch (arg)
        {
            case SharpTSObject obj:
                foreach (var k in obj.OwnEnumerableKeys())
                    yield return new(k, obj.Fields[k]);
                yield break;
            case SharpTSArray arr:
                for (int i = 0; i < arr.Length; i++)
                    if (arr.HasIndex(i))
                        yield return new(i.ToString(), arr[i]);
                yield break;
            case SharpTSInstance inst:
                foreach (var n in inst.GetFieldNames())
                    yield return new(n, inst.GetRawField(n));
                yield break;
            case IDictionary<string, object?> dict:
                foreach (var kv in dict)
                    yield return kv;
                yield break;
            case SharpTSMath math:
                foreach (var kv in math.OwnEnumerableProperties)
                    yield return new(kv.Key, kv.Value);
                yield break;
            case SharpTSFunction fn:
                foreach (var k in fn.PropertyKeys)
                    yield return new(k, fn.TryGetProperty(k, out var v) ? v : null);
                yield break;
            case SharpTSArrowFunction arrowFn:
                foreach (var k in arrowFn.PropertyKeys)
                    yield return new(k, arrowFn.TryGetProperty(k, out var av) ? av : null);
                yield break;
            case ISharpTSCallable:
                yield break;
            default:
                throw new Exception($"{apiName} requires an object argument");
        }
    }

    private static RuntimeValue KeysV2(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var keys = EnumerateOwnEnumerable(args[0].ToObject(), "Object.keys()")
            .Select(kv => (object?)kv.Key).ToList();
        return RuntimeValue.FromObject(new SharpTSArray(keys));
    }

    private static RuntimeValue ValuesV2(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var values = EnumerateOwnEnumerable(args[0].ToObject(), "Object.values()")
            .Select(kv => kv.Value).ToList();
        return RuntimeValue.FromObject(new SharpTSArray(values));
    }

    private static RuntimeValue EntriesV2(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var entries = EnumerateOwnEnumerable(args[0].ToObject(), "Object.entries()")
            .Select(kv => (object?)new SharpTSArray([(object?)kv.Key, kv.Value])).ToList();
        return RuntimeValue.FromObject(new SharpTSArray(entries));
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
        // §6.2.5.5 also reads value/get/set through the prototype chain (honoring
        // accessors); FromAnyObject only saw own fields. Re-derive them prototype-aware
        // and record presence so omitted-vs-undefined is preserved downstream (#801).
        ApplyValueAndAccessors(descriptor, descriptorArg, interpreter);

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
            bool descriptorHasValue = descriptor.HasValue;
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
        var propertyKey = interpreter.ToPropertyKeyString(args[1]);

        PreserveOmittedAttributes(target, propertyKey, descriptor, descriptorArg, interpreter);

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
                else if (descriptor.HasValue)
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
                else if (descriptor.HasValue)
                {
                    rx.SetProperty(propertyKey, descriptor.Value);
                }
                success = true;
                break;
            case SharpTSPromise promise:
                // Promise instances are objects; user code may install own
                // accessor or data properties — notably a poisoned `constructor`
                // getter that must fire when then/catch/finally resolve
                // SpeciesConstructor (test262 then/ctor-poisoned, #350). Storage
                // lives on the base SharpTSPromise so plain and subclass promises
                // both accept defineProperty. Attribute-only descriptors preserve
                // the existing value per ECMA-262 §10.1.6.3.
                if (descriptor.Get != null || descriptor.Set != null)
                {
                    promise.DefineAccessor(propertyKey, descriptor.Get, descriptor.Set);
                }
                else if (descriptor.HasValue)
                {
                    promise.SetOwnProperty(propertyKey, descriptor.Value);
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
    /// Object.getOwnPropertyDescriptor(obj, prop) - returns the property descriptor for an own property.
    /// </summary>
    private static object? GetOwnPropertyDescriptor(Interpreter interpreter, List<object?> args)
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
        var propertyKey = interpreter.ToPropertyKeyString(args[1]);

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

        if (target is null or SharpTSUndefined)
        {
            throw new Exception("TypeError: Object.defineProperties called on null or undefined");
        }

        if (props is null or SharpTSUndefined)
        {
            throw new Exception("TypeError: Cannot convert undefined or null to object");
        }

        // ECMA-262 §19.1.2.3 ObjectDefineProperties: for each own ENUMERABLE
        // property key of props, read its descriptor object via Get — firing any
        // accessor getter with `this` bound to props — then DefineProperty on the
        // target. Reading via Get (not the raw field) matters when props is a
        // boxed primitive wrapper carrying an accessor descriptor, e.g.
        // `Object.defineProperties(o, new Number(n))` where the descriptor lives
        // behind a getter whose body inspects `this instanceof Number`. (#454)
        if (props is SharpTSObject obj)
        {
            foreach (var key in OwnEnumerablePropertyKeys(obj))
            {
                var descriptor = interpreter.GetProperty(obj, key);
                DefineProperty(interpreter, [target, key, descriptor]);
            }
            return target;
        }

        // SharpTSInstance / plain Dictionary carriers store data only (no
        // separate accessor storage), so a raw field read already matches Get.
        IEnumerable<KeyValuePair<string, object?>> entries = props switch
        {
            SharpTSInstance inst => inst.GetFieldNames()
                .Select(k => new KeyValuePair<string, object?>(k, inst.GetRawField(k))),
            Dictionary<string, object?> dict => dict,
            _ => throw new Exception("TypeError: Property descriptions must be an object")
        };

        foreach (var entry in entries)
        {
            DefineProperty(interpreter, [target, entry.Key, entry.Value]);
        }

        return target;
    }

    /// <summary>
    /// Own enumerable string-keyed property names of <paramref name="obj"/> —
    /// enumerable data fields plus enumerable accessor (getter/setter)
    /// properties — excluding the internal slots that back boxed primitive
    /// wrappers (<c>new String/Number/Boolean</c>), which must stay invisible to
    /// enumeration per ECMA-262. Accessor names are stored disjointly from data
    /// fields (see <see cref="SharpTSObject.AccessorPropertyNames"/>).
    /// </summary>
    private static IEnumerable<string> OwnEnumerablePropertyKeys(SharpTSObject obj)
    {
        foreach (var key in obj.Fields.Keys)
            if (!IsBoxedPrimitiveInternalSlot(key) && obj.GetPropertyFlags(key).Enumerable)
                yield return key;
        foreach (var key in obj.AccessorPropertyNames)
            if (obj.GetPropertyFlags(key).Enumerable)
                yield return key;
    }

    /// <summary>
    /// True for the internal-slot field names used by boxed primitive wrappers
    /// (see <see cref="BuiltInConstructorFactory"/>). They hold [[StringData]] /
    /// [[NumberData]] / [[BooleanData]] plus the wrapper's type tag — not real
    /// own properties — so enumeration-based spec operations must skip them.
    /// </summary>
    private static bool IsBoxedPrimitiveInternalSlot(string key)
        => key is "__primitiveType" or "__primitiveValue";

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
    /// Object.create(proto, propertiesObject?) - creates a new object with the specified prototype.
    /// Since SharpTS doesn't have a true prototype chain, this copies properties from proto
    /// to simulate inheritance.
    /// </summary>
    private static object? Create(Interpreter interpreter, List<object?> args)
    {
        var proto = args[0];
        var propertiesObject = args.Count > 1 ? args[1] : null;

        // ECMA-262 §20.1.2.2 step 1: if Type(O) is neither Object nor Null,
        // throw TypeError. Object.create(undefined/number/string/bool/symbol/…)
        // throws; only a real object or null is a valid prototype. (Mirrors the
        // compiled-mode $Runtime.ObjectCreate guard.) Thrown as a real
        // SharpTSTypeError so guest `assert.throws(TypeError, …)` / `instanceof`
        // see a TypeError instance, not a bare string.
        if (proto is SharpTSUndefined or double or int or long or bool or string
            or System.Numerics.BigInteger or SharpTSSymbol)
            throw new Runtime.Exceptions.ThrowException(
                new SharpTSTypeError("Object prototype may only be an Object or null"));

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
        // ordinary object, whose Prototype is also null by default. (undefined
        // is rejected by the Object-or-Null guard above, so only null reaches here.)
        if (proto is null)
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
    /// ECMA-262 §6.2.5.5 ToPropertyDescriptor reads <c>value</c>/<c>get</c>/<c>set</c>
    /// with HasProperty/Get semantics — walking the descriptor object's prototype
    /// chain and honoring accessors. <see cref="SharpTSPropertyDescriptor.FromAnyObject"/>
    /// only sees own fields, so re-derive these prototype-aware with interpreter access
    /// and record presence on the descriptor (#801). An inherited setter-only
    /// <c>value</c> counts as a data descriptor whose value reads <c>undefined</c>,
    /// which <see cref="Interpreter.HasProperty"/> detects even though Get yields
    /// undefined for it.
    /// </summary>
    private static void ApplyValueAndAccessors(SharpTSPropertyDescriptor descriptor, object? descObj, Interpreter interpreter)
    {
        if (descObj is null) return;
        // TryGetDescriptorField applies Get semantics with correct presence AND
        // own-accessor shadowing (an own setter-only `value` shadows an inherited
        // getter, yielding undefined) — plain Get would walk past it (#801).
        if (interpreter.TryGetDescriptorField(descObj, "value", out var v))
        {
            descriptor.Value = v;
            descriptor.HasValue = true;
        }
        if (interpreter.TryGetDescriptorField(descObj, "get", out var g))
        {
            descriptor.HasGet = true;
            descriptor.Get = g as ISharpTSCallable;
        }
        if (interpreter.TryGetDescriptorField(descObj, "set", out var s))
        {
            descriptor.HasSet = true;
            descriptor.Set = s as ISharpTSCallable;
        }
    }

    /// <summary>
    /// ECMA-262 §10.1.6.3 ValidateAndApplyPropertyDescriptor: when redefining an
    /// EXISTING own property, attributes the descriptor omits are preserved from
    /// the current property rather than reset to false (<see cref="SharpTSPropertyDescriptor"/>
    /// defaults absent booleans to false, and <see cref="ApplyBooleanAttributes"/> only
    /// sets the ones actually present). Without this,
    /// <c>Object.defineProperty(o, "a", { writable:false })</c> on an enumerable data
    /// property <c>a</c> wrongly clears its enumerable flag, dropping it from
    /// Object.keys/values/entries/for-in (#475). Scoped to plain objects
    /// (<see cref="SharpTSObject"/>) — the surface affected by #475's enumerability
    /// changes; instances/arrays/dicts keep their existing behavior.
    /// </summary>
    private static void PreserveOmittedAttributes(
        object? target, string propertyKey, SharpTSPropertyDescriptor descriptor, object? descObj, Interpreter interpreter)
    {
        if (target is not SharpTSObject obj || descObj is null) return;
        // Only a plain object/dictionary descriptor; exotic descriptors keep prior behavior.
        if (descObj is not (SharpTSObject or Dictionary<string, object?>)) return;
        bool exists = obj.Fields.ContainsKey(propertyKey) || obj.AccessorPropertyNames.Contains(propertyKey);
        if (!exists) return; // a brand-new property defaults omitted attributes to false (spec)
        var existing = obj.GetPropertyFlags(propertyKey);
        // Attribute presence is read via interpreter.GetProperty, which walks the
        // prototype chain (matching ECMA-262 ToPropertyDescriptor), so an inherited
        // attribute is correctly treated as specified rather than preserved.
        if (!DescriptorSpecifies(descObj, "writable", interpreter)) descriptor.Writable = existing.Writable;
        if (!DescriptorSpecifies(descObj, "enumerable", interpreter)) descriptor.Enumerable = existing.Enumerable;
        if (!DescriptorSpecifies(descObj, "configurable", interpreter)) descriptor.Configurable = existing.Configurable;
    }

    /// <summary>
    /// True when the source descriptor object provides <paramref name="attr"/> —
    /// own or inherited, including setter-only accessors (ECMA-262 §7.3.11
    /// HasProperty semantics), so an inherited attribute is treated as specified
    /// rather than preserved (#801).
    /// </summary>
    private static bool DescriptorSpecifies(object? descObj, string attr, Interpreter interpreter)
        => interpreter.HasProperty(descObj, attr);

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
        => RuntimeValue.FromBoxed(FromEntries(interp, CallableInterop.ToBoxedList(args)));

    private static RuntimeValue AssignV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(Assign(interp, CallableInterop.ToBoxedList(args)));

    private static RuntimeValue DefinePropertyV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(DefineProperty(interp, CallableInterop.ToBoxedList(args)));

    private static RuntimeValue GetOwnPropertyDescriptorV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(GetOwnPropertyDescriptor(interp, CallableInterop.ToBoxedList(args)));

    private static RuntimeValue GetOwnPropertyNamesV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(GetOwnPropertyNames(interp, CallableInterop.ToBoxedList(args)));

    private static RuntimeValue CreateV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(Create(interp, CallableInterop.ToBoxedList(args)));

    private static RuntimeValue PreventExtensionsV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(PreventExtensions(interp, CallableInterop.ToBoxedList(args)));

    private static RuntimeValue IsExtensibleMethodV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(IsExtensibleMethod(interp, CallableInterop.ToBoxedList(args)));

    private static RuntimeValue GetOwnPropertySymbolsV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(GetOwnPropertySymbols(interp, CallableInterop.ToBoxedList(args)));

    private static RuntimeValue SetPrototypeOfV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(SetPrototypeOf(interp, CallableInterop.ToBoxedList(args)));

    private static RuntimeValue GroupByV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(GroupBy(interp, CallableInterop.ToBoxedList(args)));

    private static RuntimeValue DefinePropertiesV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(DefineProperties(interp, CallableInterop.ToBoxedList(args)));

    private static RuntimeValue GetOwnPropertyDescriptorsV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
        => RuntimeValue.FromBoxed(GetOwnPropertyDescriptors(interp, CallableInterop.ToBoxedList(args)));
}
