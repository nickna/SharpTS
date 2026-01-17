using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns;

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

    /// <summary>
    /// Attempts to emit IL for a property set on an Error receiver.
    /// Handles mutable properties: name, message, stack.
    /// </summary>
    public bool TryEmitPropertySet(IEmitterContext emitter, Expr receiver, string propertyName, Expr value)
    {
        // Only handle known mutable Error properties
        if (!ErrorBuiltIns.CanSetProperty(propertyName))
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit the Error object
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        // Emit the value
        emitter.EmitExpression(value);
        emitter.EmitBoxIfNeeded(value);

        // Dup value for expression result
        il.Emit(OpCodes.Dup);
        var valueTemp = il.DeclareLocal(ctx.Types.Object);
        il.Emit(OpCodes.Stloc, valueTemp);

        // Convert to string
        il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString", Type.EmptyTypes)!);

        // Call the appropriate setter
        switch (propertyName)
        {
            case "name":
                il.Emit(OpCodes.Call, ctx.Runtime!.ErrorSetName);
                break;
            case "message":
                il.Emit(OpCodes.Call, ctx.Runtime!.ErrorSetMessage);
                break;
            case "stack":
                il.Emit(OpCodes.Call, ctx.Runtime!.ErrorSetStack);
                break;
        }

        // Put value back on stack
        il.Emit(OpCodes.Ldloc, valueTemp);
        return true;
    }
}
