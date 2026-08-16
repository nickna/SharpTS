using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.TypeSystem;

/// <summary>
/// Collects, within a single function body and not crossing into a nested function, arrow, or
/// class, every value assigned to a local — variable/const declarations with an initializer and
/// plain/logical assignments — together with every <c>return</c> value. Feeds the type checker's
/// local-taint return analysis (#367): a <c>number</c>/<c>boolean</c>-typed local that is unsoundly
/// assigned an <c>any</c>/<c>undefined</c> value can hold the runtime <c>undefined</c> sentinel,
/// which the compiler's unboxed return slot would coerce to <c>NaN</c>/<c>false</c>.
/// </summary>
/// <remarks>
/// Order-independent by design: it gathers the whole body before the checker computes taint to a
/// fixpoint, so a taint that reaches an earlier return via a loop back-edge is still caught.
/// </remarks>
internal sealed class ReturnLocalTaintCollector : AstVisitorBase
{
    public List<(string Name, Expr Value)> Assignments { get; } = new();
    public List<Expr> Returns { get; } = new();
    /// <summary>Variable/const declarations: the declaration node and (if any) its initializer.</summary>
    public List<(string Name, Stmt Node, Expr? Initializer)> Declarations { get; } = new();

    public static ReturnLocalTaintCollector Collect(IReadOnlyList<Stmt> body)
    {
        var collector = new ReturnLocalTaintCollector();
        foreach (var stmt in body) collector.Visit(stmt);
        return collector;
    }

    protected override void VisitVar(Stmt.Var stmt)
    {
        Declarations.Add((stmt.Name.Lexeme, stmt, stmt.Initializer));
        if (stmt.Initializer != null)
        {
            Assignments.Add((stmt.Name.Lexeme, stmt.Initializer));
            Visit(stmt.Initializer);
        }
    }

    protected override void VisitConst(Stmt.Const stmt)
    {
        Declarations.Add((stmt.Name.Lexeme, stmt, stmt.Initializer));
        Assignments.Add((stmt.Name.Lexeme, stmt.Initializer));
        Visit(stmt.Initializer);
    }

    protected override void VisitAssign(Expr.Assign expr)
    {
        Assignments.Add((expr.Name.Lexeme, expr.Value));
        Visit(expr.Value);
    }

    // `x ||= e` / `x &&= e` / `x ??= e` can leave `x` holding `e`'s value (or, for `??=`/`&&=`,
    // an already-undefined `x`). Treat the right-hand side as a value flowing into the local.
    protected override void VisitLogicalAssign(Expr.LogicalAssign expr)
    {
        Assignments.Add((expr.Name.Lexeme, expr.Value));
        Visit(expr.Value);
    }

    protected override void VisitReturn(Stmt.Return stmt)
    {
        if (stmt.Value != null)
        {
            Returns.Add(stmt.Value);
            Visit(stmt.Value);
        }
    }

    // Nested function, arrow, and class bodies own their own return slot and are analyzed
    // independently when each is type-checked — do not descend into them here.
    protected override void VisitFunction(Stmt.Function stmt) { }
    protected override void VisitArrowFunction(Expr.ArrowFunction expr) { }
    protected override void VisitClass(Stmt.Class stmt) { }
    protected override void VisitClassExpr(Expr.ClassExpr expr) { }
    protected override void VisitAccessor(Stmt.Accessor stmt) { }
    protected override void VisitStaticBlock(Stmt.StaticBlock stmt) { }
    protected override void VisitField(Stmt.Field stmt) { }
}
