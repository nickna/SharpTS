using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.Parsing;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SharpTS.LanguageServer.Services;

/// <summary>Produces renames only when the complete affected project graph is known.</summary>
public sealed class RenameService
{
    private readonly ReferenceService _references;
    public RenameService(ReferenceService references) => _references = references;

    public LspRange? Prepare(string path, string text, Position position,
        IReadOnlyDictionary<string, string>? openDocuments = null, IReadOnlyList<string>? workspaceRoots = null)
    {
        NavigationReferenceResult result = _references.FindReferenceResult(path, text, position,
            includeDeclaration: true, openDocuments, workspaceRoots, includeDeclarationFacets: true);
        return result.IsCurrent() ? PrepareValue(path, position, result) : null;
    }

    internal async Task<(LspRange? Value, NavigationReferenceResult Domain)> PrepareAsync(
        DocumentRequestSnapshot capture, Position position, IReadOnlyList<string>? workspaceRoots,
        CancellationToken cancellationToken)
    {
        NavigationReferenceResult result = await _references.FindReferenceResultAsync(capture, position,
            includeDeclaration: true, workspaceRoots, includeDeclarationFacets: true, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return (capture.Document.FilePath is null ? null : PrepareValue(capture.Document.FilePath, position, result), result);
    }

    public WorkspaceEdit? Rename(string path, string text, Position position, string newName,
        IReadOnlyDictionary<string, string>? openDocuments = null, IReadOnlyList<string>? workspaceRoots = null)
    {
        if (!IsBindingIdentifier(newName, CancellationToken.None)) return null;
        NavigationReferenceResult result = _references.FindReferenceResult(path, text, position,
            includeDeclaration: true, openDocuments, workspaceRoots, includeDeclarationFacets: true);
        return result.IsCurrent() ? RenameValue(newName, result) : null;
    }

    internal async Task<(WorkspaceEdit? Value, NavigationReferenceResult Domain)> RenameAsync(
        DocumentRequestSnapshot capture, Position position, string newName,
        IReadOnlyList<string>? workspaceRoots, CancellationToken cancellationToken)
    {
        if (!IsBindingIdentifier(newName, cancellationToken))
            return (null, new NavigationReferenceResult([], [], IsComplete: false));
        NavigationReferenceResult result = await _references.FindReferenceResultAsync(capture, position,
            includeDeclaration: true, workspaceRoots, includeDeclarationFacets: true, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return (RenameValue(newName, result), result);
    }

    private static LspRange? PrepareValue(string path, Position position, NavigationReferenceResult result)
    {
        if (!result.IsComplete || !result.IsRenameEligible || result.Locations.Count == 0) return null;
        DocumentUri currentUri = DocumentUri.FromFileSystemPath(Path.GetFullPath(path));
        return result.Locations.Where(location => location.Uri == currentUri)
            .Select(location => location.Range).FirstOrDefault(range => Contains(range, position));
    }

    private static WorkspaceEdit? RenameValue(string newName, NavigationReferenceResult result)
    {
        if (!result.IsComplete || !result.IsRenameEligible || result.Locations.Count == 0) return null;
        return new WorkspaceEdit
        {
            Changes = result.Locations.GroupBy(location => location.Uri).ToDictionary(group => group.Key,
                group => group.OrderByDescending(location => location.Range.Start.Line)
                    .ThenByDescending(location => location.Range.Start.Character).Select(location => new TextEdit
                    {
                        Range = location.Range,
                        NewText = newName,
                    }).AsEnumerable()),
        };
    }

    private static bool Contains(LspRange range, Position position)
    {
        bool startsBefore = position.Line > range.Start.Line ||
            position.Line == range.Start.Line && position.Character >= range.Start.Character;
        bool endsAfter = position.Line < range.End.Line ||
            position.Line == range.End.Line && position.Character < range.End.Character;
        return startsBefore && endsAfter;
    }

    private static bool IsBindingIdentifier(string candidate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(candidate)) return false;
        try
        {
            List<Stmt> statements = new Parser(new Lexer($"let {candidate};")
                    .WithCancellation(cancellationToken).ScanTokens())
                .WithCancellation(cancellationToken).ParseOrThrow();
            return statements is [Stmt.Var { Name.Lexeme: var parsedName, Initializer: null }] &&
                string.Equals(parsedName, candidate, StringComparison.Ordinal);
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }
}
