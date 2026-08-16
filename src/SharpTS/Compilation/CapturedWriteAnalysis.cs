using System.Reflection.Emit;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

/// <summary>
/// Determines which variables an arrow/function body assigns to within its <em>own</em> scope —
/// descending through blocks, loops, conditionals and call arguments, but NOT into nested arrows
/// or function declarations (those introduce their own bindings, so a write there is not a write
/// to this scope's captures). Intersecting the result with a closure's capture set identifies the
/// captures it mutates, which the compiler must back with shared (reference-type) storage rather
/// than a by-value snapshot.
/// </summary>
internal static class CapturedWriteAnalysis
{
    /// <summary>
    /// Fails fast (clear <see cref="CompileException"/>) when a sync arrow inside a generator body
    /// writes a variable that the arrow captured BY VALUE into its own display class — a write that
    /// would be silently lost because it lands on the private snapshot rather than the generator's
    /// own storage (#674). Shared by the sync and async generator MoveNext emitters so they stay in
    /// step. No-op when:
    /// <list type="bullet">
    /// <item>the arrow is async (its writes go through a different capture path);</item>
    /// <item>the arrow captures nothing by value; or</item>
    /// <item>the written captures were lifted into a shared function display class — those are
    /// routed through <c>$functionDC</c> and so are absent from the by-value snapshot field map,
    /// meaning the write already reaches shared storage (the sync free-function generator case).</item>
    /// </list>
    /// </summary>
    public static void ThrowIfCapturedWriteWouldBeLost(
        Expr.ArrowFunction arrow,
        Dictionary<Expr.ArrowFunction, Dictionary<string, FieldBuilder>>? displayClassFields)
    {
        if (arrow.IsAsync ||
            displayClassFields == null ||
            !displayClassFields.TryGetValue(arrow, out var captureFields) ||
            captureFields.Count == 0)
            return;

        var written = CollectImmediateWrites(arrow);
        written.IntersectWith(captureFields.Keys);
        if (written.Count == 0)
            return;

        var names = string.Join(", ", written.OrderBy(n => n, StringComparer.Ordinal));
        throw new CompileException(
            $"Compiled mode does not yet support an arrow/callback inside a generator (function*) " +
            $"body that writes to a variable captured from the generator scope ({names}). The write " +
            $"would be lost. Rewrite the mutation outside the callback (e.g. a for…of loop), or run " +
            $"in interpreted mode. (#674)");
    }

    /// <summary>
    /// Returns the names assigned within <paramref name="arrow"/>'s own scope.
    /// </summary>
    public static HashSet<string> CollectImmediateWrites(Expr.ArrowFunction arrow)
    {
        var collector = new WrittenNameCollector();
        if (arrow.ExpressionBody != null)
            collector.Visit(arrow.ExpressionBody);
        if (arrow.BlockBody != null)
            foreach (var stmt in arrow.BlockBody)
                collector.Visit(stmt);
        return collector.Names;
    }

    private sealed class WrittenNameCollector : AstVisitorBase
    {
        public readonly HashSet<string> Names = [];

        protected override void VisitAssign(Expr.Assign expr)
        {
            Names.Add(expr.Name.Lexeme);
            base.VisitAssign(expr);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
        {
            Names.Add(expr.Name.Lexeme);
            base.VisitCompoundAssign(expr);
        }

        protected override void VisitLogicalAssign(Expr.LogicalAssign expr)
        {
            Names.Add(expr.Name.Lexeme);
            base.VisitLogicalAssign(expr);
        }

        protected override void VisitPrefixIncrement(Expr.PrefixIncrement expr)
        {
            if (expr.Operand is Expr.Variable v) Names.Add(v.Name.Lexeme);
            base.VisitPrefixIncrement(expr);
        }

        protected override void VisitPostfixIncrement(Expr.PostfixIncrement expr)
        {
            if (expr.Operand is Expr.Variable v) Names.Add(v.Name.Lexeme);
            base.VisitPostfixIncrement(expr);
        }

        // Stop at nested closure boundaries: their assignments target their own bindings (or
        // deeper captures), so they must not be attributed to the enclosing scope's captures.
        protected override void VisitArrowFunction(Expr.ArrowFunction expr) { }
        protected override void VisitFunction(Stmt.Function stmt) { }
    }
}
