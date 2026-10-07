using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using SharpTS.LanguageServer.Services;
using FileSystemWatcher = OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher;

namespace SharpTS.LanguageServer.Handlers;

/// <summary>Uses client file notifications for prompt release; physical validation is the fallback.</summary>
public sealed class AnalysisWatchedFilesHandler(
    SemanticAnalysisService analysis, DiagnosticsCoordinator diagnostics) : DidChangeWatchedFilesHandlerBase
{
    public override Task<Unit> Handle(DidChangeWatchedFilesParams request, CancellationToken cancellationToken)
    {
        analysis.InvalidateAll();
        diagnostics.RepublishAll();
        return Unit.Task;
    }

    protected override DidChangeWatchedFilesRegistrationOptions CreateRegistrationOptions(
        DidChangeWatchedFilesCapability capability, ClientCapabilities clientCapabilities) => new()
    {
        Watchers = new Container<FileSystemWatcher>(new FileSystemWatcher
        {
            GlobPattern = new GlobPattern("**/*.{ts,tsx,mts,cts,js,jsx,mjs,cjs,json,csproj,dll,xml}"),
            Kind = WatchKind.Create | WatchKind.Change | WatchKind.Delete,
        }),
    };
}

public sealed class AnalysisWorkspaceFoldersHandler(
    NavigationWorkspaceContext workspace, SemanticAnalysisService analysis,
    DiagnosticsCoordinator diagnostics) : DidChangeWorkspaceFoldersHandlerBase
{
    public override Task<Unit> Handle(DidChangeWorkspaceFoldersParams request, CancellationToken cancellationToken)
    {
        workspace.Change(request.Event.Added.Select(folder => folder.Uri.GetFileSystemPath()),
            request.Event.Removed.Select(folder => folder.Uri.GetFileSystemPath()));
        analysis.InvalidateAll();
        diagnostics.RepublishAll();
        return Unit.Task;
    }

    protected override DidChangeWorkspaceFolderRegistrationOptions CreateRegistrationOptions(
        ClientCapabilities clientCapabilities) => new() { Supported = true, ChangeNotifications = true };
}
