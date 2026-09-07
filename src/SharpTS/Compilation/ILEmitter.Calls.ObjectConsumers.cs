using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    private bool TryEmitPromotedObjectConsumer(Expr.Call call)
    {
        if (_ctx.TypeMap?.TryGetPromotedObjectCall(call, out var summary) != true
            || call.Arguments is not [Expr.Variable argument]
            || _ctx.TryGetPromotedObjectLocal(argument.Name.Lexeme) is not { } receiver)
            return false;

        // The sole argument is an exact local, so its evaluation has no effects. Emit the
        // summarized writes against the original slot (not a copied struct), in source order.
        foreach (var write in summary.Writes)
        {
            IL.Emit(OpCodes.Ldloca, receiver.Local);
            EmitNumeric(write.Value);
            IL.Emit(OpCodes.Stfld, receiver.Shape.FieldBuilders[write.Name.Lexeme]);
        }
        EmitNumeric(summary.Result);
        SetStackType(StackType.Double);
        return true;

        void EmitNumeric(Expr expression)
        {
            switch (expression)
            {
                case Expr.Literal { Value: double value }:
                    IL.Emit(OpCodes.Ldc_R8, value);
                    break;
                case Expr.Get read:
                    IL.Emit(OpCodes.Ldloca, receiver.Local);
                    IL.Emit(OpCodes.Ldfld, receiver.Shape.FieldBuilders[read.Name.Lexeme]);
                    break;
                case Expr.Grouping grouping:
                    EmitNumeric(grouping.Expression);
                    break;
                case Expr.Unary unary:
                    EmitNumeric(unary.Right);
                    if (unary.Operator.Type == TokenType.MINUS) IL.Emit(OpCodes.Neg);
                    break;
                case Expr.Binary binary:
                    EmitNumeric(binary.Left);
                    EmitNumeric(binary.Right);
                    IL.Emit(binary.Operator.Type switch
                    {
                        TokenType.PLUS => OpCodes.Add,
                        TokenType.MINUS => OpCodes.Sub,
                        TokenType.STAR => OpCodes.Mul,
                        TokenType.SLASH => OpCodes.Div,
                        TokenType.PERCENT => OpCodes.Rem,
                        _ => throw new InvalidOperationException("Invalid numeric object-consumer operator")
                    });
                    break;
                default:
                    throw new InvalidOperationException("Invalid numeric object-consumer expression");
            }
        }
    }
}
