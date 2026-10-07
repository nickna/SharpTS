using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer.Services;
using SharpTS.LanguageServer.Project;

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
    private readonly AnalysisMetadataProvider? _metadata;

    public DefinitionHandler(DocumentStore store, DefinitionService definitions, GuiContractService? gui = null,
        AnalysisMetadataProvider? metadata = null)
    {
        _store = store;
        _definitions = definitions;
        _gui = gui ?? new GuiContractService();
        _metadata = metadata;
    }

    public override async Task<LocationOrLocationLinks?> Handle(
        DefinitionParams request,
        CancellationToken ct)
    {
        string uri = request.TextDocument.Uri.ToString();
        if (!_store.TryCapture(uri, out DocumentRequestSnapshot? snapshot))
            return null;

        ct.ThrowIfCancellationRequested();
        using var guard = new EditorRequestGuard(_store, snapshot, _metadata, ct);

        Location? guiLocation = _gui.Definition(snapshot.Document.FilePath, snapshot.Document.Text,
            request.Position.Line, request.Position.Character);
        if (guiLocation is not null)
        {
            ct.ThrowIfCancellationRequested();
            return guard.IsCurrent(ct)
                ? new LocationOrLocationLinks(new[] { new LocationOrLocationLink(guiLocation) }) : null;
        }

        var result = await _definitions.FindDefinitionsAsync(snapshot, request.Position, ct);
        ct.ThrowIfCancellationRequested();
        if (!guard.IsCurrent(ct) || !result.IsCurrent(ct))
            return null;
        return new LocationOrLocationLinks(result.Locations.Select(location => new LocationOrLocationLink(location)));
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(
        DefinitionCapability capability,
        ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("typescript", "typescriptreact"),
        };
}
