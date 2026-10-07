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
            !exprStaticMethods.ContainsKey(classExprGet.Name.Lexeme)
            || emitter.ArgsContainSuspension(call.Arguments))
            return false;

        var il = emitter.IL;
        emitter.EmitExpression(classExprGet.Object);
        emitter.EnsureBoxed();
        var receiver = il.DeclareLocal(ctx.Types.Object);
        il.Emit(OpCodes.Stloc, receiver);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldloc, receiver);
        il.Emit(OpCodes.Ldstr, classExprGet.Name.Lexeme);
        il.Emit(OpCodes.Call, ctx.Runtime!.ObjectRead.Property);
        emitter.EmitArgsArrayWithSpread(call.Arguments);
        il.Emit(OpCodes.Call, ctx.Runtime.Invocation.Method);
        emitter.SetStackUnknown();
        return true;
    }
}
