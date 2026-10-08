using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using SharpTS.LanguageServer.Services;
using SharpTS.LanguageServer.Project;
using SharpTS.Parsing;

namespace SharpTS.LanguageServer.Handlers;

/// <summary>Decorator help in both modes, with shared call signatures in full mode.</summary>
public sealed class SignatureHelpHandler : SignatureHelpHandlerBase
{
    private readonly DocumentStore _store;
    private readonly DecoratorService _decorators;
    private readonly AnalysisMetadataProvider? _metadata;
    private readonly SemanticSignatureHelpService? _semantic;
    private bool _labelOffsetSupport;
    private bool _activeParameterSupport;

    public SignatureHelpHandler(DocumentStore store, DecoratorService decorators, AnalysisMetadataProvider? metadata = null,
        SemanticSignatureHelpService? semantic = null)
    {
        _store = store;
        _decorators = decorators;
        _metadata = metadata;
        _semantic = semantic;
    }

    public override async Task<SignatureHelp?> Handle(SignatureHelpParams request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_store.TryCapture(
                request.TextDocument.Uri.ToString(),
                out DocumentRequestSnapshot? capture))
            return null;
        var lines = new LineIndex(capture.Document.Text);
        int offset = lines.ToOffset(request.Position.Line + 1, request.Position.Character + 1);
        var (line, column) = lines.ToPosition(offset);
        if (line - 1 != request.Position.Line || column - 1 != request.Position.Character) return null;
        using var guard = new EditorRequestGuard(_store, capture, _metadata, ct);

        SignatureHelp? result = _decorators.SignatureHelp(
                capture.Document.Text,
                request.Position.Line,
                request.Position.Character);
        AnalysisValidation? validation = null;
        if (result is null && _semantic is not null)
        {
            var semantic = await _semantic.SignatureHelpAsync(capture, request.Position,
                _labelOffsetSupport, _activeParameterSupport, ct).ConfigureAwait(false);
            result = semantic.Help;
            validation = semantic.Validation;
        }
        ct.ThrowIfCancellationRequested();
        return guard.IsCurrent(ct) && (validation?.IsCurrent(ct) ?? true) ? result : null;
    }

    protected override SignatureHelpRegistrationOptions CreateRegistrationOptions(
        SignatureHelpCapability capability, ClientCapabilities clientCapabilities)
    {
        _labelOffsetSupport = capability?.SignatureInformation?.ParameterInformation?.LabelOffsetSupport == true;
        _activeParameterSupport = capability?.SignatureInformation?.ActiveParameterSupport == true;
        return new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("typescript", "typescriptreact"),
            TriggerCharacters = new[] { "(", "," }
        };
    }
}
