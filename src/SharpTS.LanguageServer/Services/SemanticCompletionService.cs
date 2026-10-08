using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SharpTS.LanguageServer.Services;

internal sealed record NavigationCompletionResult(CompletionList List, AnalysisValidation? Validation = null)
{
    public bool IsCurrent(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Validation?.IsCurrent(cancellationToken) ?? true;
    }
}

/// <summary>Plain text completion from captured source scopes and checked receiver projections.</summary>
public sealed class SemanticCompletionService(SemanticAnalysisService analysis)
{
    private const int CandidateLimit = 256;

    internal async Task<NavigationCompletionResult> CompletionAsync(DocumentRequestSnapshot capture,
        Position position, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (capture.Document.FilePath is null) return new(new());
        var lines = new LineIndex(capture.Document.Text);
        int offset = lines.ToOffset(position.Line + 1, position.Character + 1);
        var (line, column) = lines.ToPosition(offset);
        if (line - 1 != position.Line || column - 1 != position.Character) return new(new());

        using AnalysisLease? lease = await analysis.GetDocumentAsync(capture, cancellationToken).ConfigureAwait(false);
        if (lease is not null)
        {
            using var metadata = lease.EnterMetadataScope();
            if (TryComplete(lease.Model, offset, cancellationToken, out CompletionList? completed))
                return new(completed!, lease.Validation);
        }

        // Only a real unfinished member operator can request a fresh checked cursor graph.
        // Existing unsupported/failed semantic contexts never borrow a previously good receiver.
        IReadOnlyList<Token>? tokens = lease?.Model.Snapshot.Documents.FirstOrDefault(document =>
            ReferenceEquals(document.Document, lease.Model.Document))?.Tokens;
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
            catch (Exception) { return new(new(), lease?.Validation); }
        }
        if (tokens.Count == 0) return new(new(), lease?.Validation);
        Replacement replacement = ReplacementAt(capture.Document.Text, offset);
        if (!IsCodePosition(capture.Document.Text, tokens, offset, cancellationToken) ||
            !HasMemberOperator(tokens, replacement.Start, out _)) return new(new(), lease?.Validation);
        using AnalysisLease? recovered = await analysis.GetCursorDocumentAsync(capture, offset,
            EditorQueryKind.Completion, lease, cancellationToken).ConfigureAwait(false);
        if (recovered is null) return new(new(), lease?.Validation);
        using var recoveredMetadata = recovered.EnterMetadataScope();
        _ = TryComplete(recovered.Model, offset, cancellationToken, out CompletionList? result);
        return new(result ?? new(), recovered.Validation);
    }

    // True includes a recognized-but-unavailable context: retrying it cannot invent proof.
    private static bool TryComplete(CheckedNavigationModel model, int offset, CancellationToken cancellationToken,
        out CompletionList? result)
    {
        result = new();
        SourceDocument document = model.Document;
        if (document.EditorSyntax is not { } syntax) return true;
        var capture = model.Snapshot.Documents.FirstOrDefault(capture => ReferenceEquals(capture.Document, document));
        if (capture is null || !IsCodePosition(document.Text, capture.Tokens, offset, cancellationToken)) return true;
        Replacement replacement = ReplacementAt(document.Text, offset);
        if (syntax.FindMember(offset) is { } member && offset >= member.OperatorSpan.End)
        {
            if (member.IsIndex || replacement.Start < member.OperatorSpan.End ||
                member.Name.Start >= 0 && (replacement.Start != member.Name.Start || replacement.End != member.Name.End))
                return true;
            var receiver = model.EditorFacts.GetOccurrence(document, member.Receiver);
            var set = model.EditorFacts.GetReceiverMembers(document, member.Receiver);
            if (member.Receiver is Expr.Super ? set.ReceiverType?.IsAvailable != true :
                receiver?.Availability != EditorFactAvailability.Available || !receiver.Type.IsAvailable)
                return true;
            var candidates = new List<Candidate>();
            foreach (var candidate in set.Members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (candidate.NamespaceFacet != BindingNamespace.Type && candidate.Type.IsAvailable &&
                    ValidName(candidate.Name, allowPrivate: true) &&
                    candidate.Name.StartsWith(replacement.Prefix, StringComparison.OrdinalIgnoreCase))
                    candidates.Add(new(candidate.Name, MemberKind(candidate.Kind), candidate.Type.Text));
                if (candidates.Count == CandidateLimit) break;
            }
            result = CreateList(document, replacement, candidates, !set.IsComplete || set.IsTruncated, cancellationToken);
            return true;
        }
        if (HasMemberOperator(capture.Tokens, replacement.Start, out _)) return false;
        // A parser-discarded declaration can hide an outer spelling without leaving an AST
        // owner for the checked scope veto. Lexical recovery is intentionally unsupported.
        if (capture.ParseDiagnostics.Count != 0 || capture.HitParseErrorLimit) return true;
        if (replacement.Prefix.StartsWith('#')) return true;
        if (replacement.Start == replacement.End && syntax.FindNarrowest(offset, EditorSyntaxKind.JsxElement) is { } jsx &&
            (syntax.FindNarrowest(offset, EditorSyntaxKind.Expression) is not { } inner ||
                inner.Span.Start < jsx.Span.Start || inner.Span.End > jsx.Span.End || inner.Node is Expr.Call { JsxOrigin: not null }))
            return true;
        BindingNamespace? facet = LexicalFacet(syntax, capture.Tokens, replacement, offset);
        if (facet is null) return true;
        var locals = new List<Candidate>();
        bool truncated = false;
        foreach (var binding in model.EditorFacts.GetVisibleBindings(document, offset))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (binding.Facet != facet || binding.Availability != EditorFactAvailability.Available ||
                !binding.Type.IsAvailable || !ValidName(binding.LocalName, allowPrivate: false) ||
                !binding.LocalName.StartsWith(replacement.Prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (locals.Count == CandidateLimit) { truncated = true; break; }
            locals.Add(new(binding.LocalName, BindingKind(binding.DeclarationOwner, binding.Facet), binding.Type.Text));
        }
        result = CreateList(document, replacement, locals, truncated, cancellationToken);
        return true;
    }

    private static BindingNamespace? LexicalFacet(EditorSyntaxIndex syntax, IReadOnlyList<Token> tokens,
        Replacement replacement, int offset)
    {
        int probe = replacement.Start < replacement.End ? replacement.Start : offset;
        var name = syntax.FindNarrowest(probe, EditorSyntaxKind.Name);
        if (name is not null && name.Span.Start == replacement.Start && name.Span.End == replacement.End)
        {
            if (name.Node is NamedTypeNode { NameTokens.Count: > 1 }) return null;
            if (name.Node is TypeNode && name.Role != EditorSyntaxRole.DeclarationName) return BindingNamespace.Type;
            if (name.Node is Expr.Variable or Expr.Assign or Expr.CompoundAssign or Expr.LogicalAssign &&
                name.Role == EditorSyntaxRole.Name) return BindingNamespace.Value;
            return null;
        }
        if (replacement.Start != replacement.End) return null;
        Token? previous = PreviousToken(tokens, offset);
        if (previous is null) return BindingNamespace.Value;
        if (previous.Type is TokenType.EQUAL or TokenType.RETURN or TokenType.ARROW or TokenType.THROW or
            TokenType.PLUS or TokenType.MINUS or TokenType.STAR or TokenType.SLASH or TokenType.AND_AND or
            TokenType.OR_OR or TokenType.QUESTION_QUESTION or TokenType.BANG)
            return BindingNamespace.Value;
        if (previous.Type is TokenType.COMMA or TokenType.LEFT_PAREN)
            return syntax.FindInvocation(offset) is not null ? BindingNamespace.Value : null;
        if (previous.Type is TokenType.SEMICOLON or TokenType.LEFT_BRACE or TokenType.RIGHT_BRACE)
        {
            // A class body is a member declaration context, not a lexical expression context.
            var body = syntax.FindNarrowest(offset, role: EditorSyntaxRole.Body);
            return body?.Kind == EditorSyntaxKind.Class ? null : BindingNamespace.Value;
        }
        return null;
    }

    private sealed record Candidate(string Name, CompletionItemKind Kind, string Detail);
    private readonly record struct Replacement(int Start, int End, string Prefix);

    private static Replacement ReplacementAt(string text, int offset)
    {
        int start = offset, end = offset;
        while (start > 0 && IdentifierPart(text[start - 1])) start--;
        if (start > 0 && text[start - 1] == '#') start--;
        while (end < text.Length && IdentifierPart(text[end])) end++;
        return new(start, end, text[start..offset]);
    }

    private static bool IdentifierPart(char character) => char.IsLetterOrDigit(character) || character is '_' or '$';
    private static bool ValidName(string name, bool allowPrivate)
    {
        int start = allowPrivate && name.StartsWith('#') ? 1 : 0;
        if (name.Length <= start || name.Length > 256 ||
            !(char.IsLetter(name[start]) || name[start] is '_' or '$')) return false;
        for (int index = start + 1; index < name.Length; index++) if (!IdentifierPart(name[index])) return false;
        return true;
    }

    private static Token? PreviousToken(IReadOnlyList<Token> tokens, int offset)
    {
        int low = 0, high = tokens.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (tokens[middle].Start >= 0 && tokens[middle].End <= offset && tokens[middle].Type != TokenType.EOF)
                low = middle + 1;
            else high = middle;
        }
        return low == 0 ? null : tokens[low - 1];
    }

    private static bool HasMemberOperator(IReadOnlyList<Token> tokens, int start, out Token? memberOperator)
    {
        memberOperator = PreviousToken(tokens, start);
        if (memberOperator?.Type is not (TokenType.DOT or TokenType.QUESTION_DOT)) return false;
        Token? receiver = PreviousToken(tokens, memberOperator.Start);
        return receiver is not null && (receiver.Type is TokenType.IDENTIFIER or TokenType.THIS or TokenType.SUPER or
            TokenType.RIGHT_PAREN or TokenType.RIGHT_BRACKET or TokenType.STRING or TokenType.PRIVATE_IDENTIFIER ||
            EditorTokenFacts.IsContextualIdentifier(receiver.Type));
    }

    private static bool IsCodePosition(string text, IReadOnlyList<Token> tokens, int offset, CancellationToken cancellationToken)
    {
        int gap = 0;
        foreach (Token token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (token.Start < 0 || token.Type == TokenType.EOF) continue;
            if (token.Start == offset && token.Type is TokenType.STRING or TokenType.REGEX or TokenType.NUMBER or
                TokenType.BIGINT_LITERAL or TokenType.TEMPLATE_HEAD or TokenType.TEMPLATE_MIDDLE or
                TokenType.TEMPLATE_TAIL or TokenType.TEMPLATE_FULL) return false;
            if (token.Start >= offset) break;
            if (token.End > offset)
                return token.Type is not (TokenType.STRING or TokenType.REGEX or TokenType.NUMBER or
                    TokenType.BIGINT_LITERAL or TokenType.TEMPLATE_HEAD or TokenType.TEMPLATE_MIDDLE or
                    TokenType.TEMPLATE_TAIL or TokenType.TEMPLATE_FULL);
            gap = token.End;
        }
        // Comments are omitted by the lexer. Inspect the gap after the last real token only.
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

    private static CompletionItemKind MemberKind(EditorMemberKind kind) => kind switch
    {
        EditorMemberKind.Method => CompletionItemKind.Method,
        EditorMemberKind.Field or EditorMemberKind.ParameterProperty => CompletionItemKind.Field,
        _ => CompletionItemKind.Property,
    };

    private static CompletionItemKind BindingKind(object? owner, BindingNamespace facet) => owner switch
    {
        Stmt.Function => CompletionItemKind.Function,
        Stmt.Class or Expr.ClassExpr => CompletionItemKind.Class,
        Stmt.Interface => CompletionItemKind.Interface,
        Stmt.Enum => CompletionItemKind.Enum,
        Stmt.Namespace => CompletionItemKind.Module,
        TypeParam => CompletionItemKind.TypeParameter,
        Stmt.TypeAlias => CompletionItemKind.Reference,
        Stmt.Const => CompletionItemKind.Constant,
        _ => facet == BindingNamespace.Type ? CompletionItemKind.Reference : CompletionItemKind.Variable,
    };

    private static CompletionList CreateList(SourceDocument document, Replacement replacement,
        IEnumerable<Candidate> candidates, bool incomplete, CancellationToken cancellationToken)
    {
        var (startLine, startColumn) = document.Lines.ToPosition(replacement.Start);
        var (endLine, endColumn) = document.Lines.ToPosition(replacement.End);
        var range = new Range(startLine - 1, startColumn - 1, endLine - 1, endColumn - 1);
        var items = new List<CompletionItem>();
        foreach (var candidate in candidates.OrderBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Kind).DistinctBy(candidate => candidate.Name).Take(CandidateLimit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string detail = candidate.Detail;
            if (detail.Length > 1024)
            {
                int end = char.IsHighSurrogate(detail[1022]) ? 1022 : 1023;
                detail = detail[..end] + "…";
            }
            items.Add(new CompletionItem
            {
                Label = candidate.Name, Kind = candidate.Kind, Detail = detail,
                SortText = candidate.Name, FilterText = candidate.Name,
                InsertText = candidate.Name, InsertTextFormat = InsertTextFormat.PlainText,
                TextEdit = new TextEdit { Range = range, NewText = candidate.Name },
            });
        }
        return new(items, incomplete);
    }
}
