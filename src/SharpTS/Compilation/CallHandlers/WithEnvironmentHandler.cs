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
                or WithStatementLowering.CheckGet or WithStatementLowering.SetBinding or WithStatementLowering.CaptureCall))
            return false;
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
