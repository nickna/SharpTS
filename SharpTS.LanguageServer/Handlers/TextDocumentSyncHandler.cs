using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using SharpTS.LanguageServer.Services;

namespace SharpTS.LanguageServer.Handlers;

/// <summary>
/// Full-document text sync: on open/change, re-analyze and publish diagnostics; on close,
/// clear them. Full sync is fine for Phase 1 (single-file analyzer); incremental sync +
/// debounce + the module overlay come with the larger diagnostics work.
/// </summary>
public sealed class TextDocumentSyncHandler : TextDocumentSyncHandlerBase
{
    private readonly ILanguageServerFacade _facade;
    private readonly DocumentStore _store;
    private readonly DiagnosticsService _diagnostics;

    public TextDocumentSyncHandler(ILanguageServerFacade facade, DocumentStore store, DiagnosticsService diagnostics)
    {
        _facade = facade;
        _store = store;
        _diagnostics = diagnostics;
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) => new(uri, "typescript");

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken ct)
    {
        Refresh(request.TextDocument.Uri, request.TextDocument.Text);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken ct)
    {
        Refresh(request.TextDocument.Uri, request.ContentChanges.LastOrDefault()?.Text ?? "");
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken ct) => Unit.Task;

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken ct)
    {
        _store.Remove(request.TextDocument.Uri.ToString());
        _facade.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = request.TextDocument.Uri,
            Diagnostics = new Container<Diagnostic>()
        });
        return Unit.Task;
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("typescript", "typescriptreact"),
            Change = TextDocumentSyncKind.Full
        };

    private void Refresh(DocumentUri uri, string text)
    {
        _store.Set(uri.ToString(), text);
        _facade.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = new Container<Diagnostic>(
                _diagnostics.Analyze(text, fileName: uri.GetFileSystemPath()))
        });
    }
}
