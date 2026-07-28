using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.LanguageServer.Services;

namespace SharpTS.LanguageServer.Handlers;

/// <summary>
/// Serves semantic rename only for complete configured-workspace symbol domains.
/// </summary>
public sealed class RenameHandler : RenameHandlerBase
{
    private readonly DocumentStore _store;
    private readonly RenameService _rename;
    private readonly NavigationWorkspaceContext _workspace;

    public RenameHandler(
        DocumentStore store,
        RenameService rename,
        NavigationWorkspaceContext workspace)
    {
        _store = store;
        _rename = rename;
        _workspace = workspace;
    }

    public override Task<WorkspaceEdit?> Handle(
        RenameParams request,
        CancellationToken ct)
    {
        string uri = request.TextDocument.Uri.ToString();
        if (!_store.TryCapture(uri, out DocumentRequestSnapshot? snapshot))
            return Task.FromResult<WorkspaceEdit?>(null);

        ct.ThrowIfCancellationRequested();
        return Task.FromResult(
            _rename.Rename(
                request.TextDocument.Uri.GetFileSystemPath(),
                snapshot.Document.Text,
                request.Position,
                request.NewName,
                snapshot.TextOverlay,
                _workspace.SnapshotRoots()));
    }

    protected override RenameRegistrationOptions CreateRegistrationOptions(
        RenameCapability capability,
        ClientCapabilities clientCapabilities)
        => CreateRenameRegistrationOptions();

    internal static RenameRegistrationOptions CreateRenameRegistrationOptions() =>
        new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage(
                "typescript",
                "typescriptreact"),
            PrepareProvider = true,
        };
}

/// <summary>
/// Refuses rename before the client prompts when the semantic domain is incomplete.
/// </summary>
public sealed class PrepareRenameHandler : PrepareRenameHandlerBase
{
    private readonly DocumentStore _store;
    private readonly RenameService _rename;
    private readonly NavigationWorkspaceContext _workspace;

    public PrepareRenameHandler(
        DocumentStore store,
        RenameService rename,
        NavigationWorkspaceContext workspace)
    {
        _store = store;
        _rename = rename;
        _workspace = workspace;
    }

    public override Task<RangeOrPlaceholderRange?> Handle(
        PrepareRenameParams request,
        CancellationToken ct)
    {
        string uri = request.TextDocument.Uri.ToString();
        if (!_store.TryCapture(uri, out DocumentRequestSnapshot? snapshot))
            return Task.FromResult<RangeOrPlaceholderRange?>(null);

        ct.ThrowIfCancellationRequested();
        OmniSharp.Extensions.LanguageServer.Protocol.Models.Range? range = _rename.Prepare(
            request.TextDocument.Uri.GetFileSystemPath(),
            snapshot.Document.Text,
            request.Position,
            snapshot.TextOverlay,
            _workspace.SnapshotRoots());
        return Task.FromResult(
            range is null
                ? null
                : new RangeOrPlaceholderRange(range));
    }

    protected override RenameRegistrationOptions CreateRegistrationOptions(
        RenameCapability capability,
        ClientCapabilities clientCapabilities)
        => RenameHandler.CreateRenameRegistrationOptions();
}
