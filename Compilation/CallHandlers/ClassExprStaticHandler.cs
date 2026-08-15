using System.Reflection.Emit;
using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

/// <summary>
/// Handles static method calls on class expressions (const Factory = class { static create() {} }; Factory.create()).
/// </summary>
public class ClassExprStaticHandler : ICallHandler
{
    public int Priority => 74;

    public bool TryHandle(IEmitterContext emitter, Expr.Call call)
    {
        if (call.Callee is not Expr.Get classExprGet ||
            classExprGet.Object is not Expr.Variable classExprVar)
            return false;

        var ctx = emitter.Context;
        if (ctx.VarToClassExpr == null ||
            !ctx.VarToClassExpr.TryGetValue(classExprVar.Name.Lexeme, out var classExpr) ||
            ctx.ClassExprStaticMethods == null ||
            !ctx.ClassExprStaticMethods.TryGetValue(classExpr, out var exprStaticMethods) ||
            !exprStaticMethods.TryGetValue(classExprGet.Name.Lexeme, out var exprStaticMethod))
            return false;

        var il = emitter.IL;
        var methodParams = exprStaticMethod.GetParameters();
        emitter.EmitStaticCallArguments(call.Arguments, methodParams);
        il.Emit(OpCodes.Call, exprStaticMethod);
        emitter.SetStackUnknown();
        return true;
    }
}
