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

            "has" => BuiltInMethod.CreateV2("has", 2, static (interpreter, _, args) =>
            {
                var target = args[0].ToObject() ?? throw new Exception("Runtime Error: Reflect.has requires a target object.");
                if (target is ISharpTSOwnPropertyObject)
                {
                    var ownKey = interpreter.ToPropertyKey(args[1].ToObject());
                    return RuntimeValue.FromBoolean(ownKey is SharpTSSymbol ownSymbol
                        ? interpreter.HasSymbolProperty(target, ownSymbol)
                        : interpreter.HasProperty(target, (string)ownKey));
                }
                var propertyKey = args[1].ToObject();
                return RuntimeValue.FromBoolean(propertyKey is SharpTSSymbol symbol
                    ? interpreter.HasSymbolProperty(target, symbol)
                    : interpreter.HasProperty(target, propertyKey?.ToString() ?? ""));
            }),

            "deleteProperty" => BuiltInMethod.CreateV2("deleteProperty", 2, static (interpreter, _, args) =>
            {
                if (!args[0].IsObject)
                    throw new ThrowException(new SharpTSTypeError("Reflect.deleteProperty requires a target object"));
                var target = args[0].ToObject()!;
                var key = interpreter.ToPropertyKey(args[1].ToObject());
                if (target is ISharpTSOwnPropertyObject carrier)
                {
                    return RuntimeValue.FromBoolean(key is SharpTSSymbol ownSymbol
                        ? carrier.OwnProperties.GetOwnPropertyDescriptor(ownSymbol) is null
                            || carrier.OwnProperties.DeleteBySymbolStrict(ownSymbol, false)
                        : carrier.OwnProperties.GetOwnPropertyDescriptor((string)key) is null
                            || carrier.OwnProperties.DeletePropertyStrict((string)key, false));
                }
                if (target is SharpTSObjectNamespace objectNamespace)
                {
                    // This namespace has no Symbol storage. Absent Symbols must
                    // remain distinct from same-spelled string properties.
                    return RuntimeValue.FromBoolean(key is SharpTSSymbol
                        || objectNamespace.DeleteProperty((string)key));
                }
                if (key is SharpTSSymbol symbol)
                    return RuntimeValue.FromBoolean(interpreter.DeleteSymbolProperty(target, symbol, strictMode: false));
                var propertyKey = (string)key;
                switch (target)
                {
                    case SharpTSProxy proxy:
                        return RuntimeValue.FromBoolean(
                            proxy.TrapDeleteProperty(propertyKey, interpreter));
                    case SharpTSObject obj:
                        return RuntimeValue.FromBoolean(
                            obj.DeleteProperty(propertyKey));
                    case ISharpTSFunctionObject function:
                        var properties = FunctionObjectProperties.GetProperties(function, interpreter);
                        return RuntimeValue.FromBoolean(properties.GetOwnPropertyDescriptor(propertyKey) is null
                            || properties.DeletePropertyStrict(propertyKey, false));
                    case BoundFunction bound:
                        return RuntimeValue.FromBoolean(bound.DeleteProperty(propertyKey, false));
                    case SharpTSFunction ordinaryFunction:
                        return RuntimeValue.FromBoolean(ordinaryFunction.GetOwnPropertyDescriptor(propertyKey) is null
                            || ordinaryFunction.DeleteProperty(propertyKey));
                    case SharpTSArrowFunction arrowFunction:
                        return RuntimeValue.FromBoolean(arrowFunction.GetOwnPropertyDescriptor(propertyKey) is null
                            || arrowFunction.DeleteProperty(propertyKey));
                    case SharpTSInstance inst:
                        if ((inst.IsFrozen || inst.IsSealed) && inst.HasField(propertyKey))
                            return RuntimeValue.False;
                        return RuntimeValue.FromBoolean(inst.DeleteField(propertyKey) || inst.GetOwnPropertyDescriptor(propertyKey) is null);
                    case SharpTSPromise promise:
                        return RuntimeValue.FromBoolean(promise.DeletePropertyStrict(propertyKey, false));
                    case SharpTSClassPrototype prototype:
                        return RuntimeValue.FromBoolean(prototype.DeleteProperty(propertyKey));
                    case SharpTSClass klass:
                        return RuntimeValue.FromBoolean(klass.DeleteStaticProperty(propertyKey));
                    case ISharpTSPrimitiveConstructor primitiveConstructor:
                        return RuntimeValue.FromBoolean(primitiveConstructor.DeleteProperty(propertyKey));
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

            "get" => BuiltInMethod.CreateV2("get", 2, 3, static (interpreter, _, args) =>
            {
                if (!args[0].IsObject)
                    throw new ThrowException(new SharpTSTypeError("Reflect.get requires an object target"));
                var target = args[0].ToObject()!;
                var propertyKey = interpreter.ToPropertyKey(args[1].ToObject());
                if (target is ISharpTSOwnPropertyObject)
                {
                    object? ownReceiver = args.Length > 2 ? args[2].ToObject() : target;
                    return RuntimeValue.FromBoxed(propertyKey is SharpTSSymbol ownSymbol
                        ? interpreter.GetSymbolPropertyValue(target, ownSymbol, ownReceiver!)
                        : interpreter.GetPropertyValue(target, (string)propertyKey, ownReceiver));
                }
                if (target is SharpTSFunctionPrototype functionPrototype && propertyKey is SharpTSSymbol prototypeSymbol)
                {
                    object? prototypeReceiver = args.Length > 2 ? args[2].ToObject() : target;
                    return RuntimeValue.FromBoxed(interpreter.GetSymbolPropertyValue(
                        functionPrototype, prototypeSymbol, prototypeReceiver!));
                }
                if (target is ISharpTSFunctionObject function)
                {
                    object? functionReceiver = args.Length > 2 ? args[2].ToObject() : target;
                    return RuntimeValue.FromBoxed(propertyKey is SharpTSSymbol symbol
                        ? interpreter.GetSymbolPropertyValue(function, symbol, functionReceiver!)
                        : interpreter.GetPropertyValue(function,
                            (string)propertyKey, functionReceiver));
                }
                if (target is SharpTSFunction or SharpTSArrowFunction or BoundFunction or SharpTSClassPrototype)
                {
                    object? functionReceiver = args.Length > 2 ? args[2].ToObject() : target;
                    return RuntimeValue.FromBoxed(propertyKey is SharpTSSymbol symbol
                        ? interpreter.GetSymbolPropertyValue(target, symbol, functionReceiver!)
                        : interpreter.GetPropertyValue(target,
                            (string)propertyKey, functionReceiver));
                }
                if (target is SharpTSObjectNamespace objectNamespace && propertyKey is not SharpTSSymbol)
                {
                    object? objectReceiver = args.Length > 2 ? args[2].ToObject() : target;
                    return RuntimeValue.FromBoxed(interpreter.GetPropertyValue(objectNamespace,
                        (string)propertyKey, objectReceiver));
                }
                if (target is ISharpTSPrimitiveConstructor primitiveConstructor)
                {
                    object? primitiveReceiver = args.Length > 2 ? args[2].ToObject() : target;
                    return RuntimeValue.FromBoxed(propertyKey is SharpTSSymbol symbol
                        ? interpreter.GetPrimitiveConstructorSymbolValue(primitiveConstructor, symbol, primitiveReceiver)
                        : interpreter.GetPropertyValue(primitiveConstructor,
                            (string)propertyKey, primitiveReceiver));
                }
                object? receiver = args.Length > 2 ? args[2].ToObject() : target;
                if (propertyKey is SharpTSSymbol propertySymbol)
                    return RuntimeValue.FromBoxed(
                        interpreter.GetSymbolPropertyValue(target, propertySymbol, receiver!));
                return RuntimeValue.FromBoxed(
                    interpreter.GetPropertyValue(target, (string)propertyKey, receiver));
            }),

            "set" => BuiltInMethod.CreateV2("set", 3, 4, static (interpreter, unusedReceiver, args) =>
            {
                if (!args[0].IsObject)
                    throw new ThrowException(new SharpTSTypeError("Reflect.set requires an object target"));
                var target = args[0].ToObject()!;
                var propertyKey = interpreter.ToPropertyKey(args[1].ToObject());
                if (target is ISharpTSOwnPropertyObject)
                {
                    object? ownReceiver = args.Length > 3 ? args[3].ToObject() : target;
                    return RuntimeValue.FromBoolean(SharpTSProxy.OrdinarySet(
                        interpreter, target, propertyKey, args[2].ToObject(), ownReceiver));
                }
                if (target is SharpTSFunctionPrototype)
                {
                    object? prototypeReceiver = args.Length > 3 ? args[3].ToObject() : target;
                    return RuntimeValue.FromBoolean(SharpTSProxy.OrdinarySet(interpreter,
                        target, propertyKey, args[2].ToObject(), prototypeReceiver));
                }
                if (target is ISharpTSFunctionObject function)
                {
                    object? functionReceiver = args.Length > 3 ? args[3].ToObject() : target;
                    return RuntimeValue.FromBoolean(propertyKey is SharpTSSymbol symbol
                        ? SetSymbolProperty(interpreter, FunctionObjectProperties.GetProperties(function, interpreter).GetOwnPropertyDescriptor(symbol)
                            ?? interpreter.FindInheritedSymbolDescriptor(function, symbol),
                            symbol, args[2].ToObject(), functionReceiver)
                        : SharpTSProxy.OrdinarySet(interpreter, function,
                            (string)propertyKey, args[2].ToObject(), functionReceiver));
                }
                if (target is ISharpTSPrimitiveConstructor primitiveConstructor)
                {
                    object? primitiveReceiver = args.Length > 3 ? args[3].ToObject() : target;
                    return RuntimeValue.FromBoolean(propertyKey is SharpTSSymbol symbol
                        ? SetSymbolProperty(interpreter, primitiveConstructor.GetOwnPropertyDescriptor(symbol)
                            ?? interpreter.FindInheritedSymbolDescriptor(primitiveConstructor, symbol),
                            symbol, args[2].ToObject(), primitiveReceiver)
                        : SharpTSProxy.OrdinarySet(interpreter, primitiveConstructor,
                            (string)propertyKey, args[2].ToObject(), primitiveReceiver));
                }
                if (propertyKey is SharpTSSymbol inheritedSymbol)
                {
                    if (FunctionObjectProperties.TryGetStorage(target, interpreter, out var properties))
                    {
                        object? functionReceiver = args.Length > 3 ? args[3].ToObject() : target;
                        return RuntimeValue.FromBoolean(SetSymbolProperty(interpreter,
                            properties.GetOwnPropertyDescriptor(inheritedSymbol)
                                ?? interpreter.FindInheritedSymbolDescriptor(target, inheritedSymbol),
                            inheritedSymbol, args[2].ToObject(), functionReceiver));
                    }
                    bool ownMiss = target switch
                    {
                        SharpTSClass klass => klass.GetOwnStaticSymbolPropertyDescriptor(inheritedSymbol) is null,
                        _ => false
                    };
                    if (ownMiss && interpreter.FindInheritedSymbolDescriptor(target, inheritedSymbol) is { } inherited
                        && (inherited.HasGet || inherited.HasSet || !inherited.Writable))
                    {
                        object? inheritedReceiver = args.Length > 3 ? args[3].ToObject() : target;
                        return RuntimeValue.FromBoolean(SetSymbolProperty(interpreter, inherited,
                            inheritedSymbol, args[2].ToObject(), inheritedReceiver));
                    }
                }
                var value = args[2].ToObject();
                object? receiver = args.Length > 3 ? args[3].ToObject() : target;
                if (target is SharpTSProxy proxy)
                    return RuntimeValue.FromBoolean(proxy.TrapSetProperty(
                        propertyKey, value, interpreter, receiver));
                return RuntimeValue.FromBoolean(SharpTSProxy.OrdinarySet(
                    interpreter, target, propertyKey, value, receiver));
            }),

            "getPrototypeOf" => BuiltInMethod.CreateV2("getPrototypeOf", 1, static (interpreter, _, args) =>
            {
                var target = args[0].ToObject() ?? throw new Exception("Runtime Error: Reflect.getPrototypeOf requires a target object.");
                if (FunctionObjectProperties.TryGetStorage(target, interpreter, out var functionStorage))
                    return RuntimeValue.FromBoxed(ObjectBuiltIns.PrototypeOf(interpreter, target));
                if (target is SharpTSProxy or ISharpTSGeneratorFunctionValue or SharpTSGenerator or SharpTSAsyncGenerator
                    or SharpTSClassPrototype or SharpTSArrayGlobal or SharpTSClass or SharpTSFunctionPrototype)
                    return RuntimeValue.FromBoxed(ObjectBuiltIns.PrototypeOf(interpreter, target));
                return RuntimeValue.FromBoxed(target switch
                {
                    // Null storage also represents the ordinary implicit default;
                    // the realm-aware operation distinguishes explicit null roots.
                    SharpTSObject obj => ObjectBuiltIns.PrototypeOf(interpreter, obj),
                    SharpTSInstance inst => inst.Prototype,
                    Dictionary<string, object?> dict => PropertyDescriptorStore.GetPrototype(dict),
                    _ => null
                });
            }),

            "setPrototypeOf" => BuiltInMethod.CreateV2("setPrototypeOf", 2, static (interpreter, _, args) =>
            {
                var target = args[0].ToObject() ?? throw new Exception("Runtime Error: Reflect.setPrototypeOf requires a target object.");
                var proto = args[1].ToObject();
                if (proto is SharpTSUndefined or string or bool or double or int or long
                    or float or decimal or SharpTSSymbol or SharpTSBigInt
                    or System.Numerics.BigInteger)
                    throw new ThrowException(new SharpTSTypeError(
                        "Reflect.setPrototypeOf prototype must be an object or null"));

                return RuntimeValue.FromBoolean(
                    ObjectBuiltIns.SetPrototypeOfTarget(interpreter, target, proto));
            }),

            "isExtensible" => BuiltInMethod.CreateV2("isExtensible", 1, static (interpreter, _, args) =>
            {
                var target = args[0].ToObject() ?? throw new Exception("Runtime Error: Reflect.isExtensible requires a target object.");
                if (target is ISharpTSOwnPropertyObject carrier)
                    return RuntimeValue.FromBoolean(carrier.OwnProperties.IsExtensible);
                if (FunctionObjectProperties.TryGetStorage(target, interpreter, out var properties))
                    return RuntimeValue.FromBoolean(properties.IsExtensible);
                return RuntimeValue.FromBoolean(target switch
                {
                    SharpTSProxy proxy => proxy.TrapIsExtensible(interpreter),
                    SharpTSObject obj => obj.IsExtensible,
                    SharpTSInstance inst => inst.IsExtensible,
                    SharpTSArray arr => arr.IsExtensible,
                    SharpTSClassPrototype prototype => PropertyDescriptorStore.IsExtensible(prototype),
                    ISharpTSCallable callable => PropertyDescriptorStore.IsExtensible(callable),
                    Dictionary<string, object?> dict => PropertyDescriptorStore.IsExtensible(dict),
                    _ => false
                });
            }),

            "preventExtensions" => BuiltInMethod.CreateV2("preventExtensions", 1, static (interpreter, _, args) =>
            {
                var target = args[0].ToObject() ?? throw new Exception("Runtime Error: Reflect.preventExtensions requires a target object.");
                if (target is ISharpTSOwnPropertyObject carrier)
                {
                    carrier.OwnProperties.PreventExtensions();
                    return RuntimeValue.True;
                }
                if (target is SharpTSProxy proxy)
                    return RuntimeValue.FromBoolean(proxy.TrapPreventExtensions(interpreter));
                if (FunctionObjectProperties.TryGetStorage(target, interpreter, out var properties))
                {
                    properties.PreventExtensions();
                    PropertyDescriptorStore.PreventExtensions(target);
                    return RuntimeValue.True;
                }
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
                    case SharpTSClassPrototype prototype:
                        PropertyDescriptorStore.PreventExtensions(prototype);
                        break;
                    case SharpTSFunctionPrototype functionPrototype:
                        functionPrototype.PreventExtensionsOwnProperties();
                        PropertyDescriptorStore.PreventExtensions(functionPrototype);
                        return RuntimeValue.True;
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
                var propertyKey = interpreter.ToPropertyKey(args[1].ToObject());
                if (target is ISharpTSOwnPropertyObject carrier)
                {
                    var ownDescriptor = propertyKey is SharpTSSymbol ownSymbol
                        ? carrier.OwnProperties.GetOwnPropertyDescriptor(ownSymbol)
                        : carrier.OwnProperties.GetOwnPropertyDescriptor((string)propertyKey);
                    return ownDescriptor is null ? RuntimeValue.Undefined
                        : RuntimeValue.FromBoxed(ownDescriptor.ToObject());
                }
                if (target is SharpTSFunctionPrototype functionPrototype && propertyKey is SharpTSSymbol prototypeSymbol)
                {
                    var prototypeDescriptor = functionPrototype.GetOwnPropertyDescriptor(prototypeSymbol);
                    return prototypeDescriptor is null ? RuntimeValue.Undefined
                        : RuntimeValue.FromBoxed(prototypeDescriptor.ToObject());
                }
                if (FunctionObjectProperties.TryGetStorage(target, interpreter, out var properties))
                {
                    var functionDescriptor = propertyKey is SharpTSSymbol symbol
                        ? properties.GetOwnPropertyDescriptor(symbol)
                        : properties.GetOwnPropertyDescriptor((string)propertyKey);
                    return functionDescriptor is null ? RuntimeValue.Undefined
                        : RuntimeValue.FromBoxed(functionDescriptor.ToObject());
                }
                if (target is ISharpTSPrimitiveConstructor primitiveConstructor)
                {
                    var primitiveDescriptor = propertyKey is SharpTSSymbol symbol
                        ? primitiveConstructor.GetOwnPropertyDescriptor(symbol)
                        : primitiveConstructor.GetOwnPropertyDescriptor((string)propertyKey);
                    return primitiveDescriptor is null ? RuntimeValue.Undefined
                        : RuntimeValue.FromBoxed(primitiveDescriptor.ToObject());
                }
                if (target is SharpTSObjectNamespace)
                {
                    var ownDescriptor = propertyKey is string name
                        ? ObjectBuiltIns.OwnPropertyDescriptorOf(interpreter, target, name) : null;
                    return ownDescriptor is null ? RuntimeValue.Undefined
                        : RuntimeValue.FromBoxed(ownDescriptor.ToObject());
                }
                if (target is SharpTSProxy proxy)
                {
                    var proxyDescriptor = propertyKey is SharpTSSymbol symbol
                        ? proxy.TrapGetOwnPropertyDescriptor(symbol, interpreter)
                        : proxy.TrapGetOwnPropertyDescriptor((string)propertyKey, interpreter);
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
                var key = interpreter.ToPropertyKey(args[1].ToObject());
                if (target is ISharpTSOwnPropertyObject carrier)
                {
                    if (!args[2].IsObject)
                        throw new ThrowException(new SharpTSTypeError("Property description must be an object"));
                    var ownDescriptor = ObjectBuiltIns.ToPropertyDescriptor(interpreter, args[2].ToObject()!);
                    return RuntimeValue.FromBoolean(key is SharpTSSymbol ownSymbol
                        ? carrier.OwnProperties.DefineProperty(ownSymbol, ownDescriptor)
                        : carrier.OwnProperties.DefineProperty((string)key, ownDescriptor));
                }
                if (FunctionObjectProperties.TryGetStorage(target, interpreter, out var functionProperties))
                {
                    if (!args[2].IsObject)
                        throw new ThrowException(new SharpTSTypeError("Property description must be an object"));
                    var functionDescriptor = ObjectBuiltIns.ToPropertyDescriptor(interpreter, args[2].ToObject()!);
                    return RuntimeValue.FromBoolean(key is SharpTSSymbol functionSymbol
                        ? functionProperties.DefineProperty(functionSymbol, functionDescriptor)
                        : functionProperties.DefineProperty((string)key, functionDescriptor));
                }
                if (!args[2].IsObject)
                    throw new ThrowException(new SharpTSTypeError("Property description must be an object"));
                var descriptorArg = args[2].ToObject()!;
                if (target is SharpTSProxy proxy)
                {
                    return RuntimeValue.FromBoolean(key is SharpTSSymbol symbol
                        ? proxy.TrapDefineProperty(symbol, descriptorArg, interpreter)
                        : proxy.TrapDefineProperty((string)key, descriptorArg, interpreter));
                }

                // Abrupt descriptor evaluation is not a refused definition.
                SharpTSPropertyDescriptor descriptor = ObjectBuiltIns.ToPropertyDescriptor(interpreter, descriptorArg);
                if (key is SharpTSSymbol symbolKey)
                    return RuntimeValue.FromBoolean(target switch
                    {
                        SharpTSObject obj => obj.DefineProperty(symbolKey, descriptor),
                        SharpTSInstance inst => inst.DefineProperty(symbolKey, descriptor),
                        SharpTSArray array => array.DefineProperty(symbolKey, descriptor),
                        SharpTSClass klass => klass.DefineStaticSymbolProperty(symbolKey, descriptor),
                        SharpTSClassPrototype prototype => prototype.DefineSymbolProperty(symbolKey, descriptor),
                        SharpTSFunctionPrototype prototype => prototype.DefineProperty(symbolKey, descriptor),
                        ISharpTSPrimitiveConstructor constructor => constructor.DefineProperty(symbolKey, descriptor),
                        SharpTSMath math => math.DefineProperty(symbolKey, descriptor),
                        SharpTSJSON json => json.DefineProperty(symbolKey, descriptor),
                        SharpTSStringPrototype prototype => prototype.DefineProperty(symbolKey, descriptor),
                        SharpTSBigIntPrototype prototype => prototype.DefineProperty(symbolKey, descriptor),
                        _ => false
                    });
                var propertyKey = (string)key;
                try
                {
                    if (target is ISharpTSPrimitiveConstructor primitiveConstructor)
                        return RuntimeValue.FromBoolean(primitiveConstructor.DefineExtraProperty(propertyKey, descriptor));
                    ObjectBuiltIns.PreserveOmittedAttributes(
                        target, propertyKey, descriptor, descriptorArg, interpreter);
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
                        case SharpTSPromise promise:
                            return RuntimeValue.FromBoolean(promise.DefineProperty(propertyKey, descriptor));
                        case SharpTSArray arr:
                            return RuntimeValue.FromBoolean(arr.DefineProperty(propertyKey, descriptor));
                        case SharpTSClass klass:
                            return RuntimeValue.FromBoolean(klass.DefineStaticProperty(propertyKey, descriptor));
                        case SharpTSClassPrototype prototype:
                            return RuntimeValue.FromBoolean(prototype.DefineExtraProperty(propertyKey, descriptor));
                        case SharpTSGlobalThis globalThis:
                            return RuntimeValue.FromBoolean(globalThis.DefineProperty(propertyKey, descriptor));
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
                if (args[0].Kind != ValueKind.Object)
                    throw new ThrowException(new SharpTSTypeError(
                        "Reflect.ownKeys requires an object target"));
                var target = args[0].ToObject()!;
                List<object?> keys = [];
                if (FunctionObjectProperties.TryGetStorage(target, interpreter, out var properties))
                {
                    keys.AddRange(properties.OwnVisibleStringKeys().Cast<object?>());
                    keys.AddRange(properties.GetSymbolPropertyNames().Cast<object?>());
                    return RuntimeValue.FromObject(new SharpTSArray(keys));
                }
                switch (target)
                {
                    case ISharpTSOwnPropertyObject carrier:
                        keys.AddRange(carrier.OwnProperties.OwnVisibleStringKeys().Cast<object?>());
                        keys.AddRange(carrier.OwnProperties.GetSymbolPropertyNames().Cast<object?>());
                        break;
                    case SharpTSProxy proxy:
                        keys.AddRange(proxy.TrapOwnPropertyKeys(interpreter));
                        break;
                    case SharpTSObject obj:
                        keys.AddRange(obj.OwnVisibleStringKeys().Select(k => (object?)k));
                        keys.AddRange(obj.GetSymbolPropertyNames().Select(s => (object?)s));
                        break;
                    case ISharpTSPrimitiveConstructor primitiveConstructor:
                        keys.AddRange(primitiveConstructor.OwnStringKeys().Cast<object?>());
                        keys.AddRange(primitiveConstructor.GetSymbolPropertyNames().Cast<object?>());
                        break;
                    case SharpTSFunctionPrototype functionPrototype:
                        keys.AddRange((SharpTSArray)ObjectBuiltIns.GetOwnPropertyNames(interpreter, [target])!);
                        keys.AddRange(functionPrototype.GetSymbolPropertyNames().Cast<object?>());
                        break;
                    case SharpTSClass klass:
                        keys.AddRange(klass.OwnStaticStringKeys());
                        keys.AddRange(klass.OwnStaticSymbolKeys());
                        break;
                    case SharpTSClassPrototype prototype:
                        keys.AddRange(prototype.OwnStringKeys());
                        keys.AddRange(prototype.GetSymbolPropertyNames());
                        break;
                    case SharpTSArray array:
                        keys.AddRange(array.OwnStringKeys().Select(k => (object?)k));
                        keys.AddRange(array.GetSymbolPropertyNames().Select(s => (object?)s));
                        break;
                    case SharpTSInstance inst:
                        keys.AddRange(inst.GetFieldNames().Select(k => (object?)k));
                        keys.AddRange(inst.GetSymbolPropertyNames().Select(s => (object?)s));
                        break;
                    case SharpTSMath math:
                        keys.AddRange(math.OwnStringKeys().Cast<object?>());
                        keys.AddRange(math.GetSymbolPropertyNames().Select(s => (object?)s));
                        break;
                    case SharpTSJSON json:
                        keys.AddRange(json.OwnStringKeys().Cast<object?>());
                        keys.AddRange(json.GetSymbolPropertyNames().Cast<object?>());
                        break;
                    case SharpTSStringPrototype stringPrototype:
                        keys.AddRange(stringPrototype.GetSymbolPropertyNames().Select(s => (object?)s));
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

                return RuntimeValue.FromBoxed(FunctionBuiltIns.CallWithThis(
                    interpreter, target, thisArg, callArgs));
            }),

            "construct" => BuiltInMethod.CreateV2("construct", 2, 3, static (interpreter, _, args) =>
            {
                var target = args[0].ToObject() ?? throw new ThrowException(new SharpTSTypeError(
                    "Reflect.construct called on non-constructor"));
                var argsList = args[1].ToObject();
                if (IsNotConstructor(target))
                    throw new ThrowException(new SharpTSTypeError(
                        "Reflect.construct target is not a constructor"));

                // ECMA-262 28.1.2: validate newTarget is a constructor when
                // explicitly supplied. Test262's `isConstructor(f)` harness
                // helper relies on this — it calls
                // `Reflect.construct(function(){}, [], f)` and checks for a
                // throw when f isn't constructable.
                object? effectiveNewTarget = args.Length > 2 ? args[2].ToObject() : target;
                if (IsNotConstructor(effectiveNewTarget))
                    throw new ThrowException(new SharpTSTypeError(
                        "Reflect.construct newTarget is not a constructor"));

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

                // Promise validates the executor before
                // GetPrototypeFromConstructor(newTarget). In particular, an
                // accessor on newTarget.prototype must not run when the
                // executor argument is absent or non-callable.
                if (target is SharpTSBuiltInConstructor { Name: BuiltInNames.Promise }
                    && (callArgs.Count == 0 || callArgs[0] is not ISharpTSCallable))
                {
                    throw new ThrowException(new SharpTSTypeError(
                        "Promise executor must be callable"));
                }

                return RuntimeValue.FromBoxed(interpreter.Construct(target, callArgs, effectiveNewTarget));
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
    internal static bool IsNotConstructor(object? value)
    {
        if (value is SharpTSProxy proxy)
            return !proxy.HasConstructableTarget(target => !IsNotConstructor(target));
        // Direct rejections — methods/wrappers that are clearly callable but
        // aren't constructors per spec.
        if (Interpreter.IsNonConstructorWrapper(value))
            return true;
        if (value is BoundFunction bound)
            return IsNotConstructor(bound.Target);
        // Anything else that's not callable can't be a constructor.
        return value is not ISharpTSCallable;
    }

    private static bool SetSymbolProperty(
        Interpreter interpreter, SharpTSPropertyDescriptor? descriptor, SharpTSSymbol key,
        object? value, object? receiver)
    {
        if (descriptor is { HasGet: true } or { HasSet: true })
        {
            if (descriptor.Set is null) return false;
            FunctionBuiltIns.CallWithThis(interpreter, descriptor.Set, receiver, [value]);
            return true;
        }
        if (descriptor is { Writable: false } || RuntimeValue.FromBoxed(receiver).Kind != ValueKind.Object)
            return false;

        object? existing = receiver is SharpTSProxy proxy
            ? proxy.TrapGetOwnPropertyDescriptor(key, interpreter)
            : ObjectBuiltIns.RuntimeGetOwnPropertyDescriptor(receiver, key);
        bool hasExisting = existing is not (null or SharpTSUndefined);
        if (hasExisting)
        {
            var receiverDescriptor = SharpTSPropertyDescriptor.FromAnyObject(existing!);
            if (receiverDescriptor.HasGet || receiverDescriptor.HasSet || !receiverDescriptor.Writable)
                return false;
        }
        var valueDescriptor = new SharpTSObject([]);
        valueDescriptor.SetProperty("value", value);
        if (!hasExisting)
        {
            valueDescriptor.SetProperty("writable", true);
            valueDescriptor.SetProperty("enumerable", true);
            valueDescriptor.SetProperty("configurable", true);
        }
        try
        {
            return ObjectBuiltIns.DefinePropertyOnProxyTarget(interpreter, receiver!, key, valueDescriptor);
        }
        catch (ThrowException error) when (error.Value is SharpTSTypeError)
        {
            return false;
        }
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
