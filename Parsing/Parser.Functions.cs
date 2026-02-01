namespace SharpTS.Parsing;

public partial class Parser
{
    private Stmt FunctionDeclaration(string kind, bool isAsync = false, bool isGenerator = false)
    {
        Token name;
        if (kind == "constructor" && Match(TokenType.CONSTRUCTOR))
        {
            name = Previous();
        }
        else
        {
            name = Consume(TokenType.IDENTIFIER, $"Expect {kind} name.");
        }

        // Parse type parameters (e.g., <T, U extends Base>)
        List<TypeParam>? typeParams = ParseTypeParameters();

        Consume(TokenType.LEFT_PAREN, $"Expect '(' after {kind} name.");
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
                // Handle trailing comma: function foo(a, b,) {}
                if (Check(TokenType.RIGHT_PAREN)) break;

                // Parse parameter decorators
                List<Decorator>? paramDecorators = ParseDecorators();

                // Check for destructuring pattern parameter
                if (Check(TokenType.LEFT_BRACKET))
                {
                    // Array destructure: function f([a, b]) {}
                    int line = Peek().Line;
                    Consume(TokenType.LEFT_BRACKET, "");
                    var pattern = ParseArrayPattern();
                    Token synthName = new Token(TokenType.IDENTIFIER, $"_param{parameters.Count}", null, line);
                    string? paramType = Match(TokenType.COLON) ? ParseTypeAnnotation() : null;
                    Expr? defaultValue = Match(TokenType.EQUAL) ? Expression() : null;
                    parameters.Add(new Stmt.Parameter(synthName, paramType, defaultValue, Decorators: paramDecorators));
                    destructuredParams.Add((synthName, pattern));
                }
                else if (Check(TokenType.LEFT_BRACE))
                {
                    // Object destructure: function f({ x, y }) {}
                    int line = Peek().Line;
                    Consume(TokenType.LEFT_BRACE, "");
                    var pattern = ParseObjectPattern();
                    Token synthName = new Token(TokenType.IDENTIFIER, $"_param{parameters.Count}", null, line);
                    string? paramType = Match(TokenType.COLON) ? ParseTypeAnnotation() : null;
                    Expr? defaultValue = Match(TokenType.EQUAL) ? Expression() : null;
                    parameters.Add(new Stmt.Parameter(synthName, paramType, defaultValue, Decorators: paramDecorators));
                    destructuredParams.Add((synthName, pattern));
                }
                else
                {
                    // Check for rest parameter
                    bool isRest = Match(TokenType.DOT_DOT_DOT);

                    // Check for parameter property modifiers (only valid in constructors)
                    AccessModifier? access = null;
                    bool isReadonly = false;
                    bool isParameterProperty = false;

                    // Parse modifiers (order doesn't matter: readonly public or public readonly)
                    while (Check(TokenType.PUBLIC) || Check(TokenType.PRIVATE) ||
                           Check(TokenType.PROTECTED) || Check(TokenType.READONLY))
                    {
                        if (Match(TokenType.PUBLIC))
                        {
                            access = AccessModifier.Public;
                            isParameterProperty = true;
                        }
                        else if (Match(TokenType.PRIVATE))
                        {
                            access = AccessModifier.Private;
                            isParameterProperty = true;
                        }
                        else if (Match(TokenType.PROTECTED))
                        {
                            access = AccessModifier.Protected;
                            isParameterProperty = true;
                        }
                        else if (Match(TokenType.READONLY))
                        {
                            isReadonly = true;
                            isParameterProperty = true;
                        }
                    }

                    // If only readonly was specified, default access is public
                    if (isParameterProperty && access == null)
                    {
                        access = AccessModifier.Public;
                    }

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
                    parameters.Add(new Stmt.Parameter(paramName, paramType, defaultValue, isRest, isParameterProperty, access, isReadonly, isOptional, paramDecorators));

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

        // Check for overload signature (semicolon instead of body)
        if (Match(TokenType.SEMICOLON))
        {
            // Overload signature - no body, just declaration
            return new Stmt.Function(name, typeParams, thisType, parameters, null, returnType, IsAsync: isAsync, IsGenerator: isGenerator);
        }

        // Save current strict mode state before parsing function body
        bool previousStrictMode = _isStrictMode;

        Consume(TokenType.LEFT_BRACE, $"Expect '{{' before {kind} body.");
        List<Stmt> body = Block(parseFunctionPrologue: true, setStrictMode: true);

        // Validate duplicate parameter names in strict mode
        // This must happen after body parsing because the function's own "use strict" directive
        // could enable strict mode for this function
        if (_isStrictMode)
        {
            ValidateNoDuplicateParameters(parameters);
        }

        // Restore previous strict mode state after function body
        _isStrictMode = previousStrictMode;

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

        // Prepend parameter property assignments for constructor: this.x = x
        if (kind == "constructor")
        {
            List<Stmt> propAssignments = [];
            foreach (var param in parameters)
            {
                if (param.IsParameterProperty)
                {
                    // Generate: this.<name> = <name>
                    var thisExpr = new Expr.This(new Token(TokenType.THIS, "this", null, param.Name.Line));
                    var paramVar = new Expr.Variable(param.Name);
                    var setExpr = new Expr.Set(thisExpr, param.Name, paramVar);
                    propAssignments.Add(new Stmt.Expression(setExpr));
                }
            }
            if (propAssignments.Count > 0)
            {
                body = propAssignments.Concat(body).ToList();
            }
        }

        return new Stmt.Function(name, typeParams, thisType, parameters, body, returnType, IsAsync: isAsync, IsGenerator: isGenerator);
    }

    /// <summary>
    /// Validates that there are no duplicate parameter names.
    /// In strict mode, duplicate parameter names are a SyntaxError.
    /// </summary>
    private void ValidateNoDuplicateParameters(List<Stmt.Parameter> parameters)
    {
        var seenNames = new HashSet<string>();
        foreach (var param in parameters)
        {
            // Skip synthetic parameters (from destructuring patterns)
            if (param.Name.Lexeme.StartsWith("_param"))
                continue;

            if (!seenNames.Add(param.Name.Lexeme))
            {
                throw new Exception($"SyntaxError: Duplicate parameter name '{param.Name.Lexeme}' not allowed in strict mode");
            }
        }
    }

    /// <summary>
    /// Parse method parameters for abstract methods (no destructuring, no parameter properties).
    /// </summary>
    private List<Stmt.Parameter> ParseMethodParameters()
    {
        List<Stmt.Parameter> parameters = [];

        if (!Check(TokenType.RIGHT_PAREN))
        {
            do
            {
                // Check for rest parameter
                bool isRest = Match(TokenType.DOT_DOT_DOT);

                Token paramName = Consume(TokenType.IDENTIFIER, "Expect parameter name.");
                string? paramType = null;
                if (Match(TokenType.COLON))
                {
                    paramType = ParseTypeAnnotation();
                }
                // Abstract methods don't have a body, so no default values make sense
                // But TypeScript does allow them in the signature, so let's parse them
                Expr? defaultValue = null;
                if (Match(TokenType.EQUAL))
                {
                    defaultValue = Expression();
                }
                parameters.Add(new Stmt.Parameter(paramName, paramType, defaultValue, isRest));

                // Rest parameter must be last
                if (isRest && Check(TokenType.COMMA))
                {
                    throw new Exception("Parse Error: Rest parameter must be last.");
                }
            } while (Match(TokenType.COMMA));
        }

        return parameters;
    }
}
