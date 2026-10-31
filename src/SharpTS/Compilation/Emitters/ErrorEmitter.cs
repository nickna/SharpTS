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
        if (methodName != "toString")
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Error.prototype is mutable, so even a statically-known Error receiver
        // must resolve toString dynamically.  The previous direct CLR ToString
        // call bypassed assignments such as
        // `Error.prototype.toString = Object.prototype.toString` and also skipped
        // Error.prototype.toString's empty-name formatting rules.
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);
        var receiverLocal = emitter.SpillStackToObjectLocal();

        // Property lookup precedes argument evaluation.  Spill both the receiver
        // and resolved function through the emitter abstraction so they survive
        // an await/yield in an otherwise-ignored toString argument.
        il.Emit(OpCodes.Ldloc, receiverLocal);
        il.Emit(OpCodes.Ldstr, "toString");
        il.Emit(OpCodes.Call, ctx.Runtime!.ObjectRead.Property);
        var functionLocal = emitter.SpillStackToObjectLocal();

        emitter.EmitArgsArrayWithSpread(arguments);
        var argumentsLocal = il.DeclareLocal(ctx.Types.ObjectArray);
        il.Emit(OpCodes.Stloc, argumentsLocal);

        il.Emit(OpCodes.Ldloc, receiverLocal);
        il.Emit(OpCodes.Ldloc, functionLocal);
        il.Emit(OpCodes.Ldloc, argumentsLocal);
        il.Emit(OpCodes.Call, ctx.Runtime.Invocation.Method);
        emitter.SetStackUnknown();
        return true;
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

        var getter = propertyName switch
        {
            "name" => ctx.Runtime!.Errors.GetName,
            "message" => ctx.Runtime!.Errors.GetMessage,
            "stack" => ctx.Runtime!.Errors.GetStack,
            "cause" => ctx.Runtime!.Errors.GetCause,
            "errors" => ctx.Runtime!.Errors.AggregateErrorGetErrors,
            _ => null
        };
        if (getter is null) return false;

        if (ctx.Runtime!.Vm is { } vm)
        {
            var native = il.DefineLabel();
            var done = il.DefineLabel();
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Call, vm.IsErrorValue);
            il.Emit(OpCodes.Brfalse, native);
            il.Emit(OpCodes.Ldstr, propertyName);
            il.Emit(OpCodes.Call, vm.ReadErrorProperty);
            il.Emit(OpCodes.Br, done);
            il.MarkLabel(native);
            il.Emit(OpCodes.Call, getter);
            il.MarkLabel(done);
            emitter.SetStackUnknown();
            return true;
        }
        il.Emit(OpCodes.Call, getter);
        return true;
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

        if (propertyName == "cause")
        {
            // cause takes object? directly, no string conversion
            il.Emit(OpCodes.Call, ctx.Runtime!.Errors.SetCause);
        }
        else
        {
            // Convert to string for name, message, stack
            il.Emit(OpCodes.Callvirt, typeof(object).GetMethod("ToString", Type.EmptyTypes)!);

            // Call the appropriate setter
            switch (propertyName)
            {
                case "name":
                    il.Emit(OpCodes.Call, ctx.Runtime!.Errors.SetName);
                    break;
                case "message":
                    il.Emit(OpCodes.Call, ctx.Runtime!.Errors.SetMessage);
                    break;
                case "stack":
                    il.Emit(OpCodes.Call, ctx.Runtime!.Errors.SetStack);
                    break;
            }
        }

        // Put value back on stack
        il.Emit(OpCodes.Ldloc, valueTemp);
        return true;
    }
}
