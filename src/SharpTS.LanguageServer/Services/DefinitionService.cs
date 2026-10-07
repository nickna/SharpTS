using System.Diagnostics.CodeAnalysis;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

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

/// <summary>Resolves source positions to checker-bound declarations across a module graph.</summary>
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
        var locations = new List<Location>();
        var seen = new HashSet<(string Path, int Start)>();
        foreach (var symbol in model.Bindings.FindSymbols(model.Document, offset))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var declaration in symbol.Declarations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (seen.Add((declaration.Document.Path, declaration.Name.Start)))
                    locations.Add(NavigationLocations.From(declaration.Document, declaration.Name));
            }
        }
        return new NavigationDefinitionResult(locations.ToArray(), lease.Validation);
    }

    public void Dispose()
    {
        if (_ownsAnalysis) _analysis.Dispose();
    }
}
