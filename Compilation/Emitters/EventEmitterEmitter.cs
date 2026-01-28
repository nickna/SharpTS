using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for EventEmitter instance method calls and property access.
/// Handles EventEmitter methods like on, emit, off, once, etc.
/// </summary>
public sealed class EventEmitterEmitter : ITypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a method call on an EventEmitter receiver.
    /// </summary>
    public bool TryEmitMethodCall(IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        // Check if we can handle this method BEFORE emitting anything
        if (methodName is not "on" and not "addListener" and not "once"
            and not "off" and not "removeListener"
            and not "emit" and not "removeAllListeners"
            and not "listeners" and not "rawListeners"
            and not "listenerCount" and not "eventNames"
            and not "prependListener" and not "prependOnceListener"
            and not "setMaxListeners" and not "getMaxListeners")
            return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Now we know we can handle it, emit the receiver
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        // Cast to $EventEmitter
        il.Emit(OpCodes.Castclass, ctx.Runtime!.TSEventEmitterType);

        switch (methodName)
        {
            case "on":
            case "addListener":
                // eventName (required), listener (required)
                EmitStringArgument(emitter, arguments, 0);
                EmitListenerArgument(emitter, arguments, 1);
                il.Emit(OpCodes.Callvirt, ctx.Runtime!.TSEventEmitterOn);
                return true;

            case "once":
                EmitStringArgument(emitter, arguments, 0);
                EmitListenerArgument(emitter, arguments, 1);
                il.Emit(OpCodes.Callvirt, ctx.Runtime!.TSEventEmitterOnce);
                return true;

            case "off":
            case "removeListener":
                EmitStringArgument(emitter, arguments, 0);
                EmitListenerArgument(emitter, arguments, 1);
                il.Emit(OpCodes.Callvirt, ctx.Runtime!.TSEventEmitterOff);
                return true;

            case "emit":
                // eventName (required), ...args
                EmitStringArgument(emitter, arguments, 0);
                // Create object[] for remaining arguments
                EmitRemainingArgsAsArray(emitter, arguments, 1);
                il.Emit(OpCodes.Callvirt, ctx.Runtime!.TSEventEmitterEmit);
                il.Emit(OpCodes.Box, ctx.Types.Boolean);
                return true;

            case "removeAllListeners":
                // eventName (optional, null to remove all)
                if (arguments.Count > 0)
                {
                    EmitStringArgument(emitter, arguments, 0);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Callvirt, ctx.Runtime!.TSEventEmitterRemoveAllListeners);
                return true;

            case "listeners":
            case "rawListeners":
                // eventName (required) - returns array of listeners
                EmitStringArgument(emitter, arguments, 0);
                il.Emit(OpCodes.Callvirt, ctx.Runtime!.TSEventEmitterListeners);
                return true;

            case "listenerCount":
                // eventName (required) - returns number
                EmitStringArgument(emitter, arguments, 0);
                il.Emit(OpCodes.Callvirt, ctx.Runtime!.TSEventEmitterListenerCount);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "eventNames":
                // no arguments - returns array of event names
                il.Emit(OpCodes.Callvirt, ctx.Runtime!.TSEventEmitterEventNames);
                return true;

            case "prependListener":
                EmitStringArgument(emitter, arguments, 0);
                EmitListenerArgument(emitter, arguments, 1);
                il.Emit(OpCodes.Callvirt, ctx.Runtime!.TSEventEmitterPrependListener);
                return true;

            case "prependOnceListener":
                EmitStringArgument(emitter, arguments, 0);
                EmitListenerArgument(emitter, arguments, 1);
                il.Emit(OpCodes.Callvirt, ctx.Runtime!.TSEventEmitterPrependOnceListener);
                return true;

            case "setMaxListeners":
                // n (required) - returns this
                emitter.EmitExpression(arguments[0]);
                emitter.EmitBoxIfNeeded(arguments[0]);
                il.Emit(OpCodes.Unbox_Any, ctx.Types.Double);
                il.Emit(OpCodes.Callvirt, ctx.Runtime!.TSEventEmitterSetMaxListeners);
                return true;

            case "getMaxListeners":
                // no arguments - returns number
                il.Emit(OpCodes.Callvirt, ctx.Runtime!.TSEventEmitterGetMaxListeners);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            default:
                throw new InvalidOperationException($"Unexpected method {methodName} - early check should have filtered this");
        }
    }

    /// <summary>
    /// Attempts to emit IL for a property get on an EventEmitter receiver.
    /// </summary>
    public bool TryEmitPropertyGet(IEmitterContext emitter, Expr receiver, string propertyName)
    {
        // EventEmitter doesn't have instance properties that we need to emit
        // The static property defaultMaxListeners is on the EventEmitter constructor,
        // which would be handled differently
        return false;
    }

    /// <summary>
    /// Attempts to emit IL for a property set on an EventEmitter receiver.
    /// EventEmitter properties are not directly settable.
    /// </summary>
    public bool TryEmitPropertySet(IEmitterContext emitter, Expr receiver, string propertyName, Expr value)
    {
        return false;
    }

    #region Helper Methods

    /// <summary>
    /// Emits a string argument from the arguments list.
    /// </summary>
    private static void EmitStringArgument(IEmitterContext emitter, List<Expr> arguments, int index)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (index < arguments.Count)
        {
            emitter.EmitExpression(arguments[index]);
            emitter.EmitBoxIfNeeded(arguments[index]);
            il.Emit(OpCodes.Callvirt, ctx.Types.Object.GetMethod("ToString")!);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "");
        }
    }

    /// <summary>
    /// Emits a listener (function) argument from the arguments list.
    /// </summary>
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

    /// <summary>
    /// Emits remaining arguments starting from startIndex as an object array.
    /// </summary>
    private static void EmitRemainingArgsAsArray(IEmitterContext emitter, List<Expr> arguments, int startIndex)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        int count = arguments.Count - startIndex;
        if (count <= 0)
        {
            // Empty array
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, ctx.Types.Object);
            return;
        }

        // Create object array
        il.Emit(OpCodes.Ldc_I4, count);
        il.Emit(OpCodes.Newarr, ctx.Types.Object);

        for (int i = 0; i < count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            emitter.EmitExpression(arguments[startIndex + i]);
            emitter.EmitBoxIfNeeded(arguments[startIndex + i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
    }

    #endregion
}
