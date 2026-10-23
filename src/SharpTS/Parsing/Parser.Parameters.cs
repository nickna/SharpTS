namespace SharpTS.Parsing;

/// <summary>
/// Shared runtime-parameter parsing components. The repeated per-parameter units (named-parameter
/// tail, destructured parameter, destructuring prologue lowering) have one implementation here.
/// Ordinary functions and private methods also share their parameter-list loop; speculative-arrow
/// list parsing and backtracking stay with each caller. Function-TYPE parameter parsing (ParameterTypeNode) is
/// intentionally not routed through these — its AST output is genuinely different.
/// </summary>
public partial class Parser
{
    /// <summary>
    /// Parses a callable's parameters after its opening parenthesis. Callers
    /// retain the closing token and lower patterned bindings into their body.
    /// </summary>
    private (List<Stmt.Parameter> Parameters,
        List<(Token SynthName, DestructurePattern Pattern)> DestructuredParameters,
        string? ThisType, TypeNode? ThisTypeNode) ParseRuntimeParameters(
            bool allowParameterDecoratorsAndProperties = true)
    {
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
                // Handle trailing comma: function foo(a, b,) {}
                if (Check(TokenType.RIGHT_PAREN)) break;

                // Parse parameter decorators
                List<Decorator>? paramDecorators = allowParameterDecoratorsAndProperties ? ParseDecorators() : null;

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
                    while (allowParameterDecoratorsAndProperties &&
                           (Check(TokenType.PUBLIC) || Check(TokenType.PRIVATE) ||
                            Check(TokenType.PROTECTED) || Check(TokenType.READONLY)))
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
        return (parameters, destructuredParams, thisType, thisTypeNode);
    }

    /// <summary>
    /// Parses the deterministic tail of a named runtime parameter and constructs it. The caller
    /// has already consumed any leading <c>...</c>, parameter-property modifiers, decorators, and
    /// the name token itself (so speculative callers control error vs. backtrack on the name).
    /// Tail: optional <c>?</c>, optional <c>: type</c>, optional <c>= default</c> (only when
    /// <paramref name="allowDefault"/> — call/constructor signatures leave <c>=</c> unconsumed).
    /// </summary>
    private Stmt.Parameter ParseNamedRuntimeParameterTail(
        Token paramName,
        bool isRest,
        bool allowDefault = true,
        bool isParameterProperty = false,
        AccessModifier? access = null,
        bool isReadonly = false,
        List<Decorator>? decorators = null)
    {
        int start = paramName.Start;
        if (isRest && _current > 1 && _tokens[_current - 2].Type == TokenType.DOT_DOT_DOT)
            start = _tokens[_current - 2].Start;
        // Check for optional parameter marker (?)
        bool isOptional = Match(TokenType.QUESTION);

        string? paramType = null;
        TypeNode? paramTypeNode = null;
        if (Match(TokenType.COLON))
        {
            paramType = ParseTypeAnnotation();
            paramTypeNode = TakeTypeNode();
        }

        Expr? defaultValue = null;
        if (allowDefault && Match(TokenType.EQUAL))
        {
            defaultValue = Expression();
        }

        var parameter = new Stmt.Parameter(paramName, paramType, defaultValue, isRest,
            isParameterProperty, access, isReadonly, isOptional, decorators, paramTypeNode,
            HasBindingExpressions: defaultValue != null);
        RecordEditorName(paramName, parameter, EditorSyntaxRole.DeclarationName);
        RecordEditorRange(parameter, new SourceSpan(start, ConsumedSourceEnd),
            EditorSyntaxKind.Name, EditorSyntaxRole.Parameter);
        return parameter;
    }

    /// <summary>
    /// Parses one destructured runtime parameter starting at <c>[</c> or <c>{</c>: the pattern,
    /// a synthetic <c>_paramN</c> name, an optional type annotation, and an optional default.
    /// The caller lowers the returned pattern into a body prologue via
    /// <see cref="PrependDestructuringPrologue"/>; speculative callers wrap this in their own
    /// try/backtrack.
    /// </summary>
    private (Stmt.Parameter Parameter, DestructurePattern Pattern) ParseDestructuredParameter(
        int parameterIndex, List<Decorator>? decorators = null)
    {
        int start = CurrentSourceStart();
        int line = Peek().Line;
        DestructurePattern pattern;
        if (Match(TokenType.LEFT_BRACKET))
        {
            pattern = ParseArrayPattern();
        }
        else
        {
            Consume(TokenType.LEFT_BRACE, "Expect '[' or '{' to start destructured parameter.");
            pattern = ParseObjectPattern();
        }

        Token synthName = new Token(TokenType.IDENTIFIER, $"_param{parameterIndex}", null, line);
        string? paramType = Match(TokenType.COLON) ? ParseTypeAnnotation() : null;
        TypeNode? paramTypeNode = paramType is not null ? TakeTypeNode() : null;
        Expr? defaultValue = Match(TokenType.EQUAL) ? Expression() : null;

        List<Stmt.DestructuredParameterProperty>? destructuredProperties = pattern is ObjectPattern objectPattern
            ? objectPattern.Properties.Where(property => property.ComputedKey is null)
                .Select(property => property.Value switch
                {
                    IdentifierPattern identifier => new Stmt.DestructuredParameterProperty(
                        property.Key, identifier.Name, property.DefaultValue ?? identifier.DefaultValue,
                        identifier.Name.Lexeme != property.Key.Lexeme),
                    RestPattern rest => new Stmt.DestructuredParameterProperty(
                        property.Key, rest.Name, property.DefaultValue, IsRenamed: false),
                    _ => new Stmt.DestructuredParameterProperty(
                        property.Key, property.Key, property.DefaultValue, IsRenamed: false),
                })
                .ToList()
            : null;
        var parameter = new Stmt.Parameter(synthName, paramType, defaultValue,
            Decorators: decorators, TypeAnnotationNode: paramTypeNode,
            DestructuredProperties: destructuredProperties,
            HasBindingExpressions: defaultValue != null || ContainsBindingExpressions(pattern));
        RecordEditorRange(parameter, new SourceSpan(start, ConsumedSourceEnd),
            EditorSyntaxKind.Name, EditorSyntaxRole.Parameter);
        // The generated _paramN identifier has no written name. Original binding/key tokens
        // remain useful even though the runtime parameter itself uses a synthesized name.
        if (destructuredProperties is not null)
            foreach (Stmt.DestructuredParameterProperty property in destructuredProperties)
            {
                RecordEditorName(property.Key, parameter, EditorSyntaxRole.LiteralKey);
                RecordEditorName(property.Binding, parameter, EditorSyntaxRole.DeclarationName);
            }
        return (parameter, pattern);
    }

    private static bool ContainsBindingExpressions(DestructurePattern pattern) => pattern switch
    {
        IdentifierPattern identifier => identifier.DefaultValue != null,
        ArrayPattern array => array.Elements.Any(element => element.DefaultValue != null
            || (element.Pattern != null && ContainsBindingExpressions(element.Pattern))),
        ObjectPattern objectPattern => objectPattern.Properties.Any(property => property.ComputedKey != null
            || property.DefaultValue != null || ContainsBindingExpressions(property.Value)),
        RestPattern => false,
        _ => false
    };

    /// <summary>
    /// Lowers destructured parameters into a body prologue: each synthetic <c>_paramN</c> is
    /// desugared through its pattern ahead of the original body statements.
    /// </summary>
    private List<Stmt> PrependDestructuringPrologue(
        List<(Token SynthName, DestructurePattern Pattern)> destructuredParams,
        List<Stmt> body)
    {
        if (destructuredParams.Count == 0)
            return body;

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
            prologue.Add(desugar is Stmt.Sequence sequence
                ? sequence with { ParameterName = synthName }
                : new Stmt.Sequence([desugar], ParameterName: synthName));
        }
        return prologue.Concat(body).ToList();
    }
}
