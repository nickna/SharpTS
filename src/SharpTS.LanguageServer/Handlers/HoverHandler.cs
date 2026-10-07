using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using SharpTS.LanguageServer.Services;
using SharpTS.LanguageServer.Project;

namespace SharpTS.LanguageServer.Handlers;

/// <summary>Interop hover in both modes, with shared semantic fallback in full mode.</summary>
public sealed class HoverHandler : HoverHandlerBase
{
    private readonly DocumentStore _store;
    private readonly DecoratorService _decorators;
    private readonly MemberHoverService _members;
    private readonly GuiContractService _gui;
    private readonly AnalysisMetadataProvider? _metadata;
    private readonly SemanticHoverService? _semantic;
    private MarkupKind _format = MarkupKind.PlainText;

    public HoverHandler(DocumentStore store, DecoratorService decorators, MemberHoverService members,
        GuiContractService? gui = null, AnalysisMetadataProvider? metadata = null,
        SemanticHoverService? semantic = null)
    {
        _store = store;
        _decorators = decorators;
        _members = members;
        _gui = gui ?? new GuiContractService();
        _metadata = metadata;
        _semantic = semantic;
    }

    public override async Task<Hover?> Handle(HoverParams request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_store.TryCapture(
                request.TextDocument.Uri.ToString(),
                out DocumentRequestSnapshot? capture))
            return null;
        using var guard = new EditorRequestGuard(_store, capture, _metadata, ct);
        DocumentSnapshot snapshot = capture.Document;

        int line = request.Position.Line, ch = request.Position.Character;
        // Preserve the existing priority without starting shared analysis for a cheap result.
        Hover? result =
            _gui.Hover(snapshot.FilePath, snapshot.Text, line, ch) ??
            _decorators.Hover(snapshot.Text, line, ch) ??
            (_semantic is null ? _members.Hover(snapshot.Text, line, ch, ct) :
                _members.DeclarationHover(snapshot.Text, line, ch, ct));
        AnalysisValidation? validation = null;
        if (result is null && _semantic is not null)
        {
            var semantic = await _semantic.HoverAsync(capture, request.Position, _format, ct).ConfigureAwait(false);
            result = semantic.Hover;
            validation = semantic.Validation;
        }
        ct.ThrowIfCancellationRequested();
        return guard.IsCurrent(ct) && (validation?.IsCurrent(ct) ?? true)
            ? SemanticHoverService.AdaptMarkup(result, _format) : null;
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability, ClientCapabilities clientCapabilities)
    {
        var preferred = capability?.ContentFormat?.FirstOrDefault(format => format == MarkupKind.Markdown ||
            format == MarkupKind.PlainText);
        _format = preferred == MarkupKind.Markdown ? MarkupKind.Markdown : MarkupKind.PlainText;
        return new() { DocumentSelector = TextDocumentSelector.ForLanguage("typescript", "typescriptreact") };
    }
}
