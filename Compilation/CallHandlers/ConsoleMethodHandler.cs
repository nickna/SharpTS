using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles console method calls: console.log, console.error, console.warn, etc.
/// Delegates to the existing TryEmitConsoleMethod helper.
/// </summary>
public class ConsoleMethodHandler : ICallHandler
{
    public int Priority => 20;

    public bool TryHandle(ILEmitter emitter, Expr.Call call)
    {
        var ctx = emitter.Context;
        var helpers = emitter.Helpers;

        return helpers.TryEmitConsoleMethod(
            call,
            arg =>
            {
                emitter.EmitExpression(arg);
                emitter.EmitBoxIfNeeded(arg);
            },
            ctx.Runtime!);
    }
}
