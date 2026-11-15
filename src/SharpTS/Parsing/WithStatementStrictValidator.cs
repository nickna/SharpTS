using SharpTS.Parsing.Visitors;

namespace SharpTS.Parsing;

internal sealed class WithStatementStrictValidator(bool strict) : AstVisitorBase
{
    private bool _strict = strict;
    internal List<Token> InvalidKeywords { get; } = [];

    private void InStrictContext(bool strictContext, Action visit)
    {
        bool previous = _strict;
        _strict |= strictContext;
        try { visit(); }
        finally { _strict = previous; }
    }

    private static bool HasStrictDirective(List<Stmt>? body) =>
        body?.Any(statement => statement is Stmt.Directive { Value: "use strict" }) == true;

    protected override void VisitCall(Expr.Call expression)
    {
        if (_strict && expression.Callee is Expr.Variable { Name.IsGeneratedTemporary: true, Name.Lexeme: WithStatementLowering.ToObject })
            InvalidKeywords.Add(expression.Paren);
        base.VisitCall(expression);
    }

    protected override void VisitFunction(Stmt.Function statement) =>
        InStrictContext(HasStrictDirective(statement.Body), () => base.VisitFunction(statement));

    protected override void VisitArrowFunction(Expr.ArrowFunction expression) =>
        InStrictContext(HasStrictDirective(expression.BlockBody), () => base.VisitArrowFunction(expression));

    protected override void VisitClass(Stmt.Class statement) => InStrictContext(true, () =>
    {
        base.VisitClass(statement);
        foreach (var block in (statement.StaticInitializers ?? []).OfType<Stmt.StaticBlock>()) Visit(block);
    });
    protected override void VisitClassExpr(Expr.ClassExpr expression) => InStrictContext(true, () =>
    {
        base.VisitClassExpr(expression);
        foreach (var block in (expression.StaticInitializers ?? []).OfType<Stmt.StaticBlock>()) Visit(block);
    });
}
