using System.Diagnostics.CodeAnalysis;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.Parsing;

namespace SharpTS.LanguageServer.Services;

internal sealed record NavigationDefinitionResult(
    IReadOnlyList<Location> Locations,
    AnalysisValidation? Validation = null)
{
    public bool IsCurrent(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Validation?.IsCurrent(cancellationToken) ?? true;
    }
}

/// <summary>Resolves lexical bindings and proven source members to their declarations.</summary>
public sealed class DefinitionService : IDisposable
{
    private readonly SemanticAnalysisService _analysis;
    private readonly bool _ownsAnalysis;

    public DefinitionService(SemanticAnalysisService? analysis = null)
    {
        _analysis = analysis ?? new SemanticAnalysisService();
        _ownsAnalysis = analysis is null;
    }

    [SuppressMessage("Usage", "VSTHRD002", Justification = "Compatibility wrapper for existing synchronous callers.")]
    public IReadOnlyList<Location> FindDefinitions(
        string path, string text, Position position,
        IReadOnlyDictionary<string, string>? openDocuments = null)
    {
        NavigationDefinitionResult result = FindDefinitionsLegacyAsync(path, text, position, openDocuments)
            .GetAwaiter().GetResult();
        return result.IsCurrent() ? result.Locations : [];
    }

    internal async Task<NavigationDefinitionResult> FindDefinitionsAsync(
        DocumentRequestSnapshot capture, Position position, CancellationToken cancellationToken)
    {
        if (capture.Document.FilePath is null)
            return new NavigationDefinitionResult([]);
        using AnalysisLease? lease = await _analysis.GetDocumentAsync(capture, cancellationToken).ConfigureAwait(false);
        return Project(lease, position, cancellationToken);
    }

    private async Task<NavigationDefinitionResult> FindDefinitionsLegacyAsync(
        string path, string text, Position position, IReadOnlyDictionary<string, string>? openDocuments)
    {
        using AnalysisLease? lease = await _analysis.GetDocumentAsync(path, text, openDocuments).ConfigureAwait(false);
        return Project(lease, position, CancellationToken.None);
    }

    private static NavigationDefinitionResult Project(
        AnalysisLease? lease, Position position, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (lease is null) return new NavigationDefinitionResult([]);
        CheckedNavigationModel model = lease.Model;
        int offset = model.Document.Lines.ToOffset((int)position.Line + 1, (int)position.Character + 1);
        var targets = new List<(string Path, SourceDocument Document, Token Name)>();
        var seen = new Dictionary<string, HashSet<SourceSpan>>(StringComparer.OrdinalIgnoreCase);
        var symbols = model.Bindings.FindSymbols(model.Document, offset);
        if (symbols.Count != 0)
        {
            // A lexical binding remains authoritative even if it has no navigable declaration.
            // In particular, import/namespace bindings must not fall through to a same-spelled member.
            foreach (var symbol in symbols)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var declaration in symbol.Declarations)
                    AddTarget(declaration.Document, declaration.Name);
            }
        }
        else
        {
            foreach (var declaration in model.Members.FindDefinitions(model.Document, offset))
                AddTarget(declaration.Document, declaration.Name);
        }

        var locations = new List<Location>(targets.Count);
        foreach (var target in targets.OrderBy(target => target.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.Path, StringComparer.Ordinal)
            .ThenBy(target => target.Name.Start).ThenBy(target => target.Name.End))
        {
            cancellationToken.ThrowIfCancellationRequested();
            locations.Add(NavigationLocations.From(target.Document, target.Name));
        }
        return new NavigationDefinitionResult(locations.ToArray(), lease.Validation);

        void AddTarget(SourceDocument document, Token name)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.IsPathFullyQualified(document.Path) ? Path.GetFullPath(document.Path) : document.Path;
            if (!seen.TryGetValue(path, out var spans)) seen.Add(path, spans = []);
            if (spans.Add(name.Span)) targets.Add((path, document, name));
        }
    }

    public void Dispose()
    {
        if (_ownsAnalysis) _analysis.Dispose();
    }
}
