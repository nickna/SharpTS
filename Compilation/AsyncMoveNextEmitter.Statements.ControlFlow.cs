using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    private void EmitReturn(Stmt.Return r)
    {
        if (r.Value != null)
        {
            EmitExpression(r.Value);
            EnsureBoxed();
            if (_returnValueLocal != null)
                _il.Emit(OpCodes.Stloc, _returnValueLocal);
        }

        // If we're inside a try with finally-with-awaits, set pending return flag
        // and jump to after-finally label (which will then complete the return)
        if (_pendingReturnFlagLocal != null && _afterFinallyLabel != null)
        {
            _il.Emit(OpCodes.Ldc_I4_1);
            _il.Emit(OpCodes.Stloc, _pendingReturnFlagLocal);
            // Use Leave to exit the protected region to the after-finally label
            _il.Emit(OpCodes.Leave, _afterFinallyLabel.Value);
            return;
        }

        // Jump to set result
        _il.Emit(OpCodes.Leave, _setResultLabel);
    }

    private void EmitIf(Stmt.If i)
    {
        var elseLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        EmitExpression(i.Condition);
        EnsureBoxed();
        EmitTruthyCheck();
        _il.Emit(OpCodes.Brfalse, elseLabel);

        EmitStatement(i.ThenBranch);
        _il.Emit(OpCodes.Br, endLabel);

        _il.MarkLabel(elseLabel);
        if (i.ElseBranch != null)
            EmitStatement(i.ElseBranch);

        _il.MarkLabel(endLabel);
    }

    private void EmitBreak(Stmt.Break b)
    {
        if (b.Label != null)
        {
            // Labeled break - search for matching label in loop stack
            foreach (var loop in _loopLabels)
            {
                if (loop.LabelName == b.Label.Lexeme)
                {
                    _il.Emit(OpCodes.Br, loop.BreakLabel);
                    return;
                }
            }
            // Label not found - should have been caught by type checker
        }
        else if (_loopLabels.Count > 0)
        {
            // Unlabeled break - jump to innermost loop's break label
            _il.Emit(OpCodes.Br, _loopLabels.Peek().BreakLabel);
        }
    }

    private void EmitContinue(Stmt.Continue c)
    {
        if (c.Label != null)
        {
            // Labeled continue - search for matching label in loop stack
            foreach (var loop in _loopLabels)
            {
                if (loop.LabelName == c.Label.Lexeme)
                {
                    _il.Emit(OpCodes.Br, loop.ContinueLabel);
                    return;
                }
            }
            // Label not found - should have been caught by type checker
        }
        else if (_loopLabels.Count > 0)
        {
            // Unlabeled continue - jump to innermost loop's continue label
            _il.Emit(OpCodes.Br, _loopLabels.Peek().ContinueLabel);
        }
    }
}
