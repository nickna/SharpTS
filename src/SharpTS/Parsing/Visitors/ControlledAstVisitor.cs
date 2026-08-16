namespace SharpTS.Parsing.Visitors;

/// <summary>
/// Result that controls traversal behavior.
/// </summary>
public enum TraversalAction
{
    /// <summary>Continue normal traversal.</summary>
    Continue,

    /// <summary>Skip visiting children of the current node.</summary>
    SkipChildren,

    /// <summary>Stop all traversal immediately.</summary>
    Stop
}

/// <summary>
/// Base class for visitors that need traversal control (early termination, skip children).
/// </summary>
/// <remarks>
/// Provides hooks for pre/post processing and methods to control traversal flow.
/// Call <see cref="SkipChildren"/> to avoid visiting children of the current node.
/// Call <see cref="StopTraversal"/> to stop all traversal immediately.
/// </remarks>
public abstract class ControlledAstVisitor : AstVisitorBase
{
    private TraversalAction _action = TraversalAction.Continue;

    /// <summary>
    /// Signal to skip children of the current node.
    /// </summary>
    protected void SkipChildren() => _action = TraversalAction.SkipChildren;

    /// <summary>
    /// Signal to stop all traversal.
    /// </summary>
    protected void StopTraversal()
    {
        _action = TraversalAction.Stop;
        ShouldContinue = false;
    }

    /// <summary>
    /// Visit an expression with before/after hooks and traversal control.
    /// </summary>
    public override void Visit(Expr expr)
    {
        if (!ShouldContinue) return;
        _action = TraversalAction.Continue;

        BeforeVisit(expr);
        if (_action == TraversalAction.Continue)
            base.Visit(expr);
        if (ShouldContinue)
            AfterVisit(expr);
    }

    /// <summary>
    /// Visit a statement with before/after hooks and traversal control.
    /// </summary>
    public override void Visit(Stmt stmt)
    {
        if (!ShouldContinue) return;
        _action = TraversalAction.Continue;

        BeforeVisit(stmt);
        if (_action == TraversalAction.Continue)
            base.Visit(stmt);
        if (ShouldContinue)
            AfterVisit(stmt);
    }

    /// <summary>
    /// Called before visiting an expression node. Override to add pre-visit logic.
    /// Call <see cref="SkipChildren"/> or <see cref="StopTraversal"/> to control traversal.
    /// </summary>
    protected virtual void BeforeVisit(Expr expr) { }

    /// <summary>
    /// Called after visiting an expression node (and its children). Override to add post-visit logic.
    /// </summary>
    protected virtual void AfterVisit(Expr expr) { }

    /// <summary>
    /// Called before visiting a statement node. Override to add pre-visit logic.
    /// Call <see cref="SkipChildren"/> or <see cref="StopTraversal"/> to control traversal.
    /// </summary>
    protected virtual void BeforeVisit(Stmt stmt) { }

    /// <summary>
    /// Called after visiting a statement node (and its children). Override to add post-visit logic.
    /// </summary>
    protected virtual void AfterVisit(Stmt stmt) { }

    /// <summary>
    /// Reset the visitor state for reuse.
    /// </summary>
    protected virtual void Reset()
    {
        _action = TraversalAction.Continue;
        ShouldContinue = true;
    }
}
