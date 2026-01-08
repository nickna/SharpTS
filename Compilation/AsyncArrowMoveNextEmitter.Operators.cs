using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncArrowMoveNextEmitter
{
    private void EmitBinary(Expr.Binary b)
    {
        EmitExpression(b.Left);
        EnsureBoxed();
        EmitExpression(b.Right);
        EnsureBoxed();

        // Handle binary operations based on operator type
        var op = b.Operator.Type;
        switch (op)
        {
            case TokenType.PLUS:
                // Use runtime Add for string concatenation support
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.Add);
                break;
            case TokenType.MINUS:
                EmitNumericOp(OpCodes.Sub);
                break;
            case TokenType.STAR:
                EmitNumericOp(OpCodes.Mul);
                break;
            case TokenType.SLASH:
                EmitNumericOp(OpCodes.Div);
                break;
            case TokenType.PERCENT:
                EmitNumericOp(OpCodes.Rem);
                break;
            case TokenType.LESS:
            case TokenType.LESS_EQUAL:
            case TokenType.GREATER:
            case TokenType.GREATER_EQUAL:
                EmitComparisonOp(op);
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
                // Fallback: pop both and push null
                _il.Emit(OpCodes.Pop);
                _il.Emit(OpCodes.Ldnull);
                break;
        }
        SetStackUnknown();
    }

    private void EmitNumericOp(OpCode opcode)
    {
        // Convert to double and apply operation
        var rightLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Ldloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(opcode);
        _il.Emit(OpCodes.Box, typeof(double));
    }

    private void EmitComparisonOp(TokenType op)
    {
        // Convert to double for comparison
        var rightLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _il.Emit(OpCodes.Ldloc, rightLocal);
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);

        switch (op)
        {
            case TokenType.LESS:
                _il.Emit(OpCodes.Clt);
                break;
            case TokenType.LESS_EQUAL:
                _il.Emit(OpCodes.Cgt);
                _il.Emit(OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Ceq);
                break;
            case TokenType.GREATER:
                _il.Emit(OpCodes.Cgt);
                break;
            case TokenType.GREATER_EQUAL:
                _il.Emit(OpCodes.Clt);
                _il.Emit(OpCodes.Ldc_I4_0);
                _il.Emit(OpCodes.Ceq);
                break;
        }
        _il.Emit(OpCodes.Box, typeof(bool));
    }

    private void EmitCall(Expr.Call c)
    {
        // Emit callee
        EmitExpression(c.Callee);
        EnsureBoxed();

        // Create args array
        _il.Emit(OpCodes.Ldc_I4, c.Arguments.Count);
        _il.Emit(OpCodes.Newarr, typeof(object));

        for (int i = 0; i < c.Arguments.Count; i++)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldc_I4, i);
            EmitExpression(c.Arguments[i]);
            EnsureBoxed();
            _il.Emit(OpCodes.Stelem_Ref);
        }

        // Call InvokeValue (object callee, object[] args) -> object
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.InvokeValue);
        SetStackUnknown();
    }

    private void EmitAwait(Expr.Await aw)
    {
        int stateNum = _currentState++;
        var continueLabel = _il.DefineLabel();

        if (!_builder.AwaiterFields.TryGetValue(stateNum, out var awaiterField))
        {
            throw new InvalidOperationException($"No awaiter field for state {stateNum}");
        }

        // 1. Emit the awaited expression (should produce Task<object>)
        EmitExpression(aw.Expression);
        EnsureBoxed();

        // 2. Convert to Task<object> if needed
        _il.Emit(OpCodes.Castclass, typeof(Task<object>));

        // 3. Get awaiter: task.GetAwaiter()
        _il.Emit(OpCodes.Call, _builder.GetTaskGetAwaiterMethod());

        // 4. Store awaiter to field
        var awaiterLocal = _il.DeclareLocal(_builder.AwaiterType);
        _il.Emit(OpCodes.Stloc, awaiterLocal);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, awaiterLocal);
        _il.Emit(OpCodes.Stfld, awaiterField);

        // 5. Check IsCompleted
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, awaiterField);
        _il.Emit(OpCodes.Call, _builder.GetAwaiterIsCompletedGetter());
        _il.Emit(OpCodes.Brtrue, continueLabel);

        // 6. Not completed - suspend
        // this.<>1__state = stateNum
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, stateNum);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // builder.AwaitUnsafeOnCompleted(ref awaiter, ref this)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, _builder.BuilderField);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, awaiterField);
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Call, _builder.GetBuilderAwaitUnsafeOnCompletedMethod());

        // return (exit MoveNext)
        _il.Emit(OpCodes.Leave, _exitLabel);

        // 7. Resume point (jumped to from state switch)
        if (stateNum < _stateLabels.Count)
            _il.MarkLabel(_stateLabels[stateNum]);

        // Reset state to -1 (running)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4_M1);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // 8. Continue point (if was already completed)
        _il.MarkLabel(continueLabel);

        // 9. Get result: awaiter.GetResult()
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, awaiterField);
        _il.Emit(OpCodes.Call, _builder.GetAwaiterGetResultMethod());

        // Result is now on stack
        SetStackUnknown();
    }
}
