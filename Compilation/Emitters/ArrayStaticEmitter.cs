using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for Array static method calls.
/// Handles Array.isArray().
/// </summary>
public sealed class ArrayStaticEmitter : IStaticTypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for an Array static method call.
    /// </summary>
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (methodName)
        {
            case "isArray":
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.IsArray);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;
            case "from":
                // Emit iterable argument
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }

                // Emit mapFn (or null)
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }

                // Load Symbol.iterator and runtime type for IterateToList
                il.Emit(OpCodes.Ldsfld, ctx.Runtime!.SymbolIterator);
                il.Emit(OpCodes.Ldtoken, ctx.Runtime!.RuntimeType);
                il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayFrom);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Array has no static properties.
    /// </summary>
    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        return false;
    }
}
