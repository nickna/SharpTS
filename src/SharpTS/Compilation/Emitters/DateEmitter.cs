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

        // Date.prototype is mutable. Programs that write it must resolve the
        // method value at runtime; otherwise this emitter's direct helper call
        // would keep invoking the original intrinsic after an override.
        if (ctx.RuntimeFeatures?.UsesDatePrototypeMutation == true)
        {
            EmitDynamicPrototypeMethodCall(emitter, receiver, methodName, arguments);
            return true;
        }

        // Emit the Date object
        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        // Direct numeric getters and the single-double-argument setters keep the helper's
        // native double result on the stack. Consumers that need object/any will box it at
        // their actual boundary; numeric consumers and discarded results avoid the former
        // box/unbox pair entirely (#1487). Multi-argument setters still use the object[]
        // path and deliberately retain their boxed result for now.
        StackType resultType = StackType.Unknown;

        switch (methodName)
        {
            // Getters (no arguments, return double)
            case "getTime":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetTime);
                resultType = StackType.Double;
                break;

            case "getFullYear":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetFullYear);
                resultType = StackType.Double;
                break;

            case "getMonth":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetMonth);
                resultType = StackType.Double;
                break;

            case "getDate":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetDate);
                resultType = StackType.Double;
                break;

            case "getDay":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetDay);
                resultType = StackType.Double;
                break;

            case "getHours":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetHours);
                resultType = StackType.Double;
                break;

            case "getMinutes":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetMinutes);
                resultType = StackType.Double;
                break;

            case "getSeconds":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetSeconds);
                resultType = StackType.Double;
                break;

            case "getMilliseconds":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetMilliseconds);
                resultType = StackType.Double;
                break;

            case "getTimezoneOffset":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetTimezoneOffset);
                resultType = StackType.Double;
                break;

            // UTC getters + legacy getYear (no arguments, return double)
            case "getUTCFullYear":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetUTCFullYear);
                resultType = StackType.Double;
                break;

            case "getUTCMonth":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetUTCMonth);
                resultType = StackType.Double;
                break;

            case "getUTCDate":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetUTCDate);
                resultType = StackType.Double;
                break;

            case "getUTCDay":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetUTCDay);
                resultType = StackType.Double;
                break;

            case "getUTCHours":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetUTCHours);
                resultType = StackType.Double;
                break;

            case "getUTCMinutes":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetUTCMinutes);
                resultType = StackType.Double;
                break;

            case "getUTCSeconds":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetUTCSeconds);
                resultType = StackType.Double;
                break;

            case "getUTCMilliseconds":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetUTCMilliseconds);
                resultType = StackType.Double;
                break;

            case "getYear":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateGetYear);
                resultType = StackType.Double;
                break;

            // Simple setters (single argument, return double)
            case "setTime":
                EmitSingleDoubleArgOrNaN(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetTime);
                resultType = StackType.Double;
                break;

            case "setDate":
                EmitSingleDoubleArgOrNaN(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetDate);
                resultType = StackType.Double;
                break;

            case "setMilliseconds":
                EmitSingleDoubleArgOrNaN(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetMilliseconds);
                resultType = StackType.Double;
                break;

            // UTC simple setters + legacy setYear (single argument, return double)
            case "setUTCDate":
                EmitSingleDoubleArgOrNaN(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetUTCDate);
                resultType = StackType.Double;
                break;

            case "setUTCMilliseconds":
                EmitSingleDoubleArgOrNaN(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetUTCMilliseconds);
                resultType = StackType.Double;
                break;

            case "setYear":
                EmitSingleDoubleArgOrNaN(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetYear);
                resultType = StackType.Double;
                break;

            // Multi-argument setters (variadic, packaged as object[]). The $Runtime
            // wrapper honors the optional trailing arguments (#536).
            case "setFullYear":
                emitter.EmitArgsArray(arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetFullYear);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                break;

            case "setMonth":
                emitter.EmitArgsArray(arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetMonth);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                break;

            case "setHours":
                emitter.EmitArgsArray(arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetHours);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                break;

            case "setMinutes":
                emitter.EmitArgsArray(arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetMinutes);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                break;

            case "setSeconds":
                emitter.EmitArgsArray(arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetSeconds);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                break;

            // UTC multi-argument setters (variadic, packaged as object[])
            case "setUTCFullYear":
                emitter.EmitArgsArray(arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetUTCFullYear);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                break;

            case "setUTCMonth":
                emitter.EmitArgsArray(arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetUTCMonth);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                break;

            case "setUTCHours":
                emitter.EmitArgsArray(arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetUTCHours);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                break;

            case "setUTCMinutes":
                emitter.EmitArgsArray(arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetUTCMinutes);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                break;

            case "setUTCSeconds":
                emitter.EmitArgsArray(arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.DateSetUTCSeconds);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                break;

            // Conversion methods (no arguments, return string)
            case "toISOString":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateToISOString);
                resultType = StackType.String;
                break;

            case "toDateString":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateToDateString);
                resultType = StackType.String;
                break;

            case "toTimeString":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateToTimeString);
                resultType = StackType.String;
                break;

            case "toUTCString":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateToUTCString);
                resultType = StackType.String;
                break;

            // toLocale*: argument-less calls use the standalone BCL helper; calls that pass
            // locale/options route through DateToLocaleWithOptions to honor them (#539).
            case "toLocaleDateString":
                EmitToLocale(emitter, arguments, ctx.Runtime!.DateToLocaleDateString, kind: 0);
                resultType = StackType.String;
                break;

            case "toLocaleTimeString":
                EmitToLocale(emitter, arguments, ctx.Runtime!.DateToLocaleTimeString, kind: 1);
                resultType = StackType.String;
                break;

            case "toLocaleString":
                EmitToLocale(emitter, arguments, ctx.Runtime!.DateToLocaleString, kind: 2);
                resultType = StackType.String;
                break;

            // toJSON (no arguments, returns string | null as object)
            case "toJSON":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateToJSON);
                break;

            // valueOf (no arguments, returns double)
            case "valueOf":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateValueOf);
                resultType = StackType.Double;
                break;

            // toString (no arguments, returns string)
            case "toString":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateToString);
                resultType = StackType.String;
                break;

            default:
                return false;
        }

        emitter.SetStackType(resultType);
        return true;
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

    private static void EmitDynamicPrototypeMethodCall(
        IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);
        var receiverLocal = emitter.SpillStackToObjectLocal();

        il.Emit(OpCodes.Ldloc, receiverLocal);
        il.Emit(OpCodes.Ldstr, methodName);
        il.Emit(OpCodes.Call, ctx.Runtime!.GetProperty);
        var functionLocal = emitter.SpillStackToObjectLocal();

        emitter.EmitArgsArrayWithSpread(arguments);
        var argsLocal = il.DeclareLocal(ctx.Types.ObjectArray);
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldloc, receiverLocal);
        il.Emit(OpCodes.Ldloc, functionLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, ctx.Runtime.InvokeMethodValue);
        emitter.SetStackUnknown();
    }

    /// <summary>
    /// Attempts to emit IL for a property set on a Date receiver.
    /// Date properties are not directly settable.
    /// </summary>
    public bool TryEmitPropertySet(IEmitterContext emitter, Expr receiver, string propertyName, Expr value)
    {
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
    /// Emits a toLocale* call. With no arguments, calls the standalone BCL helper
    /// <paramref name="bclMethod"/> (current host culture). When locale/options are supplied, routes
    /// through $Runtime.DateToLocaleWithOptions (kind 0/1/2 = date/time/both) to honor them, recording
    /// the soft SharpTS runtime dependency only at this call site (#539). The Date receiver is already
    /// on the stack from <see cref="TryEmitMethodCall"/>.
    /// </summary>
    private static void EmitToLocale(IEmitterContext emitter, List<Expr> arguments, System.Reflection.Emit.MethodBuilder bclMethod, int kind)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0 && ctx.Runtime!.DateToLocaleWithOptions != null)
        {
            il.Emit(OpCodes.Ldc_I4, kind);
            emitter.EmitArgsArray(arguments);
            il.Emit(OpCodes.Call, ctx.Runtime.DateToLocaleWithOptions);
            ctx.Runtime.RequireSharpTSRuntime("Date.prototype.toLocale* with locale/options");
        }
        else
        {
            il.Emit(OpCodes.Call, bclMethod);
        }
    }

    #endregion
}
