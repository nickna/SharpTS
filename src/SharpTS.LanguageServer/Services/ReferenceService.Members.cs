using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.LanguageServer.Services;

public sealed partial class ReferenceService
{
    private static async Task<NavigationReferenceResult> FindMemberReferencesAsync(
        AnalysisLease seed, Position position, int offset, bool includeDeclaration,
        IReadOnlyList<string>? workspaceRoots,
        Func<string, AnalysisLease, CancellationToken, Task<AnalysisLease?>> buildWorkspace,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckedNavigationModel selectedModel = seed.Model;
        var validations = new List<AnalysisValidation> { seed.Validation };
        var configPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (selectedModel.Scope.ConfigPath is { } config) configPaths.Add(config);
        NavigationReferenceResult Refused()
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new([], configPaths.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                IsComplete: false, validations.Distinct().ToArray(), IsRenameEligible: false);
        }

        // A local seed is useful for selecting an identity, never a partial workspace result.
        var (line, column) = selectedModel.Document.Lines.ToPosition(offset);
        if (line - 1 != position.Line || column - 1 != position.Character ||
            workspaceRoots is not { Count: > 0 } ||
            !WithinRoots(selectedModel.Document.Path, workspaceRoots)) return Refused();
        var resolution = selectedModel.Members.FindResolution(selectedModel.Document, offset);
        if (!resolution.IsResolved) return Refused();
        FrozenSourceMemberSymbol selected = resolution.Candidates[0];
        var anchors = new HashSet<MemberDeclarationAnchor>(MemberDeclarationAnchorComparer.Instance);
        foreach (var declaration in selected.Declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryMemberAnchor(declaration.Document, declaration.Name, out var anchor) ||
                !WithinRoots(anchor.Path, workspaceRoots)) return Refused();
            anchors.Add(anchor);
        }
        if (anchors.Count == 0) return Refused();

        var leases = new List<AnalysisLease>();
        try
        {
            // Validate every configured discovery domain before collecting any new locations.
            foreach (string path in anchors.Select(anchor => anchor.Path).Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase).ThenBy(path => path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnalysisLease? lease = await buildWorkspace(path, seed, cancellationToken).ConfigureAwait(false);
                if (lease is null) return Refused();
                leases.Add(lease);
                validations.Add(lease.Validation);
                if (lease.Workspace is not { } workspace) return Refused();
                configPaths.UnionWith(workspace.ConfigPaths);
                if (!workspace.IsComplete) return Refused();
            }

            bool matched = false;
            var targets = new Dictionary<string, Dictionary<SourceSpan, MemberReferenceTarget>>(StringComparer.OrdinalIgnoreCase);
            foreach (AnalysisLease lease in leases)
            {
                foreach (CheckedNavigationModel model in lease.Workspace!.Models)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FrozenSourceMemberSymbol? match = null;
                    foreach (FrozenSourceMemberSymbol candidate in model.Members.Symbols)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!MatchesMemberAnchor(candidate, selected, anchors, cancellationToken)) continue;
                        // Distinct nominal owners cannot become one identity merely by matching shape.
                        if (match is not null) return Refused();
                        match = candidate;
                    }
                    // Completeness describes discovery, not whether a failed check visited this class.
                    if (match is null) continue;
                    matched = true;
                    foreach (FrozenMemberOccurrence occurrence in model.Members.GetOccurrences(match, includeDeclaration))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!TryMemberAnchor(occurrence.Document, occurrence.Name, out var target) ||
                            !WithinRoots(target.Path, workspaceRoots)) continue;
                        if (!targets.TryGetValue(target.Path, out var spans)) targets.Add(target.Path, spans = []);
                        spans.TryAdd(occurrence.Name.Span, new(target.Path, occurrence.Document, occurrence.Name));
                    }
                }
            }
            if (!matched) return Refused();
            var locations = new List<Location>();
            foreach (MemberReferenceTarget target in targets.Values.SelectMany(spans => spans.Values)
                .OrderBy(target => target.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(target => target.Path, StringComparer.Ordinal)
                .ThenBy(target => target.Name.Start).ThenBy(target => target.Name.End))
            {
                cancellationToken.ThrowIfCancellationRequested();
                locations.Add(NavigationLocations.From(target.Document, target.Name));
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(locations.ToArray(), configPaths.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                IsComplete: true, validations.Distinct().ToArray(), IsRenameEligible: false);
        }
        finally
        {
            foreach (AnalysisLease lease in leases) lease.Dispose();
        }
    }

    private static bool MatchesMemberAnchor(FrozenSourceMemberSymbol candidate, FrozenSourceMemberSymbol selected,
        HashSet<MemberDeclarationAnchor> anchors, CancellationToken cancellationToken)
    {
        if (candidate.Facet != selected.Facet || candidate.Kind != selected.Kind ||
            !string.Equals(candidate.Name, selected.Name, StringComparison.Ordinal)) return false;
        var declarations = new HashSet<MemberDeclarationAnchor>(MemberDeclarationAnchorComparer.Instance);
        foreach (var declaration in candidate.Declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryMemberAnchor(declaration.Document, declaration.Name, out var anchor)) return false;
            declarations.Add(anchor);
        }
        return declarations.SetEquals(anchors);
    }

    private static bool TryMemberAnchor(SourceDocument document, Token name, out MemberDeclarationAnchor anchor)
    {
        anchor = default;
        if (document.IsVirtual || !Path.IsPathFullyQualified(document.Path) || name.Start < 0 ||
            name.End <= name.Start || name.End > document.Text.Length) return false;
        anchor = new(Path.GetFullPath(document.Path), name.Start, name.End);
        return true;
    }

    private static bool WithinRoots(string path, IReadOnlyList<string> roots) =>
        Path.IsPathFullyQualified(path) && roots.Any(root => IsWithin(path, root));

    private readonly record struct MemberDeclarationAnchor(string Path, int Start, int End);
    private sealed class MemberDeclarationAnchorComparer : IEqualityComparer<MemberDeclarationAnchor>
    {
        public static MemberDeclarationAnchorComparer Instance { get; } = new();
        public bool Equals(MemberDeclarationAnchor left, MemberDeclarationAnchor right) =>
            left.Start == right.Start && left.End == right.End &&
            StringComparer.OrdinalIgnoreCase.Equals(left.Path, right.Path);
        public int GetHashCode(MemberDeclarationAnchor value) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Path), value.Start, value.End);
    }
    private sealed record MemberReferenceTarget(string Path, SourceDocument Document, Token Name);
}
