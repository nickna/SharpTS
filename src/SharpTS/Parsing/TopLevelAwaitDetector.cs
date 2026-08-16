using SharpTS.Parsing.Visitors;

namespace SharpTS.Parsing;

internal sealed class TopLevelAwaitDetector : AstVisitorBase
{
    public bool Found { get; private set; }

    public static bool Contains(IEnumerable<Stmt> statements)
    {
        var detector = new TopLevelAwaitDetector();
        foreach (Stmt statement in statements)
            detector.Visit(statement);
        return detector.Found;
    }

    protected override void VisitAwait(Expr.Await expr)
    {
        Found = true;
        ShouldContinue = false;
    }

    protected override void VisitForOf(Stmt.ForOf stmt)
    {
        if (stmt.IsAsync)
        {
            Found = true;
            ShouldContinue = false;
            return;
        }
        base.VisitForOf(stmt);
    }

    protected override void VisitFunction(Stmt.Function stmt) { }
    protected override void VisitArrowFunction(Expr.ArrowFunction expr) { }
    protected override void VisitClass(Stmt.Class stmt) { }
    protected override void VisitClassExpr(Expr.ClassExpr expr) { }
}
