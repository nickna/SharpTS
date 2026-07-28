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

    /// <summary>Returns a stable snapshot of all open documents for module-resolution overlays.</summary>
    public IReadOnlyDictionary<string, string> Snapshot() =>
        new Dictionary<string, string>(_docs);
}
