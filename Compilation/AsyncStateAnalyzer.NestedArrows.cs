using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncStateAnalyzer
{
    /// <summary>
    /// Recursively analyzes an async arrow body for nested async arrows.
    /// </summary>
    private void AnalyzeAsyncArrowBody(Expr.ArrowFunction af)
    {
        if (af.ExpressionBody != null)
        {
            AnalyzeAsyncArrowBodyExpr(af.ExpressionBody);
        }
        if (af.BlockBody != null)
        {
            foreach (var stmt in af.BlockBody)
            {
                AnalyzeAsyncArrowBodyStmt(stmt);
            }
        }
    }

    private void AnalyzeAsyncArrowBodyStmt(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Expression e:
                AnalyzeAsyncArrowBodyExpr(e.Expr);
                break;
            case Stmt.Var v:
                if (v.Initializer != null)
                    AnalyzeAsyncArrowBodyExpr(v.Initializer);
                break;
            case Stmt.Return r:
                if (r.Value != null)
                    AnalyzeAsyncArrowBodyExpr(r.Value);
                break;
            case Stmt.If i:
                AnalyzeAsyncArrowBodyExpr(i.Condition);
                AnalyzeAsyncArrowBodyStmt(i.ThenBranch);
                if (i.ElseBranch != null)
                    AnalyzeAsyncArrowBodyStmt(i.ElseBranch);
                break;
            case Stmt.While w:
                AnalyzeAsyncArrowBodyExpr(w.Condition);
                AnalyzeAsyncArrowBodyStmt(w.Body);
                break;
            case Stmt.ForOf f:
                AnalyzeAsyncArrowBodyExpr(f.Iterable);
                AnalyzeAsyncArrowBodyStmt(f.Body);
                break;
            case Stmt.Block b:
                foreach (var s in b.Statements)
                    AnalyzeAsyncArrowBodyStmt(s);
                break;
            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    AnalyzeAsyncArrowBodyStmt(s);
                break;
            case Stmt.TryCatch t:
                foreach (var ts in t.TryBlock)
                    AnalyzeAsyncArrowBodyStmt(ts);
                if (t.CatchBlock != null)
                    foreach (var cs in t.CatchBlock)
                        AnalyzeAsyncArrowBodyStmt(cs);
                if (t.FinallyBlock != null)
                    foreach (var fs in t.FinallyBlock)
                        AnalyzeAsyncArrowBodyStmt(fs);
                break;
            case Stmt.Switch s:
                AnalyzeAsyncArrowBodyExpr(s.Subject);
                foreach (var c in s.Cases)
                {
                    AnalyzeAsyncArrowBodyExpr(c.Value);
                    foreach (var cs in c.Body)
                        AnalyzeAsyncArrowBodyStmt(cs);
                }
                if (s.DefaultBody != null)
                    foreach (var ds in s.DefaultBody)
                        AnalyzeAsyncArrowBodyStmt(ds);
                break;
            case Stmt.Throw th:
                AnalyzeAsyncArrowBodyExpr(th.Value);
                break;
            case Stmt.Print p:
                AnalyzeAsyncArrowBodyExpr(p.Expr);
                break;
        }
    }

    private void AnalyzeAsyncArrowBodyExpr(Expr expr)
    {
        switch (expr)
        {
            case Expr.ArrowFunction af when af.IsAsync:
                // Found a nested async arrow!
                var captures = AnalyzeAsyncArrowCaptures(af);
                var capturesThis = captures.Contains("this");
                _asyncArrows.Add(new AsyncArrowInfo(af, captures, capturesThis, _asyncArrowNestingLevel, _currentParentArrow));

                // Recursively look for deeper nested async arrows
                var previousParent = _currentParentArrow;
                _currentParentArrow = af;
                _asyncArrowNestingLevel++;
                AnalyzeAsyncArrowBody(af);
                _asyncArrowNestingLevel--;
                _currentParentArrow = previousParent;
                break;
            case Expr.ArrowFunction af:
                // Non-async nested arrow - still check for nested async arrows inside
                AnalyzeAsyncArrowBody(af);
                break;
            case Expr.Binary b:
                AnalyzeAsyncArrowBodyExpr(b.Left);
                AnalyzeAsyncArrowBodyExpr(b.Right);
                break;
            case Expr.Logical l:
                AnalyzeAsyncArrowBodyExpr(l.Left);
                AnalyzeAsyncArrowBodyExpr(l.Right);
                break;
            case Expr.Unary u:
                AnalyzeAsyncArrowBodyExpr(u.Right);
                break;
            case Expr.Grouping g:
                AnalyzeAsyncArrowBodyExpr(g.Expression);
                break;
            case Expr.Call c:
                AnalyzeAsyncArrowBodyExpr(c.Callee);
                foreach (var arg in c.Arguments)
                    AnalyzeAsyncArrowBodyExpr(arg);
                break;
            case Expr.Get g:
                AnalyzeAsyncArrowBodyExpr(g.Object);
                break;
            case Expr.Set s:
                AnalyzeAsyncArrowBodyExpr(s.Object);
                AnalyzeAsyncArrowBodyExpr(s.Value);
                break;
            case Expr.GetIndex gi:
                AnalyzeAsyncArrowBodyExpr(gi.Object);
                AnalyzeAsyncArrowBodyExpr(gi.Index);
                break;
            case Expr.SetIndex si:
                AnalyzeAsyncArrowBodyExpr(si.Object);
                AnalyzeAsyncArrowBodyExpr(si.Index);
                AnalyzeAsyncArrowBodyExpr(si.Value);
                break;
            case Expr.Assign a:
                AnalyzeAsyncArrowBodyExpr(a.Value);
                break;
            case Expr.Ternary t:
                AnalyzeAsyncArrowBodyExpr(t.Condition);
                AnalyzeAsyncArrowBodyExpr(t.ThenBranch);
                AnalyzeAsyncArrowBodyExpr(t.ElseBranch);
                break;
            case Expr.NullishCoalescing nc:
                AnalyzeAsyncArrowBodyExpr(nc.Left);
                AnalyzeAsyncArrowBodyExpr(nc.Right);
                break;
            case Expr.Await aw:
                AnalyzeAsyncArrowBodyExpr(aw.Expression);
                break;
            case Expr.New n:
                foreach (var arg in n.Arguments)
                    AnalyzeAsyncArrowBodyExpr(arg);
                break;
            case Expr.ArrayLiteral a:
                foreach (var elem in a.Elements)
                    AnalyzeAsyncArrowBodyExpr(elem);
                break;
            case Expr.ObjectLiteral o:
                foreach (var prop in o.Properties)
                    AnalyzeAsyncArrowBodyExpr(prop.Value);
                break;
            case Expr.TemplateLiteral tl:
                foreach (var e in tl.Expressions)
                    AnalyzeAsyncArrowBodyExpr(e);
                break;
            case Expr.CompoundAssign ca:
                AnalyzeAsyncArrowBodyExpr(ca.Value);
                break;
            case Expr.CompoundSet cs:
                AnalyzeAsyncArrowBodyExpr(cs.Object);
                AnalyzeAsyncArrowBodyExpr(cs.Value);
                break;
            case Expr.CompoundSetIndex csi:
                AnalyzeAsyncArrowBodyExpr(csi.Object);
                AnalyzeAsyncArrowBodyExpr(csi.Index);
                AnalyzeAsyncArrowBodyExpr(csi.Value);
                break;
            case Expr.PrefixIncrement pi:
                AnalyzeAsyncArrowBodyExpr(pi.Operand);
                break;
            case Expr.PostfixIncrement poi:
                AnalyzeAsyncArrowBodyExpr(poi.Operand);
                break;
            case Expr.DynamicImport di:
                AnalyzeAsyncArrowBodyExpr(di.PathExpression);
                break;
        }
    }
}
