using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncStateAnalyzer
{
    #region Statement Visitor Overrides

    protected override void VisitVar(Stmt.Var stmt)
    {
        // Track variable declaration
        _declaredVariables.Add(stmt.Name.Lexeme);
        if (!_seenAwait)
            _variablesDeclaredBeforeAwait.Add(stmt.Name.Lexeme);

        base.VisitVar(stmt);
    }

    protected override void VisitForOf(Stmt.ForOf stmt)
    {
        // Loop variable is declared and potentially survives await
        _declaredVariables.Add(stmt.Variable.Lexeme);
        if (!_seenAwait)
            _variablesDeclaredBeforeAwait.Add(stmt.Variable.Lexeme);

        base.VisitForOf(stmt);
    }

    protected override void VisitForIn(Stmt.ForIn stmt)
    {
        // Loop variable is declared and potentially survives await
        _declaredVariables.Add(stmt.Variable.Lexeme);
        if (!_seenAwait)
            _variablesDeclaredBeforeAwait.Add(stmt.Variable.Lexeme);

        base.VisitForIn(stmt);
    }

    protected override void VisitFor(Stmt.For stmt)
    {
        // Visit initializer first (may declare loop variable)
        if (stmt.Initializer != null)
            Visit(stmt.Initializer);

        // Track variables used in condition and increment
        if (stmt.Condition != null)
            Visit(stmt.Condition);

        // Visit body for await detection
        Visit(stmt.Body);

        if (stmt.Increment != null)
            Visit(stmt.Increment);
    }

    protected override void VisitTryCatch(Stmt.TryCatch stmt)
    {
        _hasTryCatch = true;
        AnalyzeTryCatchWithTracking(stmt);
        // Don't call base - AnalyzeTryCatchWithTracking handles all traversal
    }

    // Don't traverse into nested declarations - they don't affect our analysis
    protected override void VisitFunction(Stmt.Function stmt) { }
    protected override void VisitClass(Stmt.Class stmt) { }
    protected override void VisitInterface(Stmt.Interface stmt) { }
    protected override void VisitTypeAlias(Stmt.TypeAlias stmt) { }
    protected override void VisitEnum(Stmt.Enum stmt) { }
    protected override void VisitNamespace(Stmt.Namespace stmt) { }

    #endregion

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
            Visit(ts);

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
                Visit(cs);
        }

        // Analyze finally block
        if (t.FinallyBlock != null)
        {
            _currentTryRegion = TryRegion.Finally;
            foreach (var fs in t.FinallyBlock)
                Visit(fs);
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
