namespace SharpTS.Parsing;

/// <summary>
/// Shared runtime-parameter parsing components. The repeated per-parameter units (named-parameter
/// tail, destructured parameter, destructuring prologue lowering) have one implementation here;
/// list-loop ownership, trailing-comma handling, rest-last policy, and speculative-arrow
/// backtracking stay with each caller. Function-TYPE parameter parsing (ParameterTypeNode) is
/// intentionally not routed through these — its AST output is genuinely different.
/// </summary>
public partial class Parser
{
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

        return new Stmt.Parameter(paramName, paramType, defaultValue, isRest,
            isParameterProperty, access, isReadonly, isOptional, decorators, paramTypeNode);
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
            ? objectPattern.Properties.Select(property => property.Value switch
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
            DestructuredProperties: destructuredProperties);
        return (parameter, pattern);
    }

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
            prologue.Add(desugar);
        }
        return prologue.Concat(body).ToList();
    }
}
