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

    public override Task<LocationContainer?> Handle(
        ReferenceParams request,
        CancellationToken ct)
    {
        string uri = request.TextDocument.Uri.ToString();
        if (!_store.TryCapture(uri, out DocumentRequestSnapshot? snapshot))
            return Task.FromResult<LocationContainer?>(null);

        ct.ThrowIfCancellationRequested();

        var locations = _references.FindReferences(
            request.TextDocument.Uri.GetFileSystemPath(),
            snapshot.Document.Text,
            request.Position,
            request.Context.IncludeDeclaration,
            snapshot.TextOverlay,
            _workspace?.SnapshotRoots());
        return Task.FromResult<LocationContainer?>(
            new LocationContainer(locations));
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
