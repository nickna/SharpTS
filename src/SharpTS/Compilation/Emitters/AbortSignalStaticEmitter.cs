using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for AbortSignal static method calls (abort, timeout, any).
/// </summary>
public sealed class AbortSignalStaticEmitter : IStaticTypeEmitterStrategy
{
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (methodName)
        {
            case "abort":
                // AbortSignal.abort(reason?) → AbortSignalAbort(reason)
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.AbortSignalAbort);
                return true;

            case "timeout":
                // AbortSignal.timeout(ms) → AbortSignalTimeout(ms)
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                    il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);
                }
                else
                {
                    il.Emit(OpCodes.Ldc_R8, 0.0);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.AbortSignalTimeout);
                return true;

            case "any":
                // AbortSignal.any(signals) → AbortSignalAny(signals)
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.AbortSignalAny);
                return true;

            default:
                return false;
        }
    }

    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        return false;
    }
}
