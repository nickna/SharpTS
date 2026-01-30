using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles the global fetch() function call.
/// </summary>
public class FetchHandler : ICallHandler
{
    public int Priority => 46; // After TimerHandler (45), before GlobalFunctionHandler (50)

    public bool TryHandle(ILEmitter emitter, Expr.Call call)
    {
        if (call.Callee is not Expr.Variable v)
            return false;

        if (v.Name.Lexeme != "fetch")
            return false;

        emitter.EmitFetch(call.Arguments);
        return true;
    }
}
