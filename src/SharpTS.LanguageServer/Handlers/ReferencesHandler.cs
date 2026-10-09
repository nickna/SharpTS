using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer.Services;

namespace SharpTS.LanguageServer.Handlers;

/// <summary>
/// Serves semantic references for checker-bound value and type identities.
/// </summary>
/// <remarks>
/// Registered only in <see cref="LanguageFeatureMode.Full"/> so the VS Code extension continues
/// leaving ordinary TypeScript navigation exclusively to <c>tsserver</c>.
/// </remarks>
public sealed class ReferencesHandler : ReferencesHandlerBase
{
    private readonly DocumentStore _store;
    private readonly ReferenceService _references;
    private readonly NavigationWorkspaceContext? _workspace;

    public ReferencesHandler(
        DocumentStore store,
        ReferenceService references,
        NavigationWorkspaceContext? workspace = null)
    {
        _store = store;
        _references = references;
        _workspace = workspace;
    }

    public override async Task<LocationContainer?> Handle(
        ReferenceParams request,
        CancellationToken ct)
    {
        string uri = request.TextDocument.Uri.ToString();
        if (!_store.TryCapture(uri, out DocumentRequestSnapshot? snapshot))
            return null;

        ct.ThrowIfCancellationRequested();

        long? workspaceVersion = _workspace?.Version;
        var result = await _references.FindReferenceResultAsync(
            snapshot,
            request.Position,
            request.Context.IncludeDeclaration,
            _workspace?.SnapshotRoots(), cancellationToken: ct);
        ct.ThrowIfCancellationRequested();
        if (!_store.IsCurrent(uri, snapshot.Document.Version, snapshot.WorkspaceVersion) ||
            _workspace?.Version != workspaceVersion || !result.IsCurrent(ct)) return null;
        return new LocationContainer(result.Locations);
    }

    protected override ReferenceRegistrationOptions CreateRegistrationOptions(
        ReferenceCapability capability,
        ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage(
                "typescript",
                "typescriptreact"),
        };
}
