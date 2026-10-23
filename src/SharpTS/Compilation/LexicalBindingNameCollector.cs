using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

/// <summary>
/// Collects lexical binding names whose emitted object storage may temporarily
/// contain the compiled temporal-dead-zone sentinel. Name-level metadata is
/// sufficient because the sentinel cannot be produced by user code; shadowed
/// non-lexical bindings merely incur no check unless their value is the sentinel.
/// </summary>
internal sealed class LexicalBindingNameCollector : AstVisitorBase
{
    private readonly HashSet<string> _names = [];

    public static HashSet<string> Collect(IEnumerable<Stmt> statements)
    {
        var collector = new LexicalBindingNameCollector();
        foreach (var statement in statements)
            collector.Visit(statement);
        return collector._names;
    }

    protected override void VisitVar(Stmt.Var stmt)
    {
        if (!stmt.IsVar)
            _names.Add(stmt.Name.Lexeme);
        base.VisitVar(stmt);
    }

    protected override void VisitConst(Stmt.Const stmt)
    {
        _names.Add(stmt.Name.Lexeme);
        base.VisitConst(stmt);
    }

    protected override void VisitClass(Stmt.Class stmt)
    {
        _names.Add(stmt.Name.Lexeme);
        base.VisitClass(stmt);
        foreach (var block in (stmt.StaticInitializers ?? []).OfType<Stmt.StaticBlock>())
            Visit(block);
    }

    protected override void VisitClassExpr(Expr.ClassExpr expr)
    {
        base.VisitClassExpr(expr);
        foreach (var block in (expr.StaticInitializers ?? []).OfType<Stmt.StaticBlock>())
            Visit(block);
    }
}
