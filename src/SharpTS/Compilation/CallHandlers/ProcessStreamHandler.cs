using System.Reflection.Emit;
using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles process.stdin.read(), process.stdout.write(), process.stderr.write() calls.
/// </summary>
public class ProcessStreamHandler : ICallHandler
{
    public int Priority => 43; // After BuiltInModuleHandler (40), before TimerHandler (45)

    public bool TryHandle(IEmitterContext emitter, Expr.Call call)
    {
        if (call.Callee is not Expr.Get methodGet)
            return false;
        if (methodGet.Object is not Expr.Get streamGet)
            return false;
        if (streamGet.Object is not Expr.Variable processVar || processVar.Name.Lexeme != "process")
            return false;

        var il = emitter.IL;
        var ctx = emitter.Context;
        string streamName = streamGet.Name.Lexeme;
        string methodName = methodGet.Name.Lexeme;

        switch (streamName)
        {
            case "stdin" when methodName == "read":
                il.Emit(OpCodes.Call, ctx.Runtime!.StdinRead);
                emitter.SetStackUnknown();
                return true;

            case "stdout" when methodName == "write":
                if (call.Arguments.Count > 0) { emitter.EmitExpression(call.Arguments[0]); emitter.EmitBoxIfNeeded(call.Arguments[0]); }
                else { il.Emit(OpCodes.Ldstr, ""); }
                il.Emit(OpCodes.Call, ctx.Runtime!.StdoutWrite);
                emitter.SetStackUnknown();
                return true;

            case "stderr" when methodName == "write":
                if (call.Arguments.Count > 0) { emitter.EmitExpression(call.Arguments[0]); emitter.EmitBoxIfNeeded(call.Arguments[0]); }
                else { il.Emit(OpCodes.Ldstr, ""); }
                il.Emit(OpCodes.Call, ctx.Runtime!.StderrWrite);
                emitter.SetStackUnknown();
                return true;

            default:
                return false;
        }
    }
}
