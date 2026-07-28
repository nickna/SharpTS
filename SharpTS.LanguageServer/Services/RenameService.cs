using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.Parsing;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SharpTS.LanguageServer.Services;

/// <summary>
/// Produces semantic workspace renames only when the complete affected project graph is known.
/// </summary>
public sealed class RenameService
{
    private readonly ReferenceService _references;

    public RenameService(ReferenceService references)
    {
        _references = references;
    }

    public LspRange? Prepare(
        string path,
        string text,
        Position position,
        IReadOnlyDictionary<string, string>? openDocuments = null,
        IReadOnlyList<string>? workspaceRoots = null)
    {
        NavigationReferenceResult result = FindCompleteDomain(
            path,
            text,
            position,
            openDocuments,
            workspaceRoots);
        if (!result.IsComplete || result.Locations.Count == 0)
            return null;

        DocumentUri currentUri = DocumentUri.FromFileSystemPath(
            Path.GetFullPath(path));
        return result.Locations
            .Where(location => location.Uri == currentUri)
            .Select(location => location.Range)
            .FirstOrDefault(range => Contains(range, position));
    }

    public WorkspaceEdit? Rename(
        string path,
        string text,
        Position position,
        string newName,
        IReadOnlyDictionary<string, string>? openDocuments = null,
        IReadOnlyList<string>? workspaceRoots = null)
    {
        if (!IsBindingIdentifier(newName))
            return null;

        NavigationReferenceResult result = FindCompleteDomain(
            path,
            text,
            position,
            openDocuments,
            workspaceRoots);
        if (!result.IsComplete || result.Locations.Count == 0)
            return null;

        return new WorkspaceEdit
        {
            Changes = result.Locations
                .GroupBy(location => location.Uri)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(location => location.Range.Start.Line)
                        .ThenByDescending(location => location.Range.Start.Character)
                        .Select(location => new TextEdit
                        {
                            Range = location.Range,
                            NewText = newName,
                        })
                        .AsEnumerable()),
        };
    }

    private NavigationReferenceResult FindCompleteDomain(
        string path,
        string text,
        Position position,
        IReadOnlyDictionary<string, string>? openDocuments,
        IReadOnlyList<string>? workspaceRoots)
        => _references.FindReferenceResult(
            path,
            text,
            position,
            includeDeclaration: true,
            openDocuments,
            workspaceRoots);

    private static bool Contains(LspRange range, Position position)
    {
        bool startsBefore =
            position.Line > range.Start.Line ||
            position.Line == range.Start.Line &&
            position.Character >= range.Start.Character;
        bool endsAfter =
            position.Line < range.End.Line ||
            position.Line == range.End.Line &&
            position.Character < range.End.Character;
        return startsBefore && endsAfter;
    }

    private static bool IsBindingIdentifier(string candidate)
    {
        if (string.IsNullOrEmpty(candidate))
            return false;

        try
        {
            List<Stmt> statements = new Parser(
                    new Lexer($"let {candidate};").ScanTokens())
                .ParseOrThrow();
            return statements is
            [
                Stmt.Var
                {
                    Name.Lexeme: var parsedName,
                    Initializer: null,
                },
            ] && string.Equals(
                parsedName,
                candidate,
                StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
