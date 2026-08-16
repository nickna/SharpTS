namespace SharpTS.Parsing;

/// <summary>Which syntactic form of JSX element a lowered call originated from.</summary>
public enum JsxElementKind
{
    /// <summary>Lowercase / dashed / namespaced tag (&lt;div&gt;, &lt;svg:rect&gt;) - checked against JSX.IntrinsicElements.</summary>
    Intrinsic,
    /// <summary>Capitalized or member-expression tag (&lt;Foo&gt;, &lt;Foo.Bar&gt;) - checked against the component's props.</summary>
    Component,
    /// <summary>&lt;&gt;...&lt;/&gt; fragment.</summary>
    Fragment,
}

/// <summary>
/// Attached to an <see cref="Expr.Call"/> produced by JSX lowering so the type checker can run
/// JSX semantics (tsc-shaped diagnostics, JSX.Element result type) instead of ordinary call
/// checking. All <see cref="Expr"/> references are ALIASES of nodes reachable from
/// <c>Call.Arguments</c> (never copies), so typeMap entries and single-visit discipline are
/// shared with the argument walk.
/// </summary>
/// <param name="Kind">Element form.</param>
/// <param name="TagName">Verbatim source tag ("div", "Foo", "Foo.Bar"); null for fragments.</param>
/// <param name="PropsExpr">The props ObjectLiteral; null when the element wrote no attributes.</param>
/// <param name="ChildExprs">Child expressions, wherever they physically sit in the lowered call.</param>
/// <param name="KeyExpr">Automatic-mode extracted key argument; null otherwise.</param>
/// <param name="Mode">The jsx mode the element was lowered under.</param>
/// <param name="Line">Source line of the opening tag (tokens carry no column).</param>
public sealed record JsxCallInfo(
    JsxElementKind Kind,
    string? TagName,
    Expr? PropsExpr,
    IReadOnlyList<Expr> ChildExprs,
    Expr? KeyExpr,
    JsxMode Mode,
    int Line);
