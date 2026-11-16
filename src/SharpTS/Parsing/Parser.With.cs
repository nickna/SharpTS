namespace SharpTS.Parsing;

public partial class Parser
{
    private Stmt WithStatement()
    {
        Token keyword = Previous();
        if (_isStrictMode)
            throw new Exception("SyntaxError: 'with' statements are not allowed in strict mode.");
        Consume(TokenType.LEFT_PAREN, "Expect '(' after 'with'.");
        Expr value = CommaExpression();
        Consume(TokenType.RIGHT_PAREN, "Expect ')' after with object.");
        Stmt body = Match(TokenType.VAR) ? VarDeclaration(isVar: true) : Statement();
        Token temporary = new(TokenType.IDENTIFIER, $"<with:{_tempVarCounter++}>", null, keyword.Line)
        { IsGeneratedTemporary = true };
        Token evaluated = new(TokenType.IDENTIFIER, $"<with:{_tempVarCounter++}>", null, keyword.Line)
        { IsGeneratedTemporary = true };
        var lowering = new WithStatementLowering(temporary, _spans, () =>
            new Token(TokenType.IDENTIFIER, $"<with:{_tempVarCounter++}>", null, keyword.Line)
            { IsGeneratedTemporary = true });
        // Evaluate in the original environment (notably for direct eval), then enter
        // a fresh captured binding so repeated entries retain distinct object scopes.
        return new Stmt.Sequence([
            new Stmt.Var(evaluated, null, WithStatementLowering.Helper(WithStatementLowering.ToObject, keyword, value)),
            new Stmt.Block([
                new Stmt.Const(temporary, null, new Expr.Variable(evaluated)),
                lowering.Rewrite(body)
            ])
        ]);
    }
}
