using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class GeneratorMoveNextEmitter
{
    protected override void EmitBinary(Expr.Binary b)
    {
        EmitExpression(b.Left);
        EnsureBoxed();
        EmitExpression(b.Right);
        EnsureBoxed();

        var op = b.Operator.Type;
        switch (op)
        {
            case TokenType.PLUS:
                _helpers.EmitCallUnknown(_ctx!.Runtime!.Add);
                break;
            case TokenType.MINUS:
                _helpers.EmitArithmeticBinary(OpCodes.Sub);
                break;
            case TokenType.STAR:
                _helpers.EmitArithmeticBinary(OpCodes.Mul);
                break;
            case TokenType.SLASH:
                _helpers.EmitArithmeticBinary(OpCodes.Div);
                break;
            case TokenType.PERCENT:
                _helpers.EmitArithmeticBinary(OpCodes.Rem);
                break;
            case TokenType.LESS:
                _helpers.EmitNumericComparison(OpCodes.Clt);
                break;
            case TokenType.LESS_EQUAL:
                _helpers.EmitNumericComparisonLe();
                break;
            case TokenType.GREATER:
                _helpers.EmitNumericComparison(OpCodes.Cgt);
                break;
            case TokenType.GREATER_EQUAL:
                _helpers.EmitNumericComparisonGe();
                break;
            case TokenType.EQUAL_EQUAL:
            case TokenType.EQUAL_EQUAL_EQUAL:
                _helpers.EmitRuntimeEquals(_ctx!.Runtime!.Equals);
                break;
            case TokenType.BANG_EQUAL:
            case TokenType.BANG_EQUAL_EQUAL:
                _helpers.EmitRuntimeNotEquals(_ctx!.Runtime!.Equals);
                break;
            default:
                _il.Emit(OpCodes.Pop);
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                break;
        }
    }

    protected override void EmitLogical(Expr.Logical l)
    {
        bool isAnd = l.Operator.Type == TokenType.AND_AND;
        _helpers.EmitLogical(
            isAnd,
            () => { EmitExpression(l.Left); EnsureBoxed(); },
            () => { EmitExpression(l.Right); EnsureBoxed(); },
            _ctx!.Runtime!.IsTruthy);
    }

    protected override void EmitUnary(Expr.Unary u)
    {
        switch (u.Operator.Type)
        {
            case TokenType.MINUS:
                _helpers.EmitUnaryMinus(() => EmitExpression(u.Right));
                break;
            case TokenType.BANG:
                _helpers.EmitUnaryNot(() => EmitExpression(u.Right), _ctx!.Runtime!.IsTruthy);
                break;
            case TokenType.TYPEOF:
                _helpers.EmitUnaryTypeOf(() => EmitExpression(u.Right), _ctx!.Runtime!.TypeOf);
                break;
            case TokenType.TILDE:
                _helpers.EmitUnaryBitwiseNot(() => EmitExpression(u.Right));
                break;
            default:
                EmitExpression(u.Right);
                EnsureBoxed();
                break;
        }
    }

    protected override void EmitTernary(Expr.Ternary t)
    {
        _helpers.EmitTernary(
            () => EmitExpression(t.Condition),
            () => EmitExpression(t.ThenBranch),
            () => EmitExpression(t.ElseBranch),
            _ctx!.Runtime!.IsTruthy);
    }

    protected override void EmitNullishCoalescing(Expr.NullishCoalescing nc)
    {
        _helpers.EmitNullishCoalescing(
            () => EmitExpression(nc.Left),
            () => EmitExpression(nc.Right));
    }

    private void EmitCompoundOperation(TokenType opType)
    {
        if (opType == TokenType.PLUS_EQUAL)
        {
            _helpers.EmitCallUnknown(_ctx!.Runtime!.Add);
            return;
        }

        switch (opType)
        {
            case TokenType.MINUS_EQUAL:
                _helpers.EmitArithmeticBinary(OpCodes.Sub);
                break;
            case TokenType.STAR_EQUAL:
                _helpers.EmitArithmeticBinary(OpCodes.Mul);
                break;
            case TokenType.SLASH_EQUAL:
                _helpers.EmitArithmeticBinary(OpCodes.Div);
                break;
            case TokenType.PERCENT_EQUAL:
                _helpers.EmitArithmeticBinary(OpCodes.Rem);
                break;
            default:
                _helpers.EmitArithmeticBinary(OpCodes.Add);
                break;
        }
    }
}
