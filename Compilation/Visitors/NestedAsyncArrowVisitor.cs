using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation.Visitors;

/// <summary>
/// Visitor that finds async arrow functions nested within an arrow function body.
/// Uses a callback pattern to notify when async arrows are found, allowing the caller
/// to manage state like nesting levels and capture analysis.
/// </summary>
internal class NestedAsyncArrowVisitor : AstVisitorBase
{
    /// <summary>
    /// Callback invoked when an async arrow function is found.
    /// Parameters: the async arrow function and whether to recurse into it.
    /// </summary>
    public Action<Expr.ArrowFunction>? OnAsyncArrowFound { get; set; }

    /// <summary>
    /// Callback invoked when a non-async arrow function is found.
    /// Used to recursively search for nested async arrows inside non-async arrows.
    /// </summary>
    public Action<Expr.ArrowFunction>? OnNonAsyncArrowFound { get; set; }

    /// <summary>
    /// Analyze an arrow function body for nested async arrows.
    /// </summary>
    public void Analyze(Expr.ArrowFunction af)
    {
        if (af.ExpressionBody != null)
            Visit(af.ExpressionBody);
        if (af.BlockBody != null)
            foreach (var stmt in af.BlockBody)
                Visit(stmt);
    }

    protected override void VisitArrowFunction(Expr.ArrowFunction expr)
    {
        if (expr.IsAsync)
        {
            // Found a nested async arrow - notify the caller
            OnAsyncArrowFound?.Invoke(expr);
        }
        else
        {
            // Non-async nested arrow - notify caller to check for nested async arrows inside
            OnNonAsyncArrowFound?.Invoke(expr);
        }
        // Don't traverse into the arrow body here - let the callbacks handle recursion
    }
}
