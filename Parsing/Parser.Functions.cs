namespace SharpTS.Parsing;

public partial class Parser
{
    private Stmt FunctionDeclaration(string kind, bool isAsync = false, bool isGenerator = false, bool isDeclare = false, (Token Name, Expr? ComputedKey)? computedMethod = null)
    {
        isDeclare |= _isDeclarationFile;

        Token name;
        Expr? computedKey = null;
        if (computedMethod is { } cm)
        {
            // Computed symbol-keyed class method ([Symbol.iterator]() {}): the caller already
            // parsed [expr] and synthesized the <computed> name token; the key expression is
            // carried on the resulting Stmt.Function for the interpreter/compiler to resolve.
            name = cm.Name;
            computedKey = cm.ComputedKey;
        }
        else if (kind == "constructor" && Match(TokenType.CONSTRUCTOR))
        {
            name = Previous();
        }
        else if (kind == "method")
        {
            // ES2015+ class MethodDefinition accepts any PropertyName —
            // including reserved words like `delete`, `class`, `if`, etc.
            // (e.g. Map has a `delete` method). ConsumePropertyName handles
            // the identifier-from-keyword conversion for AST consistency.
            name = ConsumePropertyName($"Expect {kind} name.");
        }
        else
        {
            name = ConsumeIdentifierName($"Expect {kind} name.");
        }

        if (kind == "method" && isDeclare)
            Match(TokenType.QUESTION);

        // Parse type parameters (e.g., <T, U extends Base>)
        List<TypeParam>? typeParams = ParseTypeParameters();

        Consume(TokenType.LEFT_PAREN, $"Expect '(' after {kind} name.");
        List<Stmt.Parameter> parameters = [];
        List<(Token SynthName, DestructurePattern Pattern)> destructuredParams = [];

        // Check for 'this' parameter (explicit this type annotation)
        string? thisType = null;
        TypeNode? thisTypeNode = null;
        if (Check(TokenType.THIS))
        {
            Advance(); // consume 'this'
            Consume(TokenType.COLON, "Expect ':' after 'this' in this parameter.");
            thisType = ParseTypeAnnotation();
            thisTypeNode = TakeTypeNode();
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
                if (Check(TokenType.LEFT_BRACKET) || Check(TokenType.LEFT_BRACE))
                {
                    // function f([a, b]) {} / function f({ x, y }) {}
                    var (parameter, pattern) = ParseDestructuredParameter(parameters.Count, paramDecorators);
                    parameters.Add(parameter);
                    destructuredParams.Add((parameter.Name, pattern));
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

                    Token paramName = ConsumeIdentifierName("Expect parameter name.");
                    parameters.Add(ParseNamedRuntimeParameterTail(paramName, isRest,
                        isParameterProperty: isParameterProperty, access: access,
                        isReadonly: isReadonly, decorators: paramDecorators));

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

        // Check for an overload/ambient signature. Ambient declarations follow
        // normal automatic-semicolon-insertion rules, so declaration packages
        // commonly omit the semicolon before the next line.
        bool hasSignatureTerminator = Match(TokenType.SEMICOLON);
        if (hasSignatureTerminator || (isDeclare && !Check(TokenType.LEFT_BRACE)))
        {
            // Overload signature - no body, just declaration
            return new Stmt.Function(name, typeParams, thisType, parameters, null, returnType, IsAsync: isAsync, IsGenerator: isGenerator, IsDeclare: isDeclare, ComputedKey: computedKey, ThisTypeNode: thisTypeNode, ReturnTypeNode: returnTypeNode);
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
        body = PrependDestructuringPrologue(destructuredParams, body);

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

        // Apply var hoisting: rewrite `var x` declarations in nested blocks to function-scope
        // declarations + assignments. Cheap no-op if no `var` keywords are present.
        body = VarHoister.Hoist(body);

        return new Stmt.Function(name, typeParams, thisType, parameters, body, returnType, IsAsync: isAsync, IsGenerator: isGenerator, ComputedKey: computedKey, ThisTypeNode: thisTypeNode, ReturnTypeNode: returnTypeNode);
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
                // Explicit `this` parameter (e.g. `call(this: Function, ...)` in lib.d.ts) — a
                // type-only annotation, not a runtime parameter. Consume and skip it.
                if (Check(TokenType.THIS))
                {
                    Advance(); // this
                    if (Match(TokenType.COLON)) ParseTypeAnnotation();
                    continue;
                }

                // Check for rest parameter
                bool isRest = Match(TokenType.DOT_DOT_DOT);

                Token paramName = ConsumeIdentifierName("Expect parameter name.");

                // Abstract methods don't have a body, so no default values make sense,
                // but TypeScript does allow them in the signature — the shared tail parses them.
                parameters.Add(ParseNamedRuntimeParameterTail(paramName, isRest));

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
