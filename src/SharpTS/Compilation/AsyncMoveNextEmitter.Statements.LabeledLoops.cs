using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    protected override void EmitLabeledStatement(Stmt.LabeledStatement ls)
    {
        // Create the break target shared by every label in this chain (they all wrap the same
        // statement, so they all break to the same point).
        var breakLabel = _il.DefineLabel();

        // Flatten a chain of labels (`p: q: r: loop`) down to the innermost statement, collecting
        // every label. A chain that bottoms out in a loop must hand ALL of its labels to that loop's
        // scope so `continue <outerLabel>` resolves to the loop's CONTINUE target. The pre-flatten
        // code only special-cased a label directly on a loop; an outer label of a chain (whose own
        // `.Statement` is another labeled statement, not a loop) fell into the non-loop branch below
        // with continue == break, so `continue p` exited the loop instead of advancing it. Sibling of
        // the sync #580 fix, which the async labeled-loop subsystem here did not share. (#704)
        var labelNames = new List<string>();
        Stmt inner = ls;
        while (inner is Stmt.LabeledStatement labeled)
        {
            labelNames.Add(labeled.Label.Lexeme);
            inner = labeled.Statement;
        }

        if (inner is Stmt.While or Stmt.For or Stmt.ForOf or Stmt.DoWhile or Stmt.ForIn)
        {
            // Emit the loop with every chain label attached to its break/continue targets.
            EmitLabeledLoop(labelNames, inner, breakLabel);
        }
        else
        {
            // Non-loop labeled statement(s) (a labeled block, etc.): each label is a break-only
            // target (continue == break is unreachable — `continue` to a non-iteration label is a
            // syntax error). Push every chain label so a `break <anyLabel>` exits the statement.
            foreach (var name in labelNames)
                EnterLoop(breakLabel, breakLabel, name);
            EmitStatement(inner);
            for (int i = 0; i < labelNames.Count; i++)
                ExitLoop();
        }

        _il.MarkLabel(breakLabel);
    }

    private void EmitLabeledLoop(IReadOnlyList<string> labelNames, Stmt loopStmt, Label outerBreakLabel)
    {
        // Emit a loop with the chain's label names attached
        switch (loopStmt)
        {
            case Stmt.While w:
                EmitLabeledWhile(labelNames, w, outerBreakLabel);
                break;
            case Stmt.ForOf f:
                EmitLabeledForOf(labelNames, f, outerBreakLabel);
                break;
            case Stmt.DoWhile dw:
                EmitLabeledDoWhile(labelNames, dw, outerBreakLabel);
                break;
            case Stmt.For f:
                EmitLabeledFor(labelNames, f, outerBreakLabel);
                break;
            case Stmt.ForIn fi:
                EmitLabeledForIn(labelNames, fi, outerBreakLabel);
                break;
        }
    }

    /// <summary>
    /// Pushes one loop-label entry per chain label, all sharing the same break/continue targets, so
    /// <see cref="StatementEmitterBase.FindLabeledLoop"/> resolves any of the chain's labels to this
    /// loop. (The async emitter reuses the base single-label loop stack; a chain just registers
    /// several entries at the same targets — see #704.)
    /// </summary>
    private void EnterLabeledLoop(Label breakLabel, Label continueLabel, IReadOnlyList<string> labelNames)
    {
        foreach (var name in labelNames)
            EnterLoop(breakLabel, continueLabel, name);
    }

    /// <summary>Pops the per-label entries pushed by <see cref="EnterLabeledLoop"/>.</summary>
    private void ExitLabeledLoop(IReadOnlyList<string> labelNames)
    {
        for (int i = 0; i < labelNames.Count; i++)
            ExitLoop();
    }

    private void EmitLabeledWhile(IReadOnlyList<string> labelNames, Stmt.While w, Label outerBreakLabel)
    {
        var startLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        // Push labels with the label names
        EnterLabeledLoop(outerBreakLabel, continueLabel, labelNames);

        _il.MarkLabel(startLabel);
        EmitExpression(w.Condition);
        EnsureBoxed();
        EmitTruthyCheck();
        _il.Emit(OpCodes.Brfalse, outerBreakLabel);

        EmitStatement(w.Body);

        _il.MarkLabel(continueLabel);
        _il.Emit(OpCodes.Br, startLabel);

        ExitLabeledLoop(labelNames);
    }

    private void EmitLabeledForOf(IReadOnlyList<string> labelNames, Stmt.ForOf f, Label outerBreakLabel)
    {
        if (f.IsAsync)
        {
            // for await: route to the suspending async-iterator lowering, carrying the chain's labels so
            // `break`/`continue <label>` target this loop. Its natural end falls through to
            // outerBreakLabel (marked right after by EmitLabeledStatement), so an early break runs the
            // iterator's return() cleanup, then exits. The previous synchronous enumeration here ignored
            // f.IsAsync and left the reserved await-state labels unmarked → compile failure (#728).
            EmitForAwaitOf(f, labelNames);
            return;
        }

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

        EnterLabeledLoop(outerBreakLabel, continueLabel, labelNames);

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

        ExitLabeledLoop(labelNames);
    }

    private void EmitLabeledDoWhile(IReadOnlyList<string> labelNames, Stmt.DoWhile dw, Label outerBreakLabel)
    {
        var startLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        EnterLabeledLoop(outerBreakLabel, continueLabel, labelNames);

        _il.MarkLabel(startLabel);
        EmitStatement(dw.Body);

        _il.MarkLabel(continueLabel);

        EmitExpression(dw.Condition);
        EnsureBoxed();
        EmitTruthyCheck();
        _il.Emit(OpCodes.Brtrue, startLabel);

        ExitLabeledLoop(labelNames);
    }

    private void EmitLabeledForIn(IReadOnlyList<string> labelNames, Stmt.ForIn f, Label outerBreakLabel)
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

        EnterLabeledLoop(outerBreakLabel, continueLabel, labelNames);

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

        ExitLabeledLoop(labelNames);
    }

    private void EmitLabeledFor(IReadOnlyList<string> labelNames, Stmt.For f, Label outerBreakLabel)
    {
        // Emit initializer (once, outside the loop)
        if (f.Initializer != null)
            EmitStatement(f.Initializer);

        var startLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();  // Points to increment

        // Push labels with the label names
        EnterLabeledLoop(outerBreakLabel, continueLabel, labelNames);

        _il.MarkLabel(startLabel);

        // Check condition (if present)
        if (f.Condition != null)
        {
            EmitExpression(f.Condition);
            EnsureBoxed();
            EmitTruthyCheck();
            _il.Emit(OpCodes.Brfalse, outerBreakLabel);
        }

        // Emit body
        EmitStatement(f.Body);

        // Continue target: increment goes here
        _il.MarkLabel(continueLabel);
        if (f.Increment != null)
        {
            EmitExpression(f.Increment);
            EnsureBoxed();
            _il.Emit(OpCodes.Pop);  // Discard increment result
        }

        _il.Emit(OpCodes.Br, startLabel);

        ExitLabeledLoop(labelNames);
    }
}
