using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using SharpTS.LanguageServer.Services;

namespace SharpTS.LanguageServer.Handlers;

/// <summary>Hover for SharpTS decorators (resolved .NET type + XML doc).</summary>
public sealed class HoverHandler : HoverHandlerBase
{
    private readonly DocumentStore _store;
    private readonly DecoratorService _decorators;
    private readonly MemberHoverService _members;
    private readonly GuiContractService _gui;

    public HoverHandler(DocumentStore store, DecoratorService decorators, MemberHoverService members, GuiContractService? gui = null)
    {
        _store = store;
        _decorators = decorators;
        _members = members;
        _gui = gui ?? new GuiContractService();
    }

    public override Task<Hover?> Handle(HoverParams request, CancellationToken ct)
    {
        if (!_store.TryGetSnapshot(
                request.TextDocument.Uri.ToString(),
                out DocumentSnapshot? snapshot))
            return Task.FromResult<Hover?>(null);

        int line = request.Position.Line, ch = request.Position.Character;
        // Decorator hover first (cursor on @DotNetType / a builtin); then .NET member hover.
        return Task.FromResult(
            _gui.Hover(snapshot.FilePath, snapshot.Text, line, ch) ??
            _decorators.Hover(snapshot.Text, line, ch) ??
            _members.Hover(snapshot.Text, line, ch));
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability, ClientCapabilities clientCapabilities)
        => new() { DocumentSelector = TextDocumentSelector.ForLanguage("typescript", "typescriptreact") };
}
