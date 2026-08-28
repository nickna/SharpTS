namespace SharpTS.Parsing;

public partial class Parser
{
    private Expr Expression() => Assignment();

    /// <summary>
    /// Parses a function-like block body with its own directive prologue and
    /// restores the surrounding parser strictness afterward. Function
    /// expressions, methods, accessors, and block-bodied arrows all use this
    /// path so a leading <c>"use strict"</c> is retained in the AST and applies
    /// while the remainder of that body is parsed.
    /// </summary>
    private List<Stmt> ParseFunctionExpressionBody(
        List<Stmt.Parameter>? parameters = null)
    {
        bool previousStrictMode = _isStrictMode;
        try
        {
            var body = Block(parseFunctionPrologue: true, setStrictMode: true);
            if (_isStrictMode && parameters is not null)
                ValidateNoDuplicateParameters(parameters);
            return body;
        }
        finally
        {
            _isStrictMode = previousStrictMode;
        }
    }

    /// <summary>
    /// Parses a comma (sequence) expression. Lowest precedence operator.
    /// Evaluates all operands left-to-right, returns the last value.
    /// Only called from contexts where comma is an operator, not a separator
    /// (expression statements, for loop increment, parenthesized groups).
    /// </summary>
    private Expr CommaExpression()
    {
        Expr expr = Assignment();

        while (Match(TokenType.COMMA))
        {
            Expr right = Assignment();
            expr = new Expr.Comma(expr, right);
        }

        return expr;
    }

    private Expr Assignment()
    {
        // Check for single-parameter arrow function without parentheses: x => expr.
        // Also accept TS contextual keywords as the param name (e.g.
        // `namespace => …`, `type => …`), since they are valid JS identifiers.
        if ((Check(TokenType.IDENTIFIER) || IsContextualKeyword(Peek().Type))
            && CheckNext(TokenType.ARROW))
        {
            Token raw = Advance(); // consume identifier/contextual keyword
            Token paramName = raw.Type == TokenType.IDENTIFIER
                ? raw
                : new Token(TokenType.IDENTIFIER, raw.Lexeme, null, raw.Line);
            Advance(); // consume '=>'

            // Parse the body - either block or expression
            List<Stmt>? body = null;
            Expr? exprBody = null;

            if (Match(TokenType.LEFT_BRACE))
            {
                body = ParseFunctionExpressionBody();
                body = VarHoister.Hoist(body, _spans);
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
            // Array/object literal on the left of `=` is assignment destructuring (#754): the targets
            // are existing l-values, lowered to assignment statements reusing the #685 iterator path.
            if (IsDestructuringAssignmentTarget(expr))
                return BuildDestructuringAssignment(expr, value, equals.Line);
            return DispatchAssignmentTarget(expr, value, "Invalid assignment target.",
                onVariable: (name, val) => new Expr.Assign(name, val),
                onGet: (get, val) => new Expr.Set(get.Object, get.Name, val),
                onGetPrivate: (gp, val) => new Expr.SetPrivate(gp.Object, gp.Name, val),
                onGetIndex: (gi, val) => new Expr.SetIndex(gi.Object, gi.Index, val));
        }

        // Compound assignment operators
        if (Match(TokenType.PLUS_EQUAL, TokenType.MINUS_EQUAL, TokenType.STAR_EQUAL,
                  TokenType.SLASH_EQUAL, TokenType.PERCENT_EQUAL,
                  TokenType.AMPERSAND_EQUAL, TokenType.PIPE_EQUAL, TokenType.CARET_EQUAL,
                  TokenType.LESS_LESS_EQUAL, TokenType.GREATER_GREATER_EQUAL, TokenType.GREATER_GREATER_GREATER_EQUAL))
        {
            Token op = Previous();
            Expr value = Assignment();
            return DispatchAssignmentTarget(expr, value, "Invalid compound assignment target.",
                onVariable: (name, val) => new Expr.CompoundAssign(name, op, val),
                onGet: (get, val) => new Expr.CompoundSet(get.Object, get.Name, op, val),
                onGetIndex: (gi, val) => new Expr.CompoundSetIndex(gi.Object, gi.Index, op, val));
        }

        // Logical assignment operators (&&=, ||=, ??=) - have short-circuit semantics
        if (Match(TokenType.AND_AND_EQUAL, TokenType.OR_OR_EQUAL, TokenType.QUESTION_QUESTION_EQUAL))
        {
            Token op = Previous();
            Expr value = Assignment();
            return DispatchAssignmentTarget(expr, value, "Invalid logical assignment target.",
                onVariable: (name, val) => new Expr.LogicalAssign(name, op, val),
                onGet: (get, val) => new Expr.LogicalSet(get.Object, get.Name, op, val),
                onGetIndex: (gi, val) => new Expr.LogicalSetIndex(gi.Object, gi.Index, op, val));
        }

        return expr;
    }

    /// <summary>
    /// Dispatches an assignment to the correct AST node based on the target expression type.
    /// Validates strict mode restrictions for variable assignments.
    /// </summary>
    private Expr DispatchAssignmentTarget(
        Expr target, Expr value, string errorMessage,
        Func<Token, Expr, Expr> onVariable,
        Func<Expr.Get, Expr, Expr> onGet,
        Func<Expr.GetIndex, Expr, Expr> onGetIndex,
        Func<Expr.GetPrivate, Expr, Expr>? onGetPrivate = null)
    {
        switch (target)
        {
            // Parens don't change whether an expression is a valid reference — `(a.b) = v`,
            // `(a.b) &&= v`, etc. are all valid; unwrap (recursively, for `((a.b))`) before dispatch.
            case Expr.Grouping grouping:
                return DispatchAssignmentTarget(grouping.Expression, value, errorMessage, onVariable, onGet, onGetIndex, onGetPrivate);
            case Expr.Variable variable:
                if (_isStrictMode && (variable.Name.Lexeme == "eval" || variable.Name.Lexeme == "arguments"))
                    throw new Exception("SyntaxError: Unexpected eval or arguments in strict mode");
                return onVariable(variable.Name, value);
            // `undefined = v` must PARSE (it's an identifier-shaped target) and fail in the
            // CHECKER with TS2539 ("Cannot assign to 'undefined' because it is not a variable")
            // — tsc treats this as a semantic error, not a parse error.
            case Expr.Literal lit when lit.Value is SharpTS.Runtime.Types.SharpTSUndefined:
                return onVariable(new Token(TokenType.IDENTIFIER, "undefined", null, Previous().Line), value);
            case Expr.ImportMeta importMeta:
                // Keep the syntactically-recoverable but illegal
                // `import.meta = value` form in the AST; the checker reports it
                // as an invalid/unknown assignment instead of the parser
                // abandoning the rest of the file.
                return onVariable(
                    new Token(TokenType.IDENTIFIER, "import.meta", null, importMeta.Keyword.Line),
                    value);
            case Expr.Get get:
                return onGet(get, value);
            case Expr.GetPrivate getPrivate when onGetPrivate != null:
                return onGetPrivate(getPrivate, value);
            case Expr.GetIndex getIndex:
                return onGetIndex(getIndex, value);
            default:
                throw new Exception(errorMessage);
        }
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

        // In TSX, adjacent JSX roots are not a less-than comparison. tsc commits to both
        // elements, reports TS2657 on the first root, and retains both ASTs so their normal
        // JSX diagnostics still run. This covers both a newline-separated expression
        // statement and `<div></div><div></div>` inside an initializer.
        while (_jsx is not null && IsJsxRootExpression(expr) && IsJsxElementStart())
        {
            RecordErrorAt(JsxRootLine(expr),
                "JSX expressions must have one parent element.", "TS2657");
            Expr right = ParseJsxElement();
            expr = new Expr.Comma(expr, right);
        }

        while (Match(TokenType.GREATER, TokenType.GREATER_EQUAL, TokenType.LESS, TokenType.LESS_EQUAL, TokenType.IN, TokenType.INSTANCEOF))
        {
            Token op = Previous();
            Expr right = Shift();
            expr = new Expr.Binary(expr, op, right);
        }

        return expr;
    }

    private static bool IsJsxRootExpression(Expr expr) => expr switch
    {
        Expr.Call { JsxOrigin: not null } => true,
        Expr.Comma comma => IsJsxRootExpression(comma.Right),
        _ => false,
    };

    private static int JsxRootLine(Expr expr) => expr switch
    {
        Expr.Call { JsxOrigin: { } jsx } => jsx.Line,
        Expr.Comma comma => JsxRootLine(comma.Left),
        _ => 1,
    };

    private bool IsJsxElementStart()
    {
        if (!Check(TokenType.LESS)) return false;
        TokenType next = PeekNext().Type;
        return next is TokenType.GREATER or TokenType.IDENTIFIER || IsContextualKeyword(next);
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
        // Check for generic arrow function: <T>(x) => ...
        if (Check(TokenType.LESS))
        {
            if (_jsx is not null && PeekNext().Type == TokenType.SLASH)
            {
                int line = Peek().Line;
                RecordErrorAt(line, "Declaration or statement expected.", "TS1128");
                RecordErrorAt(line, "Expression expected.", "TS1109");
                while (!IsAtEnd() && !Check(TokenType.SEMICOLON)) Advance();
                return new Expr.Literal(SharpTS.Runtime.Types.SharpTSUndefined.Instance);
            }
            if (_jsx is not null && PeekNext().Type == TokenType.DOT)
            {
                int line = Peek().Line;
                RecordErrorAt(line, "Expression expected.", "TS1109");
                RecordErrorAt(line, "Declaration or statement expected.", "TS1128");
                while (!IsAtEnd() && !Check(TokenType.SEMICOLON)) Advance();
                return new Expr.Literal(SharpTS.Runtime.Types.SharpTSUndefined.Instance);
            }

            var genericArrow = TryParseGenericArrowFunction();
            if (genericArrow != null) return genericArrow;

            if (_jsx is not null)
            {
                // TSX dialect: '<' at expression start commits to JSX (angle-bracket
                // assertions do not exist in .tsx — tsc's rule). Real parse errors
                // propagate rather than being swallowed into a confusing fallback.
                if (_jsx.Mode == JsxMode.None)
                    throw new ParseError(
                        "Cannot use JSX unless the '--jsx' flag is provided.", "TS17004");
                return ParseJsxElement();
            }

            // .ts dialect: JSX is not legal syntax; '<' here is an angle-bracket
            // type assertion (`<Type>expr`).
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

        if (Match(TokenType.BANG, TokenType.PLUS, TokenType.MINUS,
                  TokenType.TYPEOF, TokenType.TILDE, TokenType.VOID))
        {
            Token op = Previous();
            Expr right = Unary();
            return new Expr.Unary(op, right);
        }

        // delete operator: delete expr
        if (Match(TokenType.DELETE))
        {
            Token keyword = Previous();
            Expr operand = Unary();
            return new Expr.Delete(keyword, operand);
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
            // Restricted production: yield [no LineTerminator here] Expression
            Expr? value = null;
            if (!HasLineTerminatorBeforeCurrent() &&
                !Check(TokenType.SEMICOLON) && !Check(TokenType.RIGHT_BRACE) &&
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
            List<string>? typeArgs = TryParseTypeArguments(out var typeArgNodes);

            // Parse arguments. `new X` without parens is valid JS —
            // equivalent to `new X()`. Spread args (`new X(...iter)`) are allowed.
            List<Expr> arguments = [];
            if (Match(TokenType.LEFT_PAREN))
            {
                ParseNewArgumentList(arguments);
            }

            // Allow operations on new expressions
            // Examples: new Date().toISOString()
            Expr newExpr = new Expr.New(callee, typeArgs, arguments, typeArgNodes);
            return ParseCallChain(newExpr);
        }

        return Call();
    }

    private Expr Call()
    {
        Expr expr = Primary();
        return ParseCallChain(expr);
    }

    /// <summary>
    /// Parses postfix operations on an expression: method calls, property access,
    /// index access, type assertions, non-null assertions, and tagged templates.
    /// This is extracted to allow reuse after parsing new expressions.
    /// </summary>
    private Expr ParseCallChain(Expr expr)
    {
        while (true)
        {
            // Check for type arguments before call: func<T>(args)
            List<string>? typeArgs = null;
            List<TypeNode?>? typeArgNodes = null;
            if (Check(TokenType.LESS))
            {
                typeArgs = TryParseTypeArgumentsForCall(out typeArgNodes);
            }

            if (typeArgs != null || Match(TokenType.LEFT_PAREN))
            {
                expr = FinishCall(expr, typeArgs, typeArgNodes: typeArgNodes);
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
                            while (true)
                            {
                                if (Match(TokenType.DOT_DOT_DOT))
                                {
                                    args.Add(new Expr.Spread(Expression()));
                                }
                                else
                                {
                                    args.Add(Expression());
                                }
                                if (!Match(TokenType.COMMA)) break;
                                // ES2017 trailing comma.
                                if (Check(TokenType.RIGHT_PAREN)) break;
                            }
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
                if (Match(TokenType.LEFT_PAREN))
                {
                    // ?.() — optional call
                    expr = FinishCall(expr, optional: true);
                }
                else if (Match(TokenType.LEFT_BRACKET))
                {
                    // ?.[] — optional bracket access
                    Expr index = Expression();
                    Consume(TokenType.RIGHT_BRACKET, "Expect ']' after index.");
                    expr = new Expr.GetIndex(expr, index, Optional: true);
                }
                else
                {
                    // ?.prop — optional property access (existing behavior)
                    Token name = ConsumePropertyName("Expect property name after '?.'.");
                    expr = new Expr.Get(expr, name, Optional: true);
                }
            }
            else if (Match(TokenType.LEFT_BRACKET))
            {
                Expr index = Expression();
                Consume(TokenType.RIGHT_BRACKET, "Expect ']' after index.");
                expr = new Expr.GetIndex(expr, index);
            }
            else if (!HasLineTerminatorBeforeCurrent() && Match(TokenType.PLUS_PLUS, TokenType.MINUS_MINUS))
            {
                // Postfix increment/decrement (restricted: no LineTerminator before ++/--)
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
                    expr = new Expr.TypeAssertion(expr, targetType, TakeTypeNode());
                }
            }
            else if (Match(TokenType.SATISFIES))
            {
                // Satisfies operator: expr satisfies Type (TS 4.9+)
                // Validates that expr matches Type without widening the inferred type
                string constraintType = ParseTypeAnnotation();
                expr = new Expr.Satisfies(expr, constraintType, TakeTypeNode());
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

    private Expr FinishCall(Expr callee, List<string>? typeArgs = null, bool optional = false, List<TypeNode?>? typeArgNodes = null)
    {
        List<Expr> arguments = [];
        if (!Check(TokenType.RIGHT_PAREN))
        {
            while (true)
            {
                if (Match(TokenType.DOT_DOT_DOT))
                {
                    arguments.Add(new Expr.Spread(Expression()));
                }
                else
                {
                    arguments.Add(Expression());
                }
                if (!Match(TokenType.COMMA)) break;
                // ES2017 trailing comma: `f(a, b,)` — swallow the comma and stop.
                if (Check(TokenType.RIGHT_PAREN)) break;
            }
        }

        Token paren = Consume(TokenType.RIGHT_PAREN, "Expect ')' after arguments.");
        return new Expr.Call(callee, paren, typeArgs, arguments, optional, typeArgNodes);
    }

    /// <summary>
    /// Parses the callee expression for a 'new' expression — a MemberExpression per
    /// ECMA-262 §13.3. Accepts any PrimaryExpression (identifier, literal, function/class
    /// expression, parenthesized expression, this, etc.), followed by a member-access
    /// chain of `.name` and `[index]` (but NOT call arguments — those bind to the `new`).
    /// Also handles nested `new` (e.g. `new new X()`).
    /// </summary>
    private Expr ParseNewCallee()
    {
        Expr callee;

        // Nested `new`: `new new X()` parses as `new (new X())` — the inner `new X()`
        // is itself a MemberExpression callee.
        if (Match(TokenType.NEW))
        {
            Expr innerCallee = ParseNewCallee();
            List<string>? innerTypeArgs = TryParseTypeArguments(out var innerTypeArgNodes);
            List<Expr> innerArgs = [];
            if (Match(TokenType.LEFT_PAREN))
            {
                ParseNewArgumentList(innerArgs);
            }
            callee = new Expr.New(innerCallee, innerTypeArgs, innerArgs, innerTypeArgNodes);
        }
        else
        {
            // Any PrimaryExpression: literals (`new true`, `new 1`), function/class
            // expressions (`new function() {}(...)`), identifiers, parenthesized exprs,
            // `this`, array/object literals, etc.
            callee = Primary();
        }

        // Member-access chain (no call args — those bind to the enclosing `new`).
        // Property-name position after `.` accepts any keyword (JS semantics).
        while (true)
        {
            if (Match(TokenType.DOT))
            {
                Token name = ConsumePropertyName("Expect identifier after '.' in new expression.");
                callee = new Expr.Get(callee, name);
            }
            else if (Match(TokenType.LEFT_BRACKET))
            {
                Expr index = Expression();
                Consume(TokenType.RIGHT_BRACKET, "Expect ']' after index in new expression.");
                callee = new Expr.GetIndex(callee, index);
            }
            else
            {
                break;
            }
        }

        return callee;
    }

    /// <summary>
    /// Parses the argument list of a `new` expression after the opening `(`.
    /// Supports spread (`...iter`) and ES2017 trailing comma. Consumes the closing `)`.
    /// </summary>
    private void ParseNewArgumentList(List<Expr> arguments)
    {
        if (!Check(TokenType.RIGHT_PAREN))
        {
            while (true)
            {
                if (Match(TokenType.DOT_DOT_DOT))
                {
                    arguments.Add(new Expr.Spread(Expression()));
                }
                else
                {
                    arguments.Add(Expression());
                }
                if (!Match(TokenType.COMMA)) break;
                // ES2017 trailing comma: `new X(a, b,)`.
                if (Check(TokenType.RIGHT_PAREN)) break;
            }
        }
        Consume(TokenType.RIGHT_PAREN, "Expect ')' after arguments.");
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
                Token meta = ConsumePropertyName("Expect property name after 'import.'.");
                if (meta.Lexeme == "meta")
                    return new Expr.ImportMeta(keyword);

                // Invalid `import.foo` forms are syntactically recoverable.
                // Record tsc's dedicated diagnostic and retain import.meta's
                // object shape so any following access is checked normally.
                RecordErrorAt(meta.Line, "Import meta-property must be 'meta'.", "TS17012");
                return new Expr.ImportMeta(keyword, IsInvalidProperty: true);
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

        // Contextual keywords accepted as identifiers in expression context
        // (e.g. `module.exports = X` in CommonJS, `var type = typeof val` in npm packages).
        if (IsContextualKeyword(Peek().Type)
            && !(Check(TokenType.ASYNC)
                 && PeekNext().Type is TokenType.FUNCTION
                     or TokenType.LESS
                     or TokenType.LEFT_PAREN))
        {
            var token = Advance();
            return new Expr.Variable(new Token(TokenType.IDENTIFIER, token.Lexeme, null, token.Line));
        }

        if (Match(TokenType.LEFT_BRACKET))
        {
            List<Expr> elements = [];
            HashSet<int>? holeIndices = null;
            // Array literal supports holes (elisions): [,,1] or [,-0].
            // Leading/interior commas without a preceding element produce
            // holes (undefined literal + index in HoleIndices); a trailing
            // comma does not.
            while (!Check(TokenType.RIGHT_BRACKET))
            {
                if (Match(TokenType.COMMA))
                {
                    // Elided slot before anything — record a hole.
                    (holeIndices ??= []).Add(elements.Count);
                    elements.Add(new Expr.Literal(SharpTS.Runtime.Types.SharpTSUndefined.Instance));
                    continue;
                }

                if (Match(TokenType.DOT_DOT_DOT))
                {
                    elements.Add(new Expr.Spread(Expression()));
                }
                else
                {
                    elements.Add(Expression());
                }

                // If next is `,`, consume and continue (may be trailing).
                if (!Match(TokenType.COMMA)) break;
            }
            Consume(TokenType.RIGHT_BRACKET, "Expect ']' after array elements.");
            return new Expr.ArrayLiteral(elements, holeIndices);
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

                    // Generator / async method modifiers: { *gen() {} }, { async fn() {} },
                    // { async *gen() {} } and their computed/string/number-keyed forms (#757). The key
                    // forms below already dispatch to ParseObjectMethodShorthand on a following '(', so
                    // the modifiers just need to be threaded through. `async` is a modifier only when
                    // followed by a property-name start or `*`; otherwise it is a property named `async`
                    // ({ async }, { async: 1 }, { async() {} }).
                    bool isMethodAsync = false;
                    bool isMethodGenerator = false;
                    if (Check(TokenType.ASYNC) && (IsPropertyNameStart(PeekNext().Type) || PeekNext().Type == TokenType.STAR))
                    {
                        Advance();
                        isMethodAsync = true;
                    }
                    if (Match(TokenType.STAR))
                    {
                        isMethodGenerator = true;
                    }

                    // Accessor shorthand: { get foo() {} }, { set foo(v) {} }.
                    // Disambiguate from a property literally named `get`/`set`:
                    //   { get }       shorthand            (next is `,` or `}`)
                    //   { get: v }    explicit property    (next is `:`)
                    //   { get() {} }  method shorthand     (next is `(`)
                    // Only enter the accessor path when `get`/`set` is followed by
                    // a property-name starter (identifier, keyword, string, number,
                    // or `[` for a computed key).
                    if ((Check(TokenType.GET) || Check(TokenType.SET)) && IsPropertyNameStart(PeekNext().Type))
                    {
                        var kindToken = Advance(); // consume 'get' or 'set'
                        bool isGetter = kindToken.Type == TokenType.GET;

                        Expr.PropertyKey accessorKey;
                        if (Match(TokenType.LEFT_BRACKET))
                        {
                            Expr keyExpr = Expression();
                            Consume(TokenType.RIGHT_BRACKET, "Expect ']' after computed accessor key.");
                            accessorKey = new Expr.ComputedKey(keyExpr);
                        }
                        else if (Match(TokenType.STRING) || Match(TokenType.NUMBER))
                        {
                            accessorKey = new Expr.LiteralKey(Previous());
                        }
                        else
                        {
                            Token propName = ConsumePropertyName("Expect property name after 'get'/'set'.");
                            accessorKey = new Expr.IdentifierKey(propName);
                        }

                        Consume(TokenType.LEFT_PAREN, isGetter
                            ? "Expect '(' after getter name."
                            : "Expect '(' after setter name.");

                        List<Stmt.Parameter> accessorParams = [];
                        Stmt.Parameter? setterParam = null;
                        if (isGetter)
                        {
                            Consume(TokenType.RIGHT_PAREN, "Expect ')' after getter parameters (getters take no parameters).");
                        }
                        else
                        {
                            Token paramName = ConsumeIdentifierName("Expect setter parameter name.");
                            string? paramType = null;
                            TypeNode? paramTypeNode = null;
                            if (Match(TokenType.COLON))
                            {
                                paramType = ParseTypeAnnotation();
                                paramTypeNode = TakeTypeNode();
                            }
                            setterParam = new Stmt.Parameter(paramName, paramType, null, false, TypeAnnotationNode: paramTypeNode);
                            accessorParams.Add(setterParam);
                            Consume(TokenType.RIGHT_PAREN, "Expect ')' after setter parameter.");
                        }

                        string? accessorReturnType = null;
                        TypeNode? accessorReturnTypeNode = null;
                        if (Match(TokenType.COLON))
                        {
                            accessorReturnType = ParseTypeAnnotation();
                            accessorReturnTypeNode = TakeTypeNode();
                        }

                        Consume(TokenType.LEFT_BRACE, isGetter
                            ? "Expect '{' before getter body."
                            : "Expect '{' before setter body.");
                        List<Stmt> accessorBody =
                            ParseFunctionExpressionBody(accessorParams);
                        accessorBody = VarHoister.Hoist(accessorBody, _spans);

                        var accessorExpr = new Expr.ArrowFunction(
                            Name: null,
                            TypeParams: null,
                            ThisType: null,
                            Parameters: accessorParams,
                            ExpressionBody: null,
                            BlockBody: accessorBody,
                            ReturnType: accessorReturnType,
                            HasOwnThis: true,
                            ReturnTypeNode: accessorReturnTypeNode
                        );
                        properties.Add(new Expr.Property(
                            accessorKey,
                            accessorExpr,
                            IsSpread: false,
                            Kind: isGetter ? Expr.ObjectPropertyKind.Getter : Expr.ObjectPropertyKind.Setter,
                            SetterParam: setterParam));
                        continue;
                    }

                    // Computed property key: { [expr]: value } or method shorthand { [expr]() {} }
                    if (Match(TokenType.LEFT_BRACKET))
                    {
                        Expr keyExpr = Expression();
                        Consume(TokenType.RIGHT_BRACKET, "Expect ']' after computed property key.");

                        if (Check(TokenType.LEFT_PAREN))
                        {
                            var methodExpr = ParseObjectMethodShorthand(isMethodAsync, isMethodGenerator);
                            properties.Add(new Expr.Property(new Expr.ComputedKey(keyExpr), methodExpr));
                            continue;
                        }

                        // Regular computed property: { [expr]: value }
                        Consume(TokenType.COLON, "Expect ':' after computed property key.");
                        Expr computedValue = Expression();
                        properties.Add(new Expr.Property(new Expr.ComputedKey(keyExpr), computedValue));
                        continue;
                    }

                    // String literal key: { "key": value } or method shorthand { "key"() {} }
                    if (Match(TokenType.STRING))
                    {
                        Token stringKey = Previous();
                        var key = new Expr.LiteralKey(stringKey);

                        if (Check(TokenType.LEFT_PAREN))
                        {
                            var methodExpr = ParseObjectMethodShorthand(isMethodAsync, isMethodGenerator);
                            properties.Add(new Expr.Property(key, methodExpr));
                            continue;
                        }

                        Consume(TokenType.COLON, "Expect ':' after string property key.");
                        Expr stringValue = Expression();
                        properties.Add(new Expr.Property(key, stringValue));
                        continue;
                    }

                    // Number literal key: { 123: value } or method shorthand { 123() {} }
                    if (Match(TokenType.NUMBER))
                    {
                        Token numberKey = Previous();
                        var key = new Expr.LiteralKey(numberKey);

                        if (Check(TokenType.LEFT_PAREN))
                        {
                            var methodExpr = ParseObjectMethodShorthand(isMethodAsync, isMethodGenerator);
                            properties.Add(new Expr.Property(key, methodExpr));
                            continue;
                        }

                        Consume(TokenType.COLON, "Expect ':' after number property key.");
                        Expr numberValue = Expression();
                        properties.Add(new Expr.Property(key, numberValue));
                        continue;
                    }

                    // Parse regular identifier (including 'get' and 'set' as property names)
                    Token name = ConsumePropertyName("Expect property name.");
                    Expr value;
                    bool isShorthandDefault = false;

                    if (Check(TokenType.LEFT_PAREN))
                    {
                        // Method shorthand: { fn() {} }
                        value = ParseObjectMethodShorthand(isMethodAsync, isMethodGenerator);
                    }
                    else if (Match(TokenType.COLON))
                    {
                        // Explicit property: { x: value }
                        value = Expression();
                    }
                    else if (Match(TokenType.EQUAL))
                    {
                        // Cover-grammar shorthand-with-default: `{ x = 5 }` (an ES CoverInitializedName).
                        // Only valid as an object DESTRUCTURING pattern; stored as `{ x: (x = 5) }` so the
                        // #754 assignment-destructuring lowering recovers the `(target, default)`. A
                        // pure-expression `{ x = 5 }` is rejected in CheckObject via IsShorthandDefault (#780).
                        value = new Expr.Assign(name, Expression());
                        isShorthandDefault = true;
                    }
                    else
                    {
                        // Shorthand property: { x } -> { x: x }
                        value = new Expr.Variable(name);
                    }

                    properties.Add(new Expr.Property(new Expr.IdentifierKey(name), value,
                        IsShorthandDefault: isShorthandDefault));
                } while (Match(TokenType.COMMA));
            }
            if (Check(TokenType.SEMICOLON))
            {
                // Recover an object-literal property terminated with `;`: keep the TS1005
                // grammar diagnostic, but consume the delimiter so declarations after the
                // object remain in the AST (tsc also continues past this error).
                RecordError("',' expected.", "TS1005");
                Advance();
            }
            Consume(TokenType.RIGHT_BRACE, "Expect '}' after object literal.");
            return new Expr.ObjectLiteral(properties);
        }

        // async function expression: async function [name]() {} or async function*() {}
        // async arrow function:       async () => {} or async (x) => x
        // `async` is also a valid plain identifier (e.g. a destructured binding named `async`,
        // `if (async)`) when it isn't immediately followed by one of the tokens above — only
        // commit to the async-function forms when one of those follows.
        if (Check(TokenType.ASYNC) &&
            (PeekNext().Type == TokenType.FUNCTION || PeekNext().Type == TokenType.LESS || PeekNext().Type == TokenType.LEFT_PAREN))
        {
            Advance(); // consume 'async'

            // `async function ...` is an async function expression — defer to the shared
            // FunctionExpression parser (which also handles the `*` for async generators),
            // mirroring the statement-level `async function` declaration path.
            if (Match(TokenType.FUNCTION))
            {
                return FunctionExpression(isAsync: true);
            }

            if (Check(TokenType.LESS))
            {
                Expr? genericArrow = TryParseGenericArrowFunction(isAsync: true);
                if (genericArrow != null) return genericArrow;
            }

            Consume(TokenType.LEFT_PAREN, "Expect '(' after 'async' in async arrow function.");
            Expr? arrowFunc = TryParseArrowFunction(isAsync: true);
            if (arrowFunc != null) return arrowFunc;
            throw new Exception("Parse Error: Expected arrow function after 'async ('.");
        }
        if (Check(TokenType.ASYNC))
        {
            // Not followed by a function-introducing token — a bare reference to an identifier
            // named `async`.
            return new Expr.Variable(Advance());
        }

        if (Match(TokenType.LEFT_PAREN))
        {
            // Parenthesized async arrows are common in immediately-invoked
            // expressions: `(async () => { ... })()`. Parse this unambiguous
            // prefix before the ordinary parenthesized-arrow speculation;
            // otherwise the outer speculation can consume `async` as a
            // would-be parameter and leave the nested parameter list to the
            // grouping parser.
            if (Check(TokenType.ASYNC) && PeekNext().Type == TokenType.LEFT_PAREN)
            {
                int asyncStart = _current;
                Advance();
                Advance();
                Expr? asyncArrow = TryParseArrowFunction(isAsync: true);
                if (asyncArrow != null)
                {
                    Consume(TokenType.RIGHT_PAREN, "Expect ')' after async arrow function.");
                    return new Expr.Grouping(asyncArrow);
                }
                _current = asyncStart;
            }

            // Try to parse as arrow function first
            Expr? arrowFunc = TryParseArrowFunction(isAsync: false);
            if (arrowFunc != null) return arrowFunc;

            // Otherwise, parse as grouping (allows comma operator inside parens)
            Expr expr = CommaExpression();
            Consume(TokenType.RIGHT_PAREN, "Expect ')' after expression.");
            return new Expr.Grouping(expr);
        }

        // Template literals
        if (Match(TokenType.TEMPLATE_FULL))
        {
            var value = (TemplateStringValue)Previous().Literal!;
            // For untagged templates, an invalid escape (`\xtraordinary`, `\u{hello}`, ...) is a real
            // syntax error (TS1125) — but recoverable, not a reason to abort the whole file. Substitute
            // an empty string and let the checker report it against this line.
            if (value.Cooked == null)
            {
                return new Expr.TemplateLiteral([""], [], [Previous().Line]);
            }
            return new Expr.TemplateLiteral([value.Cooked], []);
        }
        if (Match(TokenType.TEMPLATE_HEAD))
        {
            return ParseTemplateLiteral();
        }

        throw new Exception("Expect expression.");
    }

    /// <summary>
    /// Parses an object-literal method-shorthand tail starting at <c>(</c>:
    /// the parameter list, optional return-type annotation, and brace body.
    /// Supports array/object destructuring patterns, rest parameters, default
    /// values, and an optional <c>this:</c> parameter (TypeScript).
    /// Returns an ArrowFunction with <c>HasOwnThis=true</c>.
    /// </summary>
    private Expr.ArrowFunction ParseObjectMethodShorthand(bool isAsync = false, bool isGenerator = false)
    {
        Consume(TokenType.LEFT_PAREN, "Expect '(' in method shorthand.");

        string? thisType = null;
        TypeNode? thisTypeNode = null;
        List<Stmt.Parameter> parameters = [];
        List<(Token SynthName, DestructurePattern Pattern)> destructuredParams = [];

        // Optional `this:` annotation parameter (TS).
        if (Check(TokenType.THIS))
        {
            Advance();
            Consume(TokenType.COLON, "Expect ':' after 'this' in this parameter.");
            thisType = ParseTypeAnnotation();
            thisTypeNode = TakeTypeNode();
            if (Check(TokenType.COMMA))
            {
                Advance();
            }
        }

        if (!Check(TokenType.RIGHT_PAREN))
        {
            do
            {
                // Destructuring patterns in method parameters
                // (e.g. yaml's `stringify({source, value}, ctx) {...}`).
                if (Check(TokenType.LEFT_BRACKET) || Check(TokenType.LEFT_BRACE))
                {
                    var (parameter, pattern) = ParseDestructuredParameter(parameters.Count);
                    parameters.Add(parameter);
                    destructuredParams.Add((parameter.Name, pattern));
                    continue;
                }

                bool isRest = Match(TokenType.DOT_DOT_DOT);
                Token paramName = ConsumeIdentifierName("Expect parameter name.");
                parameters.Add(ParseNamedRuntimeParameterTail(paramName, isRest));

                if (isRest && Check(TokenType.COMMA))
                {
                    throw new Exception("Parse Error: Rest parameter must be last.");
                }
            } while (Match(TokenType.COMMA));
        }
        Consume(TokenType.RIGHT_PAREN, "Expect ')' after method parameters.");

        string? returnType = null;
        TypeNode? returnTypeNode = null;
        if (Match(TokenType.COLON))
        {
            returnType = ParseTypeAnnotation();
            returnTypeNode = TakeTypeNode();
        }

        Consume(TokenType.LEFT_BRACE, "Expect '{' before method body.");
        List<Stmt> body = ParseFunctionExpressionBody(parameters);

        body = PrependDestructuringPrologue(destructuredParams, body);
        body = VarHoister.Hoist(body, _spans);

        return new Expr.ArrowFunction(
            Name: null,
            TypeParams: null,
            ThisType: thisType,
            Parameters: parameters,
            ExpressionBody: null,
            BlockBody: body,
            ReturnType: returnType,
            HasOwnThis: true,
            IsAsync: isAsync,
            IsGenerator: isGenerator,
            ThisTypeNode: thisTypeNode,
            ReturnTypeNode: returnTypeNode);
    }

    private Expr ParseTemplateLiteral()
    {
        var headValue = (TemplateStringValue)Previous().Literal!;
        List<string> strings = [];
        List<int>? invalidEscapeLines = null;
        void AddPart(TemplateStringValue value, int line)
        {
            if (value.Cooked == null)
            {
                strings.Add("");
                (invalidEscapeLines ??= []).Add(line);
            }
            else
            {
                strings.Add(value.Cooked);
            }
        }
        AddPart(headValue, Previous().Line);
        List<Expr> expressions = [];

        // Parse first expression
        expressions.Add(ParseTemplateInterpolation());

        // Parse middle parts
        while (Match(TokenType.TEMPLATE_MIDDLE))
        {
            AddPart((TemplateStringValue)Previous().Literal!, Previous().Line);
            expressions.Add(ParseTemplateInterpolation());
        }

        // Expect tail
        Consume(TokenType.TEMPLATE_TAIL, "Expect end of template literal.");
        AddPart((TemplateStringValue)Previous().Literal!, Previous().Line);

        return new Expr.TemplateLiteral(strings, expressions, invalidEscapeLines);
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
        expressions.Add(ParseTemplateInterpolation());

        // Parse middle parts
        while (Match(TokenType.TEMPLATE_MIDDLE))
        {
            var midValue = (TemplateStringValue)Previous().Literal!;
            cooked.Add(midValue.Cooked);
            raw.Add(midValue.Raw);
            expressions.Add(ParseTemplateInterpolation());
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
            // Must start with identifier (or contextual keyword usable as one), [, {, or ...
            if (!Check(TokenType.IDENTIFIER) && !IsContextualKeyword(Peek().Type) &&
                !Check(TokenType.LEFT_BRACKET) && !Check(TokenType.LEFT_BRACE) && !Check(TokenType.DOT_DOT_DOT))
            {
                _current = savedPosition;
                return null;
            }

            do
            {
                if (Check(TokenType.LEFT_BRACKET) || Check(TokenType.LEFT_BRACE))
                {
                    // Destructure parameter: ([a, b]) => ... / ({ x, y }) => ...
                    // Speculative: an invalid pattern (e.g. it's really an array/object literal
                    // in a grouping) must BACKTRACK, not throw — the shared component parses,
                    // this candidate parse owns the failure policy.
                    try
                    {
                        var (parameter, pattern) = ParseDestructuredParameter(parameters.Count);
                        parameters.Add(parameter);
                        destructuredParams.Add((parameter.Name, pattern));
                    }
                    catch
                    {
                        _current = savedPosition;
                        return null;
                    }
                }
                else
                {
                    // Check for rest parameter
                    bool isRest = Match(TokenType.DOT_DOT_DOT);

                    if (!Check(TokenType.IDENTIFIER) && !IsContextualKeyword(Peek().Type))
                    {
                        _current = savedPosition;
                        return null;
                    }

                    Token paramTok = Advance();
                    Token paramName = paramTok.Type == TokenType.IDENTIFIER
                        ? paramTok
                        : new Token(TokenType.IDENTIFIER, paramTok.Lexeme, null, paramTok.Line);
                    // The shared tail consumes '?'/type/default: (x?: T = v) => ...  (if this
                    // isn't an arrow, the outer speculative parse backtracks, so consuming here
                    // is safe).
                    parameters.Add(ParseNamedRuntimeParameterTail(paramName, isRest));

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
        TypeNode? returnTypeNode = null;
        if (Match(TokenType.COLON))
        {
            // This could be return type OR ternary colon - need to check for arrow after
            int beforeType = _current;
            try
            {
                returnType = ParseFunctionTypeAnnotation();
                returnTypeNode = TakeTypeNode();
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
            body = ParseFunctionExpressionBody(parameters);
        }
        else
        {
            exprBody = Expression();
        }

        // If we have destructured parameters, prepend desugaring to body
        if (destructuredParams.Count > 0)
        {
            if (body != null)
            {
                body = PrependDestructuringPrologue(destructuredParams, body);
            }
            else if (exprBody != null)
            {
                // For expression body, wrap in a block with prologue + return
                body = PrependDestructuringPrologue(destructuredParams,
                    [new Stmt.Return(new Token(TokenType.RETURN, "return", null, 0), exprBody)]);
                exprBody = null;
            }
        }

        // Validate duplicate parameter names in strict mode
        if (_isStrictMode)
        {
            ValidateNoDuplicateParameters(parameters);
        }

        if (body != null)
            body = VarHoister.Hoist(body, _spans);

        return new Expr.ArrowFunction(Name: null, TypeParams: null, ThisType: null, Parameters: parameters, ExpressionBody: exprBody, BlockBody: body, ReturnType: returnType, IsAsync: isAsync, ReturnTypeNode: returnTypeNode);
    }

    /// <summary>
    /// Tries to parse a generic arrow function starting with type parameters: &lt;T&gt;(x) => ...
    /// Returns null if not a generic arrow function (backtracking safe).
    /// </summary>
    private Expr? TryParseGenericArrowFunction(bool isAsync = false)
    {
        int savedPosition = _current;
        try
        {
            List<TypeParam>? typeParams = ParseTypeParameters();
            if (typeParams == null || typeParams.Count == 0)
            {
                _current = savedPosition;
                return null;
            }

            // In TSX a lone unconstrained `<T>` is committed to JSX, even when the
            // following tokens could form an arrow. A constraint, default, second type
            // parameter, or trailing comma disambiguates the construct as a generic arrow.
            // Preserve the token-level comma because ParseTypeParameters intentionally
            // does not retain whether a single-parameter list ended in one.
            bool hasTypeParameterComma = false;
            for (int i = savedPosition; i < _current; i++)
                hasTypeParameterComma |= _tokens[i].Type == TokenType.COMMA;
            if (_jsx is not null &&
                typeParams.Count == 1 &&
                typeParams[0].Constraint is null &&
                typeParams[0].Default is null &&
                !hasTypeParameterComma)
            {
                _current = savedPosition;
                return null;
            }

            if (!Match(TokenType.LEFT_PAREN))
            {
                _current = savedPosition;
                return null;
            }

            Expr? arrowExpr = TryParseArrowFunction(isAsync);
            if (arrowExpr is Expr.ArrowFunction arrow)
            {
                return arrow with { TypeParams = typeParams };
            }

            _current = savedPosition;
            return null;
        }
        catch
        {
            _current = savedPosition;
            return null;
        }
    }

    /// <summary>
    /// Parses a function expression: function [name](params) { body }
    /// Supports optional name (for named function expressions), generator syntax (function*),
    /// this parameter, and type annotations.
    /// </summary>
    private Expr FunctionExpression(bool isAsync = false)
    {
        // Check for generator function: function* () { } (or async function* () {})
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
        TypeNode? thisTypeNode = null;
        if (Check(TokenType.THIS))
        {
            Advance(); // consume 'this'
            if (Match(TokenType.COLON))
            {
                thisType = ParseTypeAnnotation();
                thisTypeNode = TakeTypeNode();
            }
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
                if (Check(TokenType.LEFT_BRACKET) || Check(TokenType.LEFT_BRACE))
                {
                    // function([a, b]) {} / function({ x, y }) {}
                    var (parameter, pattern) = ParseDestructuredParameter(parameters.Count);
                    parameters.Add(parameter);
                    destructuredParams.Add((parameter.Name, pattern));
                }
                else
                {
                    // Check for rest parameter
                    bool isRest = Match(TokenType.DOT_DOT_DOT);

                    Token paramName = ConsumeIdentifierName("Expect parameter name.");
                    parameters.Add(ParseNamedRuntimeParameterTail(paramName, isRest));

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
        TypeNode? returnTypeNode = null;
        if (Match(TokenType.COLON))
        {
            returnType = ParseTypeAnnotation();
            returnTypeNode = TakeTypeNode();
        }

        Consume(TokenType.LEFT_BRACE, "Expect '{' before function body.");
        List<Stmt> body = ParseFunctionExpressionBody(parameters);

        // Prepend destructuring statements for patterned parameters
        body = PrependDestructuringPrologue(destructuredParams, body);

        body = VarHoister.Hoist(body, _spans);

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
            IsAsync: isAsync,
            IsGenerator: isGenerator,
            ThisTypeNode: thisTypeNode,
            ReturnTypeNode: returnTypeNode
        );
    }

    // Parse function type annotation like "(number) => number" or "(this: Window, e: Event) => void"
    private string ParseFunctionTypeAnnotation()
    {
        // Check if it's a function type: (params) => returnType
        if (Check(TokenType.LEFT_PAREN))
        {
            Advance(); // consume '('
            return ParseFunctionTypeBody();
        }

        // Otherwise regular type
        return ParseTypeAnnotation();
    }
}
