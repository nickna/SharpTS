using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for FinalizationRegistry method calls.
/// Handles register and unregister methods.
/// </summary>
public sealed class FinalizationRegistryEmitter : ITypeEmitterStrategy
{
    public bool TryEmitMethodCall(IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (methodName)
        {
            case "register":
                // Emit: FinalizationRegistryRegister(registry, target, heldValue, token)
                emitter.EmitExpression(receiver);
                emitter.EmitBoxIfNeeded(receiver);

                // target (required)
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }

                // heldValue (optional)
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }

                // token (optional)
                if (arguments.Count > 2)
                {
                    emitter.EmitExpression(arguments[2]);
                    emitter.EmitBoxIfNeeded(arguments[2]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }

                il.Emit(OpCodes.Call, ctx.Runtime!.FinalizationRegistryRegister);
                il.Emit(OpCodes.Ldnull); // register returns undefined
                return true;

            case "unregister":
                // Emit: FinalizationRegistryUnregister(registry, token)
                emitter.EmitExpression(receiver);
                emitter.EmitBoxIfNeeded(receiver);

                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }

                il.Emit(OpCodes.Call, ctx.Runtime!.FinalizationRegistryUnregister);
                return true;

            default:
                return false;
        }
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, Expr receiver, string propertyName)
    {
        return false;
    }

    public bool TryEmitPropertySet(IEmitterContext emitter, Expr receiver, string propertyName, Expr value)
    {
        return false;
    }
}
