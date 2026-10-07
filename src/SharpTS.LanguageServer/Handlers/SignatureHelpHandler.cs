using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using SharpTS.LanguageServer.Services;
using SharpTS.LanguageServer.Project;

namespace SharpTS.LanguageServer.Handlers;

/// <summary>Signature help for builtin SharpTS decorator calls.</summary>
public sealed class SignatureHelpHandler : SignatureHelpHandlerBase
{
    private readonly DocumentStore _store;
    private readonly DecoratorService _decorators;
    private readonly AnalysisMetadataProvider? _metadata;

    public SignatureHelpHandler(DocumentStore store, DecoratorService decorators, AnalysisMetadataProvider? metadata = null)
    {
        _store = store;
        _decorators = decorators;
        _metadata = metadata;
    }

    public override Task<SignatureHelp?> Handle(SignatureHelpParams request, CancellationToken ct)
    {
        if (!_store.TryCapture(
                request.TextDocument.Uri.ToString(),
                out DocumentRequestSnapshot? capture))
            return Task.FromResult<SignatureHelp?>(null);
        using var guard = new EditorRequestGuard(_store, capture, _metadata, ct);

        SignatureHelp? result = _decorators.SignatureHelp(
                capture.Document.Text,
                request.Position.Line,
                request.Position.Character);
        return Task.FromResult(guard.IsCurrent(ct) ? result : null);
    }

    protected override SignatureHelpRegistrationOptions CreateRegistrationOptions(
        SignatureHelpCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("typescript", "typescriptreact"),
            TriggerCharacters = new[] { "(", "," }
        };
}
