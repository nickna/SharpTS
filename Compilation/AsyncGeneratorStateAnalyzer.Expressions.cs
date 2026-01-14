using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncGeneratorStateAnalyzer
{
    #region Expression Visitor Overrides

    protected override void VisitYield(Expr.Yield expr)
    {
        // Visit yield value BEFORE marking _seenSuspension, so variables in the yield
        // expression are not incorrectly marked as "used after suspension"
        base.VisitYield(expr);

        // Record this yield point as a suspension point
        var liveVars = new HashSet<string>(_declaredVariables);
        _suspensionPoints.Add(new SuspensionPoint(
            _stateCounter++,
            SuspensionType.Yield,
            expr.Value,
            liveVars,
            IsDelegatingYield: expr.IsDelegating,
            TryBlockDepth: _currentTryBlockDepth,
            EnclosingTryId: _currentTryBlockId
        ));
        _seenSuspension = true;

        if (expr.IsDelegating)
            _hasYieldStar = true;

        // Track suspension in try block
        RecordSuspensionInTryBlock();

        // Record all for...of loops we're currently inside - they need special handling
        foreach (var forOf in _forOfStack)
        {
            if (!_forOfLoopsWithSuspension.Contains(forOf))
                _forOfLoopsWithSuspension.Add(forOf);
        }
    }

    protected override void VisitAwait(Expr.Await expr)
    {
        // Visit await expression BEFORE marking _seenSuspension, so variables in the await
        // expression are not incorrectly marked as "used after suspension"
        base.VisitAwait(expr);

        // Record this await point as a suspension point
        var liveVars = new HashSet<string>(_declaredVariables);
        _suspensionPoints.Add(new SuspensionPoint(
            _stateCounter++,
            SuspensionType.Await,
            expr.Expression,
            liveVars,
            IsDelegatingYield: false,
            TryBlockDepth: _currentTryBlockDepth,
            EnclosingTryId: _currentTryBlockId
        ));
        _seenSuspension = true;

        // Track suspension in try block
        RecordSuspensionInTryBlock();

        // Record all for...of loops we're currently inside - they need special handling
        foreach (var forOf in _forOfStack)
        {
            if (!_forOfLoopsWithSuspension.Contains(forOf))
                _forOfLoopsWithSuspension.Add(forOf);
        }
    }

    protected override void VisitVariable(Expr.Variable expr)
    {
        var name = expr.Name.Lexeme;

        // Track variables used in for...of loop bodies (for hoisting when loop contains suspension)
        foreach (var loop in _forOfStack)
        {
            if (_variablesUsedInLoopBody.TryGetValue(loop, out var vars))
                vars.Add(name);
        }

        if (_seenSuspension && _declaredVariables.Contains(name))
            _variablesUsedAfterSuspension.Add(name);
        // No base call needed - leaf node
    }

    protected override void VisitAssign(Expr.Assign expr)
    {
        if (_seenSuspension && _declaredVariables.Contains(expr.Name.Lexeme))
            _variablesUsedAfterSuspension.Add(expr.Name.Lexeme);
        base.VisitAssign(expr);
    }

    protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
    {
        if (_seenSuspension && _declaredVariables.Contains(expr.Name.Lexeme))
            _variablesUsedAfterSuspension.Add(expr.Name.Lexeme);
        base.VisitCompoundAssign(expr);
    }

    protected override void VisitThis(Expr.This expr)
    {
        _usesThis = true;
        // No base call needed - leaf node
    }

    protected override void VisitSuper(Expr.Super expr)
    {
        _usesThis = true;
        // No base call needed - leaf node
    }

    protected override void VisitArrowFunction(Expr.ArrowFunction expr)
    {
        // Arrow functions inside async generators capture 'this' from lexical scope
        if (_captureVisitor.UsesThis(expr))
            _usesThis = true;

        // Don't traverse into arrow bodies - they have their own analysis
        // Arrow functions inside async generators don't affect yield/await analysis
    }

    #endregion
}
