using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace SharpTS.LanguageServer;

/// <summary>
/// An immutable version of one open text document.
/// </summary>
public sealed record DocumentSnapshot(
    string Uri,
    string Text,
    int Version,
    string? FilePath);

/// <summary>
/// One atomically captured view of a request document and every open file overlay.
/// </summary>
public sealed record DocumentRequestSnapshot(
    DocumentSnapshot Document,
    long WorkspaceVersion,
    IReadOnlyDictionary<string, DocumentSnapshot> FileSystemDocuments)
{
    public IReadOnlyDictionary<string, string> TextOverlay =>
        FileSystemDocuments.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Text,
            StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Tracks immutable, versioned open-document snapshots. All mutations and multi-document
/// captures share one lock so a request never combines the requested version with an overlay
/// from a different workspace instant.
/// </summary>
public sealed class DocumentStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DocumentSnapshot> _documents =
        new(StringComparer.OrdinalIgnoreCase);
    private long _workspaceVersion;

    /// <summary>
    /// Compatibility helper for service-level tests and non-LSP callers. Each call advances the
    /// stored document version.
    /// </summary>
    public void Set(string uri, string text)
    {
        lock (_gate)
        {
            int version = _documents.TryGetValue(uri, out DocumentSnapshot? current)
                ? current.Version + 1
                : 0;
            SetCore(uri, text, version);
        }
    }

    public bool Open(string uri, string text, int version)
    {
        lock (_gate)
        {
            if (_documents.TryGetValue(uri, out DocumentSnapshot? current) &&
                version < current.Version)
            {
                return false;
            }

            SetCore(uri, text, version);
            return true;
        }
    }

    /// <summary>
    /// Applies an LSP incremental change batch in order. Stale or duplicate document versions
    /// are ignored so delayed notifications cannot roll a buffer backwards.
    /// </summary>
    public bool ApplyChanges(
        string uri,
        int version,
        IEnumerable<TextDocumentContentChangeEvent> changes)
    {
        lock (_gate)
        {
            if (!_documents.TryGetValue(uri, out DocumentSnapshot? current) ||
                version <= current.Version)
            {
                return false;
            }

            string text = current.Text;
            foreach (TextDocumentContentChangeEvent change in changes)
            {
                text = change.Range is null
                    ? change.Text
                    : ApplyIncrementalChange(text, change.Range, change.Text);
            }

            SetCore(uri, text, version);
            return true;
        }
    }

    public bool TryGet(string uri, out string text)
    {
        lock (_gate)
        {
            if (_documents.TryGetValue(uri, out DocumentSnapshot? snapshot))
            {
                text = snapshot.Text;
                return true;
            }
        }

        text = null!;
        return false;
    }

    public bool TryGetSnapshot(string uri, out DocumentSnapshot snapshot)
    {
        lock (_gate)
            return _documents.TryGetValue(uri, out snapshot!);
    }

    public bool TryCapture(
        string uri,
        out DocumentRequestSnapshot snapshot)
    {
        lock (_gate)
        {
            if (!_documents.TryGetValue(uri, out DocumentSnapshot? document))
            {
                snapshot = null!;
                return false;
            }

            snapshot = new DocumentRequestSnapshot(
                document,
                _workspaceVersion,
                CaptureFileSystemDocuments());
            return true;
        }
    }

    public bool IsCurrent(string uri, int version, long workspaceVersion)
    {
        lock (_gate)
        {
            return _workspaceVersion == workspaceVersion &&
                _documents.TryGetValue(uri, out DocumentSnapshot? snapshot) &&
                snapshot.Version == version;
        }
    }

    public DocumentSnapshot? Remove(string uri)
    {
        lock (_gate)
        {
            if (!_documents.Remove(uri, out DocumentSnapshot? removed))
                return null;

            _workspaceVersion++;
            return removed;
        }
    }

    /// <summary>Returns open file documents keyed by normalized file-system path.</summary>
    public IReadOnlyDictionary<string, string> SnapshotFileSystemDocuments()
    {
        lock (_gate)
        {
            return CaptureFileSystemDocuments().ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Text,
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public IReadOnlyList<DocumentSnapshot> SnapshotDocuments()
    {
        lock (_gate)
            return [.. _documents.Values];
    }

    private void SetCore(string uri, string text, int version)
    {
        _documents[uri] = new DocumentSnapshot(
            uri,
            text,
            version,
            GetFilePath(uri));
        _workspaceVersion++;
    }

    private Dictionary<string, DocumentSnapshot> CaptureFileSystemDocuments()
    {
        var documents = new Dictionary<string, DocumentSnapshot>(
            StringComparer.OrdinalIgnoreCase);
        foreach (DocumentSnapshot document in _documents.Values)
        {
            if (document.FilePath is not null)
                documents[document.FilePath] = document;
        }
        return documents;
    }

    private static string? GetFilePath(string documentUri)
    {
        return Uri.TryCreate(documentUri, UriKind.Absolute, out Uri? uri) &&
            uri.IsFile
                ? Path.GetFullPath(uri.LocalPath)
                : null;
    }

    private static string ApplyIncrementalChange(
        string text,
        OmniSharp.Extensions.LanguageServer.Protocol.Models.Range range,
        string replacement)
    {
        var map = new PositionMap(text);
        int start = map.ToOffset(
            checked((int)range.Start.Line),
            checked((int)range.Start.Character));
        int end = map.ToOffset(
            checked((int)range.End.Line),
            checked((int)range.End.Character));
        if (start > end)
            throw new ArgumentException("The text change range ends before it starts.");

        return string.Concat(
            text.AsSpan(0, start),
            replacement,
            text.AsSpan(end));
    }
}
