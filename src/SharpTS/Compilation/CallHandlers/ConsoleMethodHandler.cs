using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles console method calls: console.log, console.error, console.warn, etc.
/// Delegates to the TryEmitConsoleMethod on IEmitterContext.
/// </summary>
public class ConsoleMethodHandler : ICallHandler
{
    public int Priority => 20;

    public bool TryHandle(IEmitterContext emitter, Expr.Call call)
    {
        return emitter.TryEmitConsoleMethod(call);
    }
}
