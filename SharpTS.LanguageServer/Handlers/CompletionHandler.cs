using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using SharpTS.LanguageServer.Services;

namespace SharpTS.LanguageServer.Handlers;

/// <summary>Completion of the builtin SharpTS decorators after an <c>@</c>.</summary>
public sealed class CompletionHandler : CompletionHandlerBase
{
    private readonly DocumentStore _store;
    private readonly DecoratorService _decorators;

    public CompletionHandler(DocumentStore store, DecoratorService decorators)
    {
        _store = store;
        _decorators = decorators;
    }

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken ct)
    {
        if (!_store.TryGetSnapshot(
                request.TextDocument.Uri.ToString(),
                out DocumentSnapshot? snapshot))
            return Task.FromResult(new CompletionList());

        var list = _decorators.Completion(
            snapshot.Text,
            request.Position.Line,
            request.Position.Character);
        return Task.FromResult(list ?? new CompletionList());
    }

    // No resolve step needed — items are fully populated up front.
    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken ct)
        => Task.FromResult(request);

    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("typescript", "typescriptreact"),
            TriggerCharacters = new[] { "@" }
        };
}
