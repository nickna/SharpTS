using System.Diagnostics.CodeAnalysis;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

namespace SharpTS.LanguageServer.Services;

/// <summary>
/// Debounces checks, cancels stale workspace work, updates dependency edges, and publishes only
/// results produced from the still-current immutable snapshot.
/// </summary>
public sealed class DiagnosticsCoordinator : IDisposable
{
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(150);

    private readonly object _gate = new();
    private readonly DocumentStore _store;
    private readonly DiagnosticsService _diagnostics;
    private readonly DocumentDependencyGraph _graph;
    private readonly DiagnosticsSettings _settings;
    private readonly Action<PublishDiagnosticsParams> _publish;
    private readonly TimeSpan _debounce;
    private readonly Dictionary<string, CancellationTokenSource> _documentCancellation =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Task> _pending = [];
    private CancellationTokenSource _workspaceCancellation = new();
    private bool _disposed;

    public DiagnosticsCoordinator(
        ILanguageServerFacade facade,
        DocumentStore store,
        DiagnosticsService diagnostics,
        DocumentDependencyGraph graph,
        DiagnosticsSettings settings)
        : this(
            store,
            diagnostics,
            graph,
            settings,
            parameters => facade.TextDocument.PublishDiagnostics(parameters),
            DefaultDebounce)
    {
    }

    internal DiagnosticsCoordinator(
        DocumentStore store,
        DiagnosticsService diagnostics,
        DocumentDependencyGraph graph,
        DiagnosticsSettings settings,
        Action<PublishDiagnosticsParams> publish,
        TimeSpan debounce)
    {
        _store = store;
        _diagnostics = diagnostics;
        _graph = graph;
        _settings = settings;
        _publish = publish;
        _debounce = debounce;
    }

    public void Queue(string uri)
    {
        CancellationToken token;
        lock (_gate)
        {
            ThrowIfDisposed();
            CancelWorkspace();

            if (_documentCancellation.Remove(uri, out CancellationTokenSource? old))
            {
                old.Cancel();
                old.Dispose();
            }

            var documentCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    _workspaceCancellation.Token);
            _documentCancellation[uri] = documentCancellation;
            token = documentCancellation.Token;
        }

        Track(RunChangedDocumentAsync(uri, token));
    }

    public void Close(DocumentSnapshot closed)
    {
        IReadOnlySet<string> affected =
            closed.FilePath is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : _graph.Remove(closed.FilePath);
        _diagnostics.Invalidate(closed.FilePath ?? closed.Uri);

        CancellationToken token;
        lock (_gate)
        {
            if (_documentCancellation.Remove(
                    closed.Uri,
                    out CancellationTokenSource? documentCancellation))
            {
                documentCancellation.Cancel();
                documentCancellation.Dispose();
            }
            CancelWorkspace();
            token = _workspaceCancellation.Token;
        }

        PublishEmpty(closed);
        Track(RunAffectedDocumentsAsync(
            affected.Where(path =>
                closed.FilePath is null ||
                !string.Equals(path, closed.FilePath, StringComparison.OrdinalIgnoreCase)),
            token));
    }

    public void RepublishAll()
    {
        CancellationToken token;
        lock (_gate)
        {
            ThrowIfDisposed();
            CancelWorkspace();
            token = _workspaceCancellation.Token;
        }

        Track(RunAffectedDocumentsAsync(
            _store.SnapshotDocuments()
                .Where(document => document.FilePath is not null)
                .Select(document => document.FilePath!),
            token));
    }

    internal async Task DrainAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (_gate)
                pending = [.. _pending];
            if (pending.Length == 0)
                return;
            await Task.WhenAll(pending);
        }
    }

    private async Task RunChangedDocumentAsync(
        string uri,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_debounce, cancellationToken);
            if (!_store.TryCapture(uri, out DocumentRequestSnapshot? snapshot))
                return;

            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<Parsing.Stmt> statements = _diagnostics.GetStatements(
                snapshot.Document,
                cancellationToken);
            IReadOnlySet<string> affected = _graph.Update(
                snapshot.Document,
                snapshot.TextOverlay,
                statements,
                cancellationToken);

            await AnalyzeAndPublishAsync(snapshot, affected, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A newer document/workspace version owns publication now.
        }
    }

    private async Task RunAffectedDocumentsAsync(
        IEnumerable<string> affectedPaths,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_debounce, cancellationToken);
            HashSet<string> pending = affectedPaths.ToHashSet(
                StringComparer.OrdinalIgnoreCase);
            foreach (DocumentSnapshot open in _store.SnapshotDocuments())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (open.FilePath is null || !pending.Contains(open.FilePath))
                    continue;
                if (!_store.TryCapture(open.Uri, out DocumentRequestSnapshot? snapshot))
                    continue;

                Publish(snapshot, snapshot.Document, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer document/workspace version owns publication now.
        }
    }

    private Task AnalyzeAndPublishAsync(
        DocumentRequestSnapshot workspace,
        IReadOnlySet<string> affectedPaths,
        CancellationToken cancellationToken)
    {
        foreach (DocumentSnapshot document in workspace.FileSystemDocuments.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (document.FilePath is null ||
                !affectedPaths.Contains(document.FilePath))
            {
                continue;
            }

            Publish(workspace, document, cancellationToken);
        }
        return Task.CompletedTask;
    }

    private void Publish(
        DocumentRequestSnapshot workspace,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        var diagnostics = _diagnostics.Analyze(
            workspace,
            document,
            _settings.Mode,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_store.IsCurrent(
                document.Uri,
                document.Version,
                workspace.WorkspaceVersion))
        {
            return;
        }

        _publish(new PublishDiagnosticsParams
        {
            Uri = DocumentUri.Parse(document.Uri),
            Version = document.Version,
            Diagnostics = new Container<Diagnostic>(diagnostics),
        });
    }

    private void PublishEmpty(DocumentSnapshot document)
    {
        _publish(new PublishDiagnosticsParams
        {
            Uri = DocumentUri.Parse(document.Uri),
            Version = document.Version,
            Diagnostics = new Container<Diagnostic>(),
        });
    }

    private void Track(Task task)
    {
        lock (_gate)
            _pending.Add(task);
        _ = ObserveAsync(task);
    }

    [SuppressMessage(
        "Usage",
        "VSTHRD003",
        Justification = "Background diagnostic tasks are isolated, caught, and never synchronously blocked.")]
    private async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Diagnostics are advisory. A failed analysis must not terminate the LSP process;
            // the next edit creates a fresh version and retries the pipeline.
        }
        finally
        {
            lock (_gate)
                _pending.Remove(task);
        }
    }

    private void CancelWorkspace()
    {
        _workspaceCancellation.Cancel();
        _workspaceCancellation.Dispose();
        _workspaceCancellation = new CancellationTokenSource();
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _workspaceCancellation.Cancel();
            _workspaceCancellation.Dispose();
            foreach (CancellationTokenSource cancellation in
                     _documentCancellation.Values)
            {
                cancellation.Cancel();
                cancellation.Dispose();
            }
            _documentCancellation.Clear();
        }
    }
}
