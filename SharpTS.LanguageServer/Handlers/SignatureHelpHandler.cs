using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using SharpTS.LanguageServer.Services;

namespace SharpTS.LanguageServer.Handlers;

/// <summary>Signature help for builtin SharpTS decorator calls.</summary>
public sealed class SignatureHelpHandler : SignatureHelpHandlerBase
{
    private readonly DocumentStore _store;
    private readonly DecoratorService _decorators;

    public SignatureHelpHandler(DocumentStore store, DecoratorService decorators)
    {
        _store = store;
        _decorators = decorators;
    }

    public override Task<SignatureHelp?> Handle(SignatureHelpParams request, CancellationToken ct)
    {
        if (!_store.TryGetSnapshot(
                request.TextDocument.Uri.ToString(),
                out DocumentSnapshot? snapshot))
            return Task.FromResult<SignatureHelp?>(null);

        return Task.FromResult(
            _decorators.SignatureHelp(
                snapshot.Text,
                request.Position.Line,
                request.Position.Character));
    }

    protected override SignatureHelpRegistrationOptions CreateRegistrationOptions(
        SignatureHelpCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("typescript", "typescriptreact"),
            TriggerCharacters = new[] { "(", "," }
        };
}
