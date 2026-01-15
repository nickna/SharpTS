using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles Date.now() static method call.
/// </summary>
public class DateStaticHandler : ICallHandler
{
    public int Priority => 35;

    public bool TryHandle(ILEmitter emitter, Expr.Call call)
    {
        // Must be Date.now()
        if (call.Callee is not Expr.Get dateGet ||
            dateGet.Object is not Expr.Variable dateVar ||
            dateVar.Name.Lexeme != "Date" ||
            dateGet.Name.Lexeme != "now")
        {
            return false;
        }

        var il = emitter.ILGen;
        var ctx = emitter.Context;

        il.Emit(OpCodes.Call, ctx.Runtime!.DateNow);
        il.Emit(OpCodes.Box, ctx.Types.Double);
        return true;
    }
}
