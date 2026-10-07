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

    public override async Task<WorkspaceEdit?> Handle(
        RenameParams request,
        CancellationToken ct)
    {
        string uri = request.TextDocument.Uri.ToString();
        if (!_store.TryCapture(uri, out DocumentRequestSnapshot? snapshot))
            return null;

        ct.ThrowIfCancellationRequested();
        long workspaceVersion = _workspace.Version;
        var result = await _rename.RenameAsync(snapshot, request.Position, request.NewName,
            _workspace.SnapshotRoots(), ct);
        ct.ThrowIfCancellationRequested();
        return _store.IsCurrent(uri, snapshot.Document.Version, snapshot.WorkspaceVersion) &&
            _workspace.Version == workspaceVersion && result.Domain.IsCurrent(ct) ? result.Value : null;
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

    public override async Task<RangeOrPlaceholderRange?> Handle(
        PrepareRenameParams request,
        CancellationToken ct)
    {
        string uri = request.TextDocument.Uri.ToString();
        if (!_store.TryCapture(uri, out DocumentRequestSnapshot? snapshot))
            return null;

        ct.ThrowIfCancellationRequested();
        long workspaceVersion = _workspace.Version;
        var result = await _rename.PrepareAsync(snapshot, request.Position, _workspace.SnapshotRoots(), ct);
        ct.ThrowIfCancellationRequested();
        if (!_store.IsCurrent(uri, snapshot.Document.Version, snapshot.WorkspaceVersion) ||
            _workspace.Version != workspaceVersion || !result.Domain.IsCurrent(ct)) return null;
        return result.Value is null ? null : new RangeOrPlaceholderRange(result.Value);
    }

    protected override RenameRegistrationOptions CreateRegistrationOptions(
        RenameCapability capability,
        ClientCapabilities clientCapabilities)
        => RenameHandler.CreateRenameRegistrationOptions();
}
