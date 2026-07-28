using System.Collections.Concurrent;

namespace SharpTS.LanguageServer;

/// <summary>
/// Tracks the current text of open documents (uri -> text). The overlay for in-memory
/// type-checking of dirty buffers (fed to ModuleResolver's virtualFiles) builds on this.
/// </summary>
public sealed class DocumentStore
{
    private readonly ConcurrentDictionary<string, string> _docs = new();

    public void Set(string uri, string text) => _docs[uri] = text;
    public bool TryGet(string uri, out string text) => _docs.TryGetValue(uri, out text!);
    public void Remove(string uri) => _docs.TryRemove(uri, out _);

    /// <summary>Returns open file documents keyed by normalized file-system path.</summary>
    public IReadOnlyDictionary<string, string> SnapshotFileSystemDocuments()
    {
        var documents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (documentUri, text) in _docs)
        {
            if (Uri.TryCreate(documentUri, UriKind.Absolute, out var uri) && uri.IsFile)
                documents[Path.GetFullPath(uri.LocalPath)] = text;
        }
        return documents;
    }
}
