using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncStateAnalyzer
{
    private void AnalyzeExpr(Expr expr)
    {
        switch (expr)
        {
            case Expr.Await a:
                // Record this await point with try block context
                var liveVars = new HashSet<string>(_declaredVariables);
                _awaitPoints.Add(new AwaitPoint(
                    _awaitCounter++,
                    a,
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

                // Analyze the awaited expression
                AnalyzeExpr(a.Expression);
                break;

            case Expr.Variable v:
                // Track variable usage after await
                if (_seenAwait && _declaredVariables.Contains(v.Name.Lexeme))
                    _variablesUsedAfterAwait.Add(v.Name.Lexeme);
                break;

            case Expr.Assign a:
                // Track assignment to variable after await
                if (_seenAwait && _declaredVariables.Contains(a.Name.Lexeme))
                    _variablesUsedAfterAwait.Add(a.Name.Lexeme);
                AnalyzeExpr(a.Value);
                break;

            case Expr.Binary b:
                AnalyzeExpr(b.Left);
                AnalyzeExpr(b.Right);
                break;

            case Expr.Logical l:
                AnalyzeExpr(l.Left);
                AnalyzeExpr(l.Right);
                break;

            case Expr.Unary u:
                AnalyzeExpr(u.Right);
                break;

            case Expr.Grouping g:
                AnalyzeExpr(g.Expression);
                break;

            case Expr.Call c:
                AnalyzeExpr(c.Callee);
                foreach (var arg in c.Arguments)
                    AnalyzeExpr(arg);
                break;

            case Expr.Get g:
                AnalyzeExpr(g.Object);
                break;

            case Expr.Set s:
                AnalyzeExpr(s.Object);
                AnalyzeExpr(s.Value);
                break;

            case Expr.GetIndex gi:
                AnalyzeExpr(gi.Object);
                AnalyzeExpr(gi.Index);
                break;

            case Expr.SetIndex si:
                AnalyzeExpr(si.Object);
                AnalyzeExpr(si.Index);
                AnalyzeExpr(si.Value);
                break;

            case Expr.New n:
                foreach (var arg in n.Arguments)
                    AnalyzeExpr(arg);
                break;

            case Expr.ArrayLiteral a:
                foreach (var elem in a.Elements)
                    AnalyzeExpr(elem);
                break;

            case Expr.ObjectLiteral o:
                foreach (var prop in o.Properties)
                    AnalyzeExpr(prop.Value);
                break;

            case Expr.Ternary t:
                AnalyzeExpr(t.Condition);
                AnalyzeExpr(t.ThenBranch);
                AnalyzeExpr(t.ElseBranch);
                break;

            case Expr.NullishCoalescing nc:
                AnalyzeExpr(nc.Left);
                AnalyzeExpr(nc.Right);
                break;

            case Expr.TemplateLiteral tl:
                foreach (var e in tl.Expressions)
                    AnalyzeExpr(e);
                break;

            case Expr.CompoundAssign ca:
                if (_seenAwait && _declaredVariables.Contains(ca.Name.Lexeme))
                    _variablesUsedAfterAwait.Add(ca.Name.Lexeme);
                AnalyzeExpr(ca.Value);
                break;

            case Expr.CompoundSet cs:
                AnalyzeExpr(cs.Object);
                AnalyzeExpr(cs.Value);
                break;

            case Expr.CompoundSetIndex csi:
                AnalyzeExpr(csi.Object);
                AnalyzeExpr(csi.Index);
                AnalyzeExpr(csi.Value);
                break;

            case Expr.PrefixIncrement pi:
                AnalyzeExpr(pi.Operand);
                break;

            case Expr.PostfixIncrement poi:
                AnalyzeExpr(poi.Operand);
                break;

            case Expr.ArrowFunction af:
                // Arrow functions capture 'this' from lexical scope, so we need to
                // analyze if they use 'this' and if so, hoist it for the async method
                AnalyzeArrowForThis(af);

                // If this is an async arrow, collect it for separate state machine generation
                if (af.IsAsync)
                {
                    var captures = AnalyzeAsyncArrowCaptures(af);
                    var capturesThis = captures.Contains("this");
                    _asyncArrows.Add(new AsyncArrowInfo(af, captures, capturesThis, _asyncArrowNestingLevel, _currentParentArrow));

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
                    _currentParentArrow = af;
                    _asyncArrowNestingLevel++;
                    AnalyzeAsyncArrowBody(af);
                    _asyncArrowNestingLevel--;
                    _currentParentArrow = previousParent;
                }
                break;

            case Expr.This:
                _usesThis = true;
                break;

            case Expr.Super:
                // super implicitly requires 'this' to be hoisted
                _usesThis = true;
                break;

            case Expr.Literal:
                break;

            case Expr.DynamicImport di:
                AnalyzeExpr(di.PathExpression);
                break;
        }
    }
}
