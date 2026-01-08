using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class GeneratorMoveNextEmitter
{
    private void EmitBinary(Expr.Binary b)
    {
        EmitExpression(b.Left);
        EnsureBoxed();
        EmitExpression(b.Right);
        EnsureBoxed();

        var op = b.Operator.Type;
        switch (op)
        {
            case TokenType.PLUS:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.Add);
                break;
            case TokenType.MINUS:
                EmitToDouble();
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
                _il.Emit(OpCodes.Sub);
                _il.Emit(OpCodes.Box, typeof(double));
                break;
            case TokenType.STAR:
                EmitToDouble();
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
                _il.Emit(OpCodes.Mul);
                _il.Emit(OpCodes.Box, typeof(double));
                break;
            case TokenType.SLASH:
                EmitToDouble();
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
                _il.Emit(OpCodes.Div);
                _il.Emit(OpCodes.Box, typeof(double));
                break;
            case TokenType.PERCENT:
                EmitToDouble();
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
                _il.Emit(OpCodes.Rem);
                _il.Emit(OpCodes.Box, typeof(double));
                break;
            case TokenType.LESS:
                EmitNumericComparison(OpCodes.Clt);
                break;
            case TokenType.LESS_EQUAL:
                EmitNumericComparisonLe();
                break;
            case TokenType.GREATER:
                EmitNumericComparison(OpCodes.Cgt);
                break;
            case TokenType.GREATER_EQUAL:
                EmitNumericComparisonGe();
                break;
            case TokenType.EQUAL_EQUAL:
            case TokenType.EQUAL_EQUAL_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.Equals);
                _il.Emit(OpCodes.Box, typeof(bool));
                break;
            case TokenType.BANG_EQUAL:
            case TokenType.BANG_EQUAL_EQUAL:
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.Equals);
                _il.Emit(OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Ceq);
                _il.Emit(OpCodes.Box, typeof(bool));
                break;
            default:
                _il.Emit(OpCodes.Pop);
                _il.Emit(OpCodes.Ldnull);
                break;
        }
        SetStackUnknown();
    }

    private void EmitToDouble()
    {
        var rightLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Ldloc, rightLocal);
    }

    private void EmitNumericComparison(OpCode compareOp)
    {
        var rightLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Ldloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(compareOp);
        _il.Emit(OpCodes.Box, typeof(bool));
    }

    private void EmitNumericComparisonLe()
    {
        var rightLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Ldloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Cgt);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ceq);
        _il.Emit(OpCodes.Box, typeof(bool));
    }

    private void EmitNumericComparisonGe()
    {
        var rightLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Ldloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Clt);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ceq);
        _il.Emit(OpCodes.Box, typeof(bool));
    }

    private void EmitLogical(Expr.Logical l)
    {
        var endLabel = _il.DefineLabel();

        EmitExpression(l.Left);
        EnsureBoxed();
        _il.Emit(OpCodes.Dup);
        EmitTruthyCheck();

        if (l.Operator.Type == TokenType.AND_AND)
        {
            _il.Emit(OpCodes.Brfalse, endLabel);
            _il.Emit(OpCodes.Pop);
            EmitExpression(l.Right);
            EnsureBoxed();
        }
        else
        {
            _il.Emit(OpCodes.Brtrue, endLabel);
            _il.Emit(OpCodes.Pop);
            EmitExpression(l.Right);
            EnsureBoxed();
        }

        _il.MarkLabel(endLabel);
        SetStackUnknown();
    }

    private void EmitUnary(Expr.Unary u)
    {
        EmitExpression(u.Right);

        switch (u.Operator.Type)
        {
            case TokenType.MINUS:
                EnsureBoxed();
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
                _il.Emit(OpCodes.Neg);
                _il.Emit(OpCodes.Box, typeof(double));
                break;
            case TokenType.BANG:
                EnsureBoxed();
                EmitTruthyCheck();
                _il.Emit(OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Ceq);
                _il.Emit(OpCodes.Box, typeof(bool));
                break;
            case TokenType.TYPEOF:
                EnsureBoxed();
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.TypeOf);
                break;
            case TokenType.TILDE:
                EnsureBoxed();
                _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToInt32", [typeof(object)])!);
                _il.Emit(OpCodes.Not);
                _il.Emit(OpCodes.Conv_R8);
                _il.Emit(OpCodes.Box, typeof(double));
                break;
            default:
                EnsureBoxed();
                break;
        }
        SetStackUnknown();
    }

    private void EmitTernary(Expr.Ternary t)
    {
        var elseLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        EmitExpression(t.Condition);
        EnsureBoxed();
        EmitTruthyCheck();
        _il.Emit(OpCodes.Brfalse, elseLabel);

        EmitExpression(t.ThenBranch);
        EnsureBoxed();
        _il.Emit(OpCodes.Br, endLabel);

        _il.MarkLabel(elseLabel);
        EmitExpression(t.ElseBranch);
        EnsureBoxed();

        _il.MarkLabel(endLabel);
        SetStackUnknown();
    }

    private void EmitNullishCoalescing(Expr.NullishCoalescing nc)
    {
        var rightLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        EmitExpression(nc.Left);
        EnsureBoxed();
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Brfalse, rightLabel);
        _il.Emit(OpCodes.Br, endLabel);

        _il.MarkLabel(rightLabel);
        _il.Emit(OpCodes.Pop);
        EmitExpression(nc.Right);
        EnsureBoxed();

        _il.MarkLabel(endLabel);
        SetStackUnknown();
    }

    private void EmitCompoundOperation(TokenType opType)
    {
        if (opType == TokenType.PLUS_EQUAL)
        {
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.Add);
            return;
        }

        var rightLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Ldloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);

        switch (opType)
        {
            case TokenType.MINUS_EQUAL:
                _il.Emit(OpCodes.Sub);
                break;
            case TokenType.STAR_EQUAL:
                _il.Emit(OpCodes.Mul);
                break;
            case TokenType.SLASH_EQUAL:
                _il.Emit(OpCodes.Div);
                break;
            case TokenType.PERCENT_EQUAL:
                _il.Emit(OpCodes.Rem);
                break;
            default:
                _il.Emit(OpCodes.Add);
                break;
        }

        _il.Emit(OpCodes.Box, typeof(double));
    }
}
