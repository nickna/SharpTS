using System.Collections.Immutable;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

public enum PrivateRenameDomainStatus
{
    Available, UnsupportedSelection, IncompleteSyntax, UnsupportedOwner,
    UnvisitedBody, UnsupportedDeclarations, UnresolvedOccurrence,
}

/// <summary>A complete, checked private lexical domain in one captured source file.</summary>
public sealed record PrivateRenameDomain(SourceClassInfo Owner, FrozenSourceMemberSymbol Symbol,
    SourceSpan Span, Token SelectedToken, IReadOnlyList<Token> Tokens,
    IReadOnlyList<FrozenSourceMemberSymbol> PrivateMembers);

public sealed record PrivateRenameDomainResult(PrivateRenameDomainStatus Status,
    PrivateRenameDomain? Domain = null)
{
    public bool IsAvailable => Status == PrivateRenameDomainStatus.Available && Domain is not null;
}

public sealed partial class FrozenMemberIndex
{
    /// <summary>
    /// Audits every original private token in the selected lexical class against completed
    /// checker identities. The token stream and parse-error flag must come from this exact
    /// source capture; no workspace traversal, semantic lookup or name-based usage search occurs.
    /// </summary>
    public PrivateRenameDomainResult GetPrivateRenameDomain(SourceDocument document, int offset,
        IReadOnlyList<Token> tokens, FrozenEditorSemanticIndex editorFacts, bool hasParseErrors,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        static PrivateRenameDomainResult Refused(PrivateRenameDomainStatus status) => new(status);
        if (hasParseErrors || document.EditorSyntax is not { } syntax ||
            !ReferenceEquals(syntax.Document, document) || tokens.Count == 0 ||
            tokens[^1].Type != TokenType.EOF ||
            tokens[^1].Start is not -1 && tokens[^1].Start != document.Text.Length)
            return Refused(PrivateRenameDomainStatus.IncompleteSyntax);
        if (document.IsVirtual || !Path.IsPathFullyQualified(document.Path))
            return Refused(PrivateRenameDomainStatus.UnsupportedOwner);

        Token? selectedToken = null;
        foreach (Token token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (token.Type != TokenType.PRIVATE_IDENTIFIER || !token.Span.Contains(offset)) continue;
            if (selectedToken is not null) return Refused(PrivateRenameDomainStatus.IncompleteSyntax);
            selectedToken = token;
        }
        if (selectedToken is null || !_tokens.TryGetValue(selectedToken, out var selectedOccurrence) ||
            !ReferenceEquals(selectedOccurrence.Document, document) || !selectedOccurrence.Resolution.IsResolved)
            return Refused(PrivateRenameDomainStatus.UnsupportedSelection);
        FrozenSourceMemberSymbol selected = selectedOccurrence.Resolution.Candidates[0];
        if (!IsPrivate(selected) || GetClassInfo(selected.DeclaringClassId) is not { } owner ||
            !ReferenceEquals(owner.Document, document) || owner.Owner is not (Stmt.Class or Expr.ClassExpr) ||
            owner.IsNestedPrivateEnvironment || owner.ContainsNestedClass)
            return Refused(PrivateRenameDomainStatus.UnsupportedOwner);

        var classViews = syntax.GetRecords(owner.Owner).Where(record => record.IsAuthoritative &&
            record.Kind == EditorSyntaxKind.Class && record.Role == EditorSyntaxRole.Whole)
            .Select(record => record.Span).Distinct().ToArray();
        if (classViews.Length != 1 || !classViews[0].Contains(selectedToken.Span))
            return Refused(PrivateRenameDomainStatus.IncompleteSyntax);
        SourceSpan classSpan = classViews[0];
        bool hasClassBody = false;
        foreach (EditorSyntaxRecord record in syntax.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!classSpan.Contains(record.Span)) continue;
            if (record.Origin == EditorSyntaxOrigin.Recovered)
                return Refused(PrivateRenameDomainStatus.IncompleteSyntax);
            if (!record.IsAuthoritative) continue;
            if (record.Kind == EditorSyntaxKind.Class && record.Role == EditorSyntaxRole.Whole &&
                !ReferenceEquals(record.Node, owner.Owner))
                return Refused(PrivateRenameDomainStatus.UnsupportedOwner);
            if (record.Role != EditorSyntaxRole.Body ||
                record.Kind is not (EditorSyntaxKind.Class or EditorSyntaxKind.Function or EditorSyntaxKind.Block)) continue;
            if (ReferenceEquals(record.Node, owner.Owner)) hasClassBody = true;
            if (!editorFacts.HasEnteredSourceBody(document, record.Node, record.Span))
                return Refused(PrivateRenameDomainStatus.UnvisitedBody);
        }
        if (!hasClassBody) return Refused(PrivateRenameDomainStatus.IncompleteSyntax);

        var privateMembers = new List<FrozenSourceMemberSymbol>();
        var declarationNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (FrozenSourceMemberSymbol symbol in Symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (symbol.DeclaringClassId != owner.DeclarationId || !IsPrivate(symbol)) continue;
            if (symbol.Kind is not (SourceMemberKind.Field or SourceMemberKind.Method) ||
                symbol.Declarations.Count != 1 || symbol.Name == "#constructor" ||
                !declarationNames.Add(symbol.Name))
                return Refused(PrivateRenameDomainStatus.UnsupportedDeclarations);
            SourceMemberDeclaration declaration = symbol.Declarations[0];
            if (!ReferenceEquals(declaration.Document, document) || !classSpan.Contains(declaration.Name.Span) ||
                declaration.Name.Type != TokenType.PRIVATE_IDENTIFIER ||
                declaration.Owner is not (Stmt.Field { IsPrivate: true } or Stmt.Function { IsPrivate: true, Body: not null }))
                return Refused(PrivateRenameDomainStatus.UnsupportedDeclarations);
            privateMembers.Add(symbol);
        }
        var privateMemberSet = new HashSet<FrozenSourceMemberSymbol>(privateMembers, ReferenceEqualityComparer.Instance);
        if (!privateMemberSet.Contains(selected))
            return Refused(PrivateRenameDomainStatus.UnsupportedSelection);

        var audited = new HashSet<Token>(ReferenceEqualityComparer.Instance);
        var edits = new List<Token>();
        int previousEnd = classSpan.Start;
        foreach (Token token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (token.Type != TokenType.PRIVATE_IDENTIFIER || !classSpan.Contains(token.Span)) continue;
            if (token.Start < previousEnd || token.End > document.Text.Length || token.Lexeme.Length < 2 ||
                token.Lexeme[0] != '#' || !document.Text.AsSpan(token.Start, token.Lexeme.Length).SequenceEqual(token.Lexeme) ||
                !_tokens.TryGetValue(token, out var occurrence) || !ReferenceEquals(occurrence.Document, document) ||
                !occurrence.Resolution.IsResolved)
                return Refused(PrivateRenameDomainStatus.UnresolvedOccurrence);
            FrozenSourceMemberSymbol symbol = occurrence.Resolution.Candidates[0];
            if (!IsPrivate(symbol) || symbol.DeclaringClassId != owner.DeclarationId ||
                !privateMemberSet.Contains(symbol))
                return Refused(PrivateRenameDomainStatus.UnresolvedOccurrence);
            previousEnd = token.End;
            audited.Add(token);
            if (ReferenceEquals(symbol, selected)) edits.Add(token);
        }
        foreach (FrozenSourceMemberSymbol symbol in privateMembers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!audited.Contains(symbol.Declarations[0].Name))
                return Refused(PrivateRenameDomainStatus.IncompleteSyntax);
        }
        foreach (FrozenMemberOccurrence occurrence in Occurrences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(occurrence.Document, document) ||
                occurrence.Name.Type != TokenType.PRIVATE_IDENTIFIER || !classSpan.Contains(occurrence.Name.Span)) continue;
            if (!audited.Contains(occurrence.Name)) return Refused(PrivateRenameDomainStatus.IncompleteSyntax);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(PrivateRenameDomainStatus.Available, new(owner, selected, classSpan, selectedToken,
            edits.ToImmutableArray(), privateMembers.OrderBy(symbol => symbol.Declarations[0].Name.Start).ToImmutableArray()));
    }

    private static bool IsPrivate(FrozenSourceMemberSymbol symbol) =>
        symbol.Facet is MemberFacet.PrivateInstance or MemberFacet.PrivateStatic;
}
