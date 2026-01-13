using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for Date method calls and property access.
/// Handles all TypeScript Date methods like getTime, getFullYear, setDate, toISOString, etc.
/// </summary>
public sealed class DateEmitter : ITypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a method call on a Date receiver.
    /// </summary>
    public bool TryEmitMethodCall(IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit the Date object
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        switch (methodName)
        {
            // Getters (no arguments, return double)
            case "getTime":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetTime);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "getFullYear":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetFullYear);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "getMonth":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetMonth);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "getDate":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetDate);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "getDay":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetDay);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "getHours":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetHours);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "getMinutes":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetMinutes);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "getSeconds":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetSeconds);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "getMilliseconds":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetMilliseconds);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "getTimezoneOffset":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetTimezoneOffset);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            // Simple setters (single argument, return double)
            case "setTime":
                EmitSingleDoubleArgOrNaN(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetTime);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "setDate":
                EmitSingleDoubleArgOrNaN(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetDate);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "setMilliseconds":
                EmitSingleDoubleArgOrNaN(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetMilliseconds);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            // Multi-argument setters (variadic, packaged as object[])
            case "setFullYear":
                EmitArgsArray(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetFullYear);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "setMonth":
                EmitArgsArray(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetMonth);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "setHours":
                EmitArgsArray(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetHours);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "setMinutes":
                EmitArgsArray(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetMinutes);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            case "setSeconds":
                EmitArgsArray(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetSeconds);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            // Conversion methods (no arguments, return string)
            case "toISOString":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateToISOString);
                return true;

            case "toDateString":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateToDateString);
                return true;

            case "toTimeString":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateToTimeString);
                return true;

            // valueOf (no arguments, returns double)
            case "valueOf":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateValueOf);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                return true;

            // toString (no arguments, returns string)
            case "toString":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateToString);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to emit IL for a property get on a Date receiver.
    /// Date objects don't have accessible properties in TypeScript.
    /// </summary>
    public bool TryEmitPropertyGet(IEmitterContext emitter, Expr receiver, string propertyName)
    {
        // Date doesn't expose properties directly - all access is via methods
        return false;
    }

    #region Helper Methods

    /// <summary>
    /// Emits a single argument as double, or NaN if no arguments.
    /// </summary>
    private static void EmitSingleDoubleArgOrNaN(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpressionAsDouble(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldc_R8, double.NaN);
        }
    }

    /// <summary>
    /// Emits all arguments as an object array.
    /// </summary>
    private static void EmitArgsArray(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        il.Emit(OpCodes.Ldc_I4, arguments.Count);
        il.Emit(OpCodes.Newarr, ctx.Types.Object);
        for (int i = 0; i < arguments.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            emitter.EmitExpression(arguments[i]);
            emitter.EmitBoxIfNeeded(arguments[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
    }

    #endregion
}
