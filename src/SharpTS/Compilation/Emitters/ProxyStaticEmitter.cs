using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for Proxy static method calls.
/// Handles Proxy.revocable().
/// </summary>
public sealed class ProxyStaticEmitter : IStaticTypeEmitterStrategy
{
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (methodName)
        {
            case "revocable":
                // Proxy.revocable(target, handler) -> { proxy, revoke }
                // Emit target
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                // Emit handler
                if (arguments.Count > 1)
                {
                    emitter.EmitExpression(arguments[1]);
                    emitter.EmitBoxIfNeeded(arguments[1]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                // Call emitted CreateRevocableProxy(target, handler)
                il.Emit(OpCodes.Call, ctx.Runtime!.CreateRevocableProxy);
                return true;
            default:
                return false;
        }
    }

    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        if (propertyName != "revocable") return false;
        var ctx = emitter.Context;
        ctx.Types.EmitLoadMethodInfo(ctx.IL, ctx.Runtime!.CreateRevocableProxy);
        ctx.IL.Emit(OpCodes.Ldstr, "revocable");
        ctx.IL.Emit(OpCodes.Ldc_I4_2);
        ctx.IL.Emit(OpCodes.Call, ctx.Runtime.TSFunctionGetOrCreate);
        return true;
    }

    public bool HasStaticProperty(string memberName) => memberName == "revocable";
}
