using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles globalThis.X.Y() chaining patterns like globalThis.Math.floor(), globalThis.console.log().
/// </summary>
public class GlobalThisChainHandler : ICallHandler
{
    public int Priority => 32; // After StaticTypeHandler (30), before DateStaticHandler (35)

    public bool TryHandle(IEmitterContext emitter, Expr.Call call)
    {
        // Pattern: globalThis.namespace.method()
        if (call.Callee is not Expr.Get chainedGet ||
            chainedGet.Object is not Expr.Get innerGet ||
            innerGet.Object is not Expr.Variable globalThisVar ||
            globalThisVar.Name.Lexeme != "globalThis")
        {
            return false;
        }

        var ctx = emitter.Context;
        if (ctx.TypeEmitterRegistry == null)
            return false;

        string namespaceName = innerGet.Name.Lexeme;
        string methodName = chainedGet.Name.Lexeme;

        // Handle globalThis.console.log() etc.
        if (namespaceName == "console")
        {
            var fakeCall = new Expr.Call(
                new Expr.Get(new Expr.Variable(innerGet.Name), chainedGet.Name, false),
                chainedGet.Name,
                null, // TypeArgs
                call.Arguments
            );
            if (emitter.TryEmitConsoleMethod(fakeCall))
                return true;
        }

        // Use the static emitter for the inner namespace (Math, JSON, etc.)
        var staticStrategy = ctx.TypeEmitterRegistry.GetStaticStrategy(namespaceName);
        if (staticStrategy != null && staticStrategy.TryEmitStaticCall(emitter, methodName, call.Arguments))
        {
            emitter.SetStackUnknown();
            return true;
        }

        return false;
    }
}
