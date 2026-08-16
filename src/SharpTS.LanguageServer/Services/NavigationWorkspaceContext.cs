using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace SharpTS.LanguageServer.Services;

/// <summary>
/// Immutable snapshots of the workspace folders supplied during LSP initialization.
/// </summary>
public sealed class NavigationWorkspaceContext
{
    private string[] _roots = [];

    public IReadOnlyList<string> SnapshotRoots() => [.. Volatile.Read(ref _roots)];

    public void Initialize(InitializeParams request)
    {
        IEnumerable<string> roots = request.WorkspaceFolders?.Any() == true
            ? request.WorkspaceFolders.Select(folder => folder.Uri.GetFileSystemPath())
            : RootFallback(request);

        Volatile.Write(
            ref _roots,
            roots
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static IEnumerable<string> RootFallback(InitializeParams request)
    {
        if (request.RootUri is not null)
        {
            yield return request.RootUri.GetFileSystemPath();
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(request.RootPath))
        {
            yield return request.RootPath;
            yield break;
        }

        yield return Directory.GetCurrentDirectory();
    }
}
