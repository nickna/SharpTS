using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncGeneratorMoveNextEmitter
{
    public override void EmitStatement(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Expression e:
                EmitExpression(e.Expr);
                _il.Emit(OpCodes.Pop);
                break;

            case Stmt.Var v:
                EmitVarDeclaration(v);
                break;

            case Stmt.Const c:
                EmitVarDeclaration(new Stmt.Var(c.Name, c.TypeAnnotation, c.Initializer));
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

            case Stmt.For f:
                EmitFor(f);
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

    protected override void EmitVarDeclaration(Stmt.Var v)
    {
        string name = v.Name.Lexeme;

        var field = _builder.GetVariableField(name);
        if (field != null)
        {
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
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldnull);
                _il.Emit(OpCodes.Stfld, field);
            }
        }
        else
        {
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

    protected override void EmitReturn(Stmt.Return r)
    {
        // Async generator return - store return value in Current and set state to completed
        // The return value will be available in the {value: returnValue, done: true} result
        if (r.Value != null)
        {
            // Evaluate return value and store in CurrentField
            _il.Emit(OpCodes.Ldarg_0);
            EmitExpression(r.Value);
            EnsureBoxed();
            _il.Emit(OpCodes.Stfld, _builder.CurrentField);
        }

        // Set state to -2 (completed)
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, -2);
        _il.Emit(OpCodes.Stfld, _builder.StateField);
        EmitReturnValueTaskBool(false);
    }

    protected override void EmitIf(Stmt.If i)
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

    protected override void EmitWhile(Stmt.While w)
    {
        var startLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

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

    protected override void EmitForOf(Stmt.ForOf f)
    {
        if (f.IsAsync)
        {
            EmitForAwaitOf(f);
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
        var endLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        _loopLabels.Push((endLabel, continueLabel, null));

        _il.MarkLabel(startLabel);
        _il.Emit(OpCodes.Ldloc, enumLocal);
        _il.Emit(OpCodes.Callvirt, moveNext);
        _il.Emit(OpCodes.Brfalse, endLabel);

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

    private void EmitForAwaitOf(Stmt.ForOf f)
    {
        // for await...of iterates over async iterables
        // We use the $IAsyncGenerator.next() method which returns Task<object>
        // The result is a dictionary with { value, done } properties

        string varName = f.Variable.Lexeme;
        var varField = _builder.GetVariableField(varName);

        // Emit the async iterable expression
        EmitExpression(f.Iterable);
        EnsureBoxed();

        // Cast to $IAsyncGenerator interface
        var asyncGenInterface = _ctx!.Runtime!.AsyncGeneratorInterfaceType;
        _il.Emit(OpCodes.Castclass, asyncGenInterface);

        // Store the async generator in a local
        var asyncGenLocal = _il.DeclareLocal(asyncGenInterface);
        _il.Emit(OpCodes.Stloc, asyncGenLocal);

        var startLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();
        var continueLabel = _il.DefineLabel();

        _loopLabels.Push((endLabel, continueLabel, null));

        _il.MarkLabel(startLabel);

        // Call next() which returns Task<object>
        _il.Emit(OpCodes.Ldloc, asyncGenLocal);
        _il.Emit(OpCodes.Callvirt, _ctx.Runtime.AsyncGeneratorNextMethod);

        // Await the Task<object> - get result synchronously for now
        // (full async continuation would require state machine suspension)
        var taskLocal = _il.DeclareLocal(_types.TaskOfObject);
        _il.Emit(OpCodes.Stloc, taskLocal);
        _il.Emit(OpCodes.Ldloc, taskLocal);
        var getAwaiter = _types.GetMethodNoParams(_types.TaskOfObject, "GetAwaiter");
        _il.Emit(OpCodes.Call, getAwaiter);
        var awaiterLocal = _il.DeclareLocal(_types.TaskAwaiterOfObject);
        _il.Emit(OpCodes.Stloc, awaiterLocal);

        // Result local for storing the result
        var resultLocal = _il.DeclareLocal(_types.Object);
        var getResultSuccessLabel = _il.DefineLabel();

        // Wrap GetResult() in try-catch for proper error propagation from rejected promises
        _il.BeginExceptionBlock();

        _il.Emit(OpCodes.Ldloca, awaiterLocal);
        var getResult = _types.GetMethodNoParams(_types.TaskAwaiterOfObject, "GetResult");
        _il.Emit(OpCodes.Call, getResult);
        _il.Emit(OpCodes.Stloc, resultLocal);
        _il.Emit(OpCodes.Leave, getResultSuccessLabel);

        _il.BeginCatchBlock(typeof(Exception));
        // Re-throw wrapped exception for proper propagation
        _il.Emit(OpCodes.Call, _ctx.Runtime.WrapException);
        _il.Emit(OpCodes.Call, _ctx.Runtime.CreateException);
        _il.Emit(OpCodes.Throw);
        _il.EndExceptionBlock();

        _il.MarkLabel(getResultSuccessLabel);

        // Result is a Dictionary<string, object> with { value, done }
        _il.Emit(OpCodes.Stloc, resultLocal);

        // Check if done: GetProperty(result, "done")
        // IMPORTANT: Use strict boolean check, not IsTruthy - IsTruthy treats 0 as falsy
        // which would incorrectly end the loop when yielding zero
        _il.Emit(OpCodes.Ldloc, resultLocal);
        _il.Emit(OpCodes.Ldstr, "done");
        _il.Emit(OpCodes.Call, _ctx.Runtime.GetProperty);

        // Check if it's a boxed bool true (strict check)
        var notDoneLabel = _il.DefineLabel();
        _il.Emit(OpCodes.Isinst, typeof(bool));
        _il.Emit(OpCodes.Brfalse, notDoneLabel);  // Not a bool - continue loop

        // It's a bool - unbox and check if true
        _il.Emit(OpCodes.Ldloc, resultLocal);
        _il.Emit(OpCodes.Ldstr, "done");
        _il.Emit(OpCodes.Call, _ctx.Runtime.GetProperty);
        _il.Emit(OpCodes.Unbox_Any, typeof(bool));
        _il.Emit(OpCodes.Brtrue, endLabel);  // done === true -> exit loop

        _il.MarkLabel(notDoneLabel);

        // Get value: GetProperty(result, "value")
        _il.Emit(OpCodes.Ldloc, resultLocal);
        _il.Emit(OpCodes.Ldstr, "value");
        _il.Emit(OpCodes.Call, _ctx.Runtime.GetProperty);

        // Assign to loop variable
        if (varField != null)
        {
            var valueTemp = _il.DeclareLocal(_types.Object);
            _il.Emit(OpCodes.Stloc, valueTemp);
            _il.Emit(OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldloc, valueTemp);
            _il.Emit(OpCodes.Stfld, varField);
        }
        else
        {
            var varLocal = _il.DeclareLocal(_types.Object);
            _ctx.Locals.RegisterLocal(varName, varLocal);
            _il.Emit(OpCodes.Stloc, varLocal);
        }

        EmitStatement(f.Body);

        _il.MarkLabel(continueLabel);
        _il.Emit(OpCodes.Br, startLabel);

        _il.MarkLabel(endLabel);
        _loopLabels.Pop();
    }

    protected override void EmitPrint(Stmt.Print p)
    {
        EmitExpression(p.Expr);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.ConsoleLog);
    }

    protected override void EmitDoWhile(Stmt.DoWhile dw)
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

    protected override void EmitForIn(Stmt.ForIn f)
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

    protected override void EmitThrow(Stmt.Throw t)
    {
        EmitExpression(t.Value);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateException);
        _il.Emit(OpCodes.Throw);
    }

    protected override void EmitSwitch(Stmt.Switch s)
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
            _il.Emit(OpCodes.Br, endLabel);
        else
            _il.Emit(OpCodes.Br, defaultLabel);

        for (int i = 0; i < s.Cases.Count; i++)
        {
            _il.MarkLabel(caseLabels[i]);
            foreach (var stmt in s.Cases[i].Body)
            {
                if (stmt is Stmt.Break)
                    _il.Emit(OpCodes.Br, endLabel);
                else
                    EmitStatement(stmt);
            }
        }

        if (s.DefaultBody != null)
        {
            _il.MarkLabel(defaultLabel);
            foreach (var stmt in s.DefaultBody)
            {
                if (stmt is Stmt.Break)
                    _il.Emit(OpCodes.Br, endLabel);
                else
                    EmitStatement(stmt);
            }
        }

        _il.MarkLabel(endLabel);
    }

    protected override void EmitTryCatch(Stmt.TryCatch t)
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

    protected override void EmitBreak(Stmt.Break b)
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

    protected override void EmitContinue(Stmt.Continue c)
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

    protected override void EmitLabeledStatement(Stmt.LabeledStatement ls)
    {
        var breakLabel = _il.DefineLabel();
        _loopLabels.Push((breakLabel, breakLabel, ls.Label.Lexeme));
        EmitStatement(ls.Statement);
        _loopLabels.Pop();
        _il.MarkLabel(breakLabel);
    }
}
