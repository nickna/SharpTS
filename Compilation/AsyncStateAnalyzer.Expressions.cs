using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncStateAnalyzer
{
    #region Expression Visitor Overrides

    protected override void VisitAwait(Expr.Await expr)
    {
        // Record this await point with try block context
        var liveVars = new HashSet<string>(_declaredVariables);
        _awaitPoints.Add(new AwaitPoint(
            _awaitCounter++,
            expr,
            liveVars,
            _currentTryBlockDepth,
            _currentTryBlockId
        ));
        _seenAwait = true;

        // Track that this try block has an await in the current region
        if (_currentTryBlockId.HasValue && _currentTryRegion != TryRegion.None)
        {
            var tryId = _currentTryBlockId.Value;
            if (!_tryBlockAwaitFlags.TryGetValue(tryId, out var flags))
            {
                flags = (false, false, false);
            }
            flags = _currentTryRegion switch
            {
                TryRegion.Try => (true, flags.InCatch, flags.InFinally),
                TryRegion.Catch => (flags.InTry, true, flags.InFinally),
                TryRegion.Finally => (flags.InTry, flags.InCatch, true),
                _ => flags
            };
            _tryBlockAwaitFlags[tryId] = flags;
        }

        // Continue traversal into the awaited expression
        base.VisitAwait(expr);
    }

    protected override void VisitVariable(Expr.Variable expr)
    {
        // Track variable usage after await
        if (_seenAwait && _declaredVariables.Contains(expr.Name.Lexeme))
            _variablesUsedAfterAwait.Add(expr.Name.Lexeme);
        // No base call needed - leaf node
    }

    protected override void VisitAssign(Expr.Assign expr)
    {
        // Track assignment to variable after await
        if (_seenAwait && _declaredVariables.Contains(expr.Name.Lexeme))
            _variablesUsedAfterAwait.Add(expr.Name.Lexeme);
        base.VisitAssign(expr);
    }

    protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
    {
        if (_seenAwait && _declaredVariables.Contains(expr.Name.Lexeme))
            _variablesUsedAfterAwait.Add(expr.Name.Lexeme);
        base.VisitCompoundAssign(expr);
    }

    protected override void VisitArrowFunction(Expr.ArrowFunction expr)
    {
        // Arrow functions capture 'this' from lexical scope, so we need to
        // analyze if they use 'this' and if so, hoist it for the async method
        if (_captureVisitor.UsesThis(expr))
            _usesThis = true;

        // If this is an async arrow, collect it for separate state machine generation
        if (expr.IsAsync)
        {
            var captures = AnalyzeAsyncArrowCapturesWithVisitor(expr);
            var capturesThis = captures.Contains("this");
            _asyncArrows.Add(new AsyncArrowInfo(expr, captures, capturesThis, _asyncArrowNestingLevel, _currentParentArrow));

            // Also hoist any captured variables to the outer state machine
            // so they can be accessed by the async arrow's state machine
            foreach (var capture in captures)
            {
                if (capture != "this" && _declaredVariables.Contains(capture))
                {
                    _variablesUsedAfterAwait.Add(capture);
                }
            }

            // Recursively analyze for nested async arrows
            var previousParent = _currentParentArrow;
            _currentParentArrow = expr;
            _asyncArrowNestingLevel++;
            AnalyzeAsyncArrowBodyWithVisitor(expr);
            _asyncArrowNestingLevel--;
            _currentParentArrow = previousParent;
        }
        // Don't call base - we don't want to traverse into arrow bodies for the main analysis
        // The nested async arrow visitor handles that separately
    }

    protected override void VisitThis(Expr.This expr)
    {
        _usesThis = true;
        // No base call needed - leaf node
    }

    protected override void VisitSuper(Expr.Super expr)
    {
        // super implicitly requires 'this' to be hoisted
        _usesThis = true;
        // No base call needed - leaf node
    }

    #endregion
}
