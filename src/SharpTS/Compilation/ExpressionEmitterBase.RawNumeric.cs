using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class ExpressionEmitterBase
{
    protected bool TryEmitRawNumericUnary(Expr.Unary expression)
    {
        if (!(Ctx.TypeMap?.IsRawNumericResult(expression) == true || HasRawNumericValue(expression.Right)) || expression.Operator.Type is not
                (TokenType.PLUS or TokenType.MINUS or TokenType.TILDE)) return false;
        var operand = SpillBoxed(expression.Right);
        IL.Emit(OpCodes.Ldloc, operand);
        if (expression.Operator.Type == TokenType.PLUS)
        {
            IL.Emit(OpCodes.Call, Ctx.Runtime!.NumericCoercion.ToNumber);
            IL.Emit(OpCodes.Box, Types.Double);
            SetStackUnknown(); return true;
        }
        IL.Emit(OpCodes.Call, Ctx.Runtime!.NumericCoercion.ToNumeric);
        var numeric = _helpers.SpillStoreObject();
        var number = IL.DefineLabel(); var done = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, numeric); IL.Emit(OpCodes.Isinst, Types.BigInteger); IL.Emit(OpCodes.Brfalse, number);
        IL.Emit(OpCodes.Ldloc, numeric); IL.Emit(OpCodes.Unbox_Any, Types.BigInteger);
        IL.Emit(OpCodes.Call, Types.GetMethod(Types.BigInteger,
            expression.Operator.Type == TokenType.MINUS ? "op_UnaryNegation" : "op_OnesComplement", Types.BigInteger));
        IL.Emit(OpCodes.Box, Types.BigInteger); IL.Emit(OpCodes.Br, done);
        IL.MarkLabel(number);
        IL.Emit(OpCodes.Ldloc, numeric);
        if (expression.Operator.Type == TokenType.MINUS)
        {
            IL.Emit(OpCodes.Unbox_Any, Types.Double); IL.Emit(OpCodes.Neg);
        }
        else
        {
            IL.Emit(OpCodes.Call, Ctx.Runtime.NumericCoercion.JsToInt32); IL.Emit(OpCodes.Not); IL.Emit(OpCodes.Conv_R8);
        }
        IL.Emit(OpCodes.Box, Types.Double); IL.MarkLabel(done); SetStackUnknown(); return true;
    }

    protected bool HasRawNumericValue(Expr expression)
    {
        if (Ctx.TypeMap?.IsRawNumericResult(expression) == true) return true;
        return expression switch
        {
            Expr.Variable variable => Ctx.TypeMap?.IsRawNumericStorage(variable.Name) == true,
            Expr.Grouping grouping => HasRawNumericValue(grouping.Expression),
            Expr.TypeAssertion assertion => HasRawNumericValue(assertion.Expression),
            Expr.NonNullAssertion assertion => HasRawNumericValue(assertion.Expression),
            Expr.Satisfies satisfies => HasRawNumericValue(satisfies.Expression),
            Expr.Get member => HasRawNumericValue(member.Object),
            Expr.GetIndex member => HasRawNumericValue(member.Object),
            _ => false
        };
    }
}
