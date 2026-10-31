using System.Reflection;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class ExpressionEmitterBase
{
    private protected void RecordExternalSelection(MemberInfo member, Type resultType,
        NativeClrAdmission.Marshalling marshalling = NativeClrAdmission.Marshalling.MethodResult,
        Expr? receiver = null, IReadOnlyList<Expr>? arguments = null)
    {
        if (Ctx.NativeClrAdmission is not { } admission) return;
        arguments ??= CurrentSourceExpression switch
        {
            Expr.Call call => call.Arguments,
            Expr.New construction => construction.Arguments,
            Expr.Binary binary => [binary.Left, binary.Right],
            Expr.Unary unary => [unary.Right],
            Expr.Set set => [set.Value],
            Expr.GetIndex get => [get.Index],
            Expr.SetIndex set => [set.Index, set.Value],
            _ => []
        };
        admission.Record(CurrentSourceExpression, member, resultType, marshalling, receiver, arguments);
    }
}
