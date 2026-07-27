namespace SharpTS.Parsing;

public partial class Parser
{
    /// <summary>
    /// Parses one statement, recording its source extent. See <see cref="Declaration"/> for why
    /// span capture is centralized here.
    /// </summary>
    private Stmt Statement()
    {
        int first = _current;
        Stmt statement = StatementCore();
        RecordSpanFrom(statement, first);
        return statement;
    }

    private Stmt StatementCore()
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

        // Restricted production: break [no LineTerminator here] Label
        if (Check(TokenType.IDENTIFIER) && !HasLineTerminatorBeforeCurrent())
        {
            label = Advance();
        }

        ConsumeSemicolon("Expect ';' after 'break'.");
        return new Stmt.Break(keyword, label);
    }

    private Stmt ContinueStatement()
    {
        Token keyword = Previous();
        Token? label = null;

        // Restricted production: continue [no LineTerminator here] Label
        if (Check(TokenType.IDENTIFIER) && !HasLineTerminatorBeforeCurrent())
        {
            label = Advance();
        }

        ConsumeSemicolon("Expect ';' after 'continue'.");
        return new Stmt.Continue(keyword, label);
    }

    private Stmt ForStatement()
    {
        // Check for 'for await' pattern: for await (let/const varName of asyncIterable)
        bool isAsync = Match(TokenType.AWAIT);

        Consume(TokenType.LEFT_PAREN, "Expect '(' after 'for" + (isAsync ? " await" : "") + "'.");

        // Check for for...of pattern: for (let/const/var varName of iterable)
        if (Match(TokenType.LET, TokenType.CONST, TokenType.VAR))
        {
            // Track whether the keyword was `var` so the for-loop initializer is hoistable.
            bool initIsVar = Previous().Type == TokenType.VAR;
            // Check for destructuring patterns: for (const [a, b] of ...) or for (const {x, y} of ...)
            if (Check(TokenType.LEFT_BRACKET) || Check(TokenType.LEFT_BRACE))
            {
                return ParseDestructuringForOf(isAsync);
            }

            Token varName = ConsumeIdentifierName("Expect variable name.");

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

            Stmt initializer;
            Stmt firstDecl = new Stmt.Var(varName, typeAnnotation, initValue, IsVar: initIsVar);

            // Multi-declarator support: `for (var i = 0, j = 10; ...; ...)`
            if (Check(TokenType.COMMA))
            {
                var decls = new List<Stmt> { firstDecl };
                while (Match(TokenType.COMMA))
                {
                    Token extraName = ConsumeIdentifierName("Expect variable name.");
                    string? extraType = null;
                    if (Match(TokenType.COLON)) extraType = ParseTypeAnnotation();
                    Expr? extraInit = null;
                    if (Match(TokenType.EQUAL)) extraInit = Expression();
                    decls.Add(new Stmt.Var(extraName, extraType, extraInit, IsVar: initIsVar));
                }
                initializer = new Stmt.Sequence(decls);
            }
            else
            {
                initializer = firstDecl;
            }

            Consume(TokenType.SEMICOLON, "Expect ';' after variable declaration.");
            return FinishTraditionalFor(initializer);
        }

        // ECMA-262 also allows for-in/for-of using a previously-declared variable:
        //   var prop; for (prop in obj) {...}
        // Without this branch the parser fell through to traditional-for and
        // tripped over `in`/`of` mid-init, surfacing as ParseError. Detect by
        // peeking IDENTIFIER then IN/OF and reusing the for-in/for-of nodes
        // (with the existing var name as the loop binding).
        if (Check(TokenType.IDENTIFIER) && (CheckNext(TokenType.IN) || CheckNext(TokenType.OF)))
        {
            Token varName = Advance(); // identifier
            bool isOfLoop = Match(TokenType.OF);
            if (!isOfLoop) Consume(TokenType.IN, "Expect 'in' or 'of' after for-loop binding.");
            Expr rhs = Expression();
            Consume(TokenType.RIGHT_PAREN, "Expect ')' after for-loop expression.");
            Stmt body = Statement();
            if (isOfLoop)
                return new Stmt.ForOf(varName, null, rhs, body, isAsync);
            return new Stmt.ForIn(varName, null, rhs, body, IsDeclaration: false);
        }

        // Traditional for loop without let/const
        Stmt? init;
        if (Match(TokenType.SEMICOLON))
        {
            init = null;
        }
        else
        {
            init = ExpressionStatement(allowASI: false);
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
            increment = CommaExpression();
        }
        Consume(TokenType.RIGHT_PAREN, "Expect ')' after for clauses.");

        Stmt body = Statement();

        // Return native For statement instead of desugaring to while
        // This ensures continue statements properly execute the increment
        return new Stmt.For(initializer, condition, increment, body);
    }

    /// <summary>
    /// Parses a for...of loop with destructuring pattern in the variable position.
    /// Desugars: for (const [a, b] of iterable) { body }
    /// Into: for (const _dest0 of iterable) { const [a, b] = _dest0; body }
    /// </summary>
    private Stmt ParseDestructuringForOf(bool isAsync)
    {
        int line = Previous().Line;

        // Parse the destructuring pattern
        DestructurePattern pattern;
        if (Match(TokenType.LEFT_BRACKET))
        {
            pattern = ParseArrayPattern();
        }
        else
        {
            Consume(TokenType.LEFT_BRACE, "Expect '[' or '{' for destructuring pattern.");
            pattern = ParseObjectPattern();
        }

        // Must be followed by 'of' for destructuring for...of
        if (!Match(TokenType.OF))
        {
            if (isAsync)
            {
                throw new Exception("'for await' can only be used with 'for...of' loops.");
            }
            throw new Exception("Destructuring in for loop requires 'of' keyword. Use 'for (const [a, b] of iterable)'.");
        }

        Expr iterable = Expression();
        Consume(TokenType.RIGHT_PAREN, "Expect ')' after for...of expression.");
        Stmt originalBody = Statement();

        // Generate temp variable for iteration
        Token tempVar = GenerateTempVar(line);

        // Desugar the pattern with temp var as the initializer
        Expr tempVarExpr = new Expr.Variable(tempVar);
        Stmt destructuringStmt = pattern switch
        {
            ArrayPattern arrayPattern => DesugarArrayPattern(arrayPattern, tempVarExpr),
            ObjectPattern objectPattern => DesugarObjectPattern(objectPattern, tempVarExpr),
            _ => throw new Exception("Unexpected pattern type in for...of destructuring.")
        };

        // Combine destructuring statement with original body
        List<Stmt> bodyStatements = [destructuringStmt];
        if (originalBody is Stmt.Block block)
        {
            bodyStatements.AddRange(block.Statements);
        }
        else
        {
            bodyStatements.Add(originalBody);
        }

        Stmt newBody = new Stmt.Block(bodyStatements);

        return new Stmt.ForOf(tempVar, null, iterable, newBody, isAsync);
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
        ConsumeSemicolon("Expect ';' after do-while condition.");

        return new Stmt.DoWhile(body, condition);
    }

    private Stmt ReturnStatement()
    {
        Token keyword = Previous();
        Expr? value = null;

        // Restricted production: return [no LineTerminator here] Expression
        // If there's a newline after 'return', or we see ';'/'}'/EOF, return undefined.
        if (!Check(TokenType.SEMICOLON) && !Check(TokenType.RIGHT_BRACE)
            && !IsAtEnd() && !HasLineTerminatorBeforeCurrent())
        {
            value = Expression();
        }

        ConsumeSemicolon("Expect ';' after return value.");
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
        string? catchParamType = null;
        TypeNode? catchParamTypeNode = null;
        List<Stmt>? catchBlock = null;
        List<Stmt>? finallyBlock = null;

        if (Match(TokenType.CATCH))
        {
            // Optional catch binding (ES2019): catch { } without parameter
            if (Check(TokenType.LEFT_PAREN))
            {
                Consume(TokenType.LEFT_PAREN, "Expect '(' after 'catch'.");
                catchParam = Consume(TokenType.IDENTIFIER, "Expect catch parameter name.");
                // TS catch-binding annotation: `catch (e: any)` / `(e: unknown)`.
                // Parse any annotation here; restricting it to any/unknown is the
                // type checker's job (TS1196), not a parse error (#215).
                if (Match(TokenType.COLON))
                {
                    catchParamType = ParseTypeAnnotation();
                    catchParamTypeNode = TakeTypeNode();
                }
                Consume(TokenType.RIGHT_PAREN, "Expect ')' after catch parameter.");
            }
            // else: catchParam remains null for parameterless catch

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

        return new Stmt.TryCatch(tryBlock, catchParam, catchBlock, finallyBlock, catchParamType, catchParamTypeNode);
    }

    private Stmt ThrowStatement()
    {
        Token keyword = Previous();

        // Restricted production: throw [no LineTerminator here] Expression
        // A newline after 'throw' is a syntax error (throw must have an expression).
        if (HasLineTerminatorBeforeCurrent())
        {
            throw new Exception("Illegal newline after 'throw'.");
        }

        Expr value = Expression();
        ConsumeSemicolon("Expect ';' after throw value.");
        return new Stmt.Throw(keyword, value);
    }

    private List<Stmt> Block() => Block(parseFunctionPrologue: false);

    /// <summary>
    /// Parses a block of statements enclosed in braces.
    /// </summary>
    /// <param name="parseFunctionPrologue">If true, parses directive prologue at the start (for function bodies).</param>
    /// <param name="setStrictMode">If true, updates parser strict mode based on directive prologue.</param>
    private List<Stmt> Block(bool parseFunctionPrologue, bool setStrictMode = false)
    {
        List<Stmt> statements = [];

        // Parse directive prologue at the start of function bodies
        if (parseFunctionPrologue)
        {
            var directives = ParseDirectivePrologue();
            statements.AddRange(directives);

            // Check if "use strict" directive was found
            if (setStrictMode)
            {
                foreach (var directive in directives)
                {
                    if (directive.Value == "use strict")
                    {
                        _isStrictMode = true;
                        break;
                    }
                }
            }
        }

        while (!Check(TokenType.RIGHT_BRACE) && !IsAtEnd())
        {
            try
            {
                var decl = Declaration();
                if (decl != null) statements.Add(decl);
            }
            catch (Exception ex)
            {
                RecordError(ex.Message);
                SynchronizeInBlock();
                if (_diagnostics.HitErrorLimit) break;
            }
        }

        Consume(TokenType.RIGHT_BRACE, "Expect '}' after block.");
        return statements;
    }

    /// <summary>
    /// Synchronizes within a block, stopping at statement boundaries or the closing brace.
    /// </summary>
    private void SynchronizeInBlock()
    {
        while (!IsAtEnd())
        {
            // Stop at closing brace (end of block)
            if (Check(TokenType.RIGHT_BRACE)) return;

            // Check if we're at a token that starts a new statement
            switch (Peek().Type)
            {
                case TokenType.CLASS:
                case TokenType.FUNCTION:
                case TokenType.INTERFACE:
                case TokenType.LET:
                case TokenType.CONST:
                case TokenType.VAR:
                case TokenType.IF:
                case TokenType.FOR:
                case TokenType.WHILE:
                case TokenType.DO:
                case TokenType.SWITCH:
                case TokenType.TRY:
                case TokenType.RETURN:
                case TokenType.THROW:
                case TokenType.BREAK:
                case TokenType.CONTINUE:
                    return;
            }

            Advance();

            // Check if we just passed a semicolon
            if (Previous().Type == TokenType.SEMICOLON) return;
        }
    }

    private Stmt ExpressionStatement(bool allowASI = true)
    {
        Expr expr = CommaExpression();
        // Handle console.log specially for MVP
        if (expr is Expr.Call call && call.Callee is Expr.Variable varExpr && varExpr.Name.Lexeme == "console.log")
        {
             // Simplified for MVP
        }

        if (allowASI)
            ConsumeSemicolon("Expect ';' after expression.");
        else
            Consume(TokenType.SEMICOLON, "Expect ';' after expression.");
        return new Stmt.Expression(expr);
    }
}
