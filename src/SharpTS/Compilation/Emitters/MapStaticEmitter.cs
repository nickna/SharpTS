using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for Map static method calls.
/// Handles Map.groupBy().
/// </summary>
public sealed class MapStaticEmitter : IStaticTypeEmitterStrategy
{
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (methodName)
        {
            case "groupBy":
                // Map.groupBy(iterable, callback)
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.MapGroupBy);
                return true;
            default:
                return false;
        }
    }

    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        if (propertyName != "groupBy") return false;
        var ctx = emitter.Context;
        ctx.Types.EmitLoadMethodInfo(ctx.IL, ctx.Runtime!.MapGroupBy);
        ctx.IL.Emit(OpCodes.Ldstr, "groupBy");
        ctx.IL.Emit(OpCodes.Ldc_I4_2);
        ctx.IL.Emit(OpCodes.Call, ctx.Runtime.TSFunctionGetOrCreate);
        return true;
    }
}
