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

        // Use consolidated binary operator helper
        if (!_helpers.TryEmitBinaryOperator(b.Operator.Type, _ctx!.Runtime!.Add, _ctx!.Runtime!.Equals))
        {
            // Unsupported operator - return null
            _il.Emit(OpCodes.Pop);
            _il.Emit(OpCodes.Ldnull);
            SetStackUnknown();
        }
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
