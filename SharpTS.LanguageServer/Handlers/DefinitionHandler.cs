using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer.Services;

namespace SharpTS.LanguageServer.Handlers;

/// <summary>
/// Serves <c>textDocument/definition</c> for checker-resolved value and type bindings.
/// </summary>
/// <remarks>
/// Registered only in <see cref="LanguageFeatureMode.Full"/> so the VS Code extension continues
/// leaving ordinary TypeScript navigation exclusively to <c>tsserver</c>.
/// </remarks>
public sealed class DefinitionHandler : DefinitionHandlerBase
{
    private readonly DocumentStore _store;
    private readonly DefinitionService _definitions;

    public DefinitionHandler(DocumentStore store, DefinitionService definitions)
    {
        _store = store;
        _definitions = definitions;
    }

    public override Task<LocationOrLocationLinks?> Handle(
        DefinitionParams request,
        CancellationToken ct)
    {
        string uri = request.TextDocument.Uri.ToString();
        if (!_store.TryGet(uri, out var text))
            return Task.FromResult<LocationOrLocationLinks?>(null);

        ct.ThrowIfCancellationRequested();

        var locations = _definitions.FindDefinitions(
                request.TextDocument.Uri.GetFileSystemPath(),
                text,
                request.Position,
                _store.SnapshotFileSystemDocuments())
            .Select(location => new LocationOrLocationLink(location));
        return Task.FromResult<LocationOrLocationLinks?>(
            new LocationOrLocationLinks(locations));
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(
        DefinitionCapability capability,
        ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("typescript", "typescriptreact"),
        };
}
