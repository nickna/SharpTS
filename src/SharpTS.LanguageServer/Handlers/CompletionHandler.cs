using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using SharpTS.LanguageServer.Services;
using SharpTS.LanguageServer.Project;

namespace SharpTS.LanguageServer.Handlers;

/// <summary>SharpTS-specific completion in both modes, with checked semantic fallback in full mode.</summary>
public sealed class CompletionHandler : CompletionHandlerBase
{
    private readonly DocumentStore _store;
    private readonly DecoratorService _decorators;
    private readonly GuiContractService _gui;
    private readonly AnalysisMetadataProvider? _metadata;
    private readonly SemanticCompletionService? _semantic;
    private HashSet<CompletionItemKind>? _supportedKinds;

    public CompletionHandler(DocumentStore store, DecoratorService decorators, GuiContractService? gui = null,
        AnalysisMetadataProvider? metadata = null, SemanticCompletionService? semantic = null)
    {
        _store = store;
        _decorators = decorators;
        _gui = gui ?? new GuiContractService();
        _metadata = metadata;
        _semantic = semantic;
    }

    public override async Task<CompletionList> Handle(CompletionParams request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_store.TryCapture(
                request.TextDocument.Uri.ToString(),
                out DocumentRequestSnapshot? capture))
            return new CompletionList();
        using var guard = new EditorRequestGuard(_store, capture, _metadata, ct);
        DocumentSnapshot snapshot = capture.Document;

        var list = _gui.Completion(snapshot.FilePath, snapshot.Text, request.Position.Line, request.Position.Character) ??
            _decorators.Completion(
            snapshot.Text,
            request.Position.Line,
            request.Position.Character);
        AnalysisValidation? validation = null;
        if (list is null && _semantic is not null)
        {
            var semantic = await _semantic.CompletionAsync(capture, request.Position, ct).ConfigureAwait(false);
            list = semantic.List;
            validation = semantic.Validation;
            if (_supportedKinds is not null)
                list = new CompletionList(list.Items.Select(item => _supportedKinds.Contains(item.Kind) ? item :
                    item with { Kind = _supportedKinds.Contains(CompletionItemKind.Text) ? CompletionItemKind.Text : default }),
                    list.IsIncomplete);
        }
        ct.ThrowIfCancellationRequested();
        return guard.IsCurrent(ct) && (validation?.IsCurrent(ct) ?? true) ? list ?? new CompletionList() : new CompletionList();
    }

    // No resolve step needed — items are fully populated up front.
    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken ct)
        => Task.FromResult(request);

    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability capability, ClientCapabilities clientCapabilities)
    {
        _supportedKinds = capability?.CompletionItemKind?.ValueSet?.ToHashSet();
        return new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("typescript", "typescriptreact"),
            TriggerCharacters = _semantic is null ? new[] { "@", "<", " ", "=", "\"" } :
                new[] { "@", "<", " ", "=", "\"", ".", "?" }
        };
    }
}
