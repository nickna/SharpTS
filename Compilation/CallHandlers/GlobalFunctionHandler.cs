using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles global built-in functions: parseInt, parseFloat, isNaN, isFinite.
/// </summary>
public class GlobalFunctionHandler : ICallHandler
{
    public int Priority => 50;

    public bool TryHandle(ILEmitter emitter, Expr.Call call)
    {
        if (call.Callee is not Expr.Variable v)
            return false;

        return v.Name.Lexeme switch
        {
            "parseInt" => EmitParseInt(emitter, call),
            "parseFloat" => EmitParseFloat(emitter, call),
            "isNaN" => EmitIsNaN(emitter, call),
            "isFinite" => EmitIsFinite(emitter, call),
            _ => false
        };
    }

    private static bool EmitParseInt(ILEmitter emitter, Expr.Call call)
    {
        emitter.EmitGlobalParseInt(call.Arguments);
        return true;
    }

    private static bool EmitParseFloat(ILEmitter emitter, Expr.Call call)
    {
        emitter.EmitGlobalParseFloat(call.Arguments);
        return true;
    }

    private static bool EmitIsNaN(ILEmitter emitter, Expr.Call call)
    {
        emitter.EmitGlobalIsNaN(call.Arguments);
        return true;
    }

    private static bool EmitIsFinite(ILEmitter emitter, Expr.Call call)
    {
        emitter.EmitGlobalIsFinite(call.Arguments);
        return true;
    }
}
