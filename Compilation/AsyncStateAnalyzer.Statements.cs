using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncStateAnalyzer
{
    private void AnalyzeStmt(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Var v:
                // Track variable declaration
                _declaredVariables.Add(v.Name.Lexeme);
                if (!_seenAwait)
                    _variablesDeclaredBeforeAwait.Add(v.Name.Lexeme);

                if (v.Initializer != null)
                    AnalyzeExpr(v.Initializer);
                break;

            case Stmt.Expression e:
                AnalyzeExpr(e.Expr);
                break;

            case Stmt.Return r:
                if (r.Value != null)
                    AnalyzeExpr(r.Value);
                break;

            case Stmt.If i:
                AnalyzeExpr(i.Condition);
                AnalyzeStmt(i.ThenBranch);
                if (i.ElseBranch != null)
                    AnalyzeStmt(i.ElseBranch);
                break;

            case Stmt.While w:
                AnalyzeExpr(w.Condition);
                AnalyzeStmt(w.Body);
                break;

            case Stmt.ForOf f:
                // Loop variable is declared and potentially survives await
                _declaredVariables.Add(f.Variable.Lexeme);
                if (!_seenAwait)
                    _variablesDeclaredBeforeAwait.Add(f.Variable.Lexeme);

                AnalyzeExpr(f.Iterable);
                AnalyzeStmt(f.Body);
                break;

            case Stmt.Block b:
                foreach (var s in b.Statements)
                    AnalyzeStmt(s);
                break;

            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    AnalyzeStmt(s);
                break;

            case Stmt.TryCatch t:
                _hasTryCatch = true;
                AnalyzeTryCatchWithTracking(t);
                break;

            case Stmt.Switch s:
                AnalyzeExpr(s.Subject);
                foreach (var c in s.Cases)
                {
                    AnalyzeExpr(c.Value);
                    foreach (var cs in c.Body)
                        AnalyzeStmt(cs);
                }
                if (s.DefaultBody != null)
                    foreach (var ds in s.DefaultBody)
                        AnalyzeStmt(ds);
                break;

            case Stmt.Throw th:
                AnalyzeExpr(th.Value);
                break;

            case Stmt.Print p:
                AnalyzeExpr(p.Expr);
                break;

            case Stmt.LabeledStatement ls:
                AnalyzeStmt(ls.Statement);
                break;

            case Stmt.Break:
            case Stmt.Continue:
            case Stmt.Function: // Nested functions don't affect our analysis
            case Stmt.Class:
            case Stmt.Interface:
            case Stmt.TypeAlias:
            case Stmt.Enum:
            case Stmt.Namespace:
                break;
        }
    }

    private void AnalyzeTryCatchWithTracking(Stmt.TryCatch t)
    {
        // Assign an ID to this try block
        int tryId = _tryBlockCounter++;
        int? parentTryId = _currentTryBlockId;

        // Push try block context
        if (_currentTryBlockId.HasValue)
            _tryBlockIdStack.Push(_currentTryBlockId.Value);
        _currentTryBlockId = tryId;
        _currentTryBlockDepth++;

        // Record try block info (await flags will be updated during analysis)
        _tryBlocks.Add(new TryBlockInfo(
            tryId,
            t,
            HasAwaitsInTry: false,  // Will be updated
            HasAwaitsInCatch: false,
            HasAwaitsInFinally: false,
            ParentTryId: parentTryId
        ));

        // Analyze try block
        var previousRegion = _currentTryRegion;
        _currentTryRegion = TryRegion.Try;
        foreach (var ts in t.TryBlock)
            AnalyzeStmt(ts);

        // Analyze catch block
        if (t.CatchBlock != null)
        {
            _currentTryRegion = TryRegion.Catch;
            if (t.CatchParam != null)
            {
                _declaredVariables.Add(t.CatchParam.Lexeme);
                if (!_seenAwait)
                    _variablesDeclaredBeforeAwait.Add(t.CatchParam.Lexeme);
            }
            foreach (var cs in t.CatchBlock)
                AnalyzeStmt(cs);
        }

        // Analyze finally block
        if (t.FinallyBlock != null)
        {
            _currentTryRegion = TryRegion.Finally;
            foreach (var fs in t.FinallyBlock)
                AnalyzeStmt(fs);
        }

        // Restore context
        _currentTryRegion = previousRegion;
        _currentTryBlockDepth--;
        if (_tryBlockIdStack.Count > 0)
            _currentTryBlockId = _tryBlockIdStack.Pop();
        else
            _currentTryBlockId = null;
    }
}
