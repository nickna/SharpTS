using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles static type method calls: Math.*, JSON.*, Object.*, Array.*, Number.*, Promise.*, Symbol.*, process.*.
/// Delegates to TypeEmitterRegistry for strategy-based emission.
/// </summary>
public class StaticTypeHandler : ICallHandler
{
    public int Priority => 30;

    public bool TryHandle(IEmitterContext emitter, Expr.Call call)
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

        if (staticVar.Name.Lexeme == "Number"
            && (emitter.HasVariable("Number")
                || ctx.RuntimeFeatures?.UsesNumberConstructorMutation == true))
        {
            return false;
        }
        if (staticVar.Name.Lexeme == "Math"
            && (emitter.HasVariable("Math")
                || ctx.RuntimeFeatures?.UsesMathMutation == true))
        {
            return false;
        }

        var staticStrategy = ctx.TypeEmitterRegistry.GetStaticStrategy(staticVar.Name.Lexeme);
        if (staticStrategy == null)
            return false;

        if (!staticStrategy.TryEmitStaticCall(emitter, staticGet.Name.Lexeme, call.Arguments))
            return false;

        if (staticVar.Name.Lexeme == "Number"
            && staticGet.Name.Lexeme == "parseInt"
            && NumberStaticEmitter.EmitsUnboxedDecimalParseInt(
                emitter, call.Arguments))
        {
            emitter.SetStackType(StackType.Double);
        }
        else if (staticVar.Name.Lexeme == "Math"
            && MathStaticEmitter.EmitsUnboxedFixedArityMinMax(
                emitter, staticGet.Name.Lexeme, call.Arguments))
        {
            emitter.SetStackType(StackType.Double);
        }
        else if (staticVar.Name.Lexeme == "Promise"
            && staticGet.Name.Lexeme == "resolve"
            && call.Arguments is [var resolvedValue]
            && ctx.TypeMap?.IsStablePrimitivePromiseAllSeedValue(resolvedValue) == true)
        {
            emitter.SetStackType(StackType.Double);
        }
        else
        {
            emitter.SetStackUnknown();
        }
        return true;
    }
}
