using SharpTS.Diagnostics;

namespace SharpTS.Parsing;

public enum EditorQueryKind { Syntax, Hover, Completion, SignatureHelp }

/// <summary>Limits grammar repairs in one private, cursor-specific editor parse.</summary>
public sealed record EditorRecoveryPolicy(int Version = 1, int MaxRepairs = 16, int MaxNesting = 8)
{
    public static EditorRecoveryPolicy Default { get; } = new();
}

/// <summary>A private parse of unchanged source text; recovered nodes are never authoritative locations.</summary>
public sealed record EditorParseArtifact(
    SourceDocument Document,
    IReadOnlyList<Token> Tokens,
    ParseDiagnosticResult ParseResult,
    EditorSyntaxIndex Syntax,
    int RecoveryCount)
{
    public bool IsRecovered => RecoveryCount != 0;
    public long EstimatedBytes => (long)Document.Text.Length * sizeof(char) + Tokens.Count * 96L + Syntax.EstimatedBytes;
}

public partial class Parser
{
    private int _editorCursor = -1;
    private EditorQueryKind _editorQuery;
    private EditorRecoveryPolicy? _editorRecoveryPolicy;
    private int _editorRecoveryCount;
    private int _editorArgumentDepth;
    private bool _editorCursorAllowsRecovery;
    private HashSet<Expr>? _editorRecoveredExpressions;
    private Dictionary<Expr.ArrowFunction, (int ParameterStart, int ParameterEnd, int HeaderEnd, int BodyStart)>? _editorFunctionHeaders;

    /// <summary>
    /// Parses a fresh document and token list. Repairs are limited to cursor-local member names,
    /// missing arguments and unclosed argument lists; ordinary parser entry points never enable them.
    /// </summary>
    public static EditorParseArtifact ParseForEditor(
        SourceDocument source, int cursorOffset, EditorQueryKind queryKind, EditorRecoveryPolicy policy,
        DecoratorMode decoratorMode = DecoratorMode.None, JsxParseOptions? jsxOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentOutOfRangeException.ThrowIfNegative(cursorOffset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cursorOffset, source.Text.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(policy.MaxRepairs);
        ArgumentOutOfRangeException.ThrowIfNegative(policy.MaxNesting);
        cancellationToken.ThrowIfCancellationRequested();

        var document = new SourceDocument(source.Path, source.Text, source.IsVirtual);
        bool isJsx = source.Path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
            source.Path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase);
        var lexer = new Lexer(source.Text) { JsxTolerant = isJsx };
        List<Token> tokens;
        try
        {
            tokens = lexer.WithCancellation(cancellationToken).ScanTokens();
            // Ordinary lexer EOF has no source extent; the private editor stream needs its real
            // boundary to distinguish an unfinished expression at the caret from earlier errors.
            tokens[^1] = new Token(TokenType.EOF, "", null, tokens[^1].Line, source.Text.Length);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            return new(document, [], new ParseDiagnosticResult([],
                [Diagnostic.ParseError(error.Message, new SourceLocation(source.Path, 1))]),
                new EditorSyntaxIndex(document, [], [], []), 0);
        }
        var parser = new Parser(tokens, decoratorMode)
            .WithSourceDocument(document).WithCancellation(cancellationToken).WithEditorSyntax();
        parser.AsDeclarationFile(source.Path.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase) ||
            source.Path.EndsWith(".d.mts", StringComparison.OrdinalIgnoreCase) ||
            source.Path.EndsWith(".d.cts", StringComparison.OrdinalIgnoreCase));
        if (isJsx) parser.WithJsx(source.Text, (jsxOptions ?? JsxParseOptions.Default).ApplyPragmas(lexer.Pragmas));
        parser._editorCursor = cursorOffset;
        parser._editorQuery = queryKind;
        parser._editorRecoveryPolicy = policy;
        parser._editorCursorAllowsRecovery = queryKind is EditorQueryKind.Completion or EditorQueryKind.SignatureHelp &&
            CursorAllowsRecovery(source.Text, tokens, cursorOffset, cancellationToken);
        // Repair diagnostics must not stop an otherwise bounded recovery before its containing
        // statement can be returned. Unrelated syntax failures retain the ordinary ten-error cap.
        parser.WithMaxErrors(Math.Max(10, Math.Min(policy.MaxRepairs, 64) + 1));
        ParseDiagnosticResult parsed = parser.Parse();
        cancellationToken.ThrowIfCancellationRequested();
        return new(document, Array.AsReadOnly(tokens.ToArray()), parsed,
            parser.EditorSyntax ?? new EditorSyntaxIndex(document, [], [], []),
            parser._editorRecoveryCount);
    }

    private bool EditorRecoveryEnabled => _editorCursor >= 0 && _editorCursorAllowsRecovery &&
        _editorQuery is EditorQueryKind.Completion or EditorQueryKind.SignatureHelp;

    private static bool CursorAllowsRecovery(string text, IReadOnlyList<Token> tokens, int cursor,
        CancellationToken cancellationToken)
    {
        int start = 0;
        int inspected = 0;
        foreach (Token token in tokens)
        {
            if ((inspected++ & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (token.Type == TokenType.EOF || token.Start < 0) continue;
            if (token.Start < cursor && token.End > cursor) return false;
            if (token.End <= cursor) start = Math.Max(start, token.End);
        }
        // The lexer omits comments. Inspect only the gap following the final real token before
        // the caret, so comment markers inside strings/regular expressions cannot fool this check.
        while (start < cursor)
        {
            if ((inspected++ & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (char.IsWhiteSpace(text[start])) { start++; continue; }
            if (start + 1 < cursor && text[start] == '/' && text[start + 1] == '/')
            {
                int newline = text.IndexOf('\n', start + 2);
                if (newline < 0 || newline >= cursor) return false;
                start = newline + 1;
                continue;
            }
            if (start + 1 < cursor && text[start] == '/' && text[start + 1] == '*')
            {
                int close = text.IndexOf("*/", start + 2, StringComparison.Ordinal);
                if (close < 0 || close + 2 > cursor) return false;
                start = close + 2;
                continue;
            }
            return false;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    private bool TrySpendEditorRepair()
    {
        if (!EditorRecoveryEnabled || _editorRecoveryPolicy is not { } policy ||
            _editorRecoveryCount >= Math.Min(policy.MaxRepairs, 64) ||
            _editorArgumentDepth > Math.Min(policy.MaxNesting, 32)) return false;
        _editorRecoveryCount++;
        return true;
    }

    private bool AtEditorRecoveryBoundary()
    {
        if (!EditorRecoveryEnabled) return false;
        Token next = Peek();
        return next.Start >= _editorCursor &&
            (IsAtEnd() || next.Type is TokenType.RIGHT_PAREN or TokenType.RIGHT_BRACKET or
                TokenType.RIGHT_BRACE or TokenType.SEMICOLON or TokenType.COMMA);
    }

    private Token ConsumeEditorMemberName(string message)
    {
        if (AtEditorRecoveryBoundary() && TrySpendEditorRepair())
        {
            RecordError("Identifier expected.", "TS1003");
            return new Token(TokenType.IDENTIFIER, "__editor_missing_member", null, Peek().Line);
        }
        return ConsumePropertyName(message);
    }

    private bool TryEditorArgumentHole(out Expr expression)
    {
        expression = null!;
        if (!EditorRecoveryEnabled || !Check(TokenType.COMMA)) return false;
        // Only a run of missing trailing arguments may precede the cursor. Do not reinterpret
        // arbitrary broken expressions, strings or JSX text as argument separators.
        int probe = _current;
        while (probe < _tokens.Count && _tokens[probe].Type == TokenType.COMMA &&
            _tokens[probe].Start < _editorCursor) probe++;
        if (probe >= _tokens.Count || _tokens[probe].Start < _editorCursor ||
            _tokens[probe].Type is not (TokenType.EOF or TokenType.RIGHT_PAREN or
                TokenType.RIGHT_BRACKET or TokenType.RIGHT_BRACE or TokenType.SEMICOLON) ||
            !TrySpendEditorRepair()) return false;
        RecordError("Expression expected.", "TS1109");
        expression = new Expr.Literal(SharpTS.Runtime.Types.SharpTSUndefined.Instance);
        _spans.MarkHidden(expression);
        RecordEditorRange(expression, SourceSpan.Hidden, EditorSyntaxKind.Expression,
            origin: EditorSyntaxOrigin.Synthetic);
        return true;
    }

    private Token ConsumeEditorClosingParen(string message)
    {
        if (!Check(TokenType.RIGHT_PAREN) && AtEditorRecoveryBoundary() && TrySpendEditorRepair())
        {
            RecordError("')' expected.", "TS1005");
            return new Token(TokenType.RIGHT_PAREN, ")", null, Peek().Line);
        }
        return Consume(TokenType.RIGHT_PAREN, message);
    }

    private T CompleteExpression<T>(T expression, int startOffset) where T : Expr
    {
        if (!EditorSyntaxEnabled || startOffset < 0) return expression;
        if (expression is Expr.Call { JsxOrigin: not null } && _spans.GetSpan(expression) is { } jsxSpan)
            return RecordExpression(expression, jsxSpan.Start, jsxSpan.End,
                _editorRecoveredExpressions?.Contains(expression) == true
                    ? EditorSyntaxOrigin.Recovered : EditorSyntaxOrigin.Written);
        return _editorRecoveredExpressions?.Contains(expression) == true
            ? RecordExpression(expression, startOffset, Math.Max(ConsumedSourceEnd, _editorCursor), EditorSyntaxOrigin.Recovered)
            : RecordExpression(expression, startOffset, ConsumedSourceEnd);
    }

    private int ExpressionStart(Expr expression) =>
        !EditorSyntaxEnabled ? -1 :
            _spans.GetSpan(expression) is { IsHidden: false } span ? span.Start : CurrentSourceStart();

    private T CompleteRecoveredExpression<T>(T expression, int startOffset, bool recovered) where T : Expr
    {
        if (!EditorSyntaxEnabled || startOffset < 0) return expression;
        int end = recovered ? Math.Max(ConsumedSourceEnd, _editorCursor) : ConsumedSourceEnd;
        if (recovered)
            (_editorRecoveredExpressions ??= new(ReferenceEqualityComparer.Instance)).Add(expression);
        RecordExpression(expression, startOffset, end,
            recovered ? EditorSyntaxOrigin.Recovered : EditorSyntaxOrigin.Written);
        return expression;
    }

    private Expr CompletePrimaryExpression(Expr expression, int startOffset, Token first)
    {
        if (!EditorSyntaxEnabled) return expression;
        CompleteExpression(expression, startOffset);
        switch (expression)
        {
            case Expr.Variable variable: RecordEditorName(variable.Name, variable); break;
            case Expr.This self: RecordEditorName(self.Keyword, self); break;
            case Expr.Super super:
                RecordEditorName(super.Keyword, super);
                if (super.Method is not null) RecordEditorName(super.Method, super, EditorSyntaxRole.MemberName);
                break;
            case Expr.Literal or Expr.RegexLiteral: RecordLiteralSyntax(expression, first); break;
            case Expr.Grouping:
                RecordEditorRange(expression, new SourceSpan(startOffset, ConsumedSourceEnd),
                    EditorSyntaxKind.Grouping, origin: EditorSyntaxOrigin.Grouping);
                break;
            case Expr.ObjectLiteral obj:
                foreach (Expr.Property property in obj.Properties)
                {
                    switch (property.Key)
                    {
                        case Expr.IdentifierKey key:
                            RecordEditorName(key.Name, property, EditorSyntaxRole.DeclarationName);
                            break;
                        case Expr.LiteralKey key:
                            RecordEditorName(key.Literal, property, EditorSyntaxRole.LiteralKey,
                                EditorSyntaxKind.Literal);
                            break;
                    }
                }
                break;
        }
        return expression;
    }

    private void RecordFunctionExpressionSyntax(Expr.ArrowFunction owner, int start, int parameterStart,
        int parameterEnd, int headerEnd, int bodyStart)
    {
        if (!EditorSyntaxEnabled) return;
        (_editorFunctionHeaders ??= new(ReferenceEqualityComparer.Instance))[owner] =
            (parameterStart, parameterEnd, headerEnd, bodyStart);
        CompleteExpression(owner, start);
        RecordEditorRange(owner, new SourceSpan(start, ConsumedSourceEnd), EditorSyntaxKind.Function);
        RecordEditorRange(owner, new SourceSpan(parameterStart, parameterEnd), EditorSyntaxKind.ParameterList);
        RecordEditorRange(owner, new SourceSpan(start, headerEnd), EditorSyntaxKind.Function, EditorSyntaxRole.Header);
        RecordEditorRange(owner, new SourceSpan(bodyStart, ConsumedSourceEnd), EditorSyntaxKind.Function, EditorSyntaxRole.Body);
        if (owner.Name is not null) RecordEditorName(owner.Name, owner, EditorSyntaxRole.DeclarationName);
    }
}
