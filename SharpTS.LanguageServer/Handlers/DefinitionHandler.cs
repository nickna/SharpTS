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
    private readonly GuiContractService _gui;

    public DefinitionHandler(DocumentStore store, DefinitionService definitions, GuiContractService? gui = null)
    {
        _store = store;
        _definitions = definitions;
        _gui = gui ?? new GuiContractService();
    }

    public override Task<LocationOrLocationLinks?> Handle(
        DefinitionParams request,
        CancellationToken ct)
    {
        string uri = request.TextDocument.Uri.ToString();
        if (!_store.TryCapture(uri, out DocumentRequestSnapshot? snapshot))
            return Task.FromResult<LocationOrLocationLinks?>(null);

        ct.ThrowIfCancellationRequested();

        Location? guiLocation = _gui.Definition(snapshot.Document.FilePath, snapshot.Document.Text,
            request.Position.Line, request.Position.Character);
        if (guiLocation is not null)
            return Task.FromResult<LocationOrLocationLinks?>(
                new LocationOrLocationLinks(new[] { new LocationOrLocationLink(guiLocation) }));

        var locations = _definitions.FindDefinitions(
                request.TextDocument.Uri.GetFileSystemPath(),
                snapshot.Document.Text,
                request.Position,
                snapshot.TextOverlay)
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
