namespace SharpTS.Parsing;

public partial class Parser
{
    // ============== DESTRUCTURING PATTERN PARSING ==============

    private DestructurePattern ParseDestructurePattern()
    {
        if (Match(TokenType.LEFT_BRACKET))
            return ParseArrayPattern();
        if (Match(TokenType.LEFT_BRACE))
            return ParseObjectPattern();
        if (Match(TokenType.DOT_DOT_DOT))
        {
            Token restName = Consume(TokenType.IDENTIFIER, "Expect identifier after '...'.");
            return new RestPattern(restName);
        }

        Token patternName = Consume(TokenType.IDENTIFIER, "Expect identifier in pattern.");
        Expr? defaultValue = Match(TokenType.EQUAL) ? Expression() : null;
        return new IdentifierPattern(patternName, defaultValue);
    }

    private ArrayPattern ParseArrayPattern()
    {
        int line = Previous().Line;
        List<ArrayPatternElement> elements = [];

        while (!Check(TokenType.RIGHT_BRACKET) && !IsAtEnd())
        {
            if (Check(TokenType.COMMA))
            {
                // Hole in array: [a, , c]
                elements.Add(new ArrayPatternElement(null, IsHole: true));
            }
            else
            {
                elements.Add(new ArrayPatternElement(ParseDestructurePattern(), IsHole: false));
            }

            if (!Check(TokenType.RIGHT_BRACKET))
            {
                Consume(TokenType.COMMA, "Expect ',' between array pattern elements.");
            }
        }

        Consume(TokenType.RIGHT_BRACKET, "Expect ']' after array pattern.");
        return new ArrayPattern(elements, line);
    }

    private ObjectPattern ParseObjectPattern()
    {
        int line = Previous().Line;
        List<ObjectPatternProperty> properties = [];

        while (!Check(TokenType.RIGHT_BRACE) && !IsAtEnd())
        {
            // Handle rest pattern: { ...rest }
            if (Match(TokenType.DOT_DOT_DOT))
            {
                Token restName = Consume(TokenType.IDENTIFIER, "Expect identifier after '...'.");
                properties.Add(new ObjectPatternProperty(restName, new RestPattern(restName), null));
                // Rest must be last, so break out of loop
                break;
            }

            Token key = Consume(TokenType.IDENTIFIER, "Expect property name.");
            DestructurePattern value;
            Expr? defaultValue = null;

            if (Match(TokenType.COLON))
            {
                // Rename or nested: { x: newName } or { x: { nested } }
                if (Check(TokenType.LEFT_BRACE) || Check(TokenType.LEFT_BRACKET))
                {
                    value = ParseDestructurePattern();
                }
                else
                {
                    Token rename = Consume(TokenType.IDENTIFIER, "Expect identifier after ':'.");
                    if (Match(TokenType.EQUAL))
                        defaultValue = Expression();
                    value = new IdentifierPattern(rename, defaultValue);
                }
            }
            else
            {
                // Shorthand: { x } or { x = default }
                if (Match(TokenType.EQUAL))
                    defaultValue = Expression();
                value = new IdentifierPattern(key, defaultValue);
            }

            properties.Add(new ObjectPatternProperty(key, value, defaultValue));

            if (!Check(TokenType.RIGHT_BRACE))
            {
                Consume(TokenType.COMMA, "Expect ',' between object pattern properties.");
            }
        }

        Consume(TokenType.RIGHT_BRACE, "Expect '}' after object pattern.");
        return new ObjectPattern(properties, line);
    }

    // ============== DESTRUCTURING DECLARATIONS ==============

    private Stmt DestructureArrayDeclaration()
    {
        Consume(TokenType.LEFT_BRACKET, "Expect '[' for array destructuring.");
        ArrayPattern pattern = ParseArrayPattern();

        // Optional type annotation (ignored for now, inferred from initializer)
        if (Match(TokenType.COLON))
            ParseTypeAnnotation();

        Consume(TokenType.EQUAL, "Expect '=' after destructuring pattern.");
        Expr initializer = Expression();
        Consume(TokenType.SEMICOLON, "Expect ';' after variable declaration.");

        return DesugarArrayPattern(pattern, initializer);
    }

    private Stmt DestructureObjectDeclaration()
    {
        Consume(TokenType.LEFT_BRACE, "Expect '{' for object destructuring.");
        ObjectPattern pattern = ParseObjectPattern();

        // Optional type annotation (ignored for now, inferred from initializer)
        if (Match(TokenType.COLON))
            ParseTypeAnnotation();

        Consume(TokenType.EQUAL, "Expect '=' after destructuring pattern.");
        Expr initializer = Expression();
        Consume(TokenType.SEMICOLON, "Expect ';' after variable declaration.");

        return DesugarObjectPattern(pattern, initializer);
    }

    // ============== DESUGARING METHODS ==============

    private Stmt DesugarArrayPattern(ArrayPattern pattern, Expr initializer)
    {
        List<Stmt> statements = [];

        // const _dest0 = initializer;
        Token temp = GenerateTempVar(pattern.Line);
        statements.Add(new Stmt.Var(temp, null, initializer));

        int index = 0;
        foreach (var element in pattern.Elements)
        {
            if (element.IsHole)
            {
                index++;
                continue;
            }

            if (element.Pattern is RestPattern rest)
            {
                // const rest = _dest0.slice(index);
                Expr sliceCall = new Expr.Call(
                    new Expr.Get(new Expr.Variable(temp),
                        new Token(TokenType.IDENTIFIER, "slice", null, pattern.Line)),
                    new Token(TokenType.RIGHT_PAREN, ")", null, pattern.Line),
                    null,
                    [new Expr.Literal((double)index)]
                );
                statements.Add(new Stmt.Var(rest.Name, null, sliceCall));
                break; // Rest must be last
            }

            // Access expression: _dest0[index]
            Expr accessExpr = new Expr.GetIndex(
                new Expr.Variable(temp),
                new Expr.Literal((double)index)
            );

            if (element.Pattern is IdentifierPattern id)
            {
                // Apply default value if present
                if (id.DefaultValue != null)
                {
                    accessExpr = new Expr.NullishCoalescing(accessExpr, id.DefaultValue);
                }
                statements.Add(new Stmt.Var(id.Name, null, accessExpr));
            }
            else if (element.Pattern is ArrayPattern nestedArray)
            {
                statements.Add(DesugarArrayPattern(nestedArray, accessExpr));
            }
            else if (element.Pattern is ObjectPattern nestedObj)
            {
                statements.Add(DesugarObjectPattern(nestedObj, accessExpr));
            }

            index++;
        }

        return new Stmt.Sequence(statements);
    }

    private Stmt DesugarObjectPattern(ObjectPattern pattern, Expr initializer)
    {
        List<Stmt> statements = [];
        List<string> usedKeys = [];

        // const _dest0 = initializer;
        Token temp = GenerateTempVar(pattern.Line);
        statements.Add(new Stmt.Var(temp, null, initializer));

        foreach (var prop in pattern.Properties)
        {
            // Handle rest pattern: const { x, ...rest } = obj
            if (prop.Value is RestPattern rest)
            {
                // Generate: const rest = __objectRest(_dest0, ["x", "y", ...usedKeys]);
                var excludeKeysExpr = new Expr.ArrayLiteral(
                    usedKeys.Select(k => new Expr.Literal(k) as Expr).ToList()
                );
                var restCall = new Expr.Call(
                    new Expr.Variable(new Token(TokenType.IDENTIFIER, "__objectRest", null, pattern.Line)),
                    new Token(TokenType.RIGHT_PAREN, ")", null, pattern.Line),
                    null,
                    [new Expr.Variable(temp), excludeKeysExpr]
                );
                statements.Add(new Stmt.Var(rest.Name, null, restCall));
                break; // Rest must be last
            }

            usedKeys.Add(prop.Key.Lexeme);

            // Access expression: _dest0.key
            Expr accessExpr = new Expr.Get(new Expr.Variable(temp), prop.Key);

            if (prop.Value is IdentifierPattern id)
            {
                // Apply default value if present
                Expr? defaultVal = prop.DefaultValue ?? id.DefaultValue;
                if (defaultVal != null)
                {
                    accessExpr = new Expr.NullishCoalescing(accessExpr, defaultVal);
                }
                statements.Add(new Stmt.Var(id.Name, null, accessExpr));
            }
            else if (prop.Value is ArrayPattern nestedArray)
            {
                statements.Add(DesugarArrayPattern(nestedArray, accessExpr));
            }
            else if (prop.Value is ObjectPattern nestedObj)
            {
                statements.Add(DesugarObjectPattern(nestedObj, accessExpr));
            }
        }

        return new Stmt.Sequence(statements);
    }
}
