namespace SharpTS.Parsing;

public partial class Parser
{
    /// <summary>
    /// Parses zero or more decorators (@expression or @expression(args)).
    /// Returns null if no decorators are present or decorator mode is None.
    /// </summary>
    private List<Decorator>? ParseDecorators()
    {
        if (_decoratorMode == DecoratorMode.None)
            return null;

        if (!Check(TokenType.AT))
            return null;

        List<Decorator> decorators = [];

        while (Match(TokenType.AT))
        {
            Token atToken = Previous();
            Expr decoratorExpr = ParseDecoratorExpression();
            decorators.Add(new Decorator(atToken, decoratorExpr));
        }

        return decorators.Count > 0 ? decorators : null;
    }

    /// <summary>
    /// Parses the expression part of a decorator.
    /// Supports: @identifier, @identifier.member, @identifier(args), @identifier.member(args)
    /// </summary>
    private Expr ParseDecoratorExpression()
    {
        // Start with identifier
        Token name = Consume(TokenType.IDENTIFIER, "Expect decorator name after '@'.");
        Expr expr = new Expr.Variable(name);

        // Handle member access chain: @Reflect.metadata
        while (Match(TokenType.DOT))
        {
            Token memberName = ConsumePropertyName("Expect property name after '.' in decorator.");
            expr = new Expr.Get(expr, memberName);
        }

        // Handle factory call: @decorator(args)
        if (Match(TokenType.LEFT_PAREN))
        {
            List<Expr> arguments = [];
            if (!Check(TokenType.RIGHT_PAREN))
            {
                do
                {
                    arguments.Add(Expression());
                } while (Match(TokenType.COMMA));
            }
            Token paren = Consume(TokenType.RIGHT_PAREN, "Expect ')' after decorator arguments.");
            expr = new Expr.Call(expr, paren, null, arguments);
        }

        return expr;
    }
}
