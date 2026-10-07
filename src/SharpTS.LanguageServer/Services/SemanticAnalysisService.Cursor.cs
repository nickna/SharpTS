using System.Runtime.CompilerServices;
using SharpTS.Parsing;

namespace SharpTS.LanguageServer.Services;

public sealed partial class SemanticAnalysisService
{
    // Values contain no backlink to their weak keys. A completed child cache key must not
    // keep the base AST alive after the base entry/leases have been released.
    private readonly ConditionalWeakTable<AnalysisSnapshot, object> _cursorIdentities = new();

    internal Task<AnalysisLease?> GetCursorDocumentAsync(DocumentRequestSnapshot capture,
        int cursorOffset, EditorQueryKind queryKind, AnalysisLease? seed,
        CancellationToken cancellationToken, EditorRecoveryPolicy? policy = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (capture.Document.FilePath is null || cursorOffset < 0 || cursorOffset > capture.Document.Text.Length)
            return Task.FromResult<AnalysisLease?>(null);

        AnalysisRequest request = AnalysisRequest.From(capture, _workspace.SnapshotRoots());
        // A current filesystem manifest alone cannot prove the originating editor overlay or
        // its versions. Wrong/stale seeds use the same configured cold-build path instead.
        if (seed is not null && (!seed.Validation.IsOwnedBy(this) ||
            !string.Equals(seed.RequestStamp, request.Stamp, StringComparison.Ordinal) ||
            !string.Equals(seed.Model.Document.Path, request.Path, StringComparison.OrdinalIgnoreCase) ||
            !seed.IsCurrent(cancellationToken)))
            seed = null;
        object? identity = seed is null ? null : _cursorIdentities.GetValue(seed.Model.Snapshot, _ => new object());
        var target = new EditorParseTarget(request.Path, cursorOffset, queryKind,
            policy ?? EditorRecoveryPolicy.Default);
        return GetAsync(request, null, seed, cancellationToken, target, identity);
    }

    // Cursor snapshots participate in the ordinary LRU/byte budget and have an additional
    // small completed-entry cap. All aliases of an evicted entry are removed together.
    private void TrimCursorAnalyses()
    {
        while (_lru.Count(entry => entry.Keys.Any(key => key.CursorTarget is not null)) > 4)
        {
            LinkedListNode<CacheEntry>? node = _lru.First;
            while (node is not null && node.Value.Keys.All(key => key.CursorTarget is null)) node = node.Next;
            if (node is null) return;
            Remove(node.Value);
        }
    }
}
