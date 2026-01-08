using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    private void EmitLabeledStatement(Stmt.LabeledStatement ls)
    {
        // For a labeled statement, we need to:
        // 1. If the inner statement is a loop, add the label name to the loop's entry
        // 2. If the inner statement is not a loop, just emit it (label is for break only, not loops)

        // Create labels for the labeled statement block
        var breakLabel = _il.DefineLabel();

        // Check if the inner statement is a loop - if so, we handle it specially
        if (ls.Statement is Stmt.While or Stmt.ForOf or Stmt.DoWhile or Stmt.ForIn)
        {
            // Emit the loop with the label name in the loop label stack
            EmitLabeledLoop(ls.Label.Lexeme, ls.Statement, breakLabel);
        }
        else
        {
            // Non-loop labeled statement - just push a break-only label
            // (continue doesn't make sense for non-loops)
            _loopLabels.Push((breakLabel, breakLabel, ls.Label.Lexeme));
            EmitStatement(ls.Statement);
            _loopLabels.Pop();
        }

        _il.MarkLabel(breakLabel);
    }

    private void EmitLabeledLoop(string labelName, Stmt loopStmt, Label outerBreakLabel)
    {
        // Emit a loop with a specific label name attached
        switch (loopStmt)
        {
            case Stmt.While w:
                EmitLabeledWhile(labelName, w, outerBreakLabel);
                break;
            case Stmt.ForOf f:
                EmitLabeledForOf(labelName, f, outerBreakLabel);
                break;
            case Stmt.DoWhile dw:
                EmitLabeledDoWhile(labelName, dw, outerBreakLabel);
                break;
            case Stmt.ForIn fi:
                EmitLabeledForIn(labelName, fi, outerBreakLabel);
                break;
        }
    }

    private void EmitLabeledWhile(string labelName, Stmt.While w, Label outerBreakLabel)
    {
        var startLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        // Push labels with the label name
        _loopLabels.Push((outerBreakLabel, continueLabel, labelName));

        _il.MarkLabel(startLabel);
        EmitExpression(w.Condition);
        EnsureBoxed();
        EmitTruthyCheck();
        _il.Emit(OpCodes.Brfalse, outerBreakLabel);

        EmitStatement(w.Body);

        _il.MarkLabel(continueLabel);
        _il.Emit(OpCodes.Br, startLabel);

        _loopLabels.Pop();
    }

    private void EmitLabeledForOf(string labelName, Stmt.ForOf f, Label outerBreakLabel)
    {
        string varName = f.Variable.Lexeme;
        var varField = _builder.GetVariableField(varName);

        EmitExpression(f.Iterable);
        EnsureBoxed();

        var getEnumerator = typeof(System.Collections.IEnumerable).GetMethod("GetEnumerator")!;
        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        var current = typeof(System.Collections.IEnumerator).GetProperty("Current")!.GetGetMethod()!;

        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerable));
        _il.Emit(OpCodes.Callvirt, getEnumerator);

        var enumLocal = _il.DeclareLocal(typeof(System.Collections.IEnumerator));
        _il.Emit(OpCodes.Stloc, enumLocal);

        var startLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        _loopLabels.Push((outerBreakLabel, continueLabel, labelName));

        _il.MarkLabel(startLabel);
        _il.Emit(OpCodes.Ldloc, enumLocal);
        _il.Emit(OpCodes.Callvirt, moveNext);
        _il.Emit(OpCodes.Brfalse, outerBreakLabel);

        if (varField != null)
        {
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, enumLocal);
            _il.Emit(OpCodes.Callvirt, current);
            _il.Emit(OpCodes.Stfld, varField);
        }
        else
        {
            var varLocal = _il.DeclareLocal(typeof(object));
            _ctx!.Locals.RegisterLocal(varName, varLocal);
            _il.Emit(OpCodes.Ldloc, enumLocal);
            _il.Emit(OpCodes.Callvirt, current);
            _il.Emit(OpCodes.Stloc, varLocal);
        }

        EmitStatement(f.Body);

        _il.MarkLabel(continueLabel);
        _il.Emit(OpCodes.Br, startLabel);

        _loopLabels.Pop();
    }

    private void EmitLabeledDoWhile(string labelName, Stmt.DoWhile dw, Label outerBreakLabel)
    {
        var startLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        _loopLabels.Push((outerBreakLabel, continueLabel, labelName));

        _il.MarkLabel(startLabel);
        EmitStatement(dw.Body);

        _il.MarkLabel(continueLabel);

        EmitExpression(dw.Condition);
        EnsureBoxed();
        EmitTruthyCheck();
        _il.Emit(OpCodes.Brtrue, startLabel);

        _loopLabels.Pop();
    }

    private void EmitLabeledForIn(string labelName, Stmt.ForIn f, Label outerBreakLabel)
    {
        var startLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        string varName = f.Variable.Lexeme;
        var varField = _builder.GetVariableField(varName);

        EmitExpression(f.Object);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetKeys);
        var keysLocal = _il.DeclareLocal(typeof(List<object>));
        _il.Emit(OpCodes.Stloc, keysLocal);

        var indexLocal = _il.DeclareLocal(typeof(int));
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Stloc, indexLocal);

        LocalBuilder? loopVar = null;
        if (varField == null)
        {
            loopVar = _il.DeclareLocal(typeof(object));
            _ctx!.Locals.RegisterLocal(varName, loopVar);
        }

        _loopLabels.Push((outerBreakLabel, continueLabel, labelName));

        _il.MarkLabel(startLabel);

        _il.Emit(OpCodes.Ldloc, indexLocal);
        _il.Emit(OpCodes.Ldloc, keysLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetLength);
        _il.Emit(OpCodes.Clt);
        _il.Emit(OpCodes.Brfalse, outerBreakLabel);

        _il.Emit(OpCodes.Ldloc, keysLocal);
        _il.Emit(OpCodes.Ldloc, indexLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetElement);

        if (varField != null)
        {
            var keyTemp = _il.DeclareLocal(typeof(object));
            _il.Emit(OpCodes.Stloc, keyTemp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, keyTemp);
            _il.Emit(OpCodes.Stfld, varField);
        }
        else
        {
            _il.Emit(OpCodes.Stloc, loopVar!);
        }

        EmitStatement(f.Body);

        _il.MarkLabel(continueLabel);

        _il.Emit(OpCodes.Ldloc, indexLocal);
        _il.Emit(OpCodes.Ldc_I4_1);
        _il.Emit(OpCodes.Add);
        _il.Emit(OpCodes.Stloc, indexLocal);

        _il.Emit(OpCodes.Br, startLabel);

        _loopLabels.Pop();
    }
}
