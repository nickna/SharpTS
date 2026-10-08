using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.LanguageServer.Services;

internal sealed record NavigationSignatureHelpResult(SignatureHelp? Help, AnalysisValidation? Validation = null)
{
    public bool IsCurrent(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Validation?.IsCurrent(cancellationToken) ?? true;
    }
}

/// <summary>Signature help from the exact source invocation's captured checker candidates.</summary>
public sealed class SemanticSignatureHelpService(SemanticAnalysisService analysis)
{
    internal async Task<NavigationSignatureHelpResult> SignatureHelpAsync(DocumentRequestSnapshot capture,
        Position position, bool labelOffsetSupport, bool activeParameterSupport, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (capture.Document.FilePath is null) return new(null);
        var lines = new LineIndex(capture.Document.Text);
        int offset = lines.ToOffset(position.Line + 1, position.Character + 1);
        var (line, column) = lines.ToPosition(offset);
        if (line - 1 != position.Line || column - 1 != position.Character) return new(null);

        using AnalysisLease? lease = await analysis.GetDocumentAsync(capture, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<Token>? tokens = null;
        if (lease is not null)
        {
            using var metadata = lease.EnterMetadataScope();
            var document = lease.Model.Snapshot.Documents.FirstOrDefault(document =>
                ReferenceEquals(document.Document, lease.Model.Document));
            tokens = document?.Tokens;
            if (tokens is not null && !IsCodePosition(capture.Document.Text, tokens, offset, cancellationToken, allowLiteral: true))
                return new(null, lease.Validation);
            if (TryHelp(lease.Model, offset, labelOffsetSupport, activeParameterSupport, cancellationToken,
                out SignatureHelp? help)) return new(help, lease.Validation);
        }
        if (tokens is null)
        {
            try
            {
                var lexer = new Lexer(capture.Document.Text)
                {
                    JsxTolerant = capture.Document.FilePath.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
                        capture.Document.FilePath.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase),
                };
                tokens = lexer.WithCancellation(cancellationToken).ScanTokens();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return new(null, lease?.Validation); }
        }
        // This token probe only admits unfinished call syntax. The fresh configured graph,
        // including the parser's exact invocation owner, must supply every semantic fact.
        if (!IsCodePosition(capture.Document.Text, tokens, offset, cancellationToken) ||
            !HasOpenCall(tokens, offset, cancellationToken)) return new(null, lease?.Validation);
        using AnalysisLease? recovered = await analysis.GetCursorDocumentAsync(capture, offset,
            EditorQueryKind.SignatureHelp, lease, cancellationToken).ConfigureAwait(false);
        if (recovered is null) return new(null, lease?.Validation);
        using var recoveredMetadata = recovered.EnterMetadataScope();
        _ = TryHelp(recovered.Model, offset, labelOffsetSupport, activeParameterSupport, cancellationToken, out var result);
        return new(result, recovered.Validation);
    }

    // A completed invocation, even when unavailable, cannot be repaired into different proof.
    private static bool TryHelp(CheckedNavigationModel model, int offset, bool labelOffsetSupport,
        bool activeParameterSupport, CancellationToken cancellationToken, out SignatureHelp? result)
    {
        result = null;
        SourceDocument document = model.Document;
        if (document.EditorSyntax is not { } syntax) return false;
        var invocation = syntax.FindInvocation(offset);
        if (invocation is null) return false;
        // A surrounding call does not own callback bodies, declarations, or raw JSX text.
        var context = syntax.FindNarrowest(offset, role: EditorSyntaxRole.Body);
        if (context is not null && context.Span.Start >= invocation.OpenParen.End &&
            context.Span.End <= invocation.Span.End) return true;
        var parameters = syntax.FindNarrowest(offset, EditorSyntaxKind.ParameterList);
        if (parameters is not null && parameters.Span.Start >= invocation.OpenParen.End &&
            parameters.Span.End <= invocation.Span.End) return true;
        if (syntax.FindNarrowest(offset, EditorSyntaxKind.JsxElement) is { } jsx &&
            (syntax.FindNarrowest(offset, EditorSyntaxKind.Expression) is not { } expression ||
                expression.Span.Start < jsx.Span.Start || expression.Span.End > jsx.Span.End ||
                expression.Node is Expr.Call { JsxOrigin: not null })) return true;
        var fact = model.EditorFacts.GetInvocation(document, invocation.Owner);
        if (fact is null) return invocation.CloseParen is not null;
        var candidates = new List<(int Ordinal, EditorSignaturePresentation Signature)>();
        foreach (var candidate in fact.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!candidate.Declared.IsAvailable) continue;
            candidates.Add((candidate.Ordinal, candidate.Instantiated is { IsAvailable: true } instantiated
                ? instantiated : candidate.Declared));
            if (candidates.Count == 32) break;
        }
        if (candidates.Count == 0) return invocation.CloseParen is not null;
        int? selected = null;
        if (fact.Status == EditorInvocationStatus.Selected && fact.IsComplete && !fact.IsRecovered &&
            !fact.HasHoles && !invocation.IsRecovered && fact.SelectedSignature?.IsAvailable == true &&
            fact.SelectedOrdinal is { } ordinal)
        {
            int index = candidates.FindIndex(candidate => candidate.Ordinal == ordinal);
            if (index >= 0) selected = index;
        }
        int argument = invocation.GetActiveArgumentIndex(offset);
        var signatures = new List<SignatureInformation>(candidates.Count);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var signature = candidate.Signature;
            signatures.Add(new SignatureInformation
            {
                Label = signature.Label,
                Parameters = signature.Parameters.Select(parameter => new ParameterInformation
                {
                    Label = labelOffsetSupport
                        ? new ParameterInformationLabel((parameter.LabelRange.Start, parameter.LabelRange.End))
                        : new ParameterInformationLabel(signature.Label[parameter.LabelRange.Start..parameter.LabelRange.End]),
                }).ToArray(),
                ActiveParameter = activeParameterSupport ? ParameterIndex(signature, argument) : null,
            });
        }
        result = new SignatureHelp
        {
            Signatures = signatures.ToArray(), ActiveSignature = selected,
            // LSP's first-signature fallback is presentation only; it never selects an overload.
            ActiveParameter = ParameterIndex(candidates[selected ?? 0].Signature, argument),
        };
        return true;
    }

    private static int? ParameterIndex(EditorSignaturePresentation signature, int argument) =>
        signature.IsTruncated || signature.Parameters.Count == 0 ? null : argument < signature.Parameters.Count ? argument :
            signature.HasRestParameter ? signature.Parameters.Count - 1 : null;

    private static bool HasOpenCall(IReadOnlyList<Token> tokens, int offset, CancellationToken cancellationToken)
    {
        var opens = new Stack<RecoveryDelimiter>();
        Token? previous = null;
        foreach (Token token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (token.Type == TokenType.EOF) break;
            if (token.Start < 0) continue;
            if (token.Type is TokenType.LEFT_PAREN or TokenType.LEFT_BRACKET or TokenType.LEFT_BRACE)
            {
                if (token.Start < offset && opens.TryPeek(out var parent)) parent.HasHole = false;
                bool call = token.Type == TokenType.LEFT_PAREN && previous is not null &&
                    (previous.Type is TokenType.IDENTIFIER or TokenType.RIGHT_PAREN or
                    TokenType.RIGHT_BRACKET or TokenType.GREATER or TokenType.GREATER_GREATER or TokenType.GREATER_GREATER_GREATER or
                    TokenType.PRIVATE_IDENTIFIER or TokenType.QUESTION_DOT || IsContextualIdentifier(previous.Type));
                opens.Push(new(token.Type, token.Start, call));
            }
            else if (token.Type is TokenType.RIGHT_PAREN or TokenType.RIGHT_BRACKET or TokenType.RIGHT_BRACE)
            {
                if (!opens.TryPop(out var open)) return false;
                if (!Matches(open.Kind, token.Type))
                    return token.Start >= offset && (open.IsCall && open.Start < offset ||
                        opens.Any(parent => parent.IsCall && parent.Start < offset));
                // A balanced call needs recovery only for its own cursor-local empty arguments.
                // Merely putting the caret before an existing argument does not make it unfinished.
                if (open.IsCall && open.Start < offset && offset <= token.Start && open.HasHole) return true;
                if (token.Start < offset && opens.TryPeek(out var parent)) parent.HasHole = false;
            }
            else if (token.Start < offset && opens.TryPeek(out var open))
            {
                if (token.Type == TokenType.COMMA && open.IsCall)
                    open.HasHole |= previous?.Type is TokenType.LEFT_PAREN or TokenType.COMMA;
                else open.HasHole = false;
            }
            previous = token;
        }
        return opens.Any(open => open.IsCall && open.Start < offset);
    }

    private sealed class RecoveryDelimiter(TokenType kind, int start, bool isCall)
    {
        public TokenType Kind { get; } = kind;
        public int Start { get; } = start;
        public bool IsCall { get; } = isCall;
        public bool HasHole { get; set; }
    }

    private static bool Matches(TokenType open, TokenType close) => (open, close) is
        (TokenType.LEFT_PAREN, TokenType.RIGHT_PAREN) or (TokenType.LEFT_BRACKET, TokenType.RIGHT_BRACKET) or
        (TokenType.LEFT_BRACE, TokenType.RIGHT_BRACE);

    private static bool IsContextualIdentifier(TokenType type) => type is
        TokenType.TYPE or TokenType.MODULE or TokenType.NAMESPACE or TokenType.ASYNC or TokenType.DECLARE or
        TokenType.ABSTRACT or TokenType.READONLY or TokenType.OVERRIDE or TokenType.GLOBAL or TokenType.OF or
        TokenType.FROM or TokenType.SATISFIES or TokenType.ACCESSOR or TokenType.OUT or TokenType.UNIQUE or
        TokenType.UNKNOWN or TokenType.NEVER or TokenType.INFER or TokenType.KEYOF or TokenType.ASSERTS or
        TokenType.IS or TokenType.TYPE_STRING or TokenType.TYPE_NUMBER or TokenType.TYPE_BOOLEAN or
        TokenType.TYPE_SYMBOL or TokenType.TYPE_BIGINT or TokenType.GET or TokenType.SET or TokenType.UNDEFINED or
        TokenType.CONSTRUCTOR or TokenType.SYMBOL or TokenType.BIGINT;

    private static bool IsCodePosition(string text, IReadOnlyList<Token> tokens, int offset,
        CancellationToken cancellationToken, bool allowLiteral = false)
    {
        int gap = 0;
        foreach (Token token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (token.Start < 0 || token.Type == TokenType.EOF) continue;
            if (token.Start >= offset) break;
            if (token.End > offset) return allowLiteral || !IsLiteral(token.Type);
            gap = token.End;
        }
        while (gap < offset)
        {
            if ((gap & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (char.IsWhiteSpace(text[gap])) { gap++; continue; }
            if (gap + 1 < offset && text[gap] == '/' && text[gap + 1] == '/')
            {
                int newline = text.IndexOf('\n', gap + 2);
                if (newline < 0 || newline >= offset) return false;
                gap = newline + 1;
            }
            else if (gap + 1 < offset && text[gap] == '/' && text[gap + 1] == '*')
            {
                int close = text.IndexOf("*/", gap + 2, StringComparison.Ordinal);
                if (close < 0 || close + 2 > offset) return false;
                gap = close + 2;
            }
            else return false;
        }
        return true;
    }

    private static bool IsLiteral(TokenType type) => type is TokenType.STRING or TokenType.REGEX or TokenType.NUMBER or
        TokenType.BIGINT_LITERAL or TokenType.TEMPLATE_HEAD or TokenType.TEMPLATE_MIDDLE or
        TokenType.TEMPLATE_TAIL or TokenType.TEMPLATE_FULL;
}
