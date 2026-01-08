using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncStateAnalyzer
{
    /// <summary>
    /// Analyzes an arrow function to detect if it uses 'this', which needs to be
    /// hoisted for the enclosing async method.
    /// </summary>
    private void AnalyzeArrowForThis(Expr.ArrowFunction af)
    {
        if (af.ExpressionBody != null)
        {
            AnalyzeArrowExprForThis(af.ExpressionBody);
        }
        if (af.BlockBody != null)
        {
            foreach (var stmt in af.BlockBody)
            {
                AnalyzeArrowStmtForThis(stmt);
            }
        }
    }

    private void AnalyzeArrowStmtForThis(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Expression e:
                AnalyzeArrowExprForThis(e.Expr);
                break;
            case Stmt.Var v:
                if (v.Initializer != null) AnalyzeArrowExprForThis(v.Initializer);
                break;
            case Stmt.If i:
                AnalyzeArrowExprForThis(i.Condition);
                AnalyzeArrowStmtForThis(i.ThenBranch);
                if (i.ElseBranch != null) AnalyzeArrowStmtForThis(i.ElseBranch);
                break;
            case Stmt.While w:
                AnalyzeArrowExprForThis(w.Condition);
                AnalyzeArrowStmtForThis(w.Body);
                break;
            case Stmt.Block b:
                foreach (var s in b.Statements) AnalyzeArrowStmtForThis(s);
                break;
            case Stmt.Sequence seq:
                foreach (var s in seq.Statements) AnalyzeArrowStmtForThis(s);
                break;
            case Stmt.Return r:
                if (r.Value != null) AnalyzeArrowExprForThis(r.Value);
                break;
            case Stmt.Print p:
                AnalyzeArrowExprForThis(p.Expr);
                break;
        }
    }

    private void AnalyzeArrowExprForThis(Expr expr)
    {
        switch (expr)
        {
            case Expr.This:
                _usesThis = true;
                break;
            case Expr.Super:
                _usesThis = true;
                break;
            case Expr.Binary b:
                AnalyzeArrowExprForThis(b.Left);
                AnalyzeArrowExprForThis(b.Right);
                break;
            case Expr.Logical l:
                AnalyzeArrowExprForThis(l.Left);
                AnalyzeArrowExprForThis(l.Right);
                break;
            case Expr.Unary u:
                AnalyzeArrowExprForThis(u.Right);
                break;
            case Expr.Grouping g:
                AnalyzeArrowExprForThis(g.Expression);
                break;
            case Expr.Call c:
                AnalyzeArrowExprForThis(c.Callee);
                foreach (var arg in c.Arguments) AnalyzeArrowExprForThis(arg);
                break;
            case Expr.Get g:
                AnalyzeArrowExprForThis(g.Object);
                break;
            case Expr.Set s:
                AnalyzeArrowExprForThis(s.Object);
                AnalyzeArrowExprForThis(s.Value);
                break;
            case Expr.GetIndex gi:
                AnalyzeArrowExprForThis(gi.Object);
                AnalyzeArrowExprForThis(gi.Index);
                break;
            case Expr.SetIndex si:
                AnalyzeArrowExprForThis(si.Object);
                AnalyzeArrowExprForThis(si.Index);
                AnalyzeArrowExprForThis(si.Value);
                break;
            case Expr.Assign a:
                AnalyzeArrowExprForThis(a.Value);
                break;
            case Expr.Ternary t:
                AnalyzeArrowExprForThis(t.Condition);
                AnalyzeArrowExprForThis(t.ThenBranch);
                AnalyzeArrowExprForThis(t.ElseBranch);
                break;
            case Expr.CompoundAssign ca:
                AnalyzeArrowExprForThis(ca.Value);
                break;
            case Expr.CompoundSet cs:
                AnalyzeArrowExprForThis(cs.Object);
                AnalyzeArrowExprForThis(cs.Value);
                break;
            case Expr.ArrowFunction af:
                // Nested arrow - recursively check for 'this' usage
                AnalyzeArrowForThis(af);
                break;
        }
    }
}
