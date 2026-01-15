using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncGeneratorStateAnalyzer
{
    #region Statement Visitor Overrides

    protected override void VisitVar(Stmt.Var stmt)
    {
        _declaredVariables.Add(stmt.Name.Lexeme);
        if (!_seenSuspension)
            _variablesDeclaredBeforeSuspension.Add(stmt.Name.Lexeme);
        base.VisitVar(stmt);
    }

    protected override void VisitForOf(Stmt.ForOf stmt)
    {
        _declaredVariables.Add(stmt.Variable.Lexeme);
        if (!_seenSuspension)
            _variablesDeclaredBeforeSuspension.Add(stmt.Variable.Lexeme);

        // Track for...of loops to detect suspensions inside them
        _forOfStack.Push(stmt);
        _variablesUsedInLoopBody[stmt] = [];
        base.VisitForOf(stmt);
        _forOfStack.Pop();

        // If this loop contains a suspension, all variables used in its body need hoisting
        // because the loop body will re-execute after suspension resumes
        if (_forOfLoopsWithSuspension.Contains(stmt))
        {
            foreach (var varName in _variablesUsedInLoopBody[stmt])
            {
                if (_declaredVariables.Contains(varName))
                    _variablesUsedAfterSuspension.Add(varName);
            }
        }
    }

    protected override void VisitForIn(Stmt.ForIn stmt)
    {
        _declaredVariables.Add(stmt.Variable.Lexeme);
        if (!_seenSuspension)
            _variablesDeclaredBeforeSuspension.Add(stmt.Variable.Lexeme);
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

        // Track the for loop body for suspension detection
        bool hadSuspensionBefore = _seenSuspension;
        Visit(stmt.Body);

        // If body contains suspension, variables declared in initializer need hoisting
        if (_seenSuspension && !hadSuspensionBefore)
        {
            // The loop will re-execute, so any variable used in condition/increment/body
            // that was declared before the loop needs to be hoisted
        }

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

        // Record try block info (suspension flags will be updated during analysis)
        _tryBlocks.Add(new TryBlockInfo(
            tryId,
            t,
            HasSuspensionsInTry: false,
            HasSuspensionsInCatch: false,
            HasSuspensionsInFinally: false,
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
                if (!_seenSuspension)
                    _variablesDeclaredBeforeSuspension.Add(t.CatchParam.Lexeme);
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
