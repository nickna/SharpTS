using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

/// <summary>
/// Finds boolean-array locals whose indexed accesses can share one
/// <c>CollectionsMarshal.AsSpan</c> result for the duration of a loop.
/// </summary>
/// <remarks>
/// The emitter performs the final promoted-<c>List&lt;bool&gt;</c> slot check. This
/// analysis is deliberately conservative: any receiver method call or unsupported
/// indexed mutation disqualifies the name because it could resize the list and
/// invalidate a span. Plain indexed assignments remain eligible; their emitted
/// out-of-range fallback refreshes the hoisted span after growing the list.
/// </remarks>
internal static class PromotedBooleanSpanHoistAnalyzer
{
    internal static IReadOnlySet<string> AnalyzeFor(
        Stmt body,
        Expr? condition,
        Expr? increment)
    {
        var visitor = new Visitor();
        visitor.Visit(body);
        if (condition != null) visitor.Visit(condition);
        if (increment != null) visitor.Visit(increment);

        visitor.Candidates.ExceptWith(visitor.Invalidated);
        visitor.Candidates.ExceptWith(visitor.Shadowed);
        return visitor.Candidates;
    }

    private sealed class Visitor : AstVisitorBase
    {
        internal HashSet<string> Candidates { get; } = [];
        internal HashSet<string> Invalidated { get; } = [];
        internal HashSet<string> Shadowed { get; } = [];

        protected override void VisitGetIndex(Expr.GetIndex expr)
        {
            if (expr.Object is Expr.Variable variable)
                Candidates.Add(variable.Name.Lexeme);
            base.VisitGetIndex(expr);
        }

        protected override void VisitSetIndex(Expr.SetIndex expr)
        {
            if (expr.Object is Expr.Variable variable)
                Candidates.Add(variable.Name.Lexeme);
            base.VisitSetIndex(expr);
        }

        protected override void VisitCall(Expr.Call expr)
        {
            switch (expr.Callee)
            {
                case Expr.Get { Object: Expr.Variable variable }:
                    Invalidated.Add(variable.Name.Lexeme);
                    break;
                case Expr.GetIndex { Object: Expr.Variable variable }:
                    Invalidated.Add(variable.Name.Lexeme);
                    break;
            }
            base.VisitCall(expr);
        }

        protected override void VisitSet(Expr.Set expr)
        {
            if (expr.Object is Expr.Variable variable)
                Invalidated.Add(variable.Name.Lexeme);
            base.VisitSet(expr);
        }

        protected override void VisitCompoundSetIndex(Expr.CompoundSetIndex expr)
        {
            InvalidateIndexedReceiver(expr.Object);
            base.VisitCompoundSetIndex(expr);
        }

        protected override void VisitLogicalSetIndex(Expr.LogicalSetIndex expr)
        {
            InvalidateIndexedReceiver(expr.Object);
            base.VisitLogicalSetIndex(expr);
        }

        protected override void VisitPrefixIncrement(Expr.PrefixIncrement expr)
        {
            if (expr.Operand is Expr.GetIndex index)
                InvalidateIndexedReceiver(index.Object);
            base.VisitPrefixIncrement(expr);
        }

        protected override void VisitPostfixIncrement(Expr.PostfixIncrement expr)
        {
            if (expr.Operand is Expr.GetIndex index)
                InvalidateIndexedReceiver(index.Object);
            base.VisitPostfixIncrement(expr);
        }

        protected override void VisitDelete(Expr.Delete expr)
        {
            if (expr.Operand is Expr.GetIndex index)
                InvalidateIndexedReceiver(index.Object);
            base.VisitDelete(expr);
        }

        protected override void VisitAssign(Expr.Assign expr)
        {
            Invalidated.Add(expr.Name.Lexeme);
            base.VisitAssign(expr);
        }

        protected override void VisitVar(Stmt.Var stmt)
        {
            Shadowed.Add(stmt.Name.Lexeme);
            base.VisitVar(stmt);
        }

        protected override void VisitConst(Stmt.Const stmt)
        {
            Shadowed.Add(stmt.Name.Lexeme);
            base.VisitConst(stmt);
        }

        // Nested function bodies do not execute as part of the loop itself. Array
        // promotion already rejects captured locals, and traversing them here could
        // confuse an unrelated shadowed name with the surrounding loop binding.
        protected override void VisitFunction(Stmt.Function stmt)
        {
        }

        protected override void VisitArrowFunction(Expr.ArrowFunction expr)
        {
        }

        private void InvalidateIndexedReceiver(Expr receiver)
        {
            if (receiver is Expr.Variable variable)
                Invalidated.Add(variable.Name.Lexeme);
        }
    }
}
