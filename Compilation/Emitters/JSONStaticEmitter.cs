using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for JSON static method calls.
/// Handles JSON.parse() and JSON.stringify().
/// </summary>
public sealed class JSONStaticEmitter : IStaticTypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a JSON static method call.
    /// </summary>
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (methodName)
        {
            case "parse":
                // Arg 0: text (required)
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldstr, "null");
                }

                // Arg 1: reviver (optional)
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                    il.Emit(OpCodes.Call, ctx.Runtime!.JsonParseWithReviver);
                }
                else
                {
                    il.Emit(OpCodes.Call, ctx.Runtime!.JsonParse);
                }
                return true;

            case "stringify":
                // Arg 0: value (required)
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }

                // Arg 1: replacer (optional), Arg 2: space (optional)
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);

                    if (arguments.Count > 2)
                    {
                        emitter.EmitExpression(arguments[2]);
                        emitter.EmitBoxIfNeeded(arguments[2]);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldnull);
                    }
                    il.Emit(OpCodes.Call, ctx.Runtime!.JsonStringifyFull);
                }
                else
                {
                    il.Emit(OpCodes.Call, ctx.Runtime!.JsonStringify);
                }
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// JSON has no static properties.
    /// </summary>
    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        return false;
    }
}
