using SharpTS.Compilation;
using SharpTS.Execution;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides implementations for the Reflect metadata API (reflect-metadata polyfill).
/// Used by decorators for storing and retrieving metadata on classes and class members.
/// </summary>
/// <remarks>
/// Implements the reflect-metadata API:
/// - Reflect.defineMetadata(key, value, target, propertyKey?)
/// - Reflect.getMetadata(key, target, propertyKey?)
/// - Reflect.hasMetadata(key, target, propertyKey?)
/// - Reflect.getMetadataKeys(target, propertyKey?)
/// - Reflect.metadata(key, value) - decorator factory
/// </remarks>
public static class ReflectBuiltIns
{
    /// <summary>
    /// Gets a static method from the Reflect namespace.
    /// </summary>
    public static ISharpTSCallable? GetStaticMethod(string name)
    {
        return name switch
        {
            "defineMetadata" => BuiltInMethod.CreateV2("defineMetadata", 3, 4, static (_, _, args) =>
            {
                var key = args[0].ToObject()?.ToString() ?? "";
                var value = args[1].ToObject();
                var target = args[2].ToObject() ?? throw new Exception("Runtime Error: Reflect.defineMetadata requires a target.");
                var propertyKey = args.Length > 3 ? args[3].ToObject()?.ToString() : null;

                ReflectMetadataStore.Instance.DefineMetadata(key, value, target, propertyKey);
                return RuntimeValue.Null;
            }),

            "getMetadata" => BuiltInMethod.CreateV2("getMetadata", 2, 3, static (_, _, args) =>
            {
                var key = args[0].ToObject()?.ToString() ?? "";
                var target = args[1].ToObject() ?? throw new Exception("Runtime Error: Reflect.getMetadata requires a target.");
                var propertyKey = args.Length > 2 ? args[2].ToObject()?.ToString() : null;

                return RuntimeValue.FromBoxed(ReflectMetadataStore.Instance.GetMetadata(key, target, propertyKey));
            }),

            "getOwnMetadata" => BuiltInMethod.CreateV2("getOwnMetadata", 2, 3, static (_, _, args) =>
            {
                var key = args[0].ToObject()?.ToString() ?? "";
                var target = args[1].ToObject() ?? throw new Exception("Runtime Error: Reflect.getOwnMetadata requires a target.");
                var propertyKey = args.Length > 2 ? args[2].ToObject()?.ToString() : null;

                return RuntimeValue.FromBoxed(ReflectMetadataStore.Instance.GetOwnMetadata(key, target, propertyKey));
            }),

            "hasMetadata" => BuiltInMethod.CreateV2("hasMetadata", 2, 3, static (_, _, args) =>
            {
                var key = args[0].ToObject()?.ToString() ?? "";
                var target = args[1].ToObject() ?? throw new Exception("Runtime Error: Reflect.hasMetadata requires a target.");
                var propertyKey = args.Length > 2 ? args[2].ToObject()?.ToString() : null;

                return RuntimeValue.FromBoolean(ReflectMetadataStore.Instance.HasMetadata(key, target, propertyKey));
            }),

            "hasOwnMetadata" => BuiltInMethod.CreateV2("hasOwnMetadata", 2, 3, static (_, _, args) =>
            {
                var key = args[0].ToObject()?.ToString() ?? "";
                var target = args[1].ToObject() ?? throw new Exception("Runtime Error: Reflect.hasOwnMetadata requires a target.");
                var propertyKey = args.Length > 2 ? args[2].ToObject()?.ToString() : null;

                return RuntimeValue.FromBoolean(ReflectMetadataStore.Instance.HasOwnMetadata(key, target, propertyKey));
            }),

            "getMetadataKeys" => BuiltInMethod.CreateV2("getMetadataKeys", 1, 2, static (_, _, args) =>
            {
                var target = args[0].ToObject() ?? throw new Exception("Runtime Error: Reflect.getMetadataKeys requires a target.");
                var propertyKey = args.Length > 1 ? args[1].ToObject()?.ToString() : null;

                var keys = ReflectMetadataStore.Instance.GetMetadataKeys(target, propertyKey);
                return RuntimeValue.FromObject(new SharpTSArray(keys.Cast<object?>().ToList()));
            }),

            "getOwnMetadataKeys" => BuiltInMethod.CreateV2("getOwnMetadataKeys", 1, 2, static (_, _, args) =>
            {
                var target = args[0].ToObject() ?? throw new Exception("Runtime Error: Reflect.getOwnMetadataKeys requires a target.");
                var propertyKey = args.Length > 1 ? args[1].ToObject()?.ToString() : null;

                var keys = ReflectMetadataStore.Instance.GetOwnMetadataKeys(target, propertyKey);
                return RuntimeValue.FromObject(new SharpTSArray(keys.Cast<object?>().ToList()));
            }),

            "deleteMetadata" => BuiltInMethod.CreateV2("deleteMetadata", 2, 3, static (_, _, args) =>
            {
                var key = args[0].ToObject()?.ToString() ?? "";
                var target = args[1].ToObject() ?? throw new Exception("Runtime Error: Reflect.deleteMetadata requires a target.");
                var propertyKey = args.Length > 2 ? args[2].ToObject()?.ToString() : null;

                return RuntimeValue.FromBoxed(ReflectMetadataStore.Instance.DeleteMetadata(key, target, propertyKey));
            }),

            // Decorator factory: Reflect.metadata(key, value) returns a decorator
            "metadata" => BuiltInMethod.CreateV2("metadata", 2, static (_, _, args) =>
            {
                var key = args[0].ToObject()?.ToString() ?? "";
                var value = args[1].ToObject();

                // Return a decorator function that defines the metadata
                return RuntimeValue.FromObject(new MetadataDecorator(key, value));
            }),

            // --- Standard ES2015 Reflect API ---

            "has" => BuiltInMethod.CreateV2("has", 2, static (_, _, args) =>
            {
                var target = args[0].ToObject() ?? throw new Exception("Runtime Error: Reflect.has requires a target object.");
                var propertyKey = args[1].ToObject()?.ToString() ?? "";
                return RuntimeValue.FromBoolean(target switch
                {
                    SharpTSObject obj => obj.HasProperty(propertyKey),
                    SharpTSInstance inst => inst.HasProperty(propertyKey),
                    SharpTSArray arr => propertyKey == "length"
                        || (int.TryParse(propertyKey, out var idx) && idx >= 0 && idx < arr.Length),
                    Dictionary<string, object?> dict => dict.ContainsKey(propertyKey),
                    _ => false
                });
            }),

            "deleteProperty" => BuiltInMethod.CreateV2("deleteProperty", 2, static (_, _, args) =>
            {
                var target = args[0].ToObject() ?? throw new Exception("Runtime Error: Reflect.deleteProperty requires a target object.");
                var propertyKey = args[1].ToObject()?.ToString() ?? "";
                switch (target)
                {
                    case SharpTSObject obj:
                        if ((obj.IsFrozen || obj.IsSealed) && obj.HasProperty(propertyKey))
                            return RuntimeValue.False;
                        obj.DeleteProperty(propertyKey);
                        return RuntimeValue.True;
                    case SharpTSInstance inst:
                        if ((inst.IsFrozen || inst.IsSealed) && inst.HasField(propertyKey))
                            return RuntimeValue.False;
                        inst.DeleteField(propertyKey);
                        return RuntimeValue.True;
                    case Dictionary<string, object?> dict:
                        if ((PropertyDescriptorStore.IsFrozen(dict) || PropertyDescriptorStore.IsSealed(dict))
                            && dict.ContainsKey(propertyKey))
                            return RuntimeValue.False;
                        dict.Remove(propertyKey);
                        return RuntimeValue.True;
                    default:
                        return RuntimeValue.True;
                }
            }),

            "get" => BuiltInMethod.CreateV2("get", 2, 3, static (_, _, args) =>
            {
                var target = args[0].ToObject() ?? throw new Exception("Runtime Error: Reflect.get requires a target object.");
                var propertyKey = args[1].ToObject()?.ToString() ?? "";
                return RuntimeValue.FromBoxed(target switch
                {
                    SharpTSObject obj => obj.GetProperty(propertyKey),
                    SharpTSInstance inst => inst.GetRawField(propertyKey),
                    Dictionary<string, object?> dict => dict.TryGetValue(propertyKey, out var v) ? v : null,
                    _ => null
                });
            }),

            "set" => BuiltInMethod.CreateV2("set", 3, 4, static (_, _, args) =>
            {
                var target = args[0].ToObject() ?? throw new Exception("Runtime Error: Reflect.set requires a target object.");
                var propertyKey = args[1].ToObject()?.ToString() ?? "";
                var value = args[2].ToObject();
                try
                {
                    switch (target)
                    {
                        case SharpTSObject obj:
                            if (obj.IsFrozen) return RuntimeValue.False;
                            obj.SetProperty(propertyKey, value);
                            return RuntimeValue.True;
                        case SharpTSInstance inst:
                            if (inst.IsFrozen) return RuntimeValue.False;
                            inst.SetRawField(propertyKey, value);
                            return RuntimeValue.True;
                        case Dictionary<string, object?> dict:
                            if (PropertyDescriptorStore.IsFrozen(dict)) return RuntimeValue.False;
                            dict[propertyKey] = value;
                            return RuntimeValue.True;
                        default:
                            return RuntimeValue.False;
                    }
                }
                catch
                {
                    return RuntimeValue.False;
                }
            }),

            "getPrototypeOf" => BuiltInMethod.CreateV2("getPrototypeOf", 1, static (_, _, args) =>
            {
                var target = args[0].ToObject() ?? throw new Exception("Runtime Error: Reflect.getPrototypeOf requires a target object.");
                return RuntimeValue.FromBoxed(target switch
                {
                    SharpTSObject obj => obj.Prototype,
                    SharpTSInstance inst => inst.Prototype,
                    Dictionary<string, object?> dict => PropertyDescriptorStore.GetPrototype(dict),
                    _ => null
                });
            }),

            "setPrototypeOf" => BuiltInMethod.CreateV2("setPrototypeOf", 2, static (_, _, args) =>
            {
                var target = args[0].ToObject() ?? throw new Exception("Runtime Error: Reflect.setPrototypeOf requires a target object.");
                var proto = args[1].ToObject();
                try
                {
                    switch (target)
                    {
                        case SharpTSObjectPrototype:
                            // %Object.prototype% is an immutable-prototype exotic:
                            // setting its existing null prototype succeeds, any
                            // different prototype is rejected.
                            return RuntimeValue.FromBoolean(proto is null);
                        case SharpTSObject obj:
                            if (!obj.IsExtensible) return RuntimeValue.False;
                            obj.Prototype = proto;
                            return RuntimeValue.True;
                        case Dictionary<string, object?> dict:
                            if (!PropertyDescriptorStore.IsExtensible(dict)) return RuntimeValue.False;
                            PropertyDescriptorStore.SetPrototype(dict, proto);
                            return RuntimeValue.True;
                        default:
                            return RuntimeValue.False;
                    }
                }
                catch
                {
                    return RuntimeValue.False;
                }
            }),

            "isExtensible" => BuiltInMethod.CreateV2("isExtensible", 1, static (_, _, args) =>
            {
                var target = args[0].ToObject() ?? throw new Exception("Runtime Error: Reflect.isExtensible requires a target object.");
                return RuntimeValue.FromBoolean(target switch
                {
                    SharpTSObject obj => obj.IsExtensible,
                    SharpTSInstance inst => inst.IsExtensible,
                    SharpTSArray arr => arr.IsExtensible,
                    ISharpTSCallable callable => PropertyDescriptorStore.IsExtensible(callable),
                    Dictionary<string, object?> dict => PropertyDescriptorStore.IsExtensible(dict),
                    _ => false
                });
            }),

            "preventExtensions" => BuiltInMethod.CreateV2("preventExtensions", 1, static (_, _, args) =>
            {
                var target = args[0].ToObject() ?? throw new Exception("Runtime Error: Reflect.preventExtensions requires a target object.");
                switch (target)
                {
                    case SharpTSObject obj:
                        obj.PreventExtensions();
                        break;
                    case SharpTSInstance inst:
                        inst.PreventExtensions();
                        break;
                    case SharpTSArray arr:
                        arr.PreventExtensions();
                        break;
                    case ISharpTSCallable callable:
                        PropertyDescriptorStore.PreventExtensions(callable);
                        break;
                    case Dictionary<string, object?> dict:
                        PropertyDescriptorStore.PreventExtensions(dict);
                        break;
                }
                return RuntimeValue.True;
            }),

            "getOwnPropertyDescriptor" => BuiltInMethod.CreateV2("getOwnPropertyDescriptor", 2, static (interpreter, _, args) =>
            {
                var target = args[0].ToObject() ?? throw new Exception("Runtime Error: Reflect.getOwnPropertyDescriptor requires a target object.");
                var propertyKey = args[1].ToObject()?.ToString() ?? "";
                if (target is SharpTSProxy proxy)
                {
                    var proxyDescriptor = proxy.TrapGetOwnPropertyDescriptor(propertyKey, interpreter);
                    return proxyDescriptor is null or SharpTSUndefined
                        ? RuntimeValue.Undefined
                        : RuntimeValue.FromBoxed(proxyDescriptor);
                }
                // Delegate to ObjectBuiltIns which handles all target types
                var descriptor = ObjectBuiltIns.RuntimeGetOwnPropertyDescriptor(target, propertyKey);
                return descriptor is null
                    ? RuntimeValue.Undefined
                    : RuntimeValue.FromBoxed(descriptor);
            }),

            "defineProperty" => BuiltInMethod.CreateV2("defineProperty", 3, static (interpreter, _, args) =>
            {
                var target = args[0].ToObject() ?? throw new Exception("Runtime Error: Reflect.defineProperty requires a target object.");
                var propertyKey = args[1].ToObject()?.ToString() ?? "";
                var descriptorArg = args[2].ToObject() ?? throw new Exception("TypeError: Property description must be an object");
                try
                {
                    SharpTSPropertyDescriptor descriptor = SharpTSPropertyDescriptor.FromAnyObject(descriptorArg);
                    if (target is SharpTSArray
                        && propertyKey == "length"
                        && descriptor.HasValue)
                    {
                        descriptor.Value = ArrayBuiltIns.CoerceArrayLength(
                            interpreter, descriptor.Value);
                    }
                    switch (target)
                    {
                        case SharpTSObject obj:
                            return RuntimeValue.FromBoolean(obj.DefineProperty(propertyKey, descriptor));
                        case SharpTSInstance inst:
                            return RuntimeValue.FromBoolean(inst.DefineProperty(propertyKey, descriptor));
                        case SharpTSArray arr:
                            return RuntimeValue.FromBoolean(arr.DefineProperty(propertyKey, descriptor));
                        default:
                            return RuntimeValue.False;
                    }
                }
                catch
                {
                    return RuntimeValue.False;
                }
            }),

            "ownKeys" => BuiltInMethod.CreateV2("ownKeys", 1, static (interpreter, _, args) =>
            {
                var target = args[0].ToObject() ?? throw new Exception("Runtime Error: Reflect.ownKeys requires a target object.");
                List<object?> keys = [];
                switch (target)
                {
                    case SharpTSProxy proxy:
                        keys.AddRange(proxy.TrapOwnKeys(interpreter).Select(k => (object?)k));
                        break;
                    case SharpTSObject obj:
                        keys.AddRange(obj.Fields.Keys.Select(k => (object?)k));
                        keys.AddRange(obj.GetSymbolPropertyNames().Select(s => (object?)s));
                        break;
                    case SharpTSInstance inst:
                        keys.AddRange(inst.GetFieldNames().Select(k => (object?)k));
                        keys.AddRange(inst.GetSymbolPropertyNames().Select(s => (object?)s));
                        break;
                    case Dictionary<string, object?> dict:
                        keys.AddRange(dict.Keys.Select(k => (object?)k));
                        keys.AddRange(PropertyDescriptorStore.GetSymbolKeys(dict).Select(s => (object?)s));
                        break;
                }
                return RuntimeValue.FromObject(new SharpTSArray(keys));
            }),

            "apply" => BuiltInMethod.CreateV2("apply", 3, static (interpreter, _, args) =>
            {
                var target = args[0].ToObject() as ISharpTSCallable
                    ?? throw new Exception("Runtime Error: Reflect.apply requires a function target.");
                var thisArg = args[1].ToObject();
                var argsList = args[2].ToObject();

                List<object?> callArgs;
                if (argsList == null)
                {
                    callArgs = [];
                }
                else if (argsList is SharpTSArray tsArray)
                {
                    callArgs = new List<object?>(tsArray);
                }
                else if (argsList is List<object?> list)
                {
                    callArgs = new List<object?>(list);
                }
                else
                {
                    throw new Exception("Runtime Error: Reflect.apply third argument must be an array-like object.");
                }

                // Invoke with this binding
                if (target is SharpTSFunction fn)
                {
                    var bound = new BoundFunction(fn, thisArg, []);
                    return RuntimeValue.FromBoxed(bound.Call(interpreter, callArgs));
                }
                if (target is SharpTSArrowFunction arrow && arrow.HasOwnThis)
                {
                    var bound = arrow.Bind(thisArg!);
                    return RuntimeValue.FromBoxed(bound.Call(interpreter, callArgs));
                }
                return RuntimeValue.FromBoxed(target.Call(interpreter, callArgs));
            }),

            "construct" => BuiltInMethod.CreateV2("construct", 2, 3, static (interpreter, _, args) =>
            {
                var target = args[0].ToObject() ?? throw new ThrowException(new SharpTSTypeError(
                    "Reflect.construct called on non-constructor"));
                var argsList = args[1].ToObject();

                // ECMA-262 28.1.2: validate newTarget is a constructor when
                // explicitly supplied. Test262's `isConstructor(f)` harness
                // helper relies on this — it calls
                // `Reflect.construct(function(){}, [], f)` and checks for a
                // throw when f isn't constructable.
                if (args.Length > 2 && args[2].ToObject() is { } newTarget)
                {
                    if (IsNotConstructor(newTarget))
                        throw new ThrowException(new SharpTSTypeError(
                            "Reflect.construct newTarget is not a constructor"));
                }

                List<object?> callArgs;
                if (argsList == null)
                {
                    callArgs = [];
                }
                else if (argsList is SharpTSArray tsArray)
                {
                    callArgs = new List<object?>(tsArray);
                }
                else if (argsList is List<object?> list)
                {
                    callArgs = new List<object?>(list);
                }
                else
                {
                    throw new ThrowException(new SharpTSTypeError(
                        "Reflect.construct second argument must be an array-like object"));
                }

                if (IsNotConstructor(target))
                    throw new ThrowException(new SharpTSTypeError(
                        "Reflect.construct target is not a constructor"));

                if (target is SharpTSClass cls)
                {
                    return RuntimeValue.FromBoxed(cls.Call(interpreter, callArgs));
                }
                if (target is ISharpTSCallable callable)
                {
                    return RuntimeValue.FromBoxed(callable.Call(interpreter, callArgs));
                }
                throw new ThrowException(new SharpTSTypeError(
                    "Reflect.construct target is not a constructor"));
            }),

            _ => null
        };
    }

    /// <summary>
    /// True when <paramref name="value"/> isn't a constructor — used by
    /// Reflect.construct to validate target/newTarget per ECMA-262, and by
    /// Test262's <c>isConstructor(f)</c> harness helper which calls
    /// <c>Reflect.construct(function(){}, [], f)</c> and treats a throw as
    /// "not a constructor".
    /// </summary>
    private static bool IsNotConstructor(object? value)
    {
        // Direct rejections — methods/wrappers that are clearly callable but
        // aren't constructors per spec.
        if (value is SharpTS.Runtime.Types.ArrayPrototypeMethodWrapper
            or SharpTS.Runtime.Types.StringPrototypeMethodWrapper
            or SharpTS.Runtime.Types.NumberPrototypeMethodWrapper
            or SharpTS.Runtime.Types.BooleanPrototypeMethodWrapper
            or SharpTS.Runtime.Types.SymbolPrototypeMethodWrapper
            or SharpTS.Runtime.Types.BigIntPrototypeMethodWrapper
            or SharpTS.Runtime.Types.SharpTSGlobalFunction
            or SharpTS.Runtime.Types.SharpTSObjectUnboundMethod
            or SharpTS.Runtime.Types.SharpTSArrayUnboundMethod
            or SharpTS.Runtime.Types.ErrorToStringCallable
            or BuiltInAsyncMethod
            or BoundFunction
            or BuiltInMethod { IsConstructor: false })
            return true;
        // Anything else that's not callable can't be a constructor.
        return value is not ISharpTSCallable;
    }

    /// <summary>
    /// A decorator that defines metadata on the target.
    /// Used with Reflect.metadata(key, value) factory.
    /// </summary>
    private class MetadataDecorator(string key, object? value) : ISharpTSCallable
    {
        public int Arity() => 2; // For Stage 3: (value, context), or Legacy: (target, propertyKey?, descriptor?)

        public object? Call(Interpreter interpreter, List<object?> arguments)
        {
            // This decorator just defines metadata on the target
            // Works for both Legacy and Stage 3 decorators
            if (arguments.Count >= 1 && arguments[0] != null)
            {
                var target = arguments[0]!;
                string? propertyKey = null;

                // For method/property decorators, second arg is property key (Legacy)
                // or context object (Stage 3)
                if (arguments.Count >= 2 && arguments[1] is string propKey)
                {
                    propertyKey = propKey;
                }
                else if (arguments.Count >= 2 && arguments[1] is SharpTSObject context)
                {
                    // Stage 3 context object has 'name' property
                    var name = context.GetProperty("name");
                    if (name is string contextName)
                    {
                        propertyKey = contextName;
                    }
                }

                ReflectMetadataStore.Instance.DefineMetadata(key, value, target, propertyKey);
            }

            // Return undefined (decorators that don't modify return void/undefined)
            return null;
        }
    }
}
