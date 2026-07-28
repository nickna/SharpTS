using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer.Services;

namespace SharpTS.LanguageServer.Handlers;

/// <summary>Serves structured quick fixes for SharpTS interop diagnostics.</summary>
public sealed class CodeActionHandler : CodeActionHandlerBase
{
    private readonly InteropCodeActionService _actions;

    public CodeActionHandler(InteropCodeActionService actions) =>
        _actions = actions;

    public override Task<CommandOrCodeActionContainer?> Handle(
        CodeActionParams request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<CommandOrCodeActionContainer?>(
            _actions.Create(request));
    }

    public override Task<CodeAction> Handle(
        CodeAction request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(request);
    }

    protected override CodeActionRegistrationOptions CreateRegistrationOptions(
        CodeActionCapability capability,
        ClientCapabilities clientCapabilities) =>
        CreateRegistrationOptions();

    internal static CodeActionRegistrationOptions CreateRegistrationOptions() =>
        new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage(
                "typescript",
                "typescriptreact"),
            CodeActionKinds = new Container<CodeActionKind>(
                CodeActionKind.QuickFix),
            ResolveProvider = false,
        };
}
