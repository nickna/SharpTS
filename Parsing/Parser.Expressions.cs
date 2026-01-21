namespace SharpTS.Parsing;

public partial class Parser
{
    private Expr Expression() => Assignment();

    private Expr Assignment()
    {
        // Check for single-parameter arrow function without parentheses: x => expr
        if (Check(TokenType.IDENTIFIER) && CheckNext(TokenType.ARROW))
        {
            Token paramName = Advance(); // consume identifier
            Advance(); // consume '=>'

            // Parse the body - either block or expression
            List<Stmt>? body = null;
            Expr? exprBody = null;

            if (Match(TokenType.LEFT_BRACE))
            {
                body = Block();
            }
            else
            {
                exprBody = Assignment(); // Use Assignment for proper precedence (allows nested arrows)
            }

            var param = new Stmt.Parameter(paramName, null, null);
            return new Expr.ArrowFunction(Name: null, TypeParams: null, ThisType: null, Parameters: [param], ExpressionBody: exprBody, BlockBody: body, ReturnType: null);
        }

        // Check for single-parameter async arrow function: async x => expr
        // This case is handled in Primary() for async (params) => but we need async x => too

        Expr expr = Ternary();

        if (Match(TokenType.EQUAL))
        {
            Token equals = Previous();
            Expr value = Assignment();

            if (expr is Expr.Variable variable)
            {
                return new Expr.Assign(variable.Name, value);
            }
            else if (expr is Expr.Get get)
            {
                return new Expr.Set(get.Object, get.Name, value);
            }
            else if (expr is Expr.GetPrivate getPrivate)
            {
                return new Expr.SetPrivate(getPrivate.Object, getPrivate.Name, value);
            }
            else if (expr is Expr.GetIndex getIndex)
            {
                return new Expr.SetIndex(getIndex.Object, getIndex.Index, value);
            }

            throw new Exception("Invalid assignment target.");
        }

        // Compound assignment operators
        if (Match(TokenType.PLUS_EQUAL, TokenType.MINUS_EQUAL, TokenType.STAR_EQUAL,
                  TokenType.SLASH_EQUAL, TokenType.PERCENT_EQUAL,
                  TokenType.AMPERSAND_EQUAL, TokenType.PIPE_EQUAL, TokenType.CARET_EQUAL,
                  TokenType.LESS_LESS_EQUAL, TokenType.GREATER_GREATER_EQUAL, TokenType.GREATER_GREATER_GREATER_EQUAL))
        {
            Token op = Previous();
            Expr value = Assignment();

            if (expr is Expr.Variable variable)
            {
                return new Expr.CompoundAssign(variable.Name, op, value);
            }
            else if (expr is Expr.Get get)
            {
                return new Expr.CompoundSet(get.Object, get.Name, op, value);
            }
            else if (expr is Expr.GetIndex getIndex)
            {
                return new Expr.CompoundSetIndex(getIndex.Object, getIndex.Index, op, value);
            }

            throw new Exception("Invalid compound assignment target.");
        }

        // Logical assignment operators (&&=, ||=, ??=) - have short-circuit semantics
        if (Match(TokenType.AND_AND_EQUAL, TokenType.OR_OR_EQUAL, TokenType.QUESTION_QUESTION_EQUAL))
        {
            Token op = Previous();
            Expr value = Assignment();

            if (expr is Expr.Variable variable)
            {
                return new Expr.LogicalAssign(variable.Name, op, value);
            }
            else if (expr is Expr.Get get)
            {
                return new Expr.LogicalSet(get.Object, get.Name, op, value);
            }
            else if (expr is Expr.GetIndex getIndex)
            {
                return new Expr.LogicalSetIndex(getIndex.Object, getIndex.Index, op, value);
            }

            throw new Exception("Invalid logical assignment target.");
        }

        return expr;
    }

    private Expr Ternary()
    {
        Expr expr = NullishCoalescing();

        if (Match(TokenType.QUESTION))
        {
            Expr thenBranch = Ternary();
            Consume(TokenType.COLON, "Expect ':' in ternary expression.");
            Expr elseBranch = Ternary();
            expr = new Expr.Ternary(expr, thenBranch, elseBranch);
        }

        return expr;
    }

    private Expr NullishCoalescing()
    {
        Expr expr = Or();

        while (Match(TokenType.QUESTION_QUESTION))
        {
            Expr right = Or();
            expr = new Expr.NullishCoalescing(expr, right);
        }

        return expr;
    }

    private Expr Or()
    {
        Expr expr = And();

        while (Match(TokenType.OR_OR))
        {
            Token op = Previous();
            Expr right = And();
            expr = new Expr.Logical(expr, op, right);
        }

        return expr;
    }

    private Expr And()
    {
        Expr expr = BitwiseOr();

        while (Match(TokenType.AND_AND))
        {
            Token op = Previous();
            Expr right = BitwiseOr();
            expr = new Expr.Logical(expr, op, right);
        }

        return expr;
    }

    private Expr BitwiseOr()
    {
        Expr expr = BitwiseXor();

        while (Match(TokenType.PIPE))
        {
            Token op = Previous();
            Expr right = BitwiseXor();
            expr = new Expr.Binary(expr, op, right);
        }

        return expr;
    }

    private Expr BitwiseXor()
    {
        Expr expr = BitwiseAnd();

        while (Match(TokenType.CARET))
        {
            Token op = Previous();
            Expr right = BitwiseAnd();
            expr = new Expr.Binary(expr, op, right);
        }

        return expr;
    }

    private Expr BitwiseAnd()
    {
        Expr expr = Equality();

        while (Match(TokenType.AMPERSAND))
        {
            Token op = Previous();
            Expr right = Equality();
            expr = new Expr.Binary(expr, op, right);
        }

        return expr;
    }

    private Expr Equality()
    {
        Expr expr = Comparison();

        while (Match(TokenType.BANG_EQUAL, TokenType.EQUAL_EQUAL,
                     TokenType.BANG_EQUAL_EQUAL, TokenType.EQUAL_EQUAL_EQUAL))
        {
            Token op = Previous();
            Expr right = Comparison();
            expr = new Expr.Binary(expr, op, right);
        }

        return expr;
    }

    private Expr Comparison()
    {
        Expr expr = Shift();

        while (Match(TokenType.GREATER, TokenType.GREATER_EQUAL, TokenType.LESS, TokenType.LESS_EQUAL, TokenType.IN, TokenType.INSTANCEOF))
        {
            Token op = Previous();
            Expr right = Shift();
            expr = new Expr.Binary(expr, op, right);
        }

        return expr;
    }

    private Expr Shift()
    {
        Expr expr = Term();

        while (Match(TokenType.LESS_LESS, TokenType.GREATER_GREATER, TokenType.GREATER_GREATER_GREATER))
        {
            Token op = Previous();
            Expr right = Term();
            expr = new Expr.Binary(expr, op, right);
        }

        return expr;
    }

    private Expr Term()
    {
        Expr expr = Factor();

        while (Match(TokenType.MINUS, TokenType.PLUS))
        {
            Token op = Previous();
            Expr right = Factor();
            expr = new Expr.Binary(expr, op, right);
        }

        return expr;
    }

    private Expr Factor()
    {
        Expr expr = Exponentiation();

        while (Match(TokenType.SLASH, TokenType.STAR, TokenType.PERCENT))
        {
            Token op = Previous();
            Expr right = Exponentiation();
            expr = new Expr.Binary(expr, op, right);
        }

        return expr;
    }

    private Expr Exponentiation()
    {
        Expr expr = Unary();

        // ** is right-associative, so we use recursion instead of a loop
        if (Match(TokenType.STAR_STAR))
        {
            Token op = Previous();
            Expr right = Exponentiation(); // Right-associative
            expr = new Expr.Binary(expr, op, right);
        }

        return expr;
    }

    private Expr Unary()
    {
        // Check for angle-bracket type assertion: <Type>expr
        if (Check(TokenType.LESS))
        {
            var assertion = TryParseAngleBracketAssertion();
            if (assertion != null) return assertion;
        }

        // Prefix increment/decrement
        if (Match(TokenType.PLUS_PLUS, TokenType.MINUS_MINUS))
        {
            Token op = Previous();
            Expr operand = Unary();
            if (operand is not (Expr.Variable or Expr.Get or Expr.GetIndex))
            {
                throw new Exception("Invalid operand for prefix increment/decrement.");
            }
            return new Expr.PrefixIncrement(op, operand);
        }

        if (Match(TokenType.BANG, TokenType.MINUS, TokenType.TYPEOF, TokenType.TILDE))
        {
            Token op = Previous();
            Expr right = Unary();
            return new Expr.Unary(op, right);
        }

        // await expression: await expr
        if (Match(TokenType.AWAIT))
        {
            Token keyword = Previous();
            Expr expression = Unary();
            return new Expr.Await(keyword, expression);
        }

        // yield expression: yield expr or yield* expr
        if (Match(TokenType.YIELD))
        {
            Token keyword = Previous();
            bool isDelegating = Match(TokenType.STAR);  // yield* delegates to another iterable

            // yield can be bare (yields undefined) or have an expression
            Expr? value = null;
            if (!Check(TokenType.SEMICOLON) && !Check(TokenType.RIGHT_BRACE) &&
                !Check(TokenType.RIGHT_PAREN) && !Check(TokenType.COMMA) && !IsAtEnd())
            {
                value = Assignment();  // Use Assignment to handle full expressions
            }

            return new Expr.Yield(keyword, value, isDelegating);
        }

        if (Match(TokenType.NEW))
        {
            // Parse the callee expression: can be identifier, member access, or parenthesized expression
            // Examples: new ClassName(), new Namespace.Class(), new (condition ? A : B)()
            Expr callee = ParseNewCallee();

            // Parse optional type arguments: new Class<T>()
            List<string>? typeArgs = TryParseTypeArguments();

            // Parse arguments
            Consume(TokenType.LEFT_PAREN, "Expect '(' after new expression callee.");
            List<Expr> arguments = [];
            if (!Check(TokenType.RIGHT_PAREN))
            {
                do
                {
                    arguments.Add(Expression());
                } while (Match(TokenType.COMMA));
            }
            Consume(TokenType.RIGHT_PAREN, "Expect ')' after arguments.");
            return new Expr.New(callee, typeArgs, arguments);
        }

        return Call();
    }

    private Expr Call()
    {
        Expr expr = Primary();

        while (true)
        {
            // Check for type arguments before call: func<T>(args)
            List<string>? typeArgs = null;
            if (Check(TokenType.LESS))
            {
                typeArgs = TryParseTypeArgumentsForCall();
            }

            if (typeArgs != null || Match(TokenType.LEFT_PAREN))
            {
                expr = FinishCall(expr, typeArgs);
            }
            else if (Match(TokenType.DOT))
            {
                // Check for private identifier access: obj.#field
                if (Match(TokenType.PRIVATE_IDENTIFIER))
                {
                    Token name = Previous();
                    // Check for method call: obj.#method(args)
                    if (Check(TokenType.LEFT_PAREN))
                    {
                        Consume(TokenType.LEFT_PAREN, "Expect '(' after private method name.");
                        List<Expr> args = [];
                        if (!Check(TokenType.RIGHT_PAREN))
                        {
                            do
                            {
                                if (Match(TokenType.DOT_DOT_DOT))
                                {
                                    args.Add(new Expr.Spread(Expression()));
                                }
                                else
                                {
                                    args.Add(Expression());
                                }
                            } while (Match(TokenType.COMMA));
                        }
                        Consume(TokenType.RIGHT_PAREN, "Expect ')' after arguments.");
                        expr = new Expr.CallPrivate(expr, name, args);
                    }
                    else
                    {
                        // Field access: obj.#field
                        expr = new Expr.GetPrivate(expr, name);
                    }
                }
                else
                {
                    Token name = ConsumePropertyName("Expect property name after '.'.");
                    if (expr is Expr.Variable v && v.Name.Lexeme == "console" && name.Lexeme == "log")
                    {
                        expr = new Expr.Variable(new Token(TokenType.IDENTIFIER, "console.log", null, name.Line));
                    }
                    else
                    {
                        expr = new Expr.Get(expr, name);
                    }
                }
            }
            else if (Match(TokenType.QUESTION_DOT))
            {
                Token name = ConsumePropertyName("Expect property name after '?.'.");
                expr = new Expr.Get(expr, name, Optional: true);
            }
            else if (Match(TokenType.LEFT_BRACKET))
            {
                Expr index = Expression();
                Consume(TokenType.RIGHT_BRACKET, "Expect ']' after index.");
                expr = new Expr.GetIndex(expr, index);
            }
            else if (Match(TokenType.PLUS_PLUS, TokenType.MINUS_MINUS))
            {
                // Postfix increment/decrement
                Token op = Previous();
                if (expr is not (Expr.Variable or Expr.Get or Expr.GetIndex))
                {
                    throw new Exception("Invalid operand for postfix increment/decrement.");
                }
                expr = new Expr.PostfixIncrement(expr, op);
            }
            else if (Match(TokenType.AS))
            {
                // Check for 'as const' - constant assertion for deep readonly inference
                if (Check(TokenType.CONST))
                {
                    Advance(); // consume 'const'
                    expr = new Expr.TypeAssertion(expr, "const");
                }
                else
                {
                    // Type assertion: expr as Type
                    string targetType = ParseTypeAnnotation();
                    expr = new Expr.TypeAssertion(expr, targetType);
                }
            }
            else if (Match(TokenType.SATISFIES))
            {
                // Satisfies operator: expr satisfies Type (TS 4.9+)
                // Validates that expr matches Type without widening the inferred type
                string constraintType = ParseTypeAnnotation();
                expr = new Expr.Satisfies(expr, constraintType);
            }
            else if (Match(TokenType.BANG))
            {
                // Non-null assertion: expr!
                // Asserts the value is not null/undefined at compile time
                expr = new Expr.NonNullAssertion(expr);
            }
            // Tagged template literal: expr`template ${x} literal`
            else if (Check(TokenType.TEMPLATE_FULL) || Check(TokenType.TEMPLATE_HEAD))
            {
                expr = ParseTaggedTemplateLiteral(expr);
            }
            else
            {
                break;
            }
        }

        return expr;
    }

    private Expr FinishCall(Expr callee, List<string>? typeArgs = null)
    {
        List<Expr> arguments = [];
        if (!Check(TokenType.RIGHT_PAREN))
        {
            do
            {
                if (Match(TokenType.DOT_DOT_DOT))
                {
                    arguments.Add(new Expr.Spread(Expression()));
                }
                else
                {
                    arguments.Add(Expression());
                }
            } while (Match(TokenType.COMMA));
        }

        Token paren = Consume(TokenType.RIGHT_PAREN, "Expect ')' after arguments.");
        return new Expr.Call(callee, paren, typeArgs, arguments);
    }

    /// <summary>
    /// Parses the callee expression for a 'new' expression.
    /// Handles: identifiers, member access chains, and parenthesized expressions.
    /// Does NOT handle type arguments or call arguments (those are parsed by caller).
    /// </summary>
    private Expr ParseNewCallee()
    {
        Expr callee;

        // Check for parenthesized expression: new (condition ? A : B)()
        if (Match(TokenType.LEFT_PAREN))
        {
            callee = Expression();
            Consume(TokenType.RIGHT_PAREN, "Expect ')' after expression in new callee.");
            return callee;
        }

        // Otherwise expect an identifier (class name or start of namespace path)
        Token firstIdent = Consume(TokenType.IDENTIFIER, "Expect class name after 'new'.");
        callee = new Expr.Variable(firstIdent);

        // Handle member access chain: Namespace.SubNamespace.ClassName
        while (Match(TokenType.DOT))
        {
            Token name = Consume(TokenType.IDENTIFIER, "Expect identifier after '.' in new expression.");
            callee = new Expr.Get(callee, name);
        }

        return callee;
    }

    private Expr Primary()
    {
        if (Match(TokenType.FALSE)) return new Expr.Literal(false);
        if (Match(TokenType.TRUE)) return new Expr.Literal(true);
        if (Match(TokenType.NULL)) return new Expr.Literal(null);
        if (Match(TokenType.UNDEFINED)) return new Expr.Literal(SharpTS.Runtime.Types.SharpTSUndefined.Instance);
        if (Match(TokenType.NUMBER, TokenType.STRING, TokenType.BIGINT_LITERAL)) return new Expr.Literal(Previous().Literal);
        if (Match(TokenType.REGEX))
        {
            var value = (RegexLiteralValue)Previous().Literal!;
            return new Expr.RegexLiteral(value.Pattern, value.Flags);
        }
        if (Match(TokenType.THIS)) return new Expr.This(Previous());
        if (Match(TokenType.SUPER))
        {
            Token keyword = Previous();
            // super() for constructor calls, super.method() for method calls
            if (Check(TokenType.LEFT_PAREN))
            {
                // super() - constructor call, Method is null
                return new Expr.Super(keyword, null);
            }
            Consume(TokenType.DOT, "Expect '.' or '(' after 'super'.");
            Token method;
            if (Match(TokenType.IDENTIFIER, TokenType.CONSTRUCTOR))
            {
                method = Previous();
            }
            else
            {
                throw new Exception("Expect superclass method name.");
            }
            return new Expr.Super(keyword, method);
        }

        // Dynamic import: import(pathExpr) or import.meta
        if (Match(TokenType.IMPORT))
        {
            Token keyword = Previous();

            // Check for import.meta
            if (Match(TokenType.DOT))
            {
                Token meta = Consume(TokenType.IDENTIFIER, "Expect 'meta' after 'import.'.");
                if (meta.Lexeme != "meta")
                    throw new Exception($"Parse Error: Unexpected import.{meta.Lexeme}. Only 'import.meta' is supported.");
                return new Expr.ImportMeta(keyword);
            }

            // Dynamic import: import(pathExpr)
            Consume(TokenType.LEFT_PAREN, "Expect '(' after 'import' for dynamic import.");
            Expr pathExpr = Expression();
            Consume(TokenType.RIGHT_PAREN, "Expect ')' after import path.");
            return new Expr.DynamicImport(keyword, pathExpr);
        }

        // Class expression: class [Name] { ... }
        if (Match(TokenType.CLASS))
        {
            return ClassExpression();
        }

        // Anonymous function expression: function(params) { body } or function name(params) { body }
        if (Match(TokenType.FUNCTION))
        {
            return FunctionExpression();
        }

        if (Match(TokenType.IDENTIFIER)) return new Expr.Variable(Previous());

        // Symbol and BigInt are special callable constructors
        if (Match(TokenType.SYMBOL, TokenType.BIGINT)) return new Expr.Variable(Previous());

        if (Match(TokenType.LEFT_BRACKET))
        {
            List<Expr> elements = [];
            if (!Check(TokenType.RIGHT_BRACKET))
            {
                do
                {
                    // Handle trailing comma: [1, 2, 3,]
                    if (Check(TokenType.RIGHT_BRACKET)) break;

                    if (Match(TokenType.DOT_DOT_DOT))
                    {
                        elements.Add(new Expr.Spread(Expression()));
                    }
                    else
                    {
                        elements.Add(Expression());
                    }
                } while (Match(TokenType.COMMA));
            }
            Consume(TokenType.RIGHT_BRACKET, "Expect ']' after array elements.");
            return new Expr.ArrayLiteral(elements);
        }

        if (Match(TokenType.LEFT_BRACE))
        {
            List<Expr.Property> properties = [];
            if (!Check(TokenType.RIGHT_BRACE))
            {
                do
                {
                    // Handle trailing comma: { a: 1, b: 2, }
                    if (Check(TokenType.RIGHT_BRACE)) break;

                    // Check for spread: { ...obj }
                    if (Match(TokenType.DOT_DOT_DOT))
                    {
                        Expr spreadExpr = Expression();
                        properties.Add(new Expr.Property(null, spreadExpr, IsSpread: true));
                        continue;
                    }

                    // Check for computed property key: { [expr]: value } or method shorthand { [expr]() {} }
                    if (Match(TokenType.LEFT_BRACKET))
                    {
                        Expr keyExpr = Expression();
                        Consume(TokenType.RIGHT_BRACKET, "Expect ']' after computed property key.");

                        // Check for method shorthand: { [Symbol.iterator]() {} }
                        if (Match(TokenType.LEFT_PAREN))
                        {
                            List<Stmt.Parameter> parameters = [];

                            // Check for 'this' parameter in computed method
                            string? thisType = null;
                            if (Check(TokenType.THIS))
                            {
                                Advance(); // consume 'this'
                                Consume(TokenType.COLON, "Expect ':' after 'this' in this parameter.");
                                thisType = ParseTypeAnnotation();
                                if (Check(TokenType.COMMA))
                                {
                                    Advance(); // consume ','
                                }
                            }

                            if (!Check(TokenType.RIGHT_PAREN))
                            {
                                do
                                {
                                    // Check for rest parameter
                                    bool isRest = Match(TokenType.DOT_DOT_DOT);
                                    Token paramName = Consume(TokenType.IDENTIFIER, "Expect parameter name.");
                                    bool isOptional = Match(TokenType.QUESTION);
                                    string? paramType = null;
                                    if (Match(TokenType.COLON))
                                    {
                                        paramType = ParseTypeAnnotation();
                                    }
                                    Expr? defaultValue = null;
                                    if (Match(TokenType.EQUAL))
                                    {
                                        defaultValue = Expression();
                                    }
                                    parameters.Add(new Stmt.Parameter(paramName, paramType, defaultValue, isRest, IsOptional: isOptional));

                                    // Rest parameter must be last
                                    if (isRest && Check(TokenType.COMMA))
                                    {
                                        throw new Exception("Parse Error: Rest parameter must be last.");
                                    }
                                } while (Match(TokenType.COMMA));
                            }
                            Consume(TokenType.RIGHT_PAREN, "Expect ')' after method parameters.");

                            string? returnType = null;
                            if (Match(TokenType.COLON))
                            {
                                returnType = ParseTypeAnnotation();
                            }

                            Consume(TokenType.LEFT_BRACE, "Expect '{' before method body.");
                            List<Stmt> body = Block();

                            var methodExpr = new Expr.ArrowFunction(
                                Name: null,
                                TypeParams: null,
                                ThisType: thisType,
                                Parameters: parameters,
                                ExpressionBody: null,
                                BlockBody: body,
                                ReturnType: returnType,
                                HasOwnThis: true
                            );
                            properties.Add(new Expr.Property(new Expr.ComputedKey(keyExpr), methodExpr));
                            continue;
                        }

                        // Regular computed property: { [expr]: value }
                        Consume(TokenType.COLON, "Expect ':' after computed property key.");
                        Expr computedValue = Expression();
                        properties.Add(new Expr.Property(new Expr.ComputedKey(keyExpr), computedValue));
                        continue;
                    }

                    // Check for string literal key: { "key": value }
                    if (Match(TokenType.STRING))
                    {
                        Token stringKey = Previous();
                        Consume(TokenType.COLON, "Expect ':' after string property key.");
                        Expr stringValue = Expression();
                        properties.Add(new Expr.Property(new Expr.LiteralKey(stringKey), stringValue));
                        continue;
                    }

                    // Check for number literal key: { 123: value }
                    if (Match(TokenType.NUMBER))
                    {
                        Token numberKey = Previous();
                        Consume(TokenType.COLON, "Expect ':' after number property key.");
                        Expr numberValue = Expression();
                        properties.Add(new Expr.Property(new Expr.LiteralKey(numberKey), numberValue));
                        continue;
                    }

                    Token name = Consume(TokenType.IDENTIFIER, "Expect property name.");
                    Expr value;

                    if (Match(TokenType.LEFT_PAREN))
                    {
                        // Method shorthand: { fn() {} }
                        string? thisType = null;
                        List<Stmt.Parameter> parameters = [];

                        // Check for 'this' parameter in object method
                        if (Check(TokenType.THIS))
                        {
                            Advance(); // consume 'this'
                            Consume(TokenType.COLON, "Expect ':' after 'this' in this parameter.");
                            thisType = ParseTypeAnnotation();
                            if (Check(TokenType.COMMA))
                            {
                                Advance(); // consume ','
                            }
                        }

                        if (!Check(TokenType.RIGHT_PAREN))
                        {
                            do
                            {
                                // Check for rest parameter
                                bool isRest = Match(TokenType.DOT_DOT_DOT);
                                Token paramName = Consume(TokenType.IDENTIFIER, "Expect parameter name.");
                                bool isOptional = Match(TokenType.QUESTION);
                                string? paramType = null;
                                if (Match(TokenType.COLON))
                                {
                                    paramType = ParseTypeAnnotation();
                                }
                                Expr? defaultValue = null;
                                if (Match(TokenType.EQUAL))
                                {
                                    defaultValue = Expression();
                                }
                                parameters.Add(new Stmt.Parameter(paramName, paramType, defaultValue, isRest, IsOptional: isOptional));

                                // Rest parameter must be last
                                if (isRest && Check(TokenType.COMMA))
                                {
                                    throw new Exception("Parse Error: Rest parameter must be last.");
                                }
                            } while (Match(TokenType.COMMA));
                        }
                        Consume(TokenType.RIGHT_PAREN, "Expect ')' after method parameters.");

                        string? returnType = null;
                        if (Match(TokenType.COLON))
                        {
                            returnType = ParseTypeAnnotation();
                        }

                        Consume(TokenType.LEFT_BRACE, "Expect '{' before method body.");
                        List<Stmt> body = Block();
                        value = new Expr.ArrowFunction(Name: null, TypeParams: null, ThisType: thisType, Parameters: parameters, ExpressionBody: null, BlockBody: body, ReturnType: returnType, HasOwnThis: true);
                    }
                    else if (Match(TokenType.COLON))
                    {
                        // Explicit property: { x: value }
                        value = Expression();
                    }
                    else
                    {
                        // Shorthand property: { x } -> { x: x }
                        value = new Expr.Variable(name);
                    }

                    properties.Add(new Expr.Property(new Expr.IdentifierKey(name), value));
                } while (Match(TokenType.COMMA));
            }
            Consume(TokenType.RIGHT_BRACE, "Expect '}' after object literal.");
            return new Expr.ObjectLiteral(properties);
        }

        // async arrow function: async () => {} or async (x) => x
        if (Match(TokenType.ASYNC))
        {
            Consume(TokenType.LEFT_PAREN, "Expect '(' after 'async' in async arrow function.");
            Expr? arrowFunc = TryParseArrowFunction(isAsync: true);
            if (arrowFunc != null) return arrowFunc;
            throw new Exception("Parse Error: Expected arrow function after 'async ('.");
        }

        if (Match(TokenType.LEFT_PAREN))
        {
            // Try to parse as arrow function first
            Expr? arrowFunc = TryParseArrowFunction(isAsync: false);
            if (arrowFunc != null) return arrowFunc;

            // Otherwise, parse as grouping
            Expr expr = Expression();
            Consume(TokenType.RIGHT_PAREN, "Expect ')' after expression.");
            return new Expr.Grouping(expr);
        }

        // Template literals
        if (Match(TokenType.TEMPLATE_FULL))
        {
            var value = (TemplateStringValue)Previous().Literal!;
            // For untagged templates, cooked must not be null (invalid escapes are errors)
            if (value.Cooked == null)
            {
                throw new Exception("Parse Error: Invalid escape sequence in template literal.");
            }
            return new Expr.TemplateLiteral([value.Cooked], []);
        }
        if (Match(TokenType.TEMPLATE_HEAD))
        {
            return ParseTemplateLiteral();
        }

        throw new Exception("Expect expression.");
    }

    private Expr ParseTemplateLiteral()
    {
        var headValue = (TemplateStringValue)Previous().Literal!;
        // For untagged templates, cooked must not be null
        if (headValue.Cooked == null)
        {
            throw new Exception("Parse Error: Invalid escape sequence in template literal.");
        }
        List<string> strings = [headValue.Cooked];
        List<Expr> expressions = [];

        // Parse first expression
        expressions.Add(Expression());

        // Parse middle parts
        while (Match(TokenType.TEMPLATE_MIDDLE))
        {
            var midValue = (TemplateStringValue)Previous().Literal!;
            if (midValue.Cooked == null)
            {
                throw new Exception("Parse Error: Invalid escape sequence in template literal.");
            }
            strings.Add(midValue.Cooked);
            expressions.Add(Expression());
        }

        // Expect tail
        Consume(TokenType.TEMPLATE_TAIL, "Expect end of template literal.");
        var tailValue = (TemplateStringValue)Previous().Literal!;
        if (tailValue.Cooked == null)
        {
            throw new Exception("Parse Error: Invalid escape sequence in template literal.");
        }
        strings.Add(tailValue.Cooked);

        return new Expr.TemplateLiteral(strings, expressions);
    }

    private Expr ParseTaggedTemplateLiteral(Expr tag)
    {
        if (Match(TokenType.TEMPLATE_FULL))
        {
            var value = (TemplateStringValue)Previous().Literal!;
            return new Expr.TaggedTemplateLiteral(
                tag,
                CookedStrings: [value.Cooked],
                RawStrings: [value.Raw],
                Expressions: []
            );
        }

        // Must be TEMPLATE_HEAD
        Advance(); // consume TEMPLATE_HEAD
        var firstValue = (TemplateStringValue)Previous().Literal!;
        List<string?> cooked = [firstValue.Cooked];
        List<string> raw = [firstValue.Raw];
        List<Expr> expressions = [];

        // Parse first expression
        expressions.Add(Expression());

        // Parse middle parts
        while (Match(TokenType.TEMPLATE_MIDDLE))
        {
            var midValue = (TemplateStringValue)Previous().Literal!;
            cooked.Add(midValue.Cooked);
            raw.Add(midValue.Raw);
            expressions.Add(Expression());
        }

        // Expect tail
        Consume(TokenType.TEMPLATE_TAIL, "Expect end of template literal.");
        var tailValue = (TemplateStringValue)Previous().Literal!;
        cooked.Add(tailValue.Cooked);
        raw.Add(tailValue.Raw);

        return new Expr.TaggedTemplateLiteral(tag, cooked, raw, expressions);
    }

    // Try to parse arrow function after seeing '('
    // Returns null if not an arrow function (caller should parse as grouping)
    private Expr? TryParseArrowFunction(bool isAsync = false)
    {
        int savedPosition = _current;

        // Try to parse parameter list
        List<Stmt.Parameter> parameters = [];
        List<(Token SynthName, DestructurePattern Pattern)> destructuredParams = [];

        if (!Check(TokenType.RIGHT_PAREN))
        {
            // Must start with identifier, [, {, or ... for it to be arrow function params
            if (!Check(TokenType.IDENTIFIER) && !Check(TokenType.LEFT_BRACKET) && !Check(TokenType.LEFT_BRACE) && !Check(TokenType.DOT_DOT_DOT))
            {
                _current = savedPosition;
                return null;
            }

            do
            {
                if (Check(TokenType.LEFT_BRACKET))
                {
                    // Array destructure parameter: ([a, b]) => ...
                    int line = Peek().Line;
                    Consume(TokenType.LEFT_BRACKET, "");
                    try
                    {
                        var pattern = ParseArrayPattern();
                        Token synthName = new Token(TokenType.IDENTIFIER, $"_param{parameters.Count}", null, line);
                        string? paramType = Match(TokenType.COLON) ? ParseTypeAnnotation() : null;
                        Expr? defaultValue = Match(TokenType.EQUAL) ? Expression() : null;
                        parameters.Add(new Stmt.Parameter(synthName, paramType, defaultValue));
                        destructuredParams.Add((synthName, pattern));
                    }
                    catch
                    {
                        // Not a valid destructuring pattern, backtrack
                        _current = savedPosition;
                        return null;
                    }
                }
                else if (Check(TokenType.LEFT_BRACE))
                {
                    // Object destructure parameter: ({ x, y }) => ...
                    int line = Peek().Line;
                    Consume(TokenType.LEFT_BRACE, "");
                    try
                    {
                        var pattern = ParseObjectPattern();
                        Token synthName = new Token(TokenType.IDENTIFIER, $"_param{parameters.Count}", null, line);
                        string? paramType = Match(TokenType.COLON) ? ParseTypeAnnotation() : null;
                        Expr? defaultValue = Match(TokenType.EQUAL) ? Expression() : null;
                        parameters.Add(new Stmt.Parameter(synthName, paramType, defaultValue));
                        destructuredParams.Add((synthName, pattern));
                    }
                    catch
                    {
                        // Not a valid destructuring pattern (e.g., it's an object literal), backtrack
                        _current = savedPosition;
                        return null;
                    }
                }
                else
                {
                    // Check for rest parameter
                    bool isRest = Match(TokenType.DOT_DOT_DOT);

                    if (!Check(TokenType.IDENTIFIER))
                    {
                        _current = savedPosition;
                        return null;
                    }

                    Token paramName = Advance();
                    string? paramType = null;
                    if (Match(TokenType.COLON))
                    {
                        paramType = ParseTypeAnnotation();
                    }
                    Expr? defaultValue = null;
                    if (Match(TokenType.EQUAL))
                    {
                        defaultValue = Expression();
                    }
                    parameters.Add(new Stmt.Parameter(paramName, paramType, defaultValue, isRest));

                    // Rest parameter must be last
                    if (isRest && Check(TokenType.COMMA))
                    {
                        _current = savedPosition;
                        return null; // Invalid: rest must be last
                    }
                }
            } while (Match(TokenType.COMMA));
        }

        if (!Match(TokenType.RIGHT_PAREN))
        {
            _current = savedPosition;
            return null;
        }

        // Check for optional return type
        string? returnType = null;
        if (Match(TokenType.COLON))
        {
            // This could be return type OR ternary colon - need to check for arrow after
            int beforeType = _current;
            try
            {
                returnType = ParseFunctionTypeAnnotation();
            }
            catch
            {
                _current = savedPosition;
                return null;
            }
        }

        // Must see '=>' for it to be an arrow function
        if (!Match(TokenType.ARROW))
        {
            _current = savedPosition;
            return null;
        }

        // Parse body - either block or expression
        List<Stmt>? body = null;
        Expr? exprBody = null;

        if (Match(TokenType.LEFT_BRACE))
        {
            body = Block();
        }
        else
        {
            exprBody = Expression();
        }

        // If we have destructured parameters, prepend desugaring to body
        if (destructuredParams.Count > 0)
        {
            List<Stmt> prologue = [];
            foreach (var (synthName, pattern) in destructuredParams)
            {
                var paramVar = new Expr.Variable(synthName);
                Stmt desugar = pattern switch
                {
                    ArrayPattern ap => DesugarArrayPattern(ap, paramVar),
                    ObjectPattern op => DesugarObjectPattern(op, paramVar),
                    _ => throw new Exception("Unknown pattern type")
                };
                prologue.Add(desugar);
            }

            if (body != null)
            {
                body = prologue.Concat(body).ToList();
            }
            else if (exprBody != null)
            {
                // For expression body, wrap in a block with prologue + return
                body = prologue.Concat([new Stmt.Return(new Token(TokenType.RETURN, "return", null, 0), exprBody)]).ToList();
                exprBody = null;
            }
        }

        return new Expr.ArrowFunction(Name: null, TypeParams: null, ThisType: null, Parameters: parameters, ExpressionBody: exprBody, BlockBody: body, ReturnType: returnType, IsAsync: isAsync);  // TODO: Parse type params
    }

    /// <summary>
    /// Parses a function expression: function [name](params) { body }
    /// Supports optional name (for named function expressions), generator syntax (function*),
    /// this parameter, and type annotations.
    /// </summary>
    private Expr FunctionExpression()
    {
        // Check for generator function: function* () { }
        bool isGenerator = Match(TokenType.STAR);

        // Optional function name (for named function expressions)
        // Named function expressions have their name visible inside the function body for recursion
        Token? functionName = null;
        if (Check(TokenType.IDENTIFIER))
        {
            functionName = Advance();
        }

        // Parse type parameters: function<T, U>(params) { }
        List<TypeParam>? typeParams = ParseTypeParameters();

        Consume(TokenType.LEFT_PAREN, "Expect '(' after function name.");
        List<Stmt.Parameter> parameters = [];
        List<(Token SynthName, DestructurePattern Pattern)> destructuredParams = [];

        // Check for 'this' parameter (explicit this type annotation)
        string? thisType = null;
        if (Check(TokenType.THIS))
        {
            Advance(); // consume 'this'
            Consume(TokenType.COLON, "Expect ':' after 'this' in this parameter.");
            thisType = ParseTypeAnnotation();
            // If there are more parameters, consume the comma
            if (Check(TokenType.COMMA))
            {
                Advance();
            }
        }

        if (!Check(TokenType.RIGHT_PAREN))
        {
            do
            {
                // Handle trailing comma: function(a, b,) {}
                if (Check(TokenType.RIGHT_PAREN)) break;

                // Check for destructuring pattern parameter
                if (Check(TokenType.LEFT_BRACKET))
                {
                    // Array destructure: function([a, b]) {}
                    int line = Peek().Line;
                    Consume(TokenType.LEFT_BRACKET, "");
                    var pattern = ParseArrayPattern();
                    Token synthName = new Token(TokenType.IDENTIFIER, $"_param{parameters.Count}", null, line);
                    string? paramType = Match(TokenType.COLON) ? ParseTypeAnnotation() : null;
                    Expr? defaultValue = Match(TokenType.EQUAL) ? Expression() : null;
                    parameters.Add(new Stmt.Parameter(synthName, paramType, defaultValue));
                    destructuredParams.Add((synthName, pattern));
                }
                else if (Check(TokenType.LEFT_BRACE))
                {
                    // Object destructure: function({ x, y }) {}
                    int line = Peek().Line;
                    Consume(TokenType.LEFT_BRACE, "");
                    var pattern = ParseObjectPattern();
                    Token synthName = new Token(TokenType.IDENTIFIER, $"_param{parameters.Count}", null, line);
                    string? paramType = Match(TokenType.COLON) ? ParseTypeAnnotation() : null;
                    Expr? defaultValue = Match(TokenType.EQUAL) ? Expression() : null;
                    parameters.Add(new Stmt.Parameter(synthName, paramType, defaultValue));
                    destructuredParams.Add((synthName, pattern));
                }
                else
                {
                    // Check for rest parameter
                    bool isRest = Match(TokenType.DOT_DOT_DOT);

                    Token paramName = Consume(TokenType.IDENTIFIER, "Expect parameter name.");

                    // Check for optional parameter marker (?)
                    bool isOptional = Match(TokenType.QUESTION);

                    string? paramType = null;
                    if (Match(TokenType.COLON))
                    {
                        paramType = ParseTypeAnnotation();
                    }
                    Expr? defaultValue = null;
                    if (Match(TokenType.EQUAL))
                    {
                        defaultValue = Expression();
                    }
                    parameters.Add(new Stmt.Parameter(paramName, paramType, defaultValue, isRest, IsOptional: isOptional));

                    // Rest parameter must be last
                    if (isRest && Check(TokenType.COMMA))
                    {
                        throw new Exception("Parse Error: Rest parameter must be last.");
                    }
                }
            } while (Match(TokenType.COMMA));
        }
        Consume(TokenType.RIGHT_PAREN, "Expect ')' after parameters.");

        string? returnType = null;
        if (Match(TokenType.COLON))
        {
            returnType = ParseTypeAnnotation();
        }

        Consume(TokenType.LEFT_BRACE, "Expect '{' before function body.");
        List<Stmt> body = Block();

        // Prepend destructuring statements for patterned parameters
        if (destructuredParams.Count > 0)
        {
            List<Stmt> prologue = [];
            foreach (var (synthName, pattern) in destructuredParams)
            {
                var paramVar = new Expr.Variable(synthName);
                Stmt desugar = pattern switch
                {
                    ArrayPattern ap => DesugarArrayPattern(ap, paramVar),
                    ObjectPattern op => DesugarObjectPattern(op, paramVar),
                    _ => throw new Exception("Unknown pattern type")
                };
                prologue.Add(desugar);
            }
            body = prologue.Concat(body).ToList();
        }

        // Return as ArrowFunction with block body (HasOwnThis=true for function expressions)
        return new Expr.ArrowFunction(
            Name: functionName,
            TypeParams: typeParams,
            ThisType: thisType,
            Parameters: parameters,
            ExpressionBody: null,
            BlockBody: body,
            ReturnType: returnType,
            HasOwnThis: true,
            IsGenerator: isGenerator
        );
    }

    // Parse function type annotation like "(number) => number" or "(this: Window, e: Event) => void"
    private string ParseFunctionTypeAnnotation()
    {
        // Check if it's a function type: (params) => returnType
        if (Check(TokenType.LEFT_PAREN))
        {
            Advance(); // consume '('
            string? thisType = null;
            List<string> paramTypes = [];

            // Check for 'this' parameter in function type
            if (Check(TokenType.THIS))
            {
                Advance(); // consume 'this'
                Consume(TokenType.COLON, "Expect ':' after 'this' in function type.");
                thisType = ParseTypeAnnotation();
                if (Check(TokenType.COMMA))
                {
                    Advance(); // consume ','
                }
            }

            if (!Check(TokenType.RIGHT_PAREN))
            {
                do
                {
                    paramTypes.Add(ParseTypeAnnotation());
                } while (Match(TokenType.COMMA));
            }
            Consume(TokenType.RIGHT_PAREN, "Expect ')' in function type.");
            Consume(TokenType.ARROW, "Expect '=>' in function type.");
            string returnType = ParseTypeAnnotation();

            // Include this type in the string representation
            if (thisType != null)
            {
                return $"(this: {thisType}, {string.Join(", ", paramTypes)}) => {returnType}";
            }
            return $"({string.Join(", ", paramTypes)}) => {returnType}";
        }

        // Otherwise regular type
        return ParseTypeAnnotation();
    }
}
