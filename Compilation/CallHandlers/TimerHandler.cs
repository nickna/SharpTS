using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles timer functions: setTimeout, clearTimeout, setInterval, clearInterval.
/// </summary>
public class TimerHandler : ICallHandler
{
    public int Priority => 45; // After BuiltInModuleHandler (40), before GlobalFunctionHandler (50)

    public bool TryHandle(ILEmitter emitter, Expr.Call call)
    {
        if (call.Callee is not Expr.Variable v)
            return false;

        return v.Name.Lexeme switch
        {
            "setTimeout" => EmitSetTimeout(emitter, call),
            "clearTimeout" => EmitClearTimeout(emitter, call),
            "setInterval" => EmitSetInterval(emitter, call),
            "clearInterval" => EmitClearInterval(emitter, call),
            _ => false
        };
    }

    private static bool EmitSetTimeout(ILEmitter emitter, Expr.Call call)
    {
        emitter.EmitSetTimeout(call.Arguments);
        return true;
    }

    private static bool EmitClearTimeout(ILEmitter emitter, Expr.Call call)
    {
        emitter.EmitClearTimeout(call.Arguments);
        return true;
    }

    private static bool EmitSetInterval(ILEmitter emitter, Expr.Call call)
    {
        emitter.EmitSetInterval(call.Arguments);
        return true;
    }

    private static bool EmitClearInterval(ILEmitter emitter, Expr.Call call)
    {
        emitter.EmitClearInterval(call.Arguments);
        return true;
    }
}
