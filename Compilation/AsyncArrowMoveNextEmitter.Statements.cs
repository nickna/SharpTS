using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncArrowMoveNextEmitter
{
    private void EmitStatement(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Expression e:
                EmitExpression(e.Expr);
                // Pop unused value
                if (_stackType != StackType.Unknown || true) // Always pop expression results
                    _il.Emit(OpCodes.Pop);
                SetStackUnknown();
                break;

            case Stmt.Var v:
                if (v.Initializer != null)
                {
                    EmitExpression(v.Initializer);
                    EnsureBoxed();
                    StoreVariable(v.Name.Lexeme);
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                    StoreVariable(v.Name.Lexeme);
                }
                break;

            case Stmt.Return r:
                if (r.Value != null)
                {
                    EmitExpression(r.Value);
                    EnsureBoxed();
                }
                else
                {
                    _il.Emit(OpCodes.Ldnull);
                }
                EmitSetResult();
                _il.Emit(OpCodes.Leave, _exitLabel);
                break;

            case Stmt.If i:
                EmitIf(i);
                break;

            case Stmt.While w:
                EmitWhile(w);
                break;

            case Stmt.Block b:
                foreach (var s in b.Statements)
                    EmitStatement(s);
                break;

            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    EmitStatement(s);
                break;

            // Add more statement types as needed
            default:
                // For unhandled statements, emit a placeholder
                break;
        }
    }

    private void EmitIf(Stmt.If i)
    {
        var elseLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        EmitExpression(i.Condition);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
        _il.Emit(OpCodes.Brfalse, elseLabel);

        EmitStatement(i.ThenBranch);
        _il.Emit(OpCodes.Br, endLabel);

        _il.MarkLabel(elseLabel);
        if (i.ElseBranch != null)
            EmitStatement(i.ElseBranch);

        _il.MarkLabel(endLabel);
    }

    private void EmitWhile(Stmt.While w)
    {
        var startLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        _il.MarkLabel(startLabel);

        EmitExpression(w.Condition);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
        _il.Emit(OpCodes.Brfalse, endLabel);

        EmitStatement(w.Body);
        _il.Emit(OpCodes.Br, startLabel);

        _il.MarkLabel(endLabel);
    }

    private void EmitReturnNull()
    {
        _il.Emit(OpCodes.Ldnull);
        EmitSetResult();
        _il.Emit(OpCodes.Leave, _exitLabel);
    }

    private void EmitSetResult()
    {
        // Store result
        _il.Emit(OpCodes.Stloc, _resultLocal!);

        // Set state to -2 (completed)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // builder.SetResult(result)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, _builder.BuilderField);
        _il.Emit(OpCodes.Ldloc, _resultLocal!);
        _il.Emit(OpCodes.Call, _builder.GetBuilderSetResultMethod());
    }

    private void EmitCatchBlock()
    {
        // Store exception
        _il.Emit(OpCodes.Stloc, _exceptionLocal!);

        // Set state to -2 (completed)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);

        // builder.SetException(exception)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldflda, _builder.BuilderField);
        _il.Emit(OpCodes.Ldloc, _exceptionLocal!);
        _il.Emit(OpCodes.Call, _builder.GetBuilderSetExceptionMethod());
    }
}
