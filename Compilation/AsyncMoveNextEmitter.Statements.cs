using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
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

            case Stmt.TryCatch t:
                EmitTryCatch(t);
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

            // Skip other statements for now
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
                // Emit expression first (important: may contain await which clears stack)
                EmitExpression(v.Initializer);
                EnsureBoxed();
                // Now store to field - load 'this' after await completes
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

        // Push labels for break/continue
        _loopLabels.Push((endLabel, continueLabel, null));

        // Body executes at least once
        _il.MarkLabel(startLabel);
        EmitStatement(dw.Body);

        // Continue target is after the body, before condition check
        _il.MarkLabel(continueLabel);

        // Evaluate condition
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

        // Evaluate object and get keys
        EmitExpression(f.Object);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetKeys);
        var keysLocal = _il.DeclareLocal(typeof(List<object>));
        _il.Emit(OpCodes.Stloc, keysLocal);

        // Create index variable
        var indexLocal = _il.DeclareLocal(typeof(int));
        _il.Emit(OpCodes.Ldc_I4_0);
        _il.Emit(OpCodes.Stloc, indexLocal);

        // Loop variable (holds current key)
        LocalBuilder? loopVar = null;
        if (varField == null)
        {
            loopVar = _il.DeclareLocal(typeof(object));
            _ctx!.Locals.RegisterLocal(varName, loopVar);
        }

        // Push labels for break/continue
        _loopLabels.Push((endLabel, continueLabel, null));

        _il.MarkLabel(startLabel);

        // Check if index < keys.Count
        _il.Emit(OpCodes.Ldloc, indexLocal);
        _il.Emit(OpCodes.Ldloc, keysLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetLength);
        _il.Emit(OpCodes.Clt);
        _il.Emit(OpCodes.Brfalse, endLabel);

        // Get current key: keys[index]
        _il.Emit(OpCodes.Ldloc, keysLocal);
        _il.Emit(OpCodes.Ldloc, indexLocal);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetElement);

        // Store to loop variable
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

        // Emit body
        EmitStatement(f.Body);

        _il.MarkLabel(continueLabel);

        // Increment index
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

        // Evaluate subject once
        EmitExpression(s.Subject);
        EnsureBoxed();
        var subjectLocal = _il.DeclareLocal(typeof(object));
        _il.Emit(OpCodes.Stloc, subjectLocal);

        // Generate case comparisons
        for (int i = 0; i < s.Cases.Count; i++)
        {
            _il.Emit(OpCodes.Ldloc, subjectLocal);
            EmitExpression(s.Cases[i].Value);
            EnsureBoxed();
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.Equals);
            _il.Emit(OpCodes.Brtrue, caseLabels[i]);
        }

        // Jump to default or end
        if (s.DefaultBody == null)
        {
            _il.Emit(OpCodes.Br, endLabel);
        }
        else
        {
            _il.Emit(OpCodes.Br, defaultLabel);
        }

        // Emit case bodies
        for (int i = 0; i < s.Cases.Count; i++)
        {
            _il.MarkLabel(caseLabels[i]);
            foreach (var stmt in s.Cases[i].Body)
            {
                if (stmt is Stmt.Break)
                {
                    // Unlabeled break - exits switch only
                    _il.Emit(OpCodes.Br, endLabel);
                }
                else
                {
                    EmitStatement(stmt);
                }
            }
            // Fall through if no break
        }

        // Default case
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
        // Check if this try block contains any await points
        bool hasAwaitsInTry = ContainsAwait(t.TryBlock);
        bool hasAwaitsInCatch = t.CatchBlock != null && ContainsAwait(t.CatchBlock);
        bool hasAwaitsInFinally = t.FinallyBlock != null && ContainsAwait(t.FinallyBlock);

        if (hasAwaitsInTry || hasAwaitsInCatch || hasAwaitsInFinally)
        {
            // Complex case: await inside protected region
            EmitTryCatchWithAwaits(t, hasAwaitsInTry, hasAwaitsInCatch, hasAwaitsInFinally);
        }
        else
        {
            // Simple case: no awaits in protected regions
            EmitSimpleTryCatch(t);
        }
    }

    private void EmitSimpleTryCatch(Stmt.TryCatch t)
    {
        _il.BeginExceptionBlock();

        // Emit try block statements
        foreach (var stmt in t.TryBlock)
            EmitStatement(stmt);

        // Emit catch block if present
        if (t.CatchBlock != null)
        {
            _il.BeginCatchBlock(typeof(Exception));

            if (t.CatchParam != null)
            {
                // Create local for the exception parameter
                var exLocal = _il.DeclareLocal(typeof(object));
                _ctx!.Locals.RegisterLocal(t.CatchParam.Lexeme, exLocal);

                // Wrap the .NET exception to TypeScript exception object
                _il.Emit(OpCodes.Call, _ctx.Runtime!.WrapException);
                _il.Emit(OpCodes.Stloc, exLocal);
            }
            else
            {
                // No catch parameter - just pop the exception
                _il.Emit(OpCodes.Pop);
            }

            // Emit catch block statements
            foreach (var stmt in t.CatchBlock)
                EmitStatement(stmt);
        }

        // Emit finally block if present
        if (t.FinallyBlock != null)
        {
            _il.BeginFinallyBlock();
            foreach (var stmt in t.FinallyBlock)
                EmitStatement(stmt);
        }

        _il.EndExceptionBlock();
    }

    private void EmitTryCatchWithAwaits(Stmt.TryCatch t, bool hasAwaitsInTry, bool hasAwaitsInCatch, bool hasAwaitsInFinally)
    {
        // For try blocks with awaits, we need to use a flag-based approach:
        // 1. Use a local to track if an exception was caught
        // 2. Emit try body with special handling around each await
        // 3. Check exception flag after try/await completion

        // Create locals for exception tracking
        var caughtExceptionLocal = _il.DeclareLocal(typeof(object));
        var skipCatchLabel = _il.DefineLabel();
        var afterTryCatchLabel = _il.DefineLabel();

        // Initialize caught exception to null
        _il.Emit(OpCodes.Ldnull);
        _il.Emit(OpCodes.Stloc, caughtExceptionLocal);

        // If there are awaits in finally, set up pending return tracking
        // This allows return statements inside try to defer to after finally runs
        LocalBuilder? pendingReturnLocal = null;
        Label? afterFinallyLabel = null;
        var previousPendingReturnLocal = _pendingReturnFlagLocal;
        var previousAfterFinallyLabel = _afterFinallyLabel;

        if (hasAwaitsInFinally && t.FinallyBlock != null)
        {
            pendingReturnLocal = _il.DeclareLocal(typeof(bool));
            afterFinallyLabel = _il.DefineLabel();

            // Initialize pending return flag to false
            _il.Emit(OpCodes.Ldc_I4_0);
            _il.Emit(OpCodes.Stloc, pendingReturnLocal);

            // Set context for return statements to use
            _pendingReturnFlagLocal = pendingReturnLocal;
            _afterFinallyLabel = afterFinallyLabel;
        }

        if (hasAwaitsInTry)
        {
            // Emit try body with segmented exception handling
            EmitTryBodyWithAwaits(t.TryBlock, caughtExceptionLocal);
        }
        else if (hasAwaitsInFinally)
        {
            // No awaits in try but awaits in finally - need to capture exception from try
            // so we can run the finally with awaits before rethrowing
            _il.BeginExceptionBlock();
            foreach (var stmt in t.TryBlock)
                EmitStatement(stmt);

            // Always catch to capture exception for finally handling
            _il.BeginCatchBlock(typeof(Exception));
            _il.Emit(OpCodes.Call, _ctx!.Runtime!.WrapException);
            _il.Emit(OpCodes.Stloc, caughtExceptionLocal);

            _il.EndExceptionBlock();
        }
        else
        {
            // No awaits in try or finally - use normal try block
            _il.BeginExceptionBlock();
            foreach (var stmt in t.TryBlock)
                EmitStatement(stmt);

            if (t.CatchBlock != null)
            {
                _il.BeginCatchBlock(typeof(Exception));
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.WrapException);
                _il.Emit(OpCodes.Stloc, caughtExceptionLocal);
            }
            _il.EndExceptionBlock();
        }

        // Check if we need to run catch block
        if (t.CatchBlock != null)
        {
            _il.Emit(OpCodes.Ldloc, caughtExceptionLocal);
            _il.Emit(OpCodes.Brfalse, skipCatchLabel);

            // Store exception in catch param if needed
            if (t.CatchParam != null)
            {
                var exLocal = _il.DeclareLocal(typeof(object));
                _ctx!.Locals.RegisterLocal(t.CatchParam.Lexeme, exLocal);
                _il.Emit(OpCodes.Ldloc, caughtExceptionLocal);
                _il.Emit(OpCodes.Stloc, exLocal);
            }

            // Clear the exception local since catch handled it
            _il.Emit(OpCodes.Ldnull);
            _il.Emit(OpCodes.Stloc, caughtExceptionLocal);

            // Emit catch block (may contain awaits)
            // If we're inside an outer try context, wrap catch statements in try/catch
            // so that throws propagate to the outer try's exception handling
            var outerExceptionLocal = _currentTryCatchExceptionLocal;
            if (outerExceptionLocal != null && outerExceptionLocal != caughtExceptionLocal)
            {
                // We're nested inside another try-with-awaits
                // Wrap catch block in try/catch to propagate exceptions outward
                _il.BeginExceptionBlock();
                foreach (var stmt in t.CatchBlock)
                    EmitStatement(stmt);
                _il.BeginCatchBlock(typeof(Exception));
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.WrapException);
                _il.Emit(OpCodes.Stloc, outerExceptionLocal);
                _il.EndExceptionBlock();
            }
            else
            {
                foreach (var stmt in t.CatchBlock)
                    EmitStatement(stmt);
            }

            // Fall through to finally (don't skip it)
            _il.MarkLabel(skipCatchLabel);
        }

        // Finally block - must always execute
        if (t.FinallyBlock != null)
        {
            // Mark label for return statements inside try to jump to finally
            if (afterFinallyLabel != null)
                _il.MarkLabel(afterFinallyLabel.Value);

            if (hasAwaitsInFinally)
            {
                // Finally with awaits - need special handling
                // The finally must run regardless of exception, so we emit it
                // and track if there's a pending exception to rethrow after
                EmitFinallyBodyWithAwaits(t.FinallyBlock, caughtExceptionLocal);
            }
            else
            {
                // No awaits in finally - emit directly
                foreach (var stmt in t.FinallyBlock)
                    EmitStatement(stmt);
            }

            // After finally, check if we need to rethrow a pending exception
            // (but only if there was no catch block that handled it)
            if (t.CatchBlock == null)
            {
                var noExceptionLabel = _il.DefineLabel();
                _il.Emit(OpCodes.Ldloc, caughtExceptionLocal);
                _il.Emit(OpCodes.Brfalse, noExceptionLabel);

                // Rethrow the exception
                _il.Emit(OpCodes.Ldloc, caughtExceptionLocal);
                _il.Emit(OpCodes.Call, _ctx!.Runtime!.CreateException);
                _il.Emit(OpCodes.Throw);

                _il.MarkLabel(noExceptionLabel);
            }

            // After finally, check if there was a pending return
            if (pendingReturnLocal != null)
            {
                var noPendingReturnLabel = _il.DefineLabel();
                _il.Emit(OpCodes.Ldloc, pendingReturnLocal);
                _il.Emit(OpCodes.Brfalse, noPendingReturnLabel);

                // Pending return - jump to set result
                _il.Emit(OpCodes.Leave, _setResultLabel);

                _il.MarkLabel(noPendingReturnLabel);
            }
        }

        // Restore previous context
        _pendingReturnFlagLocal = previousPendingReturnLocal;
        _afterFinallyLabel = previousAfterFinallyLabel;

        _il.MarkLabel(afterTryCatchLabel);
    }

    private void EmitFinallyBodyWithAwaits(List<Stmt> finallyBody, LocalBuilder caughtExceptionLocal)
    {
        // For finally with awaits, we use a similar strategy to try with awaits:
        // - Segment the code around awaits
        // - Each segment runs regardless of exception state
        // - After all awaits complete, we can rethrow if needed

        // Unlike try, we don't wrap in try/catch - finally must run even if
        // statements throw, but if finally itself throws, that replaces the original exception

        foreach (var stmt in finallyBody)
        {
            EmitStatement(stmt);
        }
    }

    private void EmitTryBodyWithAwaits(List<Stmt> tryBody, LocalBuilder caughtExceptionLocal)
    {
        // Strategy:
        // 1. Wrap synchronous segments in try blocks
        // 2. For awaits, wrap the GetResult call in try/catch using context fields
        // 3. After each await, check if an exception was caught

        // Set context for await exception handling
        var previousExceptionLocal = _currentTryCatchExceptionLocal;
        var afterTryLabel = _il.DefineLabel();
        _currentTryCatchExceptionLocal = caughtExceptionLocal;
        _currentTryCatchSkipLabel = afterTryLabel;

        var stmtsBeforeAwait = new List<Stmt>();

        foreach (var stmt in tryBody)
        {
            if (ContainsAwait([stmt]))
            {
                // Emit accumulated statements in a try block
                if (stmtsBeforeAwait.Count > 0)
                {
                    // Check if exception was already caught
                    var skipSegmentLabel = _il.DefineLabel();
                    _il.Emit(OpCodes.Ldloc, caughtExceptionLocal);
                    _il.Emit(OpCodes.Brtrue, skipSegmentLabel);

                    EmitSegmentInTry(stmtsBeforeAwait, caughtExceptionLocal);
                    _il.MarkLabel(skipSegmentLabel);
                    stmtsBeforeAwait.Clear();
                }

                // Check if exception was caught before continuing with await
                var skipAwaitLabel = _il.DefineLabel();
                _il.Emit(OpCodes.Ldloc, caughtExceptionLocal);
                _il.Emit(OpCodes.Brtrue, skipAwaitLabel);

                // Emit the statement containing await
                // EmitAwait will check _currentTryCatchExceptionLocal and wrap GetResult in try/catch
                EmitStatement(stmt);

                _il.MarkLabel(skipAwaitLabel);
            }
            else
            {
                stmtsBeforeAwait.Add(stmt);
            }
        }

        // Emit remaining statements in a try block
        if (stmtsBeforeAwait.Count > 0)
        {
            // Check if exception was already caught
            var skipLabel = _il.DefineLabel();
            _il.Emit(OpCodes.Ldloc, caughtExceptionLocal);
            _il.Emit(OpCodes.Brtrue, skipLabel);

            EmitSegmentInTry(stmtsBeforeAwait, caughtExceptionLocal);
            _il.MarkLabel(skipLabel);
        }

        _il.MarkLabel(afterTryLabel);

        // Restore context
        _currentTryCatchExceptionLocal = previousExceptionLocal;
        _currentTryCatchSkipLabel = null;
    }

    private void EmitSegmentInTry(List<Stmt> statements, LocalBuilder caughtExceptionLocal)
    {
        _il.BeginExceptionBlock();
        foreach (var stmt in statements)
            EmitStatement(stmt);

        _il.BeginCatchBlock(typeof(Exception));
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.WrapException);
        _il.Emit(OpCodes.Stloc, caughtExceptionLocal);

        _il.EndExceptionBlock();
    }

    private bool ContainsAwait(List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            if (ContainsAwaitInStmt(stmt))
                return true;
        }
        return false;
    }

    private bool ContainsAwaitInStmt(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Expression e:
                return ContainsAwaitInExpr(e.Expr);
            case Stmt.Var v:
                return v.Initializer != null && ContainsAwaitInExpr(v.Initializer);
            case Stmt.Return r:
                return r.Value != null && ContainsAwaitInExpr(r.Value);
            case Stmt.If i:
                return ContainsAwaitInExpr(i.Condition) ||
                       ContainsAwaitInStmt(i.ThenBranch) ||
                       (i.ElseBranch != null && ContainsAwaitInStmt(i.ElseBranch));
            case Stmt.While w:
                return ContainsAwaitInExpr(w.Condition) || ContainsAwaitInStmt(w.Body);
            case Stmt.Block b:
                return ContainsAwait(b.Statements);
            case Stmt.Sequence seq:
                return ContainsAwait(seq.Statements);
            case Stmt.TryCatch t:
                return ContainsAwait(t.TryBlock) ||
                       (t.CatchBlock != null && ContainsAwait(t.CatchBlock)) ||
                       (t.FinallyBlock != null && ContainsAwait(t.FinallyBlock));
            default:
                return false;
        }
    }

    private bool ContainsAwaitInExpr(Expr expr)
    {
        switch (expr)
        {
            case Expr.Await:
                return true;
            case Expr.Binary b:
                return ContainsAwaitInExpr(b.Left) || ContainsAwaitInExpr(b.Right);
            case Expr.Logical l:
                return ContainsAwaitInExpr(l.Left) || ContainsAwaitInExpr(l.Right);
            case Expr.Unary u:
                return ContainsAwaitInExpr(u.Right);
            case Expr.Grouping g:
                return ContainsAwaitInExpr(g.Expression);
            case Expr.Call c:
                if (ContainsAwaitInExpr(c.Callee)) return true;
                foreach (var arg in c.Arguments)
                    if (ContainsAwaitInExpr(arg)) return true;
                return false;
            case Expr.Assign a:
                return ContainsAwaitInExpr(a.Value);
            case Expr.Ternary t:
                return ContainsAwaitInExpr(t.Condition) ||
                       ContainsAwaitInExpr(t.ThenBranch) ||
                       ContainsAwaitInExpr(t.ElseBranch);
            case Expr.Get g:
                return ContainsAwaitInExpr(g.Object);
            case Expr.Set s:
                return ContainsAwaitInExpr(s.Object) || ContainsAwaitInExpr(s.Value);
            default:
                return false;
        }
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
