using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for async generator method calls.
/// Handles next(), return(value), and throw(error) methods on async generators.
/// </summary>
public sealed class AsyncGeneratorEmitter : ITypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a method call on an async generator receiver.
    /// </summary>
    public bool TryEmitMethodCall(IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Check if runtime has async generator interface defined
        if (ctx.Runtime?.AsyncGeneratorInterfaceType == null)
            return false;

        switch (methodName)
        {
            case "next":
                // Emit the async generator object
                emitter.EmitExpression(receiver);
                emitter.EmitBoxIfNeeded(receiver);
                // Cast to $IAsyncGenerator interface
                il.Emit(OpCodes.Castclass, ctx.Runtime.AsyncGeneratorInterfaceType);
                // Emit the sent value: user-supplied argument or undefined (#473).
                // An omitted argument delivers undefined to the resumed yield expression.
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldsfld, ctx.Runtime.UndefinedInstance);
                }
                // Call next(sentValue) which returns Task<object>
                il.Emit(OpCodes.Callvirt, ctx.Runtime.AsyncGeneratorNextMethod);
                return true;

            case "return":
                // Emit the async generator object
                emitter.EmitExpression(receiver);
                emitter.EmitBoxIfNeeded(receiver);
                // Cast to $IAsyncGenerator interface
                il.Emit(OpCodes.Castclass, ctx.Runtime.AsyncGeneratorInterfaceType);
                // Emit value argument; an omitted argument is undefined, not null, so return() reports
                // { value: undefined } — an explicit return(null) still reports null (#618).
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldsfld, ctx.Runtime.UndefinedInstance);
                }
                // Call return(value) which returns Task<object>
                il.Emit(OpCodes.Callvirt, ctx.Runtime.AsyncGeneratorReturnMethod);
                return true;

            case "throw":
                // Emit the async generator object
                emitter.EmitExpression(receiver);
                emitter.EmitBoxIfNeeded(receiver);
                // Cast to $IAsyncGenerator interface
                il.Emit(OpCodes.Castclass, ctx.Runtime.AsyncGeneratorInterfaceType);
                // Emit error argument; an omitted argument is the undefined sentinel, not null (#618).
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldsfld, ctx.Runtime.UndefinedInstance);
                }
                // Call throw(error) which returns Task<object>
                il.Emit(OpCodes.Callvirt, ctx.Runtime.AsyncGeneratorThrowMethod);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to emit IL for a property get on an async generator receiver.
    /// Async generators don't have special properties (unlike regular generators).
    /// </summary>
    public bool TryEmitPropertyGet(IEmitterContext emitter, Expr receiver, string propertyName)
    {
        // Async generators don't have special properties we need to handle
        return false;
    }

    /// <summary>
    /// Attempts to emit IL for a property set on an async generator receiver.
    /// Async generators don't have settable properties.
    /// </summary>
    public bool TryEmitPropertySet(IEmitterContext emitter, Expr receiver, string propertyName, Expr value)
    {
        return false;
    }
}
