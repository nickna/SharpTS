using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation.Visitors;

/// <summary>
/// Visitor that detects 'this' or 'super' usage within an AST subtree.
/// Used by AsyncStateAnalyzer to determine if arrow functions capture 'this'.
/// </summary>
internal class ThisUsageVisitor : ControlledAstVisitor
{
    /// <summary>
    /// Gets whether 'this' or 'super' was found during traversal.
    /// </summary>
    public bool UsesThis { get; private set; }

    /// <summary>
    /// Analyze an arrow function to determine if it uses 'this'.
    /// </summary>
    public void Analyze(Expr.ArrowFunction af)
    {
        Reset();
        UsesThis = false;

        if (af.ExpressionBody != null)
            Visit(af.ExpressionBody);
        if (af.BlockBody != null)
            foreach (var s in af.BlockBody)
                Visit(s);
    }

    protected override void VisitThis(Expr.This expr)
    {
        UsesThis = true;
        StopTraversal();
    }

    protected override void VisitSuper(Expr.Super expr)
    {
        UsesThis = true;
        StopTraversal();
    }

    // Nested arrow functions inherit 'this' lexically, so we continue traversing into them
    // The base class VisitArrowFunction handles this correctly
}
