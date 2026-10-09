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
    private readonly Action<Exception> _reportFailure;
    private readonly TimeSpan _debounce;
    private readonly HashSet<string> _queuedDocuments =
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
        TimeSpan debounce,
        Action<Exception>? reportFailure = null)
    {
        _store = store;
        _diagnostics = diagnostics;
        _graph = graph;
        _settings = settings;
        _publish = publish;
        _debounce = debounce;
        // Stdio is the LSP transport. Internal failures belong on the host's stderr channel.
        _reportFailure = reportFailure ?? (exception =>
            Console.Error.WriteLine($"SharpTS background diagnostics failed: {exception}"));
    }

    public void Queue(string uri)
    {
        CancellationToken token;
        lock (_gate)
        {
            ThrowIfDisposed();
            _queuedDocuments.Add(uri);
            CancelWorkspace();
            token = _workspaceCancellation.Token;
        }

        Track(RunPendingDocumentsAsync(token));
    }

    public void Close(DocumentSnapshot closed)
    {
        _diagnostics.Invalidate(closed.FilePath ?? closed.Uri);

        CancellationToken token;
        lock (_gate)
        {
            ThrowIfDisposed();
            CancelWorkspace();
            IReadOnlySet<string> affected = closed.FilePath is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : _graph.Remove(closed.FilePath);
            bool reopened = _store.TryGetSnapshot(closed.Uri, out _);
            if (reopened)
                _queuedDocuments.Add(closed.Uri);
            else
                _queuedDocuments.Remove(closed.Uri);
            QueueAffectedDocuments(affected);
            token = _workspaceCancellation.Token;
            if (!reopened)
                PublishEmpty(closed);
        }

        Track(RunPendingDocumentsAsync(token));
    }

    public void RepublishAll()
    {
        CancellationToken token;
        lock (_gate)
        {
            ThrowIfDisposed();
            _queuedDocuments.UnionWith(_store.SnapshotDocuments().Select(document => document.Uri));
            CancelWorkspace();
            token = _workspaceCancellation.Token;
        }

        Track(RunPendingDocumentsAsync(token));
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

    private async Task RunPendingDocumentsAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(_debounce, cancellationToken);
        var prepared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Cancellation retires a capture, not its queued URIs. Expand all affected open files
        // before publishing so import cycles cannot continually requeue an already-published file.
        while (true)
        {
            string[] unprepared;
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                unprepared = _queuedDocuments.Where(uri => !prepared.Contains(uri)).ToArray();
            }
            if (unprepared.Length == 0)
                break;

            foreach (string uri in unprepared)
            {
                cancellationToken.ThrowIfCancellationRequested();
                prepared.Add(uri);
                try
                {
                    if (!_store.TryCapture(uri, out DocumentRequestSnapshot? snapshot))
                        continue;
                    DiagnosticsDocumentInputs inputs = await _diagnostics.GetDocumentInputsAsync(
                        snapshot, _settings.Mode, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!_store.IsCurrent(uri, snapshot.Document.Version, snapshot.WorkspaceVersion))
                        continue;
                    IReadOnlySet<string> affected = _graph.Update(
                        snapshot.Document, snapshot.TextOverlay, inputs, cancellationToken);
                    lock (_gate)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        QueueAffectedDocuments(affected);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    failed.Add(uri);
                    _reportFailure(exception);
                }
            }
        }

        foreach (string uri in prepared)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (failed.Contains(uri))
                continue;
            try
            {
                if (!_store.TryCapture(uri, out DocumentRequestSnapshot? snapshot))
                {
                    lock (_gate)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        _queuedDocuments.Remove(uri);
                    }
                    continue;
                }
                await PublishAsync(snapshot, snapshot.Document, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                // Retain this URI for a later event without starving the rest of this batch.
                _reportFailure(exception);
            }
        }
    }

    private void QueueAffectedDocuments(IReadOnlySet<string> affectedPaths)
    {
        foreach (DocumentSnapshot document in _store.SnapshotDocuments())
        {
            if (document.FilePath is not null && affectedPaths.Contains(document.FilePath))
                _queuedDocuments.Add(document.Uri);
        }
    }

    private async Task PublishAsync(
        DocumentRequestSnapshot workspace,
        DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        DiagnosticsAnalysisResult result = await _diagnostics.AnalyzeResultAsync(
            workspace,
            document,
            _settings.Mode,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (!result.IsCurrent(cancellationToken))
            return;
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_store.IsCurrent(document.Uri, document.Version, workspace.WorkspaceVersion))
                return;
            if (!result.IsCurrent(cancellationToken))
                return;
            _publish(new PublishDiagnosticsParams
            {
                Uri = DocumentUri.Parse(document.Uri),
                Version = document.Version,
                Diagnostics = new Container<Diagnostic>(result.Diagnostics),
            });
            if (!cancellationToken.IsCancellationRequested)
                _queuedDocuments.Remove(document.Uri);
        }
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
        // Drain the observation, including failure reporting, rather than the faulted analysis.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
            _pending.Add(completion.Task);
        _ = ObserveAsync(task, completion);
    }

    [SuppressMessage(
        "Usage",
        "VSTHRD003",
        Justification = "Background diagnostic tasks are isolated, caught, and never synchronously blocked.")]
    private async Task ObserveAsync(Task task, TaskCompletionSource completion)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A newer document/workspace version owns publication now.
        }
        catch (Exception ex)
        {
            // Diagnostics are advisory. A failed analysis must not terminate the LSP process;
            // the next edit creates a fresh version and retries the pipeline.
            _reportFailure(ex);
        }
        finally
        {
            lock (_gate)
            {
                _pending.Remove(completion.Task);
                completion.SetResult();
            }
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
            _queuedDocuments.Clear();
        }
    }
}
