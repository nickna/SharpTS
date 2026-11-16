using System.Reflection.Emit;
using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;

namespace SharpTS.Compilation.CallHandlers;

internal sealed class WithEnvironmentHandler : ICallHandler
{
    public int Priority => 17;

    public bool TryHandle(IEmitterContext emitter, Expr.Call call)
    {
        if (call.Callee is not Expr.Variable { Name.IsGeneratedTemporary: true } helper
            || helper.Name.Lexeme is not (WithStatementLowering.ToObject or WithStatementLowering.HasBinding
                or WithStatementLowering.CheckGet or WithStatementLowering.SetBinding or WithStatementLowering.CaptureCall
                or WithStatementLowering.IsIntrinsicEval or WithStatementLowering.CaptureLexicalCall))
            return false;
        if (helper.Name.Lexeme == WithStatementLowering.CaptureLexicalCall)
        {
            emitter.EmitExpression(call.Arguments[0]);
            emitter.EmitBoxIfNeeded(call.Arguments[0]);
            emitter.SetStackUnknown();
            return true;
        }
        if (helper.Name.Lexeme == WithStatementLowering.IsIntrinsicEval)
        {
            var ctx = emitter.Context;
            var il = emitter.IL;
            emitter.EmitExpression(call.Arguments[0]);
            emitter.EmitBoxIfNeeded(call.Arguments[0]);
            il.Emit(OpCodes.Ldtoken, ctx.Runtime!.GlobalObject.IndirectEval);
            il.Emit(OpCodes.Call, ctx.Types.MethodBaseGetMethodFromHandle);
            il.Emit(OpCodes.Castclass, ctx.Types.MethodInfo);
            il.Emit(OpCodes.Ldstr, "eval");
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, ctx.Runtime.FunctionConstruction.GetOrCreate);
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Box, ctx.Types.Boolean);
            emitter.SetStackUnknown();
            return true;
        }
        var construction = emitter.Context.Runtime!.ObjectConstruction;
        var method = helper.Name.Lexeme switch {
            WithStatementLowering.ToObject => construction.WithObject,
            WithStatementLowering.HasBinding => construction.WithHasBinding,
            WithStatementLowering.CheckGet => construction.WithGetCheck,
            WithStatementLowering.CaptureCall => construction.WithCaptureCall,
            _ => construction.WithSet
        };
        ((ExpressionEmitterBase)emitter).EmitWithEnvironmentCall(call, method);
        return true;
    }
}
