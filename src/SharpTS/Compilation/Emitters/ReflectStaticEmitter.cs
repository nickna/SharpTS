using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for standard ES2015 Reflect static method calls.
/// Handles Reflect.has(), Reflect.get(), Reflect.set(), Reflect.deleteProperty(), etc.
/// </summary>
public sealed class ReflectStaticEmitter : IStaticTypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a Reflect static method call.
    /// </summary>
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (methodName)
        {
            case "has":
            {
                // Reflect.has(target, propertyKey) → bool
                // Reuse HasIn which takes (key, obj) - note reversed order
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                // HasIn takes (key, obj), but we have (target, key) on stack
                // We need to swap: store target, load key, load target
                var targetLocal = il.DeclareLocal(typeof(object));
                var keyLocal = il.DeclareLocal(typeof(object));
                il.Emit(OpCodes.Stloc, keyLocal);
                il.Emit(OpCodes.Stloc, targetLocal);
                il.Emit(OpCodes.Ldloc, keyLocal);
                il.Emit(OpCodes.Ldloc, targetLocal);
                il.Emit(OpCodes.Call, ctx.Runtime!.HasIn);
                il.Emit(OpCodes.Box, typeof(bool));
                return true;
            }

            case "deleteProperty":
            {
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                    il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Call, ctx.Runtime!.ReflectDeleteProperty);
                il.Emit(OpCodes.Box, typeof(bool));
                return true;
            }

            case "get":
            {
                // Reflect.get(target, propertyKey, receiver?) → value
                var targetLocal = il.DeclareLocal(ctx.Types.Object);
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                    il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Stloc, targetLocal);

                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                il.Emit(OpCodes.Call, ctx.Runtime!.ToJsString);
                var keyLocal = il.DeclareLocal(ctx.Types.String);
                il.Emit(OpCodes.Stloc, keyLocal);
                il.Emit(OpCodes.Ldloc, targetLocal);
                il.Emit(OpCodes.Ldloc, keyLocal);
                if (arguments.Count > 2)
                {
                    emitter.EmitExpression(arguments[2]);
                    emitter.EmitBoxIfNeeded(arguments[2]);
                }
                else
                {
                    il.Emit(OpCodes.Ldloc, targetLocal);
                }
                il.Emit(OpCodes.Call, ctx.Runtime.ReflectGet);
                return true;
            }

            case "set":
            {
                // Reflect.set(target, propertyKey, value, receiver?) → bool
                var targetLocal = il.DeclareLocal(ctx.Types.Object);
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                    il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Stloc, targetLocal);

                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                if (arguments.Count > 2)
                {
                    emitter.EmitExpression(arguments[2]);
                    emitter.EmitBoxIfNeeded(arguments[2]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                if (arguments.Count > 3)
                {
                    emitter.EmitExpression(arguments[3]);
                    emitter.EmitBoxIfNeeded(arguments[3]);
                }
                else
                    il.Emit(OpCodes.Ldloc, targetLocal);

                il.Emit(OpCodes.Call, ctx.Runtime!.ReflectSet);
                il.Emit(OpCodes.Box, typeof(bool));
                return true;
            }

            case "getPrototypeOf":
            {
                // Reflect.getPrototypeOf(target) → object?
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectGetPrototypeOf);
                return true;
            }

            case "setPrototypeOf":
            {
                // Reflect.setPrototypeOf(target, proto) → bool
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                il.Emit(OpCodes.Call, ctx.Runtime!.ReflectSetPrototypeOf);
                il.Emit(OpCodes.Box, typeof(bool));
                return true;
            }

            case "isExtensible":
            {
                // Reflect.isExtensible(target) → bool
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectIsExtensible);
                il.Emit(OpCodes.Box, typeof(bool));
                return true;
            }

            case "preventExtensions":
            {
                // Reflect.preventExtensions(target) → true
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                il.Emit(OpCodes.Call, ctx.Runtime!.ReflectPreventExtensions);
                il.Emit(OpCodes.Box, typeof(bool));
                return true;
            }

            case "getOwnPropertyDescriptor":
            {
                // Reflect.getOwnPropertyDescriptor(target, propertyKey) → descriptor | undefined
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                il.Emit(OpCodes.Call, ctx.Runtime!.ObjectGetOwnPropertyDescriptor);
                return true;
            }

            case "defineProperty":
            {
                // Reflect.defineProperty(target, propertyKey, descriptor) → bool
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                if (arguments.Count > 2)
                {
                    emitter.EmitExpression(arguments[2]);
                    emitter.EmitBoxIfNeeded(arguments[2]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                il.Emit(OpCodes.Call, ctx.Runtime!.ReflectDefineProperty);
                il.Emit(OpCodes.Box, typeof(bool));
                return true;
            }

            case "ownKeys":
            {
                // Reflect.ownKeys(target) → array of keys
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                il.Emit(OpCodes.Call, ctx.Runtime!.ReflectOwnKeys);
                return true;
            }

            case "apply":
            {
                // Reflect.apply(target, thisArg, argsList)
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                if (arguments.Count > 2)
                {
                    emitter.EmitExpression(arguments[2]);
                    emitter.EmitBoxIfNeeded(arguments[2]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                il.Emit(OpCodes.Call, ctx.Runtime!.ReflectApply);
                return true;
            }

            case "construct":
            {
                // Reflect.construct(target, argsList, newTarget?)
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                // newTarget — defaults to target inside ReflectConstruct when null.
                if (arguments.Count > 2)
                {
                    emitter.EmitExpression(arguments[2]);
                    emitter.EmitBoxIfNeeded(arguments[2]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                il.Emit(OpCodes.Call, ctx.Runtime!.ReflectConstruct);
                return true;
            }

            case "defineMetadata":
            {
                // Reflect.defineMetadata(key, value, target[, propertyKey])
                for (int i = 0; i < 4; i++)
                {
                    if (i < arguments.Count)
                    {
                        emitter.EmitExpression(arguments[i]);
                        emitter.EmitBoxIfNeeded(arguments[i]);
                    }
                    else
                        il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.ReflectDefineMetadata);
                il.Emit(OpCodes.Ldnull); // defineMetadata returns void; push null for expression result
                return true;
            }

            case "getMetadata":
            {
                // Reflect.getMetadata(key, target[, propertyKey])
                for (int i = 0; i < 3; i++)
                {
                    if (i < arguments.Count)
                    {
                        emitter.EmitExpression(arguments[i]);
                        emitter.EmitBoxIfNeeded(arguments[i]);
                    }
                    else
                        il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.ReflectGetMetadata);
                return true;
            }

            case "hasMetadata":
            {
                // Reflect.hasMetadata(key, target[, propertyKey])
                for (int i = 0; i < 3; i++)
                {
                    if (i < arguments.Count)
                    {
                        emitter.EmitExpression(arguments[i]);
                        emitter.EmitBoxIfNeeded(arguments[i]);
                    }
                    else
                        il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.ReflectHasMetadata);
                return true;
            }

            case "getMetadataKeys":
            {
                // Reflect.getMetadataKeys(target[, propertyKey])
                for (int i = 0; i < 2; i++)
                {
                    if (i < arguments.Count)
                    {
                        emitter.EmitExpression(arguments[i]);
                        emitter.EmitBoxIfNeeded(arguments[i]);
                    }
                    else
                        il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.ReflectGetMetadataKeys);
                return true;
            }

            case "deleteMetadata":
            {
                // Reflect.deleteMetadata(key, target[, propertyKey])
                for (int i = 0; i < 3; i++)
                {
                    if (i < arguments.Count)
                    {
                        emitter.EmitExpression(arguments[i]);
                        emitter.EmitBoxIfNeeded(arguments[i]);
                    }
                    else
                        il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.ReflectDeleteMetadata);
                return true;
            }

            case "metadata":
            {
                // Reflect.metadata(key, value) → decorator factory
                // Returns a TSFunction wrapping $ReflectMetadataDecorator closure
                // When invoked as @Reflect.metadata("role", "admin") class MyClass {},
                // the decorator system calls: Reflect.metadata("role", "admin")(MyClass)

                if (ctx.Runtime!.ReflectMetadataDecoratorCtor == null ||
                    ctx.Runtime!.ReflectMetadataDecoratorInvoke == null)
                    return false;

                // Emit args
                if (arguments.Count >= 1)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                if (arguments.Count >= 2)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                    il.Emit(OpCodes.Ldnull);

                // new $ReflectMetadataDecorator(key, value)
                il.Emit(OpCodes.Newobj, ctx.Runtime!.ReflectMetadataDecoratorCtor);

                // Store instance, then wrap in TSFunction: new $TSFunction(instance, Invoke)
                var closureLocal = il.DeclareLocal(typeof(object));
                il.Emit(OpCodes.Stloc, closureLocal);
                il.Emit(OpCodes.Ldloc, closureLocal); // target for TSFunction ctor
                ctx.Types.EmitLoadMethodInfoViaHandle(il, ctx.Runtime!.ReflectMetadataDecoratorInvoke);
                il.Emit(OpCodes.Newobj, ctx.Runtime!.TSFunctionCtor);

                return true;
            }

            default:
                return false;
        }
    }

    /// <summary>
    /// Reflect has no static properties.
    /// </summary>
    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        return false;
    }
}
