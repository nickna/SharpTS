using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

/// <summary>
/// Finds regex literals that are safe to hoist to a per-site static <c>$RegExp</c>
/// in compiled mode. A hoisted literal is constructed once (lazily, on first
/// evaluation) and loaded with <c>ldsfld</c> thereafter, instead of allocating and
/// running the full <c>$RegExp</c> constructor on every evaluation — the dominant
/// cost of a regex literal in a hot loop (see
/// <c>docs/plans/regex-literal-hoisting-scope.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// ECMA-262 §13.2.7.3 makes each literal evaluation a fresh RegExp object, so
/// sharing one instance is observable only through (1) <c>lastIndex</c>
/// statefulness — for <c>g</c>/<c>y</c> flags via <c>test</c>/<c>exec</c>, which
/// advance it across calls; (2) object mutation; (3) identity (<c>a === b</c>). A
/// literal is therefore hoisted only when it appears <em>directly</em> in a
/// consuming position where it cannot escape to be mutated or compared:
/// </para>
/// <list type="bullet">
/// <item><c>(/lit/).test(x)</c> / <c>(/lit/).exec(x)</c> — only when neither
/// <c>g</c> nor <c>y</c> is set (otherwise a shared <c>lastIndex</c> would leak
/// across evaluations).</item>
/// <item><c>str.match/matchAll/replace/replaceAll/search/split(/lit/, …)</c> —
/// always safe: <c>SharpTSRegExp</c>'s <c>MatchAll</c>/<c>Replace</c>/<c>Search</c>/
/// <c>Split</c> scan from position 0 and never read the instance
/// <c>LastIndex</c>.</item>
/// </list>
/// <para>
/// Anything else (assigned to a variable, returned, passed to a user function,
/// mutated, compared) is left as a fresh per-evaluation construction.
/// </para>
/// </remarks>
internal sealed class RegexLiteralHoistAnalyzer : AstVisitorBase
{
    // Expr.RegexLiteral is a record (value equality). Keying by value would (a)
    // merge two distinct literal sites that happen to share /pat/flags and, far
    // worse, (b) let an escaping `const r = /pat/` value-match a hoisted `/pat/`
    // elsewhere and be wrongly hoisted (then mutated). Key strictly by node
    // identity so only the exact nodes flagged here are ever hoisted.
    private readonly HashSet<Expr.RegexLiteral> _hoistable = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Walks <paramref name="statements"/> and returns the set of regex-literal
    /// nodes (by identity) that are safe to hoist.
    /// </summary>
    public static HashSet<Expr.RegexLiteral> Analyze(List<Stmt> statements)
    {
        var analyzer = new RegexLiteralHoistAnalyzer();
        foreach (var stmt in statements)
            analyzer.Visit(stmt);
        return analyzer._hoistable;
    }

    protected override void VisitCall(Expr.Call call)
    {
        if (call.Callee is Expr.Get get)
        {
            string method = get.Name.Lexeme;

            // Receiver position: (/lit/).test(x) / (/lit/).exec(x).
            if ((method == "test" || method == "exec")
                && get.Object is Expr.RegexLiteral receiver)
            {
                // test/exec honor lastIndex for global/sticky regexes; a shared
                // instance would advance it across evaluations. Safe only when
                // neither g nor y is present.
                if (!receiver.Flags.Contains('g') && !receiver.Flags.Contains('y'))
                    _hoistable.Add(receiver);
            }
            // Argument position: str.<consumer>(/lit/, …) — the regex is arg 0.
            else if (IsStatelessStringConsumer(method)
                     && call.Arguments.Count >= 1
                     && call.Arguments[0] is Expr.RegexLiteral arg)
            {
                _hoistable.Add(arg);
            }
        }

        base.VisitCall(call); // continue into callee/args (and any nested literals)
    }

    /// <summary>
    /// String.prototype methods that consume a RegExp by scanning from position 0,
    /// never reading the instance <c>lastIndex</c> — so a shared instance is safe
    /// regardless of flags.
    /// </summary>
    private static bool IsStatelessStringConsumer(string method) =>
        method is "match" or "matchAll" or "replace" or "replaceAll"
                or "search" or "split";
}
