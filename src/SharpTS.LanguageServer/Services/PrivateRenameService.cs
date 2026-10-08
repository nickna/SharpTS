using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.LanguageServer.Services;

internal sealed record NavigationPrivateRenameResult<T>(T? Value, bool IsPrivateContext = false,
    AnalysisValidation? Validation = null) where T : class
{
    public bool IsCurrent(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Validation?.IsCurrent(cancellationToken) ?? true;
    }
}

/// <summary>Renames only one proven ECMAScript private lexical domain in an open source capture.</summary>
public sealed class PrivateRenameService(SemanticAnalysisService analysis)
{
    internal Task<NavigationPrivateRenameResult<RangeOrPlaceholderRange>> PrepareAsync(
        DocumentRequestSnapshot capture, Position position, CancellationToken cancellationToken) =>
        QueryAsync(capture, position, domain => new RangeOrPlaceholderRange(new PlaceholderRange
        {
            Range = NavigationLocations.From(domain.Owner.Document, domain.SelectedToken).Range,
            Placeholder = domain.SelectedToken.Lexeme,
        }), cancellationToken);

    internal Task<NavigationPrivateRenameResult<WorkspaceEdit>> RenameAsync(
        DocumentRequestSnapshot capture, Position position, string newName, CancellationToken cancellationToken)
    {
        return QueryAsync(capture, position, domain =>
        {
            string? replacement = NormalizePrivateIdentifier(newName, cancellationToken);
            return replacement is null ? null : CreateEdit(capture, domain, replacement, cancellationToken);
        }, cancellationToken);
    }

    private async Task<NavigationPrivateRenameResult<T>> QueryAsync<T>(DocumentRequestSnapshot capture,
        Position position, Func<PrivateRenameDomain, T?> project, CancellationToken cancellationToken) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasKnownOpenVersion(capture)) return new(null);
        var lines = new LineIndex(capture.Document.Text);
        int offset = lines.ToOffset(position.Line + 1, position.Character + 1);
        var (line, column) = lines.ToPosition(offset);
        if (line - 1 != position.Line || column - 1 != position.Character) return new(null);
        if (!MaySelectPrivateIdentifier(capture.Document.Text, offset, cancellationToken)) return new(null);

        using AnalysisLease? lease = await analysis.GetDocumentAsync(capture, cancellationToken).ConfigureAwait(false);
        if (lease is null) return new(null);
        CheckedNavigationModel model = lease.Model;
        if (model.Bindings.FindSymbols(model.Document, offset).Count != 0)
            return new(null, Validation: lease.Validation);
        if (!model.Snapshot.TryGetDocument(model.Document.Path, out AnalysisDocument? parsed) ||
            parsed is null || !ReferenceEquals(parsed.Document, model.Document) ||
            !string.Equals(model.Document.Text, capture.Document.Text, StringComparison.Ordinal))
            return new(null, Validation: lease.Validation);

        bool isPrivate = false;
        foreach (Token token in parsed.Tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (token.Type != TokenType.PRIVATE_IDENTIFIER || !token.Span.Contains(offset)) continue;
            isPrivate = true;
            break;
        }
        if (!isPrivate) return new(null, Validation: lease.Validation);
        PrivateRenameDomainResult proof = model.Members.GetPrivateRenameDomain(model.Document, offset,
            parsed.Tokens, model.EditorFacts, parsed.HasRecoveredSyntax, cancellationToken);
        if (!proof.IsAvailable || proof.Domain is null || !lease.IsCurrent(cancellationToken))
            return new(null, IsPrivateContext: true, Validation: lease.Validation);
        T? value = project(proof.Domain);
        cancellationToken.ThrowIfCancellationRequested();
        return new(lease.IsCurrent(cancellationToken) ? value : null, IsPrivateContext: true, Validation: lease.Validation);
    }

    private static bool HasKnownOpenVersion(DocumentRequestSnapshot capture) =>
        capture.Document.FilePath is { } path &&
        capture.FileSystemDocuments.TryGetValue(Path.GetFullPath(path), out DocumentSnapshot? open) &&
        open.Version == capture.Document.Version &&
        string.Equals(open.Uri, capture.Document.Uri, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(open.Text, capture.Document.Text, StringComparison.Ordinal);

    // Admission only: short ordinary identifiers bypass analysis. Longer runs stay admitted
    // conservatively; captured lexer tokens and the frozen domain remain the sole authority.
    private static bool MaySelectPrivateIdentifier(string text, int offset, CancellationToken cancellationToken)
    {
        if (offset < 0 || offset >= text.Length) return false;
        int cursor = offset;
        for (int scanned = 0; scanned < 256; scanned++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            char current = text[cursor];
            if (current == '#') return cursor + 1 < text.Length &&
                (char.IsLetter(text[cursor + 1]) || text[cursor + 1] is '_' or '$');
            if (!(char.IsLetterOrDigit(current) || current is '_' or '$') || cursor == 0) return false;
            cursor--;
        }
        return true;
    }

    private static WorkspaceEdit? CreateEdit(DocumentRequestSnapshot capture, PrivateRenameDomain domain,
        string replacement, CancellationToken cancellationToken)
    {
        foreach (FrozenSourceMemberSymbol member in domain.PrivateMembers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(member, domain.Symbol) && string.Equals(member.Name, replacement, StringComparison.Ordinal))
                return null;
        }

        Token[] tokens = domain.Tokens.GroupBy(token => token.Span).Select(group => group.First())
            .OrderBy(token => token.Start).ThenBy(token => token.End).ToArray();
        if (tokens.Length == 0) return null;
        var edits = new List<TextEdit>(tokens.Length);
        int previousEnd = -1;
        foreach (Token token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (token.Start < previousEnd || token.Start < 0 || token.End > capture.Document.Text.Length ||
                !capture.Document.Text.AsSpan(token.Start, token.Span.Length).SequenceEqual(token.Lexeme))
                return null;
            previousEnd = token.End;
            edits.Add(new TextEdit
            {
                Range = NavigationLocations.From(domain.Owner.Document, token).Range,
                NewText = replacement,
            });
        }
        return new WorkspaceEdit
        {
            DocumentChanges = new Container<WorkspaceEditDocumentChange>(new WorkspaceEditDocumentChange(new TextDocumentEdit
            {
                TextDocument = new OptionalVersionedTextDocumentIdentifier
                {
                    Uri = DocumentUri.Parse(capture.Document.Uri),
                    Version = capture.Document.Version,
                },
                Edits = new TextEditContainer(edits),
            })),
        };
    }

    private static string? NormalizePrivateIdentifier(string candidate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(candidate)) return null;
        string name = candidate[0] == '#' ? candidate : "#" + candidate;
        if (string.Equals(name, "#constructor", StringComparison.Ordinal)) return null;
        try
        {
            List<Stmt> statements = new Parser(new Lexer($"class RenameProbe {{ {name}; }}")
                    .WithCancellation(cancellationToken).ScanTokens())
                .WithCancellation(cancellationToken).ParseOrThrow();
            return statements is [Stmt.Class { Fields: [Stmt.Field { IsPrivate: true, Name.Lexeme: var parsedName }] }] &&
                string.Equals(parsedName, name, StringComparison.Ordinal) ? name : null;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }
}
