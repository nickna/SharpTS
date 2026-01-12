using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncGeneratorStateAnalyzer
{
    #region Expression Visitor Overrides

    protected override void VisitYield(Expr.Yield expr)
    {
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

        base.VisitYield(expr);
    }

    protected override void VisitAwait(Expr.Await expr)
    {
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

        base.VisitAwait(expr);
    }

    protected override void VisitVariable(Expr.Variable expr)
    {
        if (_seenSuspension && _declaredVariables.Contains(expr.Name.Lexeme))
            _variablesUsedAfterSuspension.Add(expr.Name.Lexeme);
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
