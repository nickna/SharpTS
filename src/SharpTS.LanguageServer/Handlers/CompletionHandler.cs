using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using SharpTS.LanguageServer.Services;
using SharpTS.LanguageServer.Project;

namespace SharpTS.LanguageServer.Handlers;

/// <summary>Completion of the builtin SharpTS decorators after an <c>@</c>.</summary>
public sealed class CompletionHandler : CompletionHandlerBase
{
    private readonly DocumentStore _store;
    private readonly DecoratorService _decorators;
    private readonly GuiContractService _gui;
    private readonly AnalysisMetadataProvider? _metadata;

    public CompletionHandler(DocumentStore store, DecoratorService decorators, GuiContractService? gui = null,
        AnalysisMetadataProvider? metadata = null)
    {
        _store = store;
        _decorators = decorators;
        _gui = gui ?? new GuiContractService();
        _metadata = metadata;
    }

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken ct)
    {
        if (!_store.TryCapture(
                request.TextDocument.Uri.ToString(),
                out DocumentRequestSnapshot? capture))
            return Task.FromResult(new CompletionList());
        using var guard = new EditorRequestGuard(_store, capture, _metadata, ct);
        DocumentSnapshot snapshot = capture.Document;

        var list = _gui.Completion(snapshot.FilePath, snapshot.Text, request.Position.Line, request.Position.Character) ??
            _decorators.Completion(
            snapshot.Text,
            request.Position.Line,
            request.Position.Character);
        return Task.FromResult(guard.IsCurrent(ct) ? list ?? new CompletionList() : new CompletionList());
    }

    // No resolve step needed — items are fully populated up front.
    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken ct)
        => Task.FromResult(request);

    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("typescript", "typescriptreact"),
            TriggerCharacters = new[] { "@", "<", " ", "=", "\"" }
        };
}
