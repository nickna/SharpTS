using SharpTS.Parsing.Visitors;

namespace SharpTS.Parsing;

public partial class Parser
{
    // Traditional for initializers use the NoIn grammar. Preserve the parser's
    // existing ordinary `in` behavior while enforcing it for the new private-name
    // production. These overrides stop at subexpressions whose grammar enables In.
    private sealed class PrivateInForInitializerValidator : AstVisitorBase
    {
        protected override void VisitPrivateIn(Expr.PrivateIn expr)
            => throw new Exception("Private identifier 'in' expressions in for-loop initializers must be parenthesized.");

        protected override void VisitGrouping(Expr.Grouping expr) { }
        protected override void VisitArrayLiteral(Expr.ArrayLiteral expr) { }
        protected override void VisitObjectLiteral(Expr.ObjectLiteral expr) { }
        protected override void VisitTemplateLiteral(Expr.TemplateLiteral expr) { }
        protected override void VisitClassExpr(Expr.ClassExpr expr) { }
        protected override void VisitDynamicImport(Expr.DynamicImport expr) { }

        protected override void VisitCall(Expr.Call expr) => Visit(expr.Callee);
        protected override void VisitCallPrivate(Expr.CallPrivate expr) => Visit(expr.Object);
        protected override void VisitNew(Expr.New expr) => Visit(expr.Callee);
        protected override void VisitGetIndex(Expr.GetIndex expr) => Visit(expr.Object);
        protected override void VisitTaggedTemplateLiteral(Expr.TaggedTemplateLiteral expr) => Visit(expr.Tag);

        protected override void VisitTernary(Expr.Ternary expr)
        {
            Visit(expr.Condition);
            Visit(expr.ElseBranch);
        }

        protected override void VisitArrowFunction(Expr.ArrowFunction expr)
        {
            if (expr.ExpressionBody is not null)
                Visit(expr.ExpressionBody);
        }

        protected override void VisitDestructuringAssign(Expr.DestructuringAssign expr)
        {
            if (expr.RawDefault is not null)
                Visit(expr.RawDefault);
        }

        protected override void VisitSetIndex(Expr.SetIndex expr)
        {
            Visit(expr.Object);
            Visit(expr.Value);
        }

        protected override void VisitCompoundSetIndex(Expr.CompoundSetIndex expr)
        {
            Visit(expr.Object);
            Visit(expr.Value);
        }

        protected override void VisitLogicalSetIndex(Expr.LogicalSetIndex expr)
        {
            Visit(expr.Object);
            Visit(expr.Value);
        }
    }
}
