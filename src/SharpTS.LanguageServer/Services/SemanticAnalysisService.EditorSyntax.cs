using System.Runtime.CompilerServices;
using SharpTS.Parsing;

namespace SharpTS.LanguageServer.Services;

internal sealed record EditorSyntaxCacheStatistics(long Parses, long CacheHits,
    int RetainedArtifacts, long EstimatedRetainedBytes);

public sealed partial class SemanticAnalysisService
{
    private readonly ConditionalWeakTable<AnalysisDocument, object> _syntaxIdentities = new();
    private readonly Dictionary<EditorSyntaxKey, LinkedListNode<EditorSyntaxEntry>> _editorSyntax = [];
    private readonly LinkedList<EditorSyntaxEntry> _editorSyntaxLru = [];
    private long _editorSyntaxBytes, _editorSyntaxParses, _editorSyntaxHits;

    internal EditorSyntaxCacheStatistics EditorSyntaxStatistics
    {
        get
        {
            lock (_gate) return new(_editorSyntaxParses, _editorSyntaxHits,
                _editorSyntax.Count, _editorSyntaxBytes);
        }
    }

    /// <summary>
    /// Returns a separate cursor-specific parse without checking it or changing the base graph.
    /// Callers use the captured document's ordinary index first and request this artifact only
    /// when an unfinished expression needs local recovery. Recovered syntax has no bindings.
    /// </summary>
    internal EditorParseArtifact? GetEditorSyntax(AnalysisLease analysis, int cursorOffset,
        EditorQueryKind queryKind, CancellationToken cancellationToken = default,
        EditorRecoveryPolicy? policy = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!analysis.IsCurrent(cancellationToken) ||
            !analysis.Model.Snapshot.TryGetDocument(analysis.Model.Document.Path, out var document))
            return null;
        ArgumentOutOfRangeException.ThrowIfNegative(cursorOffset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cursorOffset, document!.Document.Text.Length);
        policy ??= EditorRecoveryPolicy.Default;
        AnalysisOptions? options = analysis.Model.Snapshot.Options;
        EditorSyntaxKey key;
        long generation;
        EditorParseArtifact? artifact = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            generation = _generation;
            key = new(_syntaxIdentities.GetValue(document, static _ => new object()),
                cursorOffset, queryKind, policy, options);
            if (_editorSyntax.TryGetValue(key, out var cached))
            {
                _editorSyntaxLru.Remove(cached);
                _editorSyntaxLru.AddLast(cached);
                _editorSyntaxHits++;
                artifact = cached.Value.Artifact;
            }
        }
        if (artifact is not null)
            return analysis.IsCurrent(cancellationToken) ? artifact : null;

        artifact = Parser.ParseForEditor(document.Document, cursorOffset, queryKind, policy,
            options?.DecoratorMode ?? DecoratorMode.Stage3, options?.JsxOptions, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!analysis.IsCurrent(cancellationToken)) return null;
        lock (_gate)
        {
            if (_disposed || _generation != generation) return null;
            _editorSyntaxParses++;
            // Another caller may have finished this cursor in the meantime. Its independently
            // parsed artifact has the same inputs, but retaining only one keeps the bound exact.
            if (_editorSyntax.TryGetValue(key, out var existing)) return existing.Value.Artifact;
            long available = Math.Min(8 * 1024 * 1024, Math.Max(0, _maxRetainedBytes - _bytes));
            int countLimit = (int)Math.Min(32L, (long)_maxSnapshots * 4);
            if (countLimit == 0 || artifact.EstimatedBytes > available) return artifact;
            var entry = new EditorSyntaxEntry(key, artifact);
            _editorSyntax.Add(key, _editorSyntaxLru.AddLast(entry));
            _editorSyntaxBytes += artifact.EstimatedBytes;
            TrimEditorSyntax(countLimit);
        }
        return artifact;
    }

    // All cache operations use the same gate as checked snapshots, so the total estimated
    // retained payload remains within the service's configured byte budget.
    private void TrimEditorSyntax(int countLimit = 32)
    {
        long available = Math.Min(8 * 1024 * 1024, Math.Max(0, _maxRetainedBytes - _bytes));
        while (_editorSyntaxLru.First is { } first &&
               (_editorSyntax.Count > countLimit || _editorSyntaxBytes > available))
        {
            _editorSyntaxLru.RemoveFirst();
            _editorSyntax.Remove(first.Value.Key);
            _editorSyntaxBytes -= first.Value.Artifact.EstimatedBytes;
        }
    }

    private void ClearEditorSyntax()
    {
        _editorSyntax.Clear();
        _editorSyntaxLru.Clear();
        _editorSyntaxBytes = 0;
    }

    private sealed record EditorSyntaxKey(object DocumentIdentity, int CursorOffset,
        EditorQueryKind QueryKind, EditorRecoveryPolicy Policy, AnalysisOptions? Options);
    private sealed record EditorSyntaxEntry(EditorSyntaxKey Key, EditorParseArtifact Artifact);
}
