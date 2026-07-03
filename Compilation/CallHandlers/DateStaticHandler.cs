using System.Reflection.Emit;
using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles the static Date methods Date.now(), Date.UTC(...) and Date.parse(s).
/// </summary>
public class DateStaticHandler : ICallHandler
{
    public int Priority => 35;

    public bool TryHandle(IEmitterContext emitter, Expr.Call call)
    {
        // Must be Date.<member>(...)
        if (call.Callee is not Expr.Get dateGet ||
            dateGet.Object is not Expr.Variable dateVar ||
            dateVar.Name.Lexeme != "Date")
        {
            return false;
        }

        var il = emitter.IL;
        var ctx = emitter.Context;

        switch (dateGet.Name.Lexeme)
        {
            case "now":
                il.Emit(OpCodes.Call, ctx.Runtime!.DateNow);
                emitter.SetStackType(StackType.Double);
                return true;

            // Date.UTC(year, month?, ...): the components are packaged as object[]; $TSDate.UTC
            // honors each supplied (finite) component and returns the UTC timestamp (#538).
            case "UTC" when ctx.Runtime?.TSDateUTCStatic != null:
                emitter.EmitArgsArray(call.Arguments);
                il.Emit(OpCodes.Call, ctx.Runtime.TSDateUTCStatic);
                emitter.SetStackType(StackType.Double);
                return true;

            // Date.parse(s): parse the (single) string argument to a timestamp, or NaN (#538).
            case "parse" when ctx.Runtime?.TSDateParseStatic != null:
                if (call.Arguments.Count > 0)
                {
                    emitter.EmitExpression(call.Arguments[0]);
                    emitter.EnsureBoxed();
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime.TSDateParseStatic);
                emitter.SetStackType(StackType.Double);
                return true;

            default:
                return false;
        }
    }
}
