using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.TypeSystem;

namespace SharpTS.LanguageServer.Services;

internal sealed record NavigationReferenceResult(
    IReadOnlyList<Location> Locations,
    IReadOnlyList<string> ConfigPaths,
    bool IsComplete);

/// <summary>
/// Resolves a source position to every checker-bound occurrence of the same semantic symbol.
/// </summary>
public sealed class ReferenceService
{
    public IReadOnlyList<Location> FindReferences(
        string path,
        string text,
        Position position,
        bool includeDeclaration,
        IReadOnlyDictionary<string, string>? openDocuments = null,
        IReadOnlyList<string>? workspaceRoots = null)
        => FindReferenceResult(
            path,
            text,
            position,
            includeDeclaration,
            openDocuments,
            workspaceRoots).Locations;

    internal NavigationReferenceResult FindReferenceResult(
        string path,
        string text,
        Position position,
        bool includeDeclaration,
        IReadOnlyDictionary<string, string>? openDocuments = null,
        IReadOnlyList<string>? workspaceRoots = null,
        bool includeDeclarationFacets = false)
    {
        if (NavigationModelBuilder.TryBuild(path, text, openDocuments) is not { } model)
            return new NavigationReferenceResult([], [], IsComplete: false);

        int offset = model.Document.Lines.ToOffset(
            (int)position.Line + 1,
            (int)position.Character + 1);
        IReadOnlyList<BindingSymbol> selectedSymbols =
            model.Checker.Bindings.FindSymbols(model.Document, offset);
        if (includeDeclarationFacets)
        {
            var expandedSymbols = new HashSet<BindingSymbol>(
                selectedSymbols,
                ReferenceEqualityComparer.Instance);
            foreach (BindingSymbol symbol in selectedSymbols)
            {
                foreach (BindingDeclaration declaration in symbol.Declarations)
                {
                    expandedSymbols.UnionWith(
                        model.Checker.Bindings.FindSymbols(
                            declaration.Document,
                            declaration.Name.Start));
                }
            }

            selectedSymbols = expandedSymbols.ToArray();
        }
        if (selectedSymbols.Count == 0)
        {
            return new NavigationReferenceResult(
                [],
                model.Scope.ConfigPath is null ? [] : [model.Scope.ConfigPath],
                model.Scope.IsComplete);
        }

        var locations = new Dictionary<LocationKey, Location>();
        AddOccurrences(
            locations,
            model.Checker.Bindings.FindReferences(
                selectedSymbols,
                includeDeclaration));

        if (workspaceRoots is not { Count: > 0 })
        {
            return new NavigationReferenceResult(
                Sort(locations.Values),
                model.Scope.ConfigPath is null ? [] : [model.Scope.ConfigPath],
                model.Scope.IsComplete);
        }

        BindingAnchor[] anchors = selectedSymbols
            .SelectMany(symbol => symbol.Declarations.Select(declaration =>
                new BindingAnchor(
                    Path.GetFullPath(declaration.Document.Path),
                    declaration.Name.Start,
                    symbol.Namespace)))
            .Distinct()
            .ToArray();
        bool isComplete = anchors.Length > 0 &&
            anchors.All(anchor => workspaceRoots.Any(
                root => IsWithin(anchor.Path, root)));
        var configPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pathAnchors in anchors.GroupBy(
                     anchor => anchor.Path,
                     StringComparer.OrdinalIgnoreCase))
        {
            string anchorPath = pathAnchors.Key;
            string? anchorText = ReadText(anchorPath, openDocuments);
            if (anchorText is null)
            {
                isComplete = false;
                continue;
            }

            CheckedNavigationWorkspace workspace =
                NavigationModelBuilder.BuildWorkspace(
                    anchorPath,
                    anchorText,
                    openDocuments,
                    workspaceRoots);
            isComplete &= workspace.IsComplete;
            configPaths.UnionWith(workspace.ConfigPaths);

            foreach (CheckedNavigationModel projectModel in workspace.Models)
            {
                var matchingSymbols = new HashSet<BindingSymbol>(
                    ReferenceEqualityComparer.Instance);
                foreach (BindingAnchor anchor in pathAnchors)
                {
                    foreach (BindingSymbol candidate in
                             projectModel.Checker.Bindings.FindSymbols(
                                 projectModel.Document,
                                 anchor.Offset))
                    {
                        if (candidate.Namespace == anchor.Namespace &&
                            candidate.Declarations.Any(declaration =>
                                declaration.Name.Start == anchor.Offset &&
                                string.Equals(
                                    Path.GetFullPath(declaration.Document.Path),
                                    anchor.Path,
                                    StringComparison.OrdinalIgnoreCase)))
                        {
                            matchingSymbols.Add(candidate);
                        }
                    }
                }

                AddOccurrences(
                    locations,
                    projectModel.Checker.Bindings.FindReferences(
                        matchingSymbols.ToArray(),
                        includeDeclaration));
            }
        }

        return new NavigationReferenceResult(
            Sort(locations.Values),
            configPaths.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            isComplete);
    }

    private static void AddOccurrences(
        Dictionary<LocationKey, Location> locations,
        IReadOnlyList<BindingOccurrence> occurrences)
    {
        foreach (BindingOccurrence occurrence in occurrences)
        {
            Location location = NavigationLocations.From(
                occurrence.Document,
                occurrence.Name);
            locations[LocationKey.From(location)] = location;
        }
    }

    private static IReadOnlyList<Location> Sort(IEnumerable<Location> locations) =>
        locations
            .OrderBy(location => location.Uri.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(location => location.Range.Start.Line)
            .ThenBy(location => location.Range.Start.Character)
            .ToArray();

    private static string? ReadText(
        string path,
        IReadOnlyDictionary<string, string>? openDocuments)
    {
        if (openDocuments is not null)
        {
            foreach (var (documentPath, documentText) in openDocuments)
            {
                if (string.Equals(
                        Path.GetFullPath(documentPath),
                        path,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return documentText;
                }
            }
        }
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static bool IsWithin(string path, string root)
    {
        string relative = Path.GetRelativePath(
            Path.GetFullPath(root),
            Path.GetFullPath(path));
        return !Path.IsPathRooted(relative) &&
            !relative.Equals("..", StringComparison.Ordinal) &&
            !relative.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal);
    }

    private sealed record BindingAnchor(
        string Path,
        int Offset,
        BindingNamespace Namespace);

    private readonly record struct LocationKey(
        string Uri,
        int StartLine,
        int StartCharacter,
        int EndLine,
        int EndCharacter)
    {
        public static LocationKey From(Location location) =>
            new(
                location.Uri.ToString(),
                (int)location.Range.Start.Line,
                (int)location.Range.Start.Character,
                (int)location.Range.End.Line,
                (int)location.Range.End.Character);
    }
}
