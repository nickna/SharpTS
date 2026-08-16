using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using SharpTS.LanguageServer.Services;

namespace SharpTS.LanguageServer.Handlers;

/// <summary>
/// Serves <c>textDocument/documentSymbol</c>: the outline of declarations in a file.
/// </summary>
/// <remarks>
/// Registered only in <see cref="LanguageFeatureMode.Full"/>. In the VS Code experience
/// <c>tsserver</c> already provides this, and two servers answering would duplicate every entry.
/// </remarks>
public sealed class DocumentSymbolHandler : DocumentSymbolHandlerBase
{
    private readonly DocumentStore _store;
    private readonly DocumentSymbolService _symbols;

    public DocumentSymbolHandler(DocumentStore store, DocumentSymbolService symbols)
    {
        _store = store;
        _symbols = symbols;
    }

    public override Task<SymbolInformationOrDocumentSymbolContainer?> Handle(
        DocumentSymbolParams request, CancellationToken ct)
    {
        string uri = request.TextDocument.Uri.ToString();
        if (!_store.TryGetSnapshot(uri, out DocumentSnapshot? snapshot))
            return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(null);

        ct.ThrowIfCancellationRequested();

        var symbols = _symbols.GetSymbols(
                request.TextDocument.Uri.GetFileSystemPath(),
                snapshot.Text)
            .Select(symbol => new SymbolInformationOrDocumentSymbol(symbol))
            .ToArray();

        return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(
            new SymbolInformationOrDocumentSymbolContainer(symbols));
    }

    protected override DocumentSymbolRegistrationOptions CreateRegistrationOptions(
        DocumentSymbolCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("typescript", "typescriptreact"),
        };
}
