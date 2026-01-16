namespace SharpTS.Parsing;

public partial class Parser
{
    private Stmt Statement()
    {
        // Handle empty statements (just semicolons)
        if (Match(TokenType.SEMICOLON)) return new Stmt.Expression(new Expr.Literal(null));

        if (Match(TokenType.BREAK)) return BreakStatement();
        if (Match(TokenType.CONTINUE)) return ContinueStatement();
        if (Match(TokenType.FOR)) return ForStatement();
        if (Match(TokenType.IF)) return IfStatement();
        if (Match(TokenType.SWITCH)) return SwitchStatement();
        if (Match(TokenType.TRY)) return TryStatement();
        if (Match(TokenType.THROW)) return ThrowStatement();
        if (Match(TokenType.DO)) return DoWhileStatement();
        if (Match(TokenType.WHILE)) return WhileStatement();
        if (Match(TokenType.RETURN)) return ReturnStatement();
        if (Match(TokenType.LEFT_BRACE)) return new Stmt.Block(Block());

        // Check for labeled statement: identifier : statement
        if (Check(TokenType.IDENTIFIER) && PeekNext().Type == TokenType.COLON)
        {
            return LabeledStatement();
        }

        return ExpressionStatement();
    }

    private Stmt LabeledStatement()
    {
        Token label = Advance();                              // Consume the label identifier
        Consume(TokenType.COLON, "Expect ':' after label.");  // Consume the colon
        Stmt statement = Statement();                         // Parse the labeled statement (recursive)
        return new Stmt.LabeledStatement(label, statement);
    }

    private Stmt BreakStatement()
    {
        Token keyword = Previous();
        Token? label = null;

        // Check for optional label: break labelName;
        if (Check(TokenType.IDENTIFIER))
        {
            label = Advance();
        }

        Consume(TokenType.SEMICOLON, "Expect ';' after 'break'.");
        return new Stmt.Break(keyword, label);
    }

    private Stmt ContinueStatement()
    {
        Token keyword = Previous();
        Token? label = null;

        // Check for optional label: continue labelName;
        if (Check(TokenType.IDENTIFIER))
        {
            label = Advance();
        }

        Consume(TokenType.SEMICOLON, "Expect ';' after 'continue'.");
        return new Stmt.Continue(keyword, label);
    }

    private Stmt ForStatement()
    {
        // Check for 'for await' pattern: for await (let/const varName of asyncIterable)
        bool isAsync = Match(TokenType.AWAIT);

        Consume(TokenType.LEFT_PAREN, "Expect '(' after 'for" + (isAsync ? " await" : "") + "'.");

        // Check for for...of pattern: for (let/const varName of iterable)
        if (Match(TokenType.LET, TokenType.CONST))
        {
            Token varName = Consume(TokenType.IDENTIFIER, "Expect variable name.");

            // Check for optional type annotation
            string? typeAnnotation = null;
            if (Match(TokenType.COLON))
            {
                typeAnnotation = ParseTypeAnnotation();
            }

            // If we see 'of', this is a for...of loop
            if (Match(TokenType.OF))
            {
                Expr iterable = Expression();
                Consume(TokenType.RIGHT_PAREN, "Expect ')' after for...of expression.");
                Stmt body = Statement();
                return new Stmt.ForOf(varName, typeAnnotation, iterable, body, isAsync);
            }

            // 'for await' must be followed by 'of', not 'in' or traditional for
            if (isAsync)
            {
                throw new Exception("'for await' can only be used with 'for...of' loops.");
            }

            // If we see 'in', this is a for...in loop
            if (Match(TokenType.IN))
            {
                Expr obj = Expression();
                Consume(TokenType.RIGHT_PAREN, "Expect ')' after for...in expression.");
                Stmt body = Statement();
                return new Stmt.ForIn(varName, typeAnnotation, obj, body);
            }

            // Otherwise it's a traditional for loop - we need to handle the initializer
            // We've already consumed let/const and the variable name, so reconstruct
            Expr? initValue = null;
            if (Match(TokenType.EQUAL))
            {
                initValue = Expression();
            }
            Consume(TokenType.SEMICOLON, "Expect ';' after variable declaration.");

            Stmt initializer = new Stmt.Var(varName, typeAnnotation, initValue);
            return FinishTraditionalFor(initializer);
        }

        // Traditional for loop without let/const
        Stmt? init;
        if (Match(TokenType.SEMICOLON))
        {
            init = null;
        }
        else
        {
            init = ExpressionStatement();
        }

        return FinishTraditionalFor(init);
    }

    private Stmt FinishTraditionalFor(Stmt? initializer)
    {
        Expr? condition = null;
        if (!Check(TokenType.SEMICOLON))
        {
            condition = Expression();
        }
        Consume(TokenType.SEMICOLON, "Expect ';' after loop condition.");

        Expr? increment = null;
        if (!Check(TokenType.RIGHT_PAREN))
        {
            increment = Expression();
        }
        Consume(TokenType.RIGHT_PAREN, "Expect ')' after for clauses.");

        Stmt body = Statement();

        // Return native For statement instead of desugaring to while
        // This ensures continue statements properly execute the increment
        return new Stmt.For(initializer, condition, increment, body);
    }

    private Stmt WhileStatement()
    {
        Consume(TokenType.LEFT_PAREN, "Expect '(' after 'while'.");
        Expr condition = Expression();
        Consume(TokenType.RIGHT_PAREN, "Expect ')' after condition.");
        Stmt body = Statement();

        return new Stmt.While(condition, body);
    }

    private Stmt DoWhileStatement()
    {
        Stmt body = Statement();
        Consume(TokenType.WHILE, "Expect 'while' after do body.");
        Consume(TokenType.LEFT_PAREN, "Expect '(' after 'while'.");
        Expr condition = Expression();
        Consume(TokenType.RIGHT_PAREN, "Expect ')' after condition.");
        Consume(TokenType.SEMICOLON, "Expect ';' after do-while condition.");

        return new Stmt.DoWhile(body, condition);
    }

    private Stmt ReturnStatement()
    {
        Token keyword = Previous();
        Expr? value = null;
        if (!Check(TokenType.SEMICOLON))
        {
            value = Expression();
        }

        Consume(TokenType.SEMICOLON, "Expect ';' after return value.");
        return new Stmt.Return(keyword, value);
    }

    private Stmt IfStatement()
    {
        Consume(TokenType.LEFT_PAREN, "Expect '(' after 'if'.");
        Expr condition = Expression();
        Consume(TokenType.RIGHT_PAREN, "Expect ')' after if condition.");

        Stmt thenBranch = Statement();
        Stmt? elseBranch = null;
        if (Match(TokenType.ELSE))
        {
            elseBranch = Statement();
        }

        return new Stmt.If(condition, thenBranch, elseBranch);
    }

    private Stmt SwitchStatement()
    {
        Consume(TokenType.LEFT_PAREN, "Expect '(' after 'switch'.");
        Expr subject = Expression();
        Consume(TokenType.RIGHT_PAREN, "Expect ')' after switch expression.");
        Consume(TokenType.LEFT_BRACE, "Expect '{' before switch body.");

        List<Stmt.SwitchCase> cases = [];
        List<Stmt>? defaultBody = null;

        while (!Check(TokenType.RIGHT_BRACE) && !IsAtEnd())
        {
            if (Match(TokenType.CASE))
            {
                Expr caseValue = Expression();
                Consume(TokenType.COLON, "Expect ':' after case value.");

                List<Stmt> caseBody = [];
                while (!Check(TokenType.CASE) && !Check(TokenType.DEFAULT) &&
                       !Check(TokenType.RIGHT_BRACE) && !IsAtEnd())
                {
                    caseBody.Add(Declaration());
                }
                cases.Add(new Stmt.SwitchCase(caseValue, caseBody));
            }
            else if (Match(TokenType.DEFAULT))
            {
                Consume(TokenType.COLON, "Expect ':' after 'default'.");

                defaultBody = [];
                while (!Check(TokenType.CASE) && !Check(TokenType.RIGHT_BRACE) && !IsAtEnd())
                {
                    defaultBody.Add(Declaration());
                }
            }
            else
            {
                throw new Exception("Expect 'case' or 'default' in switch body.");
            }
        }

        Consume(TokenType.RIGHT_BRACE, "Expect '}' after switch body.");
        return new Stmt.Switch(subject, cases, defaultBody);
    }

    private Stmt TryStatement()
    {
        Consume(TokenType.LEFT_BRACE, "Expect '{' after 'try'.");
        List<Stmt> tryBlock = Block();

        Token? catchParam = null;
        List<Stmt>? catchBlock = null;
        List<Stmt>? finallyBlock = null;

        if (Match(TokenType.CATCH))
        {
            Consume(TokenType.LEFT_PAREN, "Expect '(' after 'catch'.");
            catchParam = Consume(TokenType.IDENTIFIER, "Expect catch parameter name.");
            Consume(TokenType.RIGHT_PAREN, "Expect ')' after catch parameter.");
            Consume(TokenType.LEFT_BRACE, "Expect '{' before catch block.");
            catchBlock = Block();
        }

        if (Match(TokenType.FINALLY))
        {
            Consume(TokenType.LEFT_BRACE, "Expect '{' after 'finally'.");
            finallyBlock = Block();
        }

        if (catchBlock == null && finallyBlock == null)
        {
            throw new Exception("Try statement must have catch or finally clause.");
        }

        return new Stmt.TryCatch(tryBlock, catchParam, catchBlock, finallyBlock);
    }

    private Stmt ThrowStatement()
    {
        Token keyword = Previous();
        Expr value = Expression();
        Consume(TokenType.SEMICOLON, "Expect ';' after throw value.");
        return new Stmt.Throw(keyword, value);
    }

    private List<Stmt> Block()
    {
        List<Stmt> statements = [];
        while (!Check(TokenType.RIGHT_BRACE) && !IsAtEnd())
        {
            statements.Add(Declaration());
        }

        Consume(TokenType.RIGHT_BRACE, "Expect '}' after block.");
        return statements;
    }

    private Stmt ExpressionStatement()
    {
        Expr expr = Expression();
        // Handle console.log specially for MVP
        if (expr is Expr.Call call && call.Callee is Expr.Variable varExpr && varExpr.Name.Lexeme == "console.log")
        {
             // Simplified for MVP
        }

        Consume(TokenType.SEMICOLON, "Expect ';' after expression.");
        return new Stmt.Expression(expr);
    }
}
