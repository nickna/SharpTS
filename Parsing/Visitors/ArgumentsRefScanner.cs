namespace SharpTS.Parsing.Visitors;

/// <summary>
/// Detects whether a function body references the JS <c>arguments</c> binding,
/// stopping at nested non-arrow function boundaries (those bind their own
/// <c>arguments</c> per spec, so a reference inside them belongs to the nested
/// function) and descending into true arrows (which inherit it lexically).
/// Single source shared by the closure analyzer (compiled mode) and the
/// interpreter's lazy <c>arguments</c> materialization — keep the scoping rules
/// here so the two modes can't drift.
/// </summary>
public sealed class ArgumentsRefScanner : AstVisitorBase
{
    private readonly bool _treatEvalReferenceAsUse;

    /// <param name="treatEvalReferenceAsUse">
    /// When true, a reference to the identifier <c>eval</c> also counts as a use.
    /// The interpreter implements direct eval against the live scope chain, so
    /// <c>eval("arguments[0]")</c> can observe the binding without the scanner
    /// ever seeing the <c>arguments</c> identifier — callers that gate binding
    /// creation on this scan must stay conservative in that case. The compiled
    /// closure analyzer passes false (its existing behavior).
    /// </param>
    public ArgumentsRefScanner(bool treatEvalReferenceAsUse = false)
        => _treatEvalReferenceAsUse = treatEvalReferenceAsUse;

    public bool Found { get; private set; }

    protected override void VisitVariable(Expr.Variable expr)
    {
        if (expr.Name.Lexeme == "arguments" ||
            (_treatEvalReferenceAsUse && expr.Name.Lexeme == "eval"))
        {
            Found = true;
            ShouldContinue = false;
        }
    }

    // Nested non-arrow function declarations/expressions introduce their own
    // `arguments` binding — references inside belong to that inner function,
    // so stop descending.
    protected override void VisitFunction(Stmt.Function stmt) { /* skip */ }

    protected override void VisitArrowFunction(Expr.ArrowFunction expr)
    {
        // Function expressions (HasOwnThis=true) behave like declarations: their
        // own `arguments` shadows ours. True arrow functions (HasOwnThis=false)
        // inherit lexically, so we must recurse.
        if (expr.HasOwnThis) return;
        base.VisitArrowFunction(expr);
    }
}
