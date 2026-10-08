using System.Diagnostics.CodeAnalysis;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.TypeSystem;

namespace SharpTS.LanguageServer.Services;

internal sealed record NavigationReferenceResult(
    IReadOnlyList<Location> Locations,
    IReadOnlyList<string> ConfigPaths,
    bool IsComplete,
    IReadOnlyList<AnalysisValidation>? Validations = null,
    bool IsRenameEligible = false)
{
    public bool IsCurrent(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Validations is null || Validations.All(validation => validation.IsCurrent(cancellationToken));
    }
}

/// <summary>Finds every checker-bound occurrence of the selected semantic symbol.</summary>
public sealed partial class ReferenceService : IDisposable
{
    private readonly SemanticAnalysisService _analysis;
    private readonly bool _ownsAnalysis;

    public ReferenceService(SemanticAnalysisService? analysis = null)
    {
        _analysis = analysis ?? new SemanticAnalysisService();
        _ownsAnalysis = analysis is null;
    }

    public IReadOnlyList<Location> FindReferences(
        string path, string text, Position position, bool includeDeclaration,
        IReadOnlyDictionary<string, string>? openDocuments = null,
        IReadOnlyList<string>? workspaceRoots = null) =>
        FindReferenceResult(path, text, position, includeDeclaration, openDocuments, workspaceRoots).Locations;

    [SuppressMessage("Usage", "VSTHRD002", Justification = "Compatibility wrapper for existing synchronous callers.")]
    internal NavigationReferenceResult FindReferenceResult(
        string path, string text, Position position, bool includeDeclaration,
        IReadOnlyDictionary<string, string>? openDocuments = null,
        IReadOnlyList<string>? workspaceRoots = null, bool includeDeclarationFacets = false)
    {
        NavigationReferenceResult result = FindLegacyAsync(path, text, position, includeDeclaration,
            openDocuments, workspaceRoots, includeDeclarationFacets).GetAwaiter().GetResult();
        return result.IsCurrent() ? result : result with { Locations = [], IsComplete = false };
    }

    internal async Task<NavigationReferenceResult> FindReferenceResultAsync(
        DocumentRequestSnapshot capture, Position position, bool includeDeclaration,
        IReadOnlyList<string>? workspaceRoots = null, bool includeDeclarationFacets = false,
        CancellationToken cancellationToken = default)
    {
        if (capture.Document.FilePath is null)
            return new NavigationReferenceResult([], [], IsComplete: false);
        using AnalysisLease? seed = await _analysis.GetDocumentAsync(capture, cancellationToken).ConfigureAwait(false);
        return await FindCoreAsync(seed, position, includeDeclaration, workspaceRoots, includeDeclarationFacets,
            (anchor, retainedSeed, ct) => _analysis.GetWorkspaceAsync(capture, anchor, retainedSeed,
                workspaceRoots!, ct), cancellationToken).ConfigureAwait(false);
    }

    private async Task<NavigationReferenceResult> FindLegacyAsync(
        string path, string text, Position position, bool includeDeclaration,
        IReadOnlyDictionary<string, string>? openDocuments,
        IReadOnlyList<string>? workspaceRoots, bool includeDeclarationFacets)
    {
        using AnalysisLease? seed = await _analysis.GetDocumentAsync(path, text, openDocuments).ConfigureAwait(false);
        return await FindCoreAsync(seed, position, includeDeclaration, workspaceRoots, includeDeclarationFacets,
            (anchor, retainedSeed, ct) => _analysis.GetWorkspaceAsync(path, text, openDocuments, anchor,
                retainedSeed, workspaceRoots!, ct), CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task<NavigationReferenceResult> FindCoreAsync(
        AnalysisLease? seed, Position position, bool includeDeclaration,
        IReadOnlyList<string>? workspaceRoots, bool includeDeclarationFacets,
        Func<string, AnalysisLease, CancellationToken, Task<AnalysisLease?>> buildWorkspace,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (seed is null) return new NavigationReferenceResult([], [], IsComplete: false);
        CheckedNavigationModel model = seed.Model;
        var validations = new List<AnalysisValidation> { seed.Validation };
        int offset = model.Document.Lines.ToOffset((int)position.Line + 1, (int)position.Character + 1);
        IReadOnlyList<FrozenBindingSymbol> selectedSymbols = model.Bindings.FindSymbols(model.Document, offset);
        if (includeDeclarationFacets)
        {
            var expandedSymbols = new HashSet<FrozenBindingSymbol>(selectedSymbols, ReferenceEqualityComparer.Instance);
            foreach (FrozenBindingSymbol symbol in selectedSymbols)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (BindingDeclaration declaration in symbol.Declarations)
                    expandedSymbols.UnionWith(model.Bindings.FindSymbols(declaration.Document, declaration.Name.Start));
            }
            selectedSymbols = expandedSymbols.ToArray();
        }
        if (selectedSymbols.Count == 0)
            return await FindMemberReferencesAsync(seed, position, offset, includeDeclaration, workspaceRoots,
                buildWorkspace, cancellationToken).ConfigureAwait(false);

        bool isRenameEligible = selectedSymbols.All(symbol =>
            symbol.RenameEligibility == BindingRenameEligibility.AllowedLexical);

        var locations = new Dictionary<LocationKey, Location>();
        AddOccurrences(locations, model.Bindings.FindReferences(selectedSymbols, includeDeclaration), cancellationToken);
        if (workspaceRoots is not { Count: > 0 })
            return new NavigationReferenceResult(Sort(locations.Values),
                model.Scope.ConfigPath is null ? [] : [model.Scope.ConfigPath], model.Scope.IsComplete, validations,
                isRenameEligible);

        BindingAnchor[] anchors = selectedSymbols.SelectMany(symbol => symbol.Declarations.Select(declaration =>
            new BindingAnchor(Path.GetFullPath(declaration.Document.Path), declaration.Name.Start, symbol.Namespace)))
            .Distinct().ToArray();
        bool isComplete = anchors.Length > 0 && anchors.All(anchor => workspaceRoots.Any(root => IsWithin(anchor.Path, root)));
        var configPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pathAnchors in anchors.GroupBy(anchor => anchor.Path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using AnalysisLease? workspaceLease = await buildWorkspace(pathAnchors.Key, seed, cancellationToken)
                .ConfigureAwait(false);
            if (workspaceLease?.Workspace is not { } workspace)
            {
                isComplete = false;
                continue;
            }
            validations.Add(workspaceLease.Validation);
            isComplete &= workspace.IsComplete;
            configPaths.UnionWith(workspace.ConfigPaths);
            foreach (CheckedNavigationModel projectModel in workspace.Models)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var matchingSymbols = new HashSet<FrozenBindingSymbol>(ReferenceEqualityComparer.Instance);
                foreach (BindingAnchor anchor in pathAnchors)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (FrozenBindingSymbol candidate in projectModel.Bindings.FindSymbols(projectModel.Document, anchor.Offset))
                    {
                        if (candidate.Namespace == anchor.Namespace && candidate.Declarations.Any(declaration =>
                            declaration.Name.Start == anchor.Offset && string.Equals(
                                Path.GetFullPath(declaration.Document.Path), anchor.Path, StringComparison.OrdinalIgnoreCase)))
                            matchingSymbols.Add(candidate);
                    }
                }
                isRenameEligible &= matchingSymbols.All(symbol =>
                    symbol.RenameEligibility == BindingRenameEligibility.AllowedLexical);
                AddOccurrences(locations, projectModel.Bindings.FindReferences(matchingSymbols.ToArray(), includeDeclaration),
                    cancellationToken);
            }
        }
        return new NavigationReferenceResult(Sort(locations.Values),
            configPaths.Order(StringComparer.OrdinalIgnoreCase).ToArray(), isComplete, validations.Distinct().ToArray(),
            isRenameEligible);
    }

    private static void AddOccurrences(Dictionary<LocationKey, Location> locations,
        IReadOnlyList<BindingOccurrence> occurrences, CancellationToken cancellationToken)
    {
        foreach (BindingOccurrence occurrence in occurrences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Location location = NavigationLocations.From(occurrence.Document, occurrence.Name);
            locations[LocationKey.From(location)] = location;
        }
    }

    private static IReadOnlyList<Location> Sort(IEnumerable<Location> locations) =>
        locations.OrderBy(location => location.Uri.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(location => location.Range.Start.Line).ThenBy(location => location.Range.Start.Character).ToArray();

    private static bool IsWithin(string path, string root)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return !Path.IsPathRooted(relative) && !relative.Equals("..", StringComparison.Ordinal) &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (_ownsAnalysis) _analysis.Dispose();
    }

    private sealed record BindingAnchor(string Path, int Offset, BindingNamespace Namespace);

    private readonly record struct LocationKey(string Uri, int StartLine, int StartCharacter, int EndLine, int EndCharacter)
    {
        public static LocationKey From(Location location) => new(location.Uri.ToString(),
            (int)location.Range.Start.Line, (int)location.Range.Start.Character,
            (int)location.Range.End.Line, (int)location.Range.End.Character);
    }
}
