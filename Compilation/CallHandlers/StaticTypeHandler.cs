using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles static type method calls: Math.*, JSON.*, Object.*, Array.*, Number.*, Promise.*, Symbol.*, process.*.
/// Delegates to TypeEmitterRegistry for strategy-based emission.
/// </summary>
public class StaticTypeHandler : ICallHandler
{
    public int Priority => 30;

    public bool TryHandle(ILEmitter emitter, Expr.Call call)
    {
        // Must be a method call on a static variable (e.g., Math.floor())
        if (call.Callee is not Expr.Get staticGet ||
            staticGet.Object is not Expr.Variable staticVar)
        {
            return false;
        }

        var ctx = emitter.Context;
        if (ctx.TypeEmitterRegistry == null)
            return false;

        var staticStrategy = ctx.TypeEmitterRegistry.GetStaticStrategy(staticVar.Name.Lexeme);
        if (staticStrategy == null)
            return false;

        if (!staticStrategy.TryEmitStaticCall(emitter, staticGet.Name.Lexeme, call.Arguments))
            return false;

        emitter.ResetStackType();
        return true;
    }
}
