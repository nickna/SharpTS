using SharpTS.Compilation;
using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

public static partial class ObjectBuiltIns
{
    private static readonly BuiltInStaticMemberLookup _staticLookup =
        BuiltInStaticBuilder.Create()
            .MethodV2("keys", 1, KeysV2)
            .MethodV2("values", 1, ValuesV2)
            .MethodV2("entries", 1, EntriesV2)
            .MethodV2("fromEntries", 0, 1, 1, FromEntriesV2)
            .MethodV2("hasOwn", 2, HasOwnV2)
            .MethodV2("is", 0, 2, 2, IsV2)
            .MethodV2("assign", 1, int.MaxValue, 2, AssignV2)
            .MethodV2("freeze", 1, FreezeV2)
            .MethodV2("seal", 1, SealV2)
            .MethodV2("isFrozen", 1, IsFrozenV2)
            .MethodV2("isSealed", 1, IsSealedV2)
            .MethodV2("defineProperty", 3, DefinePropertyV2)
            .MethodV2("getOwnPropertyDescriptor", 2, GetOwnPropertyDescriptorV2)
            .MethodV2("getOwnPropertyNames", 1, GetOwnPropertyNamesV2)
            .MethodV2("create", 1, 2, 2, CreateV2)
            .MethodV2("preventExtensions", 1, PreventExtensionsV2)
            .MethodV2("isExtensible", 1, IsExtensibleMethodV2)
            .MethodV2("getOwnPropertySymbols", 1, GetOwnPropertySymbolsV2)
            .MethodV2("getPrototypeOf", 0, 1, 1, GetPrototypeOfV2)
            .MethodV2("setPrototypeOf", 0, 2, 2, SetPrototypeOfV2)
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
    private static IEnumerable<KeyValuePair<string, object?>> EnumerateOwnEnumerable(
        Interpreter interpreter, object? arg, string apiName)
    {
        switch (arg)
        {
            case null:
            case SharpTSUndefined:
                throw new ThrowException(new SharpTSTypeError(
                    $"{apiName} called on null or undefined"));
            case string text:
                for (int i = 0; i < text.Length; i++)
                    yield return new(i.ToString(), text[i].ToString());
                yield break;
            case bool:
            case double:
            case int:
            case long:
            case System.Numerics.BigInteger:
            case SharpTSBigInt:
            case SharpTSSymbol:
                yield break;
            case SharpTSObject obj:
                foreach (var k in obj.OwnEnumerableKeys())
                    yield return new(k, interpreter.GetProperty(obj, k));
                yield break;
            case SharpTSArray arr:
                foreach (var key in arr.OwnEnumerableKeys())
                {
                    object? value = uint.TryParse(key, out uint index)
                        ? interpreter.GetProperty(arr, key)
                        : arr.GetNamedProperty(key);
                    yield return new(key, value);
                }
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
            case SharpTSJSON json:
                foreach (var key in json.OwnEnumerableKeys())
                    yield return new(key, json.TryGetExtra(key));
                yield break;
            case SharpTSDate date:
                foreach (var key in date.OwnEnumerableKeys())
                    yield return new(key, date.TryGetExtra(key));
                yield break;
            case SharpTSRegExp regex:
                foreach (var key in regex.OwnEnumerableKeys())
                    yield return new(key, regex.TryGetProperty(key, out var value) ? value : null);
                yield break;
            case SharpTSError error:
                foreach (var key in error.OwnEnumerableKeys())
                    yield return new(key, interpreter.GetProperty(error, key));
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

    private static RuntimeValue KeysV2(Interpreter interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var keys = EnumerateOwnEnumerable(interpreter, args[0].ToObject(), "Object.keys()")
            .Select(kv => (object?)kv.Key).ToList();
        return RuntimeValue.FromObject(new SharpTSArray(keys));
    }

    private static RuntimeValue ValuesV2(Interpreter interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var values = EnumerateOwnEnumerable(interpreter, args[0].ToObject(), "Object.values()")
            .Select(kv => kv.Value).ToList();
        return RuntimeValue.FromObject(new SharpTSArray(values));
    }

    private static RuntimeValue EntriesV2(Interpreter interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var entries = EnumerateOwnEnumerable(interpreter, args[0].ToObject(), "Object.entries()")
            .Select(kv => (object?)new SharpTSArray([(object?)kv.Key, kv.Value])).ToList();
        return RuntimeValue.FromObject(new SharpTSArray(entries));
    }

    private static object? FromEntries(Interpreter interpreter, List<object?> args)
    {
        if (args.Count == 0 || args[0] is null or SharpTSUndefined)
            throw new ThrowException(new SharpTSTypeError(
                "Object.fromEntries requires an iterable argument"));

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

    private static RuntimeValue HasOwnV2(
        Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var obj = args[0].ToObject();
        // Object.hasOwn performs ToObject before ToPropertyKey. In particular,
        // nullish targets throw without observing a coercible property key.
        if (obj is null or SharpTSUndefined)
            throw new ThrowException(new SharpTSTypeError(
                "Object.hasOwn called on null or undefined"));
        // A Symbol key stays a Symbol (ToPropertyKey); anything else stringifies.
        var key = args[1].ToObject() is SharpTSSymbol sym
            ? (object)sym
            : interp.ToPropertyKeyString(args[1].ToObject());
        return RuntimeValue.FromBoolean(SharpTSObjectUnboundMethod.HasOwn(interp, obj, key));
    }

    private static RuntimeValue IsV2(Interpreter _, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var value1 = args.Length > 0 ? args[0] : RuntimeValue.Undefined;
        var value2 = args.Length > 1 ? args[1] : RuntimeValue.Undefined;

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
            var d1 = Interpreter.ToNumber(value1);
            var d2 = Interpreter.ToNumber(value2);
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

    private static object? Assign(Interpreter interpreter, List<object?> args)
    {
        // Object.assign(target, ...sources)
        if (args.Count == 0 || args[0] is null or SharpTSUndefined)
            throw new ThrowException(new SharpTSTypeError(
                "Object.assign called on null or undefined"));

        args[0] = BuiltInConstructorFactory.ToObject(args[0], interpreter);
        var target = args[0]!;
        for (int i = 1; i < args.Count; i++)
        {
            if (args[i] is null or SharpTSUndefined) continue;
            foreach (var entry in EnumerateOwnEnumerable(interpreter, args[i], "Object.assign"))
                SetAssignedProperty(target, entry.Key, entry.Value);
            switch (args[i])
            {
                case SharpTSObject source:
                    foreach (var symbol in source.GetSymbolPropertyNames())
                        SetAssignedSymbol(target, symbol, source.GetBySymbol(symbol));
                    break;
                case SharpTSInstance source:
                    foreach (var symbol in source.GetSymbolPropertyNames())
                        SetAssignedSymbol(target, symbol, source.GetBySymbol(symbol));
                    break;
            }
        }
        return target;
    }

    private static void SetAssignedProperty(object target, string name, object? value)
    {
        switch (target)
        {
            case SharpTSObject obj: obj.SetPropertyStrict(name, value, strictMode: true); break;
            case SharpTSInstance instance: instance.SetRawFieldStrict(name, value, strictMode: true); break;
            case SharpTSArray array when uint.TryParse(name, out uint index): array.Set(index, value); break;
            case SharpTSArray array: array.SetNamedProperty(name, value); break;
            case SharpTSFunction function: function.SetProperty(name, value); break;
            case SharpTSArrowFunction function: function.SetProperty(name, value); break;
            case SharpTSAsyncFunction function: function.SetProperty(name, value); break;
            case SharpTSAsyncArrowFunction function: function.SetProperty(name, value); break;
            case SharpTSRegExp regex: regex.SetProperty(name, value); break;
            case SharpTSError error: error.SetProperty(name, value); break;
            case IDictionary<string, object?> dict: dict[name] = value; break;
            default: throw new ThrowException(new SharpTSTypeError(
                $"Object.assign target does not support properties ({target.GetType().Name})"));
        }
    }

    private static void SetAssignedSymbol(object target, SharpTSSymbol symbol, object? value)
    {
        switch (target)
        {
            case SharpTSObject obj: obj.SetBySymbolStrict(symbol, value, strictMode: true); break;
            case SharpTSInstance instance: instance.SetBySymbolStrict(symbol, value, strictMode: true); break;
            default: throw new ThrowException(new SharpTSTypeError(
                $"Object.assign target does not support symbol properties ({target.GetType().Name})"));
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
            case SharpTSFunction fn:
                fn.FreezeOwnProperties();
                PropertyDescriptorStore.Freeze(fn);
                return RuntimeValue.FromObject(fn);
            case SharpTSArrowFunction arrow:
                arrow.FreezeOwnProperties();
                PropertyDescriptorStore.Freeze(arrow);
                return RuntimeValue.FromObject(arrow);
            case SharpTSDate date:
                date.FreezeOwnProperties();
                PropertyDescriptorStore.Freeze(date);
                return RuntimeValue.FromObject(date);
            case SharpTSRegExp regex:
                regex.FreezeOwnProperties();
                PropertyDescriptorStore.Freeze(regex);
                return RuntimeValue.FromObject(regex);
            case Dictionary<string, object?> dict:
                PropertyDescriptorStore.Freeze(dict);
                return RuntimeValue.FromObject(dict);
            case System.Collections.IDictionary idict:
                PropertyDescriptorStore.Freeze(idict);
                return RuntimeValue.FromObject(idict);
            default:
                if (args[0].Kind == ValueKind.Object && arg is not null)
                    PropertyDescriptorStore.Freeze(arg);
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
            case SharpTSFunction fn:
                fn.SealOwnProperties();
                PropertyDescriptorStore.Seal(fn);
                return RuntimeValue.FromObject(fn);
            case SharpTSArrowFunction arrow:
                arrow.SealOwnProperties();
                PropertyDescriptorStore.Seal(arrow);
                return RuntimeValue.FromObject(arrow);
            case SharpTSDate date:
                date.SealOwnProperties();
                PropertyDescriptorStore.Seal(date);
                return RuntimeValue.FromObject(date);
            case SharpTSRegExp regex:
                regex.SealOwnProperties();
                PropertyDescriptorStore.Seal(regex);
                return RuntimeValue.FromObject(regex);
            case Dictionary<string, object?> dict:
                PropertyDescriptorStore.Seal(dict);
                return RuntimeValue.FromObject(dict);
            case System.Collections.IDictionary idict:
                PropertyDescriptorStore.Seal(idict);
                return RuntimeValue.FromObject(idict);
            default:
                if (args[0].Kind == ValueKind.Object && arg is not null)
                    PropertyDescriptorStore.Seal(arg);
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
            _ when args[0].Kind == ValueKind.Object && arg is not null
                => PropertyDescriptorStore.IsFrozen(arg),
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
            _ when args[0].Kind == ValueKind.Object && arg is not null
                => PropertyDescriptorStore.IsSealed(arg),
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

        if (descriptorArg is null or SharpTSUndefined or string or bool
            or byte or sbyte or short or ushort or int or uint or long or ulong
            or float or double or decimal or SharpTSBigInt or SharpTSSymbol)
        {
            throw new ThrowException(
                new SharpTSTypeError(
                    $"Property description must be an object (got {descriptorArg?.GetType().Name ?? "null"})"));
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
        ValidatePropertyDescriptor(descriptor);

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
                    if (!symObj.DefineProperty(symKey, descriptor))
                    {
                        throw new ThrowException(new SharpTSTypeError(
                            "Cannot redefine symbol property"));
                    }
                    return target;
                case SharpTSArray symArray:
                    if (!symArray.DefineProperty(symKey, descriptor))
                    {
                        throw new ThrowException(new SharpTSTypeError(
                            "Cannot redefine symbol property"));
                    }
                    return target;
                case SharpTSMath symMath:
                    if (!symMath.DefineProperty(symKey, descriptor))
                    {
                        throw new ThrowException(new SharpTSTypeError(
                            "Cannot redefine symbol property"));
                    }
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
                case SharpTSRegExp symRegex:
                    if (isAccessor)
                        symRegex.DefineSymbolAccessor(symKey, descriptor.Get, descriptor.Set);
                    else if (descriptorHasValue)
                        symRegex.SetBySymbol(symKey, descriptor.Value);
                    return target;
                // `Object.defineProperty(Error.prototype, Symbol.toStringTag, …)` and friends.
                case SharpTSClassPrototype symClassProto:
                    if (descriptorHasValue)
                        symClassProto.SetBySymbol(symKey, descriptor.Value);
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
            case SharpTSObjectNamespace objectNamespace:
                success = objectNamespace.DefineProperty(propertyKey, descriptor);
                break;
            case SharpTSInstance inst:
                success = inst.DefineProperty(propertyKey, descriptor);
                break;
            case SharpTSArray arr:
                // Arrays can have properties defined on them
                if (propertyKey == "length" && descriptor.HasValue)
                    descriptor.Value = ArrayBuiltIns.CoerceArrayLength(
                        interpreter, descriptor.Value);
                success = arr.DefineProperty(propertyKey, descriptor);
                break;
            case SharpTSArrayGlobal arrayGlobal:
                success = arrayGlobal.DefineExtraProperty(propertyKey, descriptor);
                break;
            case SharpTSArrayPrototype arrayPrototype:
                success = arrayPrototype.DefineExtraProperty(propertyKey, descriptor);
                break;
            case SharpTSMath math:
                success = math.DefineExtraProperty(propertyKey, descriptor);
                break;
            case SharpTSJSON json:
                success = json.DefineExtraProperty(propertyKey, descriptor);
                break;
            case SharpTSDate date:
                success = date.DefineExtraProperty(propertyKey, descriptor);
                break;
            case SharpTSStringPrototype stringPrototype:
                success = stringPrototype.DefineExtraProperty(propertyKey, descriptor);
                break;
            case SharpTSNumberPrototype numberPrototype:
                success = numberPrototype.DefineExtraProperty(propertyKey, descriptor);
                break;
            case SharpTSBooleanPrototype booleanPrototype:
                success = booleanPrototype.DefineExtraProperty(propertyKey, descriptor);
                break;
            case SharpTSBigIntPrototype bigIntPrototype:
                success = bigIntPrototype.DefineExtraProperty(propertyKey, descriptor);
                break;
            case SharpTSSymbolPrototype symbolPrototype:
                success = symbolPrototype.DefineExtraProperty(propertyKey, descriptor);
                break;
            case SharpTSFunctionPrototype functionPrototype:
                success = functionPrototype.DefineExtraProperty(propertyKey, descriptor);
                break;
            case SharpTSObjectPrototype objectPrototype:
                success = objectPrototype.DefineExtraProperty(propertyKey, descriptor);
                break;
            case SharpTSClassPrototype classPrototype:
                success = classPrototype.DefineExtraProperty(propertyKey, descriptor);
                break;
            case SharpTSPromisePrototype promisePrototype:
                success = promisePrototype.DefineExtraProperty(propertyKey, descriptor);
                break;
            case SharpTSGlobalThis globalThis:
                success = globalThis.DefineProperty(propertyKey, descriptor);
                break;
            case Dictionary<string, object?> dict:
                // Compiled mode: Dictionary<string, object?> for any-typed object literals
                var compiledDesc = CompiledPropertyDescriptor.FromAny(descriptorArg);
                success = PropertyDescriptorStore.DefineProperty(dict, propertyKey, compiledDesc);
                break;
            case SharpTSFunction fn:
                success = fn.DefineProperty(propertyKey, descriptor);
                break;
            case SharpTSArrowFunction arrow:
                success = arrow.DefineProperty(propertyKey, descriptor);
                break;
            case BoundFunction bound:
                success = bound.DefineProperty(propertyKey, descriptor);
                break;
            case SharpTSRegExp rx:
                // RegExp expandos are ordinary descriptor-bearing properties.
                // Reuse SharpTSObject validation so non-configurable properties
                // reject illegal redefinitions and omitted fields are retained.
                success = rx.DefineProperty(propertyKey, descriptor);
                break;
            case SharpTSError error:
                success = error.DefineProperty(propertyKey, descriptor);
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

        // A callable installed as a data-property value retains its identity on
        // ordinary reads (`obj.prop === value`). Invocation still acquires the
        // receiver at the call site; see EvaluateCallCore.
        if (target is SharpTSObject identityObject
            && descriptor.HasValue
            && descriptor.Value is ISharpTSCallable)
        {
            identityObject.PreserveCallableValueIdentityFor(propertyKey);
        }

        return target;
    }

    /// <summary>
    /// Object.getOwnPropertyDescriptor(obj, prop) - returns the property descriptor for an own property.
    /// </summary>
    private static object? GetOwnPropertyDescriptor(Interpreter interpreter, List<object?> args)
    {
        var target = args[0];

        if (target is null or SharpTSUndefined)
            throw new ThrowException(new SharpTSTypeError(
                "Object.getOwnPropertyDescriptor called on null or undefined"));

        // Symbol-keyed lookup goes through the symbol-dict path; the spec keeps
        // symbols distinct from string keys, and SharpTSObject/Instance store
        // them in a separate map.
        if (args[1] is SharpTSSymbol symKey)
        {
            return GetOwnPropertyDescriptorBySymbol(target, symKey);
        }

        // ECMA-262 §7.1.19: ToPropertyKey on the name argument.
        var propertyKey = interpreter.ToPropertyKeyString(args[1]);

        if (target is SharpTSProxy proxy)
        {
            var proxyDescriptor = proxy.TrapGetOwnPropertyDescriptor(propertyKey, interpreter);
            return proxyDescriptor is null or SharpTSUndefined
                ? SharpTSUndefined.Instance
                : proxyDescriptor;
        }

        var descriptor = OwnPropertyDescriptorOf(interpreter, target, propertyKey);

        if (descriptor == null)
        {
            return SharpTSUndefined.Instance;
        }

        // Return as an object
        return descriptor.ToObject();
    }

    /// <summary>
    /// <c>target.[[GetOwnPropertyDescriptor]](propertyKey)</c> for a string key, as a
    /// descriptor record rather than the guest-visible object. Split out of
    /// <c>Object.getOwnPropertyDescriptor</c> so the Annex B accessor lookups
    /// (<c>__lookupGetter__</c> / <c>__lookupSetter__</c>) can walk a prototype chain
    /// without round-tripping each level through a guest object.
    /// </summary>
    internal static SharpTSPropertyDescriptor? OwnPropertyDescriptorOf(
        Interpreter interpreter, object target, string propertyKey)
    {
        return target switch
        {
            SharpTSObject obj => obj.GetOwnPropertyDescriptor(propertyKey),
            SharpTSObjectNamespace objectNamespace
                => objectNamespace.GetOwnPropertyDescriptor(propertyKey)
                    ?? GetBuiltInOwnPropertyDescriptor(interpreter, target, propertyKey),
            SharpTSArrayGlobal arrayGlobal
                => arrayGlobal.GetOwnPropertyDescriptor(propertyKey)
                    ?? GetBuiltInOwnPropertyDescriptor(interpreter, target, propertyKey),
            SharpTSInstance inst => inst.GetOwnPropertyDescriptor(propertyKey),
            SharpTSArray arr => arr.GetOwnPropertyDescriptor(propertyKey),
            SharpTSArrayPrototype arrayPrototype => arrayPrototype.GetOwnPropertyDescriptor(propertyKey)
                ?? GetBuiltInOwnPropertyDescriptor(interpreter, target, propertyKey),
            SharpTSMath math when math.IsBuiltInDeleted(propertyKey) => null,
            SharpTSMath math => math.GetOwnPropertyDescriptor(propertyKey)
                ?? GetBuiltInOwnPropertyDescriptor(interpreter, target, propertyKey),
            SharpTSJSON json when json.IsBuiltInDeleted(propertyKey) => null,
            SharpTSJSON json => json.GetOwnPropertyDescriptor(propertyKey)
                ?? GetBuiltInOwnPropertyDescriptor(interpreter, target, propertyKey),
            SharpTSDate date => date.GetOwnPropertyDescriptor(propertyKey)
                ?? GetBuiltInOwnPropertyDescriptor(interpreter, target, propertyKey),
            SharpTSStringPrototype stringPrototype => stringPrototype.GetOwnPropertyDescriptor(propertyKey)
                ?? GetBuiltInOwnPropertyDescriptor(interpreter, target, propertyKey),
            SharpTSNumberPrototype numberPrototype => numberPrototype.GetOwnPropertyDescriptor(propertyKey)
                ?? GetBuiltInOwnPropertyDescriptor(interpreter, target, propertyKey),
            SharpTSBooleanPrototype booleanPrototype => booleanPrototype.GetOwnPropertyDescriptor(propertyKey)
                ?? GetBuiltInOwnPropertyDescriptor(interpreter, target, propertyKey),
            SharpTSBigIntPrototype bigIntPrototype => bigIntPrototype.GetOwnPropertyDescriptor(propertyKey)
                ?? GetBuiltInOwnPropertyDescriptor(interpreter, target, propertyKey),
            SharpTSSymbolPrototype symbolPrototype => symbolPrototype.GetOwnPropertyDescriptor(propertyKey)
                ?? GetBuiltInOwnPropertyDescriptor(interpreter, target, propertyKey),
            SharpTSFunctionPrototype functionPrototype => functionPrototype.GetOwnPropertyDescriptor(propertyKey)
                ?? GetBuiltInOwnPropertyDescriptor(interpreter, target, propertyKey),
            SharpTSObjectPrototype objectPrototype => objectPrototype.GetOwnPropertyDescriptor(propertyKey)
                ?? GetBuiltInOwnPropertyDescriptor(interpreter, target, propertyKey),
            SharpTSClassPrototype classPrototype => classPrototype.GetOwnPropertyDescriptor(propertyKey)
                ?? GetBuiltInOwnPropertyDescriptor(interpreter, target, propertyKey),
            SharpTSPromisePrototype promisePrototype when !promisePrototype.HasOwnProperty(propertyKey)
                => null,
            SharpTSPromisePrototype promisePrototype
                => promisePrototype.GetOwnPropertyDescriptor(propertyKey)
                    ?? GetBuiltInOwnPropertyDescriptor(interpreter, target, propertyKey),
            SharpTSRegExp regex => regex.GetOwnPropertyDescriptor(propertyKey)
                ?? GetBuiltInOwnPropertyDescriptor(interpreter, target, propertyKey),
            SharpTSError error => error.GetOwnPropertyDescriptor(propertyKey)
                ?? GetBuiltInOwnPropertyDescriptor(interpreter, target, propertyKey),
            SharpTSGlobalThis globalThis => globalThis.GetOwnPropertyDescriptor(propertyKey)
                ?? GetBuiltInOwnPropertyDescriptor(interpreter, target, propertyKey),
            SharpTSClass klass when propertyKey == "prototype" => DataDescriptor(
                klass.Prototype,
                writable: false,
                enumerable: false,
                configurable: false),
            SharpTSBuiltInConstructor { Name: BuiltInNames.RegExp }
                when propertyKey == "prototype" => DataDescriptor(
                    interpreter.GetRegExpPrototype(),
                    writable: false,
                    enumerable: false,
                    configurable: false),
            SharpTSSymbol => null,
            Dictionary<string, object?> dict => GetDictionaryPropertyDescriptor(dict, propertyKey),
            // Function metadata: ECMA-262 §17 — built-in functions expose `name`
            // and `length` as { writable: false, enumerable: false, configurable: true }
            // data properties. test262's verifyProperty() checks introspect these
            // via getOwnPropertyDescriptor; without this branch the descriptor
            // lookup returns null and the assertion fails.
            IBuiltInFunctionMetadata meta when propertyKey is "name" or "length"
                => meta.HasMetadataProperty(propertyKey)
                    ? GetCallableMetaDescriptor((ISharpTSCallable)meta, propertyKey)
                    : null,
            ISharpTSCallable callable when propertyKey is "name" or "length"
                => GetCallableMetaDescriptor(callable, propertyKey),
            SharpTSFunction fn => GetFunctionOwnPropertyDescriptor(fn, propertyKey),
            SharpTSArrowFunction arrow => GetFunctionOwnPropertyDescriptor(arrow, propertyKey),
            BoundFunction bound => bound.GetOwnPropertyDescriptor(propertyKey)
                ?? GetBuiltInOwnPropertyDescriptor(interpreter, target, propertyKey),
            _ => GetBuiltInOwnPropertyDescriptor(interpreter, target, propertyKey)
        };
    }

    /// <summary>
    /// Annex B §B.2.2.2 / §B.2.2.3 — <c>Object.prototype.__defineGetter__</c> and
    /// <c>__defineSetter__</c>. Installs an enumerable, configurable accessor whose
    /// [[Get]]/[[Set]] is <paramref name="fn"/>, which must be callable.
    /// </summary>
    internal static object? DefineAccessorProperty(
        Interpreter interpreter, object? target, object? key, object? fn, bool isGetter)
    {
        string label = isGetter ? "__defineGetter__" : "__defineSetter__";
        if (target is null or SharpTSUndefined)
            throw new ThrowException(new SharpTSTypeError(
                $"Object.prototype.{label} called on null or undefined"));
        if (fn is not ISharpTSCallable)
            throw new ThrowException(new SharpTSTypeError(
                $"Object.prototype.{label}: callback must be callable"));

        // Step 4 runs ToPropertyKey AFTER the IsCallable check, so a throwing
        // toString on the key is only observable once the callback is valid.
        var descriptor = new SharpTSObject([]);
        descriptor.SetProperty(isGetter ? "get" : "set", fn);
        descriptor.SetProperty("enumerable", true);
        descriptor.SetProperty("configurable", true);
        DefineProperty(interpreter, [target, key, descriptor]);
        return SharpTSUndefined.Instance;
    }

    /// <summary>
    /// Annex B §B.2.2.4 / §B.2.2.5 — <c>Object.prototype.__lookupGetter__</c> and
    /// <c>__lookupSetter__</c>. Walks the prototype chain and returns the first own
    /// descriptor found: its [[Get]]/[[Set]] slot when it is an accessor, otherwise
    /// undefined (a shadowing data property hides an inherited accessor).
    /// </summary>
    internal static object? LookupAccessorProperty(
        Interpreter interpreter, object? target, object? key, bool isGetter)
    {
        string label = isGetter ? "__lookupGetter__" : "__lookupSetter__";
        if (target is null or SharpTSUndefined)
            throw new ThrowException(new SharpTSTypeError(
                $"Object.prototype.{label} called on null or undefined"));

        var propertyKey = interpreter.ToPropertyKeyString(key);
        object? current = target;
        // Bounded like the interpreter's other prototype walks, so a cyclic
        // __proto__ can't spin here.
        for (int i = 0; i < 64 && current is not null and not SharpTSUndefined; i++)
        {
            var descriptor = OwnPropertyDescriptorOf(interpreter, current, propertyKey);
            if (descriptor != null)
            {
                var accessor = isGetter ? descriptor.Get : descriptor.Set;
                return accessor ?? (object)SharpTSUndefined.Instance;
            }
            current = PrototypeOf(interpreter, current);
        }
        return SharpTSUndefined.Instance;
    }

    private static SharpTSPropertyDescriptor? GetFunctionOwnPropertyDescriptor(
        SharpTSFunction function,
        string propertyKey)
        => function.GetOwnPropertyDescriptor(propertyKey);

    private static SharpTSPropertyDescriptor? GetFunctionOwnPropertyDescriptor(
        SharpTSArrowFunction function,
        string propertyKey)
        => function.GetOwnPropertyDescriptor(propertyKey);

    /// <summary>
    /// Synthesizes the standard descriptor shape for members exposed by the
    /// interpreter's built-in registry. Methods are writable/configurable and
    /// non-enumerable; constructor prototype properties and numeric constants
    /// are read-only, non-enumerable, and non-configurable.
    /// </summary>
    private static SharpTSPropertyDescriptor? GetBuiltInOwnPropertyDescriptor(
        Interpreter interpreter,
        object target,
        string propertyKey)
    {
        if (target is SharpTSBuiltInConstructor constructor)
        {
            var overlay = interpreter.GetBuiltInConstructorOverlayDescriptor(
                constructor, propertyKey);
            if (overlay is not null)
                return overlay;
            if (!interpreter.HasBuiltInConstructorOwnProperty(constructor, propertyKey))
                return null;
        }

        // Date instances inherit registry-backed methods from Date.prototype;
        // only the intrinsic prototype owns them. Date.prototype uses the same
        // SharpTSDate representation so the marker is required to distinguish
        // the two object roles for own-property introspection.
        if (target is SharpTSDate date)
        {
            if (!date.IsPrototype || date.IsBuiltInDeleted(propertyKey))
                return null;
            var dateMember = DateBuiltIns.GetMember(date, propertyKey);
            if (dateMember is null && propertyKey == "constructor")
                dateMember = interpreter.GetProperty(date, propertyKey);
            return dateMember is null
                ? null
                : DataDescriptor(
                    dateMember,
                    writable: true,
                    enumerable: false,
                    configurable: true);
        }

        bool isKnownBuiltIn = target is ISharpTSCallable
            || target is SharpTSArrayPrototype or SharpTSFunctionPrototype or SharpTSMath
            || BuiltInRegistry.Instance.HasInstanceMembers(target);
        if (!isKnownBuiltIn)
            return null;

        var value = target switch
        {
            SharpTSObjectNamespace objectNamespace => propertyKey == "prototype"
                ? interpreter.GetObjectPrototype()
                : objectNamespace.GetMember(propertyKey),
            SharpTSJSON json => json.GetMember(propertyKey) switch
            {
                BuiltInMethod method => method.Bind(json),
                var member => member,
            },
            SharpTSStringNamespace str => propertyKey == "prototype"
                ? interpreter.GetStringPrototype()
                : str.GetMember(propertyKey),
            SharpTSNumberNamespace num => propertyKey == "prototype"
                ? interpreter.GetNumberPrototype()
                : num.GetMember(propertyKey),
            SharpTSBooleanNamespace boolean => propertyKey == "prototype"
                ? interpreter.GetBooleanPrototype()
                : boolean.GetMember(propertyKey),
            SharpTSArrayGlobal array => propertyKey == "prototype"
                ? interpreter.GetArrayPrototype()
                : array.GetMember(propertyKey),
            _ => interpreter.GetProperty(target, propertyKey),
        };
        if (value is BuiltInMethod { IsConstant: true } constant)
            value = constant.CallBoxed(interpreter, []);
        if (value is null or SharpTSUndefined)
            return null;

        bool isMethod = value is ISharpTSCallable;
        return DataDescriptor(
            value,
            writable: isMethod,
            enumerable: false,
            configurable: isMethod);
    }

    private static SharpTSPropertyDescriptor DataDescriptor(
        object? value,
        bool writable,
        bool enumerable,
        bool configurable) => new()
    {
        Value = value,
        HasValue = true,
        Writable = writable,
        Enumerable = enumerable,
        Configurable = configurable,
    };

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
    /// Returns the complete descriptor for a Symbol-keyed property when the
    /// target tracks one. Plain objects preserve symbol descriptor attributes;
    /// legacy specialized runtime types retain their existing default shape.
    /// </summary>
    private static object? GetOwnPropertyDescriptorBySymbol(object target, SharpTSSymbol key)
    {
        switch (target)
        {
            case SharpTSObject obj when obj.GetOwnPropertyDescriptor(key) is { } descriptor:
                return descriptor.ToObject();
            case SharpTSInstance inst when inst.HasSymbolProperty(key):
                return DescriptorObjectFor(inst.GetBySymbol(key));
            case SharpTSArray array when array.GetOwnPropertyDescriptor(key) is { } descriptor:
                return descriptor.ToObject();
            case SharpTSMath math when math.GetOwnPropertyDescriptor(key) is { } descriptor:
                return descriptor.ToObject();
            default:
                return SharpTSUndefined.Instance;
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

        if (target is null or SharpTSUndefined or string or bool
            or double or int or long or System.Numerics.BigInteger
            or SharpTSBigInt or SharpTSSymbol)
        {
            throw new ThrowException(new SharpTSTypeError(
                "Object.defineProperties called on non-object"));
        }

        if (props is null or SharpTSUndefined)
        {
            throw new Exception("TypeError: Cannot convert undefined or null to object");
        }

        // ToObject on non-null primitives. Boolean/number/bigint/symbol wrappers
        // have no enumerable own properties; the empty string used by the ES5
        // boundary cases likewise contributes no descriptors.
        if (props is bool or double or int or long or System.Numerics.BigInteger
            or SharpTSBigInt or SharpTSSymbol
            || props is string { Length: 0 })
        {
            return target;
        }

        // ECMA-262 §19.1.2.3 ObjectDefineProperties: snapshot own enumerable
        // keys and read every descriptor through Get before applying it. The
        // carrier may be any object kind (function, array/arguments, Math,
        // Date, RegExp, JSON, …), and accessor reads bind `this` to that carrier.
        var entries = OwnEnumerableKeysForDefineProperties(props)
            .Select(key => new KeyValuePair<string, object?>(
                key, ReadDefinePropertiesValue(interpreter, props, key)))
            .ToList();

        foreach (var entry in entries)
        {
            DefineProperty(interpreter, [target, entry.Key, entry.Value]);
        }

        return target;
    }

    private static IEnumerable<string> OwnEnumerableKeysForDefineProperties(object props)
        => props switch
        {
            SharpTSObject obj => OwnEnumerablePropertyKeys(obj),
            SharpTSArray array => array.OwnEnumerableKeys(),
            SharpTSInstance instance => instance.OwnEnumerableKeys(),
            IDictionary<string, object?> dict => dict.Keys,
            SharpTSMath math => math.OwnEnumerableKeys(),
            SharpTSJSON json => json.OwnEnumerableKeys(),
            SharpTSDate date => date.OwnEnumerableKeys(),
            SharpTSRegExp regex => regex.OwnEnumerableKeys(),
            SharpTSError error => error.OwnEnumerableKeys(),
            SharpTSFunction function => function.PropertyKeys,
            SharpTSArrowFunction arrow => arrow.PropertyKeys,
            ISharpTSCallable => [],
            _ => throw new ThrowException(new SharpTSTypeError(
                "Property descriptions must be an object")),
        };

    private static object? ReadDefinePropertiesValue(
        Interpreter interpreter,
        object carrier,
        string key)
    {
        SharpTSPropertyDescriptor? descriptor = carrier switch
        {
            SharpTSObject obj => obj.GetOwnPropertyDescriptor(key),
            SharpTSArray array => array.GetOwnPropertyDescriptor(key),
            SharpTSInstance instance => instance.GetOwnPropertyDescriptor(key),
            SharpTSMath math => math.GetOwnPropertyDescriptor(key),
            SharpTSJSON json => json.GetOwnPropertyDescriptor(key),
            SharpTSDate date => date.GetOwnPropertyDescriptor(key),
            SharpTSRegExp regex => regex.GetOwnPropertyDescriptor(key),
            SharpTSError error => error.GetOwnPropertyDescriptor(key),
            SharpTSFunction function => function.GetOwnPropertyDescriptor(key),
            SharpTSArrowFunction arrow => arrow.GetOwnPropertyDescriptor(key),
            _ => null,
        };

        if (descriptor is { HasGet: true })
        {
            return descriptor.Get is { } getter
                ? FunctionBuiltIns.CallWithThis(interpreter, getter, carrier, [])
                : SharpTSUndefined.Instance;
        }

        return interpreter.GetProperty(carrier, key);
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

        if (target is null or SharpTSUndefined)
            throw new ThrowException(new SharpTSTypeError(
                "Object.getOwnPropertyDescriptors called on null or undefined"));

        // Get all own property names (including non-enumerable ones from defineProperty)
        List<string> names = target switch
        {
            SharpTSObject obj => GetAllOwnPropertyNames(obj),
            SharpTSInstance inst => inst.GetFieldNames().ToList(),
            SharpTSArray arr => GetOwnPropertyNamesFromArray(arr).Select(n => n!.ToString()!).ToList(),
            SharpTSProxy proxy => proxy.TrapOwnKeys(interpreter),
            Dictionary<string, object?> dict => dict.Keys.ToList(),
            _ => []
        };

        var result = new Dictionary<string, object?>();

        foreach (var name in names)
        {
            var descriptor = GetOwnPropertyDescriptor(interpreter, [target, name]);
            if (descriptor is not (null or SharpTSUndefined))
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
        HashSet<string> names = new(obj.Fields.Keys.Where(k => !IsBoxedPrimitiveInternalSlot(k)));
        foreach (var key in obj.AccessorPropertyNames)
        {
            if (!IsBoxedPrimitiveInternalSlot(key)) names.Add(key);
        }
        return names.ToList();
    }

    /// <summary>
    /// Object.getOwnPropertyNames(obj) - returns an array of all own property names (including non-enumerable).
    /// </summary>
    private static object? GetOwnPropertyNames(Interpreter _, List<object?> args)
    {
        var target = args[0];

        if (target is null or SharpTSUndefined)
            throw new ThrowException(new SharpTSTypeError(
                "Object.getOwnPropertyNames called on null or undefined"));

        List<object?> names = target switch
        {
            SharpTSObjectNamespace =>
                new object?[] { "length", "name", "prototype" }
                    .Concat(StaticMemberNames.Cast<object?>()).ToList(),
            SharpTSObject obj => GetOwnPropertyNamesFromObject(obj),
            SharpTSInstance inst => inst.GetFieldNames().Select(k => (object?)k).ToList(),
            SharpTSArray arr => GetOwnPropertyNamesFromArray(arr),
            SharpTSError error => error.OwnPropertyNames.Select(k => (object?)k).ToList(),
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
        HashSet<string> names = new(obj.Fields.Keys.Where(k => !IsBoxedPrimitiveInternalSlot(k)));

        // Add accessor property names (getters define properties even without data)
        foreach (var key in obj.AccessorPropertyNames)
        {
            if (!IsBoxedPrimitiveInternalSlot(key)) names.Add(key);
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
            if (arr.HasIndex(i)) names.Add(i.ToString());
        }

        // Add "length"
        names.Add("length");

        foreach (var name in arr.NamedPropertyNames)
            names.Add(name);

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

        // Object.create's second argument uses the same
        // ObjectDefineProperties algorithm as Object.defineProperties. Reusing
        // it preserves abrupt completion for invalid descriptor values such as
        // an explicit undefined instead of silently treating them as empty
        // descriptors.
        if (args.Count > 1 && propertiesObject is not SharpTSUndefined)
        {
            DefineProperties(interpreter, [result, propertiesObject]);
        }

        return result;
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
        if (interpreter.TryGetDescriptorField(descObj, "writable", out var w))
        {
            descriptor.HasWritable = true;
            descriptor.Writable = Compilation.RuntimeTypes.IsTruthy(w);
        }
        if (interpreter.TryGetDescriptorField(descObj, "enumerable", out var e))
        {
            descriptor.HasEnumerable = true;
            descriptor.Enumerable = Compilation.RuntimeTypes.IsTruthy(e);
        }
        if (interpreter.TryGetDescriptorField(descObj, "configurable", out var c))
        {
            descriptor.HasConfigurable = true;
            descriptor.Configurable = Compilation.RuntimeTypes.IsTruthy(c);
        }
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
            descriptor.Get = g switch
            {
                SharpTSUndefined => null,
                ISharpTSCallable callable => callable,
                _ => throw new ThrowException(
                    new SharpTSTypeError("Getter must be a function or undefined")),
            };
        }
        if (interpreter.TryGetDescriptorField(descObj, "set", out var s))
        {
            descriptor.HasSet = true;
            descriptor.Set = s switch
            {
                SharpTSUndefined => null,
                ISharpTSCallable callable => callable,
                _ => throw new ThrowException(
                    new SharpTSTypeError("Setter must be a function or undefined")),
            };
        }

        // Descriptor fields are ordinary data properties. Reading a callable
        // from `desc.value` / `desc.get` / `desc.set` must return that exact
        // function object; receiver binding belongs to a subsequent call, not
        // the property read. Remember this on interpreter record objects after
        // extraction so identity checks remain stable.
        if (descObj is SharpTSObject identityObject)
        {
            if (descriptor.HasValue && descriptor.Value is ISharpTSCallable)
                identityObject.PreserveCallableValueIdentityFor("value");
            if (descriptor.HasGet && descriptor.Get is not null)
                identityObject.PreserveCallableValueIdentityFor("get");
            if (descriptor.HasSet && descriptor.Set is not null)
                identityObject.PreserveCallableValueIdentityFor("set");
        }
    }

    /// <summary>
    /// ECMA-262 §6.2.5.5 ToPropertyDescriptor rejects descriptors that combine
    /// accessor fields with data-property fields.
    /// </summary>
    private static void ValidatePropertyDescriptor(SharpTSPropertyDescriptor descriptor)
    {
        if ((descriptor.HasGet || descriptor.HasSet) &&
            (descriptor.HasValue || descriptor.HasWritable))
        {
            throw new ThrowException(
                new SharpTSTypeError("Invalid property descriptor: cannot specify accessors and a value or writable attribute"));
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
            case SharpTSFunction function:
                function.PreventExtensions();
                return function;
            case SharpTSArrowFunction arrow:
                arrow.PreventExtensions();
                return arrow;
            case ISharpTSCallable callable:
                PropertyDescriptorStore.PreventExtensions(callable);
                return callable;
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
            SharpTSFunction function => function.IsExtensible,
            SharpTSArrowFunction arrow => arrow.IsExtensible,
            ISharpTSCallable callable => PropertyDescriptorStore.IsExtensible(callable),
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
        if (args[0] is null or SharpTSUndefined)
            throw new ThrowException(new SharpTSTypeError(
                "Object.getOwnPropertySymbols called on null or undefined"));

        List<object?> symbols = args[0] switch
        {
            SharpTSObject obj => obj.GetSymbolPropertyNames().Select(s => (object?)s).ToList(),
            SharpTSInstance inst => inst.GetSymbolPropertyNames().Select(s => (object?)s).ToList(),
            SharpTSArray array => array.GetSymbolPropertyNames().Select(s => (object?)s).ToList(),
            SharpTSMath math => math.GetSymbolPropertyNames().Select(s => (object?)s).ToList(),
            Dictionary<string, object?> dict => PropertyDescriptorStore.GetSymbolKeys(dict)
                                                  .Select(s => (object?)s).ToList(),
            _ => []
        };
        return new SharpTSArray(symbols);
    }

    private static RuntimeValue GetPrototypeOfV2(Interpreter interp, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var target = args.Length > 0 ? args[0].ToObject() : SharpTSUndefined.Instance;
        if (target is null or SharpTSUndefined)
            throw new ThrowException(new SharpTSTypeError(
                "Object.getPrototypeOf called on null or undefined"));

        var proto = PrototypeOf(interp, target);
        return proto != null ? RuntimeValue.FromObject(proto) : RuntimeValue.Null;
    }

    /// <summary>
    /// The [[Prototype]] of <paramref name="target"/>, resolved against
    /// <paramref name="interp"/>'s realm. Shared by <c>Object.getPrototypeOf</c> and
    /// <c>Object.prototype.isPrototypeOf</c> so the two agree — they used to disagree for
    /// arrays, which reported a null prototype and made
    /// <c>Array.prototype.isPrototypeOf([])</c> false.
    /// </summary>
    public static object? PrototypeOf(Interpreter? interp, object? target) => target switch
    {
        // A plain object literal has no explicit [[Prototype]] link but still inherits
        // Object.prototype; only Object.create(null) genuinely has none.
        SharpTSObject { Prototype: null, IsNullPrototype: false } => interp?.GetObjectPrototype(),
        SharpTSObject obj => obj.Prototype,
        // A class instance's [[Prototype]] is its constructor's `prototype` object, so
        // `Object.getPrototypeOf(new C()) === C.prototype`.
        SharpTSInstance inst => inst.RuntimeClass.Prototype,
        // Native errors raised from C# carry only their type name; resolve it back to the
        // same constructor the global `TypeError` identifier yields so the prototypes match.
        SharpTSError err => interp?.GetErrorClass(err.ErrorTypeName).Prototype,
        // ECMA-262 §23.1.3: ordinary Array exotic objects have Array.prototype as their
        // [[Prototype]]. Subclass instances keep their class chain instead.
        SharpTSArraySubclassInstance sub => sub.Klass,
        SharpTSArray => interp?.GetArrayPrototype(),
        SharpTSPromiseSubclassInstance promiseSub => promiseSub.Klass.Prototype,
        SharpTSPromise => interp?.GetPromisePrototype(),
        // ECMA-262 §22.2.6: a RegExp instance's [[Prototype]] is the
        // per-realm RegExp.prototype object, so `Object.getPrototypeOf(/x/)
        // === RegExp.prototype` (the from-regexp-like tests assert this).
        SharpTSRegExp => interp?.GetRegExpPrototype(),
        SharpTSMath => interp?.GetObjectPrototype(),
        SharpTSJSON => interp?.GetObjectPrototype(),
        string => interp?.GetStringPrototype(),
        bool => interp?.GetBooleanPrototype(),
        double or int or long => interp?.GetNumberPrototype(),
        System.Numerics.BigInteger or SharpTSBigInt => interp?.GetBigIntPrototype(),
        Dictionary<string, object?> dict => PropertyDescriptorStore.GetPrototype(dict),
        // ECMA-262 §20.2.3: every function object — built-in constructors included —
        // has Function.prototype as its [[Prototype]], so
        // `Function.prototype.isPrototypeOf(Array)` holds. Function.prototype itself
        // bottoms out at Object.prototype.
        SharpTSFunctionPrototype => interp?.GetObjectPrototype(),
        SharpTSArrayPrototype => interp?.GetObjectPrototype(),
        SharpTSPromisePrototype => interp?.GetObjectPrototype(),
        SharpTSStringPrototype => interp?.GetObjectPrototype(),
        SharpTSClassPrototype classPrototype
            => (object?)classPrototype.Class.Superclass?.Prototype ?? interp?.GetObjectPrototype(),
        // The Number prototype object is ordinary and inherits from this realm's
        // Object.prototype.
        SharpTSNumberPrototype => interp?.GetObjectPrototype(),
        SharpTSBooleanPrototype => interp?.GetObjectPrototype(),
        SharpTSBigIntPrototype => interp?.GetObjectPrototype(),
        SharpTSSymbolPrototype => interp?.GetObjectPrototype(),
        // §10.2.5: a derived constructor's [[Prototype]] is its base constructor, so
        // `Object.getPrototypeOf(RangeError) === Error`. A base class falls back to
        // Function.prototype like any other function object.
        SharpTSClass klass => (object?)klass.Superclass ?? interp?.GetFunctionPrototype(),
        ISharpTSCallable => interp?.GetFunctionPrototype(),
        _ => null
    };

    /// <summary>
    /// Object.setPrototypeOf(obj, proto) - sets the prototype of an object.
    /// </summary>
    private static object? SetPrototypeOf(Interpreter _, List<object?> args)
    {
        var target = args.Count > 0 ? args[0] : SharpTSUndefined.Instance;
        var proto = args.Count > 1 ? args[1] : SharpTSUndefined.Instance;

        if (target is null or SharpTSUndefined)
            throw new ThrowException(new SharpTSTypeError(
                "Object.setPrototypeOf called on null or undefined"));

        switch (target)
        {
            case SharpTSObject obj:
                if (!obj.IsExtensible)
                    throw new Exception("TypeError: Object is not extensible");
                obj.Prototype = proto;
                // An explicit null prototype is distinct from "never linked": the latter
                // still inherits Object.prototype. Record which one this is so
                // Object.getPrototypeOf can tell them apart.
                obj.IsNullPrototype = proto is null or SharpTSUndefined;
                return obj;

            case SharpTSInstance:
                // Cannot change prototype of class instances
                throw new Exception("TypeError: Cannot set prototype of class instance");

            case Dictionary<string, object?> dict:
                if (!PropertyDescriptorStore.IsExtensible(dict))
                    throw new Exception("TypeError: Object is not extensible");
                PropertyDescriptorStore.SetPrototype(dict, proto);
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
    {
        var arg = args[0].ToObject();
        var result = PreventExtensions(interp, CallableInterop.ToBoxedList(args));
        if (args[0].Kind == ValueKind.Object
            && arg is not null
            && arg is not (SharpTSObject or SharpTSInstance or SharpTSArray
                or Dictionary<string, object?> or System.Collections.IDictionary))
        {
            PropertyDescriptorStore.PreventExtensions(arg);
        }
        return RuntimeValue.FromBoxed(result);
    }

    private static RuntimeValue IsExtensibleMethodV2(Interpreter interp, RuntimeValue recv, ReadOnlySpan<RuntimeValue> args)
    {
        var arg = args[0].ToObject();
        if (args[0].Kind == ValueKind.Object
            && arg is not null
            && arg is not (SharpTSObject or SharpTSInstance or SharpTSArray
                or Dictionary<string, object?> or System.Collections.IDictionary))
        {
            return RuntimeValue.FromBoolean(PropertyDescriptorStore.IsExtensible(arg));
        }
        return RuntimeValue.FromBoxed(IsExtensibleMethod(interp, CallableInterop.ToBoxedList(args)));
    }

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
