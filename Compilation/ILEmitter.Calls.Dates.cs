using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Date and RegExp method call emission for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    private void EmitDateMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the Date object
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);

        switch (methodName)
        {
            // Getters (no arguments, return double)
            case "getTime":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateGetTime);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                return;
            case "getFullYear":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateGetFullYear);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                return;
            case "getMonth":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateGetMonth);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                return;
            case "getDate":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateGetDate);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                return;
            case "getDay":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateGetDay);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                return;
            case "getHours":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateGetHours);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                return;
            case "getMinutes":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateGetMinutes);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                return;
            case "getSeconds":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateGetSeconds);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                return;
            case "getMilliseconds":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateGetMilliseconds);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                return;
            case "getTimezoneOffset":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateGetTimezoneOffset);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                return;

            // Simple setters (single argument, return double)
            case "setTime":
                if (arguments.Count > 0)
                {
                    EmitExpressionAsDouble(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_R8, double.NaN);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateSetTime);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                return;
            case "setDate":
                if (arguments.Count > 0)
                {
                    EmitExpressionAsDouble(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_R8, double.NaN);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateSetDate);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                return;
            case "setMilliseconds":
                if (arguments.Count > 0)
                {
                    EmitExpressionAsDouble(arguments[0]);
                }
                else
                {
                    IL.Emit(OpCodes.Ldc_R8, double.NaN);
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateSetMilliseconds);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                return;

            // Multi-argument setters (variadic, packaged as object[])
            case "setFullYear":
            case "setMonth":
            case "setHours":
            case "setMinutes":
            case "setSeconds":
                // Create args array
                IL.Emit(OpCodes.Ldc_I4, arguments.Count);
                IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
                for (int i = 0; i < arguments.Count; i++)
                {
                    IL.Emit(OpCodes.Dup);
                    IL.Emit(OpCodes.Ldc_I4, i);
                    EmitExpression(arguments[i]);
                    EmitBoxIfNeeded(arguments[i]);
                    IL.Emit(OpCodes.Stelem_Ref);
                }
                // Call appropriate runtime method
                var setMethod = methodName switch
                {
                    "setFullYear" => _ctx.Runtime!.DateSetFullYear,
                    "setMonth" => _ctx.Runtime!.DateSetMonth,
                    "setHours" => _ctx.Runtime!.DateSetHours,
                    "setMinutes" => _ctx.Runtime!.DateSetMinutes,
                    "setSeconds" => _ctx.Runtime!.DateSetSeconds,
                    _ => throw new Exception($"Unknown Date method: {methodName}")
                };
                IL.Emit(OpCodes.Call, setMethod);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                return;

            // Conversion methods (no arguments, return string)
            case "toISOString":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateToISOString);
                return;
            case "toDateString":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateToDateString);
                return;
            case "toTimeString":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateToTimeString);
                return;

            // valueOf (no arguments, returns double)
            case "valueOf":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateValueOf);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
                return;

            // toString (no arguments, returns string)
            case "toString":
                IL.Emit(OpCodes.Call, _ctx.Runtime!.DateToString);
                return;
        }
    }

    /// <summary>
    /// Emits code for RegExp method calls (test, exec).
    /// </summary>
    private void EmitRegExpMethodCall(Expr obj, string methodName, List<Expr> arguments)
    {
        // Emit the RegExp object
        EmitExpression(obj);
        EmitBoxIfNeeded(obj);

        switch (methodName)
        {
            case "test":
                // regex.test(str) -> bool
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.Stringify);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExpTest);
                IL.Emit(OpCodes.Box, _ctx.Types.Boolean);
                SetStackUnknown();
                break;

            case "exec":
                // regex.exec(str) -> array|null
                if (arguments.Count > 0)
                {
                    EmitExpression(arguments[0]);
                    EmitBoxIfNeeded(arguments[0]);
                    IL.Emit(OpCodes.Call, _ctx.Runtime!.Stringify);
                }
                else
                {
                    IL.Emit(OpCodes.Ldstr, "");
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.RegExpExec);
                SetStackUnknown();
                break;
        }
    }

    /// <summary>
    /// Emits code for new Date(...) construction with various argument forms.
    /// </summary>
    private void EmitNewDate(List<Expr> arguments)
    {
        switch (arguments.Count)
        {
            case 0:
                // new Date() - current date/time
                IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateDateNoArgs);
                break;

            case 1:
                // new Date(value) - milliseconds or ISO string
                EmitExpression(arguments[0]);
                EmitBoxIfNeeded(arguments[0]);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateDateFromValue);
                break;

            default:
                // new Date(year, month, day?, hours?, minutes?, seconds?, ms?)
                // Emit all 7 arguments, using 0 for missing ones
                for (int i = 0; i < 7; i++)
                {
                    if (i < arguments.Count)
                    {
                        EmitExpressionAsDouble(arguments[i]);
                    }
                    else
                    {
                        // Default values: day=1, others=0
                        IL.Emit(OpCodes.Ldc_R8, i == 2 ? 1.0 : 0.0);
                    }
                }
                IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateDateFromComponents);
                break;
        }
        // All Date constructors return an object, reset stack type
        SetStackUnknown();
    }

    /// <summary>
    /// Emits code for new RegExp(...) construction.
    /// </summary>
    private void EmitNewRegExp(List<Expr> arguments)
    {
        switch (arguments.Count)
        {
            case 0:
                // new RegExp() - empty pattern
                IL.Emit(OpCodes.Ldstr, "");
                IL.Emit(OpCodes.Ldstr, "");
                IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateRegExpWithFlags);
                break;

            case 1:
                // new RegExp(pattern) - pattern only
                EmitExpression(arguments[0]);
                EmitBoxIfNeeded(arguments[0]);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.Stringify); // Ensure pattern is a string
                IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateRegExp);
                break;

            default:
                // new RegExp(pattern, flags) - pattern and flags
                EmitExpression(arguments[0]);
                EmitBoxIfNeeded(arguments[0]);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.Stringify); // Ensure pattern is a string
                EmitExpression(arguments[1]);
                EmitBoxIfNeeded(arguments[1]);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.Stringify); // Ensure flags is a string
                IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateRegExpWithFlags);
                break;
        }
        SetStackUnknown();
    }
}
