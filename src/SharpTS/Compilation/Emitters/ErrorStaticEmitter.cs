using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>Emits the ES2026 <c>Error.isError</c> static method.</summary>
public sealed class ErrorStaticEmitter : IStaticTypeEmitterStrategy
{
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        if (methodName != "isError") return false;

        var ctx = emitter.Context;
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            ctx.IL.Emit(OpCodes.Ldnull);
        }
        ctx.IL.Emit(OpCodes.Call, ctx.Runtime!.ErrorIsError);
        ctx.IL.Emit(OpCodes.Box, ctx.Types.Boolean);
        return true;
    }

    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        if (propertyName != "isError") return false;
        var ctx = emitter.Context;
        ctx.Types.EmitLoadMethodInfo(ctx.IL, ctx.Runtime!.ErrorIsError);
        ctx.IL.Emit(OpCodes.Ldstr, "isError");
        ctx.IL.Emit(OpCodes.Ldc_I4_1);
        ctx.IL.Emit(OpCodes.Call, ctx.Runtime.TSFunctionGetOrCreate);
        return true;
    }

    public bool HasStaticProperty(string memberName) => memberName == "isError";
}
