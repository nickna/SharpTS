using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncStateAnalyzer
{
    /// <summary>
    /// Analyzes an async arrow function to determine which variables it captures
    /// from the enclosing scope.
    /// </summary>
    private HashSet<string> AnalyzeAsyncArrowCaptures(Expr.ArrowFunction af)
    {
        var captures = new HashSet<string>();
        var arrowLocals = new HashSet<string>();

        // Add arrow parameters as locals (not captures)
        foreach (var param in af.Parameters)
        {
            arrowLocals.Add(param.Name.Lexeme);
        }

        // Analyze arrow body for variable references
        if (af.ExpressionBody != null)
        {
            CollectArrowCaptures(af.ExpressionBody, arrowLocals, captures);
        }
        if (af.BlockBody != null)
        {
            foreach (var stmt in af.BlockBody)
            {
                CollectArrowCapturesFromStmt(stmt, arrowLocals, captures);
            }
        }

        return captures;
    }

    private void CollectArrowCapturesFromStmt(Stmt stmt, HashSet<string> locals, HashSet<string> captures)
    {
        switch (stmt)
        {
            case Stmt.Var v:
                locals.Add(v.Name.Lexeme);
                if (v.Initializer != null)
                    CollectArrowCaptures(v.Initializer, locals, captures);
                break;
            case Stmt.Expression e:
                CollectArrowCaptures(e.Expr, locals, captures);
                break;
            case Stmt.Return r:
                if (r.Value != null)
                    CollectArrowCaptures(r.Value, locals, captures);
                break;
            case Stmt.If i:
                CollectArrowCaptures(i.Condition, locals, captures);
                CollectArrowCapturesFromStmt(i.ThenBranch, locals, captures);
                if (i.ElseBranch != null)
                    CollectArrowCapturesFromStmt(i.ElseBranch, locals, captures);
                break;
            case Stmt.While w:
                CollectArrowCaptures(w.Condition, locals, captures);
                CollectArrowCapturesFromStmt(w.Body, locals, captures);
                break;
            case Stmt.ForOf f:
                locals.Add(f.Variable.Lexeme);
                CollectArrowCaptures(f.Iterable, locals, captures);
                CollectArrowCapturesFromStmt(f.Body, locals, captures);
                break;
            case Stmt.Block b:
                foreach (var s in b.Statements)
                    CollectArrowCapturesFromStmt(s, locals, captures);
                break;
            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    CollectArrowCapturesFromStmt(s, locals, captures);
                break;
            case Stmt.TryCatch t:
                foreach (var ts in t.TryBlock)
                    CollectArrowCapturesFromStmt(ts, locals, captures);
                if (t.CatchBlock != null)
                {
                    if (t.CatchParam != null)
                        locals.Add(t.CatchParam.Lexeme);
                    foreach (var cs in t.CatchBlock)
                        CollectArrowCapturesFromStmt(cs, locals, captures);
                }
                if (t.FinallyBlock != null)
                    foreach (var fs in t.FinallyBlock)
                        CollectArrowCapturesFromStmt(fs, locals, captures);
                break;
            case Stmt.Switch s:
                CollectArrowCaptures(s.Subject, locals, captures);
                foreach (var c in s.Cases)
                {
                    CollectArrowCaptures(c.Value, locals, captures);
                    foreach (var cs in c.Body)
                        CollectArrowCapturesFromStmt(cs, locals, captures);
                }
                if (s.DefaultBody != null)
                    foreach (var ds in s.DefaultBody)
                        CollectArrowCapturesFromStmt(ds, locals, captures);
                break;
            case Stmt.Throw th:
                CollectArrowCaptures(th.Value, locals, captures);
                break;
            case Stmt.Print p:
                CollectArrowCaptures(p.Expr, locals, captures);
                break;
        }
    }

    private void CollectArrowCaptures(Expr expr, HashSet<string> locals, HashSet<string> captures)
    {
        switch (expr)
        {
            case Expr.Variable v:
                var name = v.Name.Lexeme;
                // If not a local and is declared in outer scope, it's a capture
                if (!locals.Contains(name) && _declaredVariables.Contains(name))
                {
                    captures.Add(name);
                }
                break;
            case Expr.Assign a:
                if (!locals.Contains(a.Name.Lexeme) && _declaredVariables.Contains(a.Name.Lexeme))
                {
                    captures.Add(a.Name.Lexeme);
                }
                CollectArrowCaptures(a.Value, locals, captures);
                break;
            case Expr.CompoundAssign ca:
                if (!locals.Contains(ca.Name.Lexeme) && _declaredVariables.Contains(ca.Name.Lexeme))
                {
                    captures.Add(ca.Name.Lexeme);
                }
                CollectArrowCaptures(ca.Value, locals, captures);
                break;
            case Expr.This:
                captures.Add("this");
                break;
            case Expr.Binary b:
                CollectArrowCaptures(b.Left, locals, captures);
                CollectArrowCaptures(b.Right, locals, captures);
                break;
            case Expr.Logical l:
                CollectArrowCaptures(l.Left, locals, captures);
                CollectArrowCaptures(l.Right, locals, captures);
                break;
            case Expr.Unary u:
                CollectArrowCaptures(u.Right, locals, captures);
                break;
            case Expr.Grouping g:
                CollectArrowCaptures(g.Expression, locals, captures);
                break;
            case Expr.Call c:
                CollectArrowCaptures(c.Callee, locals, captures);
                foreach (var arg in c.Arguments)
                    CollectArrowCaptures(arg, locals, captures);
                break;
            case Expr.Get g:
                CollectArrowCaptures(g.Object, locals, captures);
                break;
            case Expr.Set s:
                CollectArrowCaptures(s.Object, locals, captures);
                CollectArrowCaptures(s.Value, locals, captures);
                break;
            case Expr.GetIndex gi:
                CollectArrowCaptures(gi.Object, locals, captures);
                CollectArrowCaptures(gi.Index, locals, captures);
                break;
            case Expr.SetIndex si:
                CollectArrowCaptures(si.Object, locals, captures);
                CollectArrowCaptures(si.Index, locals, captures);
                CollectArrowCaptures(si.Value, locals, captures);
                break;
            case Expr.New n:
                foreach (var arg in n.Arguments)
                    CollectArrowCaptures(arg, locals, captures);
                break;
            case Expr.ArrayLiteral a:
                foreach (var elem in a.Elements)
                    CollectArrowCaptures(elem, locals, captures);
                break;
            case Expr.ObjectLiteral o:
                foreach (var prop in o.Properties)
                    CollectArrowCaptures(prop.Value, locals, captures);
                break;
            case Expr.Ternary t:
                CollectArrowCaptures(t.Condition, locals, captures);
                CollectArrowCaptures(t.ThenBranch, locals, captures);
                CollectArrowCaptures(t.ElseBranch, locals, captures);
                break;
            case Expr.NullishCoalescing nc:
                CollectArrowCaptures(nc.Left, locals, captures);
                CollectArrowCaptures(nc.Right, locals, captures);
                break;
            case Expr.TemplateLiteral tl:
                foreach (var e in tl.Expressions)
                    CollectArrowCaptures(e, locals, captures);
                break;
            case Expr.Await aw:
                CollectArrowCaptures(aw.Expression, locals, captures);
                break;
            case Expr.ArrowFunction af:
                // Nested arrow functions may also capture - but we handle them separately
                // Their captures become our captures if they're from our scope
                var nestedCaptures = AnalyzeAsyncArrowCaptures(af);
                foreach (var cap in nestedCaptures)
                {
                    if (!locals.Contains(cap))
                        captures.Add(cap);
                }
                break;
            case Expr.CompoundSet cs:
                CollectArrowCaptures(cs.Object, locals, captures);
                CollectArrowCaptures(cs.Value, locals, captures);
                break;
            case Expr.CompoundSetIndex csi:
                CollectArrowCaptures(csi.Object, locals, captures);
                CollectArrowCaptures(csi.Index, locals, captures);
                CollectArrowCaptures(csi.Value, locals, captures);
                break;
            case Expr.PrefixIncrement pi:
                CollectArrowCaptures(pi.Operand, locals, captures);
                break;
            case Expr.PostfixIncrement poi:
                CollectArrowCaptures(poi.Operand, locals, captures);
                break;
            case Expr.DynamicImport di:
                CollectArrowCaptures(di.PathExpression, locals, captures);
                break;
        }
    }
}
