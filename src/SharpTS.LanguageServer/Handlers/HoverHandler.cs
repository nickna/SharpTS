using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using SharpTS.LanguageServer.Services;
using SharpTS.LanguageServer.Project;

namespace SharpTS.LanguageServer.Handlers;

/// <summary>Hover for SharpTS decorators (resolved .NET type + XML doc).</summary>
public sealed class HoverHandler : HoverHandlerBase
{
    private readonly DocumentStore _store;
    private readonly DecoratorService _decorators;
    private readonly MemberHoverService _members;
    private readonly GuiContractService _gui;
    private readonly AnalysisMetadataProvider? _metadata;

    public HoverHandler(DocumentStore store, DecoratorService decorators, MemberHoverService members,
        GuiContractService? gui = null, AnalysisMetadataProvider? metadata = null)
    {
        _store = store;
        _decorators = decorators;
        _members = members;
        _gui = gui ?? new GuiContractService();
        _metadata = metadata;
    }

    public override Task<Hover?> Handle(HoverParams request, CancellationToken ct)
    {
        if (!_store.TryCapture(
                request.TextDocument.Uri.ToString(),
                out DocumentRequestSnapshot? capture))
            return Task.FromResult<Hover?>(null);
        using var guard = new EditorRequestGuard(_store, capture, _metadata, ct);
        DocumentSnapshot snapshot = capture.Document;

        int line = request.Position.Line, ch = request.Position.Character;
        // Decorator hover first (cursor on @DotNetType / a builtin); then .NET member hover.
        Hover? result =
            _gui.Hover(snapshot.FilePath, snapshot.Text, line, ch) ??
            _decorators.Hover(snapshot.Text, line, ch) ??
            _members.Hover(snapshot.Text, line, ch);
        return Task.FromResult(guard.IsCurrent(ct) ? result : null);
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability, ClientCapabilities clientCapabilities)
        => new() { DocumentSelector = TextDocumentSelector.ForLanguage("typescript", "typescriptreact") };
}
