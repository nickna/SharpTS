using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for Error method calls and property access.
/// Handles all JavaScript Error properties (name, message, stack) and methods (toString).
/// Also handles AggregateError's errors property.
/// </summary>
public sealed class ErrorEmitter : ITypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a method call on an Error receiver.
    /// </summary>
    public bool TryEmitMethodCall(IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit the Error object
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        switch (methodName)
        {
            case "toString":
                il.Emit(OpCodes.Call, ctx.Runtime!.ErrorToString);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to emit IL for a property get on an Error receiver.
    /// </summary>
    public bool TryEmitPropertyGet(IEmitterContext emitter, Expr receiver, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit the Error object
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        switch (propertyName)
        {
            case "name":
                il.Emit(OpCodes.Call, ctx.Runtime!.ErrorGetName);
                return true;

            case "message":
                il.Emit(OpCodes.Call, ctx.Runtime!.ErrorGetMessage);
                return true;

            case "stack":
                il.Emit(OpCodes.Call, ctx.Runtime!.ErrorGetStack);
                return true;

            case "errors":
                // For AggregateError, get the errors array
                il.Emit(OpCodes.Call, ctx.Runtime!.AggregateErrorGetErrors);
                return true;

            default:
                return false;
        }
    }
}
