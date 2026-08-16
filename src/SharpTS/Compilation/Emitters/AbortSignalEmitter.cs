using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for AbortSignal instance method calls and property access.
/// </summary>
public sealed class AbortSignalEmitter : ITypeEmitterStrategy
{
    public bool TryEmitMethodCall(IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        if (methodName is not "throwIfAborted" and not "addEventListener" and not "removeEventListener")
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit receiver
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        switch (methodName)
        {
            case "throwIfAborted":
                il.Emit(OpCodes.Call, ctx.Runtime!.AbortSignalThrowIfAborted);
                // throwIfAborted returns void, push undefined
                il.Emit(OpCodes.Ldnull);
                return true;

            case "addEventListener":
                EmitStringArgument(emitter, arguments, 0);
                EmitListenerArgument(emitter, arguments, 1);
                il.Emit(OpCodes.Call, ctx.Runtime!.AbortSignalAddEventListener);
                return true;

            case "removeEventListener":
                EmitStringArgument(emitter, arguments, 0);
                EmitListenerArgument(emitter, arguments, 1);
                il.Emit(OpCodes.Call, ctx.Runtime!.AbortSignalRemoveEventListener);
                return true;

            default:
                return false;
        }
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, Expr receiver, string propertyName)
    {
        if (propertyName is not "aborted" and not "reason" and not "onabort")
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit receiver
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        switch (propertyName)
        {
            case "aborted":
                il.Emit(OpCodes.Call, ctx.Runtime!.AbortSignalGetAborted);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "reason":
                il.Emit(OpCodes.Call, ctx.Runtime!.AbortSignalGetReason);
                return true;

            case "onabort":
                il.Emit(OpCodes.Call, ctx.Runtime!.AbortSignalGetOnAbort);
                return true;

            default:
                return false;
        }
    }

    public bool TryEmitPropertySet(IEmitterContext emitter, Expr receiver, string propertyName, Expr value)
    {
        if (propertyName != "onabort")
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit receiver
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        // Emit value
        emitter.EmitExpression(value);
        emitter.EmitBoxIfNeeded(value);

        // Dup value for expression result (assignment is an expression in TS)
        il.Emit(OpCodes.Dup);
        var resultTemp = il.DeclareLocal(ctx.Types.Object);
        il.Emit(OpCodes.Stloc, resultTemp);

        il.Emit(OpCodes.Call, ctx.Runtime!.AbortSignalSetOnAbort);

        // Restore value on stack
        il.Emit(OpCodes.Ldloc, resultTemp);
        return true;
    }

    #region Helpers

    private static void EmitStringArgument(IEmitterContext emitter, List<Expr> arguments, int index)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (index < arguments.Count)
        {
            emitter.EmitExpression(arguments[index]);
            emitter.EmitBoxIfNeeded(arguments[index]);
            il.Emit(OpCodes.Callvirt, ctx.Types.GetMethod(ctx.Types.Object, "ToString")!);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "abort");
        }
    }

    private static void EmitListenerArgument(IEmitterContext emitter, List<Expr> arguments, int index)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (index < arguments.Count)
        {
            emitter.EmitExpression(arguments[index]);
            emitter.EmitBoxIfNeeded(arguments[index]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
    }

    #endregion
}
