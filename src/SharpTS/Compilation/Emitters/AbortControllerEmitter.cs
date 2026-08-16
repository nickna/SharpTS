using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for AbortController instance method calls and property access.
/// </summary>
public sealed class AbortControllerEmitter : ITypeEmitterStrategy
{
    public bool TryEmitMethodCall(IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        if (methodName != "abort")
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit receiver (the AbortController object)
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        // Emit reason argument (optional)
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Call AbortControllerAbort(controller, reason)
        il.Emit(OpCodes.Call, ctx.Runtime!.AbortControllerAbort);
        return true;
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, Expr receiver, string propertyName)
    {
        if (propertyName != "signal")
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit receiver
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        // Call AbortControllerGetSignal(controller)
        il.Emit(OpCodes.Call, ctx.Runtime!.AbortControllerGetSignal);
        return true;
    }

    public bool TryEmitPropertySet(IEmitterContext emitter, Expr receiver, string propertyName, Expr value)
    {
        return false;
    }
}
