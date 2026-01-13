using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    protected override void EmitTryCatch(Stmt.TryCatch t)
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

        List<Stmt> stmtsBeforeAwait = [];

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
}
