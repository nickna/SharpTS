using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class GeneratorMoveNextEmitter
{
    private void EmitStatement(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Expression e:
                EmitExpression(e.Expr);
                // Pop unused result if any
                _il.Emit(OpCodes.Pop);
                break;

            case Stmt.Var v:
                EmitVarDeclaration(v);
                break;

            case Stmt.Return r:
                EmitReturn(r);
                break;

            case Stmt.If i:
                EmitIf(i);
                break;

            case Stmt.While w:
                EmitWhile(w);
                break;

            case Stmt.Block b:
                if (b.Statements != null)
                    foreach (var s in b.Statements)
                        EmitStatement(s);
                break;

            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    EmitStatement(s);
                break;

            case Stmt.Print p:
                EmitPrint(p);
                break;

            case Stmt.ForOf f:
                EmitForOf(f);
                break;

            case Stmt.DoWhile dw:
                EmitDoWhile(dw);
                break;

            case Stmt.ForIn fi:
                EmitForIn(fi);
                break;

            case Stmt.Throw t:
                EmitThrow(t);
                break;

            case Stmt.Switch s:
                EmitSwitch(s);
                break;

            case Stmt.TryCatch tc:
                EmitTryCatch(tc);
                break;

            case Stmt.Break b:
                EmitBreak(b);
                break;

            case Stmt.Continue c:
                EmitContinue(c);
                break;

            case Stmt.LabeledStatement ls:
                EmitLabeledStatement(ls);
                break;

            default:
                break;
        }
    }

    private void EmitVarDeclaration(Stmt.Var v)
    {
        string name = v.Name.Lexeme;

        // Check if this variable is hoisted to state machine
        var field = _builder.GetVariableField(name);
        if (field != null)
        {
            // Hoisted variable - store to field
            if (v.Initializer != null)
            {
                EmitExpression(v.Initializer);
                EnsureBoxed();
                var temp = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, temp);
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloc, temp);
                _il.Emit(OpCodes.Stfld, field);
            }
            else
            {
                // Initialize to null
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldnull);
                _il.Emit(OpCodes.Stfld, field);
            }
        }
        else
        {
            // Not hoisted - use local variable
            var local = _il.DeclareLocal(typeof(object));
            _ctx!.Locals.RegisterLocal(name, local);

            if (v.Initializer != null)
            {
                EmitExpression(v.Initializer);
                EnsureBoxed();
                _il.Emit(OpCodes.Stloc, local);
            }
            else
            {
                _il.Emit(OpCodes.Ldnull);
                _il.Emit(OpCodes.Stloc, local);
            }
        }
    }

    private void EmitReturn(Stmt.Return r)
    {
        // Generator return - set state to completed and return false
        // Note: In generators, return value is available via iterator result's done=true

        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Ret);
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

    private void EmitWhile(Stmt.While w)
    {
        var startLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        // Push labels for break/continue
        _loopLabels.Push((endLabel, continueLabel, null));

        _il.MarkLabel(startLabel);
        EmitExpression(w.Condition);
        EnsureBoxed();
        EmitTruthyCheck();
        _il.Emit(OpCodes.Brfalse, endLabel);

        EmitStatement(w.Body);

        _il.MarkLabel(continueLabel);
        _il.Emit(OpCodes.Br, startLabel);

        _il.MarkLabel(endLabel);
        _loopLabels.Pop();
    }

    private void EmitForOf(Stmt.ForOf f)
    {
        string varName = f.Variable.Lexeme;
        var varField = _builder.GetVariableField(varName);

        // Emit iterable
        EmitExpression(f.Iterable);
        EnsureBoxed();

        // Cast to IEnumerable and get enumerator
        var getEnumerator = typeof(System.Collections.IEnumerable).GetMethod("GetEnumerator")!;
        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        var current = typeof(System.Collections.IEnumerator).GetProperty("Current")!.GetGetMethod()!;

        _il.Emit(OpCodes.Castclass, typeof(System.Collections.IEnumerable));
        _il.Emit(OpCodes.Callvirt, getEnumerator);

        var enumLocal = _il.DeclareLocal(typeof(System.Collections.IEnumerator));
        _il.Emit(OpCodes.Stloc, enumLocal);

        var startLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        // Push labels for break/continue
        _loopLabels.Push((endLabel, continueLabel, null));

        _il.MarkLabel(startLabel);
        _il.Emit(OpCodes.Ldloc, enumLocal);
        _il.Emit(OpCodes.Callvirt, moveNext);
        _il.Emit(OpCodes.Brfalse, endLabel);

        // Set loop variable
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

        _il.MarkLabel(endLabel);
        _loopLabels.Pop();
    }

    private void EmitPrint(Stmt.Print p)
    {
        EmitExpression(p.Expr);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.ConsoleLog);
    }

    private void EmitDoWhile(Stmt.DoWhile dw)
    {
        var startLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        _loopLabels.Push((endLabel, continueLabel, null));

        _il.MarkLabel(startLabel);
        EmitStatement(dw.Body);

        _il.MarkLabel(continueLabel);

        EmitExpression(dw.Condition);
        EnsureBoxed();
        EmitTruthyCheck();
        _il.Emit(OpCodes.Brtrue, startLabel);

        _il.MarkLabel(endLabel);
        _loopLabels.Pop();
    }

    private void EmitForIn(Stmt.ForIn f)
    {
        var startLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
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

        _loopLabels.Push((endLabel, continueLabel, null));

        _il.MarkLabel(startLabel);

        _il.Emit(OpCodes.Ldloc, indexLocal);
        _il.Emit(OpCodes.Ldloc, keysLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetLength);
        _il.Emit(OpCodes.Clt);
        _il.Emit(OpCodes.Brfalse, endLabel);

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

        _il.MarkLabel(endLabel);
        _loopLabels.Pop();
    }

    private void EmitThrow(Stmt.Throw t)
    {
        EmitExpression(t.Value);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateException);
        _il.Emit(OpCodes.Throw);
    }

    private void EmitSwitch(Stmt.Switch s)
    {
        var endLabel = _il.DefineLabel();
        var defaultLabel = _il.DefineLabel();
        var caseLabels = s.Cases.Select(_ => _il.DefineLabel()).ToList();

        EmitExpression(s.Subject);
        EnsureBoxed();
        var subjectLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, subjectLocal);

        for (int i = 0; i < s.Cases.Count; i++)
        {
            _il.Emit(OpCodes.Ldloc, subjectLocal);
            EmitExpression(s.Cases[i].Value);
            EnsureBoxed();
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.Equals);
            _il.Emit(OpCodes.Brtrue, caseLabels[i]);
        }

        if (s.DefaultBody == null)
        {
            _il.Emit(OpCodes.Br, endLabel);
        }
        else
        {
            _il.Emit(OpCodes.Br, defaultLabel);
        }

        for (int i = 0; i < s.Cases.Count; i++)
        {
            _il.MarkLabel(caseLabels[i]);
            foreach (var stmt in s.Cases[i].Body)
            {
                if (stmt is Stmt.Break)
                {
                    _il.Emit(OpCodes.Br, endLabel);
                }
                else
                {
                    EmitStatement(stmt);
                }
            }
        }

        if (s.DefaultBody != null)
        {
            _il.MarkLabel(defaultLabel);
            foreach (var stmt in s.DefaultBody)
            {
                if (stmt is Stmt.Break)
                {
                    _il.Emit(OpCodes.Br, endLabel);
                }
                else
                {
                    EmitStatement(stmt);
                }
            }
        }

        _il.MarkLabel(endLabel);
    }

    private void EmitTryCatch(Stmt.TryCatch t)
    {
        _il.BeginExceptionBlock();

        foreach (var stmt in t.TryBlock)
            EmitStatement(stmt);

        if (t.CatchBlock != null)
        {
            _il.BeginCatchBlock(typeof(Exception));

            if (t.CatchParam != null)
            {
                var exLocal = _il.DeclareLocal(typeof(object));
                _ctx!.Locals.RegisterLocal(t.CatchParam.Lexeme, exLocal);
                _il.Emit(OpCodes.Call, _ctx.Runtime!.WrapException);
                _il.Emit(OpCodes.Stloc, exLocal);
            }
            else
            {
                _il.Emit(OpCodes.Pop);
            }

            foreach (var stmt in t.CatchBlock)
                EmitStatement(stmt);
        }

        if (t.FinallyBlock != null)
        {
            _il.BeginFinallyBlock();
            foreach (var stmt in t.FinallyBlock)
                EmitStatement(stmt);
        }

        _il.EndExceptionBlock();
    }

    private void EmitBreak(Stmt.Break b)
    {
        if (b.Label != null)
        {
            foreach (var loop in _loopLabels)
            {
                if (loop.LabelName == b.Label.Lexeme)
                {
                    _il.Emit(OpCodes.Br, loop.BreakLabel);
                    return;
                }
            }
        }
        else if (_loopLabels.Count > 0)
        {
            _il.Emit(OpCodes.Br, _loopLabels.Peek().BreakLabel);
        }
    }

    private void EmitContinue(Stmt.Continue c)
    {
        if (c.Label != null)
        {
            foreach (var loop in _loopLabels)
            {
                if (loop.LabelName == c.Label.Lexeme)
                {
                    _il.Emit(OpCodes.Br, loop.ContinueLabel);
                    return;
                }
            }
        }
        else if (_loopLabels.Count > 0)
        {
            _il.Emit(OpCodes.Br, _loopLabels.Peek().ContinueLabel);
        }
    }

    private void EmitLabeledStatement(Stmt.LabeledStatement ls)
    {
        var breakLabel = _il.DefineLabel();

        if (ls.Statement is Stmt.While or Stmt.ForOf or Stmt.DoWhile or Stmt.ForIn)
        {
            EmitLabeledLoop(ls.Label.Lexeme, ls.Statement, breakLabel);
        }
        else
        {
            _loopLabels.Push((breakLabel, breakLabel, ls.Label.Lexeme));
            EmitStatement(ls.Statement);
            _loopLabels.Pop();
        }

        _il.MarkLabel(breakLabel);
    }

    private void EmitLabeledLoop(string labelName, Stmt loopStmt, Label outerBreakLabel)
    {
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
