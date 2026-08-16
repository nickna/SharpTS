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
/// Incremental text sync backed by immutable versions. Diagnostics are queued through the
/// debounced workspace coordinator rather than computed on the protocol thread.
/// </summary>
public sealed class TextDocumentSyncHandler : TextDocumentSyncHandlerBase
{
    private readonly DocumentStore _store;
    private readonly DiagnosticsCoordinator _diagnostics;

    public TextDocumentSyncHandler(
        DocumentStore store,
        DiagnosticsCoordinator diagnostics)
    {
        _store = store;
        _diagnostics = diagnostics;
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) => new(uri, "typescript");

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken ct)
    {
        string uri = request.TextDocument.Uri.ToString();
        if (_store.Open(
                uri,
                request.TextDocument.Text,
                request.TextDocument.Version ?? 0))
        {
            _diagnostics.Queue(uri);
        }
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken ct)
    {
        string uri = request.TextDocument.Uri.ToString();
        int version = request.TextDocument.Version ??
            (_store.TryGetSnapshot(uri, out DocumentSnapshot? current)
                ? current.Version + 1
                : 0);
        if (_store.ApplyChanges(
                uri,
                version,
                request.ContentChanges))
        {
            _diagnostics.Queue(uri);
        }
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken ct) => Unit.Task;

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken ct)
    {
        if (_store.Remove(request.TextDocument.Uri.ToString()) is { } closed)
            _diagnostics.Close(closed);
        return Unit.Task;
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("typescript", "typescriptreact"),
            Change = TextDocumentSyncKind.Incremental,
            Save = new SaveOptions { IncludeText = false },
        };
}
