using System.Security.Cryptography;
using System.Text;
using SharpTS.Configuration;
using SharpTS.IO;
using SharpTS.LanguageServer.Project;
using SharpTS.Parsing;

namespace SharpTS.LanguageServer.Services;

internal sealed record AnalysisStatistics(long Builds, long Checks, long CacheHits,
    int RetainedSnapshots, long EstimatedRetainedBytes);

/// <summary>
/// A bounded cache of completed, build-owned semantic components. A caller cancels its wait;
/// only workspace invalidation or disposal cancels shared work. Every reuse verifies the exact
/// observed filesystem inputs, including missing resolution candidates and directory inventories.
/// </summary>
public sealed partial class SemanticAnalysisService : IDisposable
{
    private readonly object _gate = new();
    private readonly NavigationWorkspaceContext _workspace;
    private readonly AnalysisMetadataProvider? _metadata;
    private readonly int _maxSnapshots;
    private readonly long _maxRetainedBytes;
    private readonly SemaphoreSlim _buildSlots = new(2);
    private readonly SemaphoreSlim _admission = new(16);
    private readonly Dictionary<RequestKey, BuildState> _inflight = [];
    private readonly Dictionary<RequestKey, CacheEntry> _cache = [];
    private readonly LinkedList<CacheEntry> _lru = [];
    private long _generation, _builds, _checks, _hits, _bytes;
    private bool _disposed;

    public SemanticAnalysisService(NavigationWorkspaceContext? workspace = null,
        AnalysisMetadataProvider? metadata = null, int maxSnapshots = 8,
        long maxRetainedBytes = 64 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxSnapshots);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRetainedBytes);
        _workspace = workspace ?? new NavigationWorkspaceContext();
        _metadata = metadata;
        _maxSnapshots = maxSnapshots;
        _maxRetainedBytes = maxRetainedBytes;
    }

    internal Action? BeforeCheck { get; set; }
    internal AnalysisStatistics Statistics
    {
        get { lock (_gate) return new(_builds, _checks, _hits, _lru.Count, _bytes); }
    }

    internal Task<AnalysisLease?> GetDocumentAsync(DocumentRequestSnapshot request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return request.Document.FilePath is null ? Task.FromResult<AnalysisLease?>(null) :
            GetAsync(AnalysisRequest.From(request, _workspace.SnapshotRoots()), null, null, cancellationToken);
    }

    internal Task<AnalysisLease?> GetDocumentAsync(string path, string text,
        IReadOnlyDictionary<string, string>? openDocuments = null,
        CancellationToken cancellationToken = default) =>
        GetAsync(AnalysisRequest.From(path, text, openDocuments, _workspace.SnapshotRoots()),
            null, null, cancellationToken);

    internal Task<AnalysisLease?> GetWorkspaceAsync(DocumentRequestSnapshot request, string anchorPath,
        AnalysisLease seed, IReadOnlyList<string> roots, CancellationToken cancellationToken) =>
        GetAsync(AnalysisRequest.From(request, roots), anchorPath, seed, cancellationToken);

    internal Task<AnalysisLease?> GetWorkspaceAsync(string path, string text,
        IReadOnlyDictionary<string, string>? openDocuments, string anchorPath,
        AnalysisLease seed, IReadOnlyList<string> roots, CancellationToken cancellationToken) =>
        GetAsync(AnalysisRequest.From(path, text, openDocuments, roots), anchorPath, seed, cancellationToken);

    private async Task<AnalysisLease?> GetAsync(AnalysisRequest request, string? anchorPath,
        AnalysisLease? seed, CancellationToken cancellationToken,
        EditorParseTarget? cursorTarget = null, object? seedIdentity = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string selectedPath = anchorPath is null ? request.Path : Path.GetFullPath(anchorPath);
        RequestKey key;
        BuildState? state = null;
        AnalysisLease? cached = null;
        CacheEntry? capturedEntry = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            key = new(selectedPath, request.Stamp, anchorPath is not null, _generation,
                cursorTarget, seedIdentity);
            if (_cache.TryGetValue(key, out capturedEntry)) cached = capturedEntry.Data.Acquire(selectedPath);
            if (cached is null && _inflight.TryGetValue(key, out state)) state.Waiters++;
        }
        if (cached is not null)
        {
            bool current;
            try { current = cached.IsCurrent(cancellationToken); }
            catch { cached.Dispose(); throw; }
            if (current)
            {
                lock (_gate)
                {
                    _hits++;
                    if (_cache.TryGetValue(key, out var entry)) Touch(entry);
                }
                return cached;
            }
            cached.Dispose();
            lock (_gate)
                if (_cache.TryGetValue(key, out var entry) && ReferenceEquals(entry, capturedEntry)) Remove(entry);
        }
        if (state is null)
        {
            await _admission.WaitAsync(cancellationToken).ConfigureAwait(false);
            bool admitted = true;
            bool retryCache = false;
            try
            {
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    key = key with { Generation = _generation };
                    if (_cache.ContainsKey(key)) retryCache = true;
                    else if (_inflight.TryGetValue(key, out state)) state.Waiters++;
                    else
                    {
                        // Keep a seed lease for the entire shared build, independent of its caller.
                        AnalysisLease? retainedSeed = seed?.Retain();
                        state = new BuildState(key, _workspace.Version) { Waiters = 1 };
                        _inflight.Add(key, state);
                        BuildState captured = state;
                        state.Task = Task.Run(() => BuildAsync(captured, request, selectedPath, retainedSeed));
                        _ = ObserveBuildFailureAsync(state.Task);
                        admitted = false; // FinishBuild releases the admission slot.
                    }
                }
            }
            finally { if (admitted) _admission.Release(); }
            if (retryCache) return await GetAsync(request, anchorPath, seed, cancellationToken,
                cursorTarget, seedIdentity).ConfigureAwait(false);
        }
        try
        {
            AnalysisData? data = await state!.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            AnalysisLease? lease = data?.Acquire(selectedPath);
            try
            {
                if (lease is not null && !lease.IsCurrent(cancellationToken))
                {
                    lease.Dispose();
                    return null;
                }
            }
            catch { lease?.Dispose(); throw; }
            return lease;
        }
        finally
        {
            lock (_gate)
            {
                state!.Waiters--;
                ReleaseFinished(state);
            }
        }
    }

    private async Task<AnalysisData?> BuildAsync(BuildState state, AnalysisRequest request,
        string selectedPath, AnalysisLease? seed)
    {
        AnalysisData? data = null;
        AnalysisMetadataView? metadata = null;
        bool slot = false;
        try
        {
            CancellationToken ct = state.Cancellation.Token;
            await _buildSlots.WaitAsync(ct).ConfigureAwait(false);
            slot = true;
            Interlocked.Increment(ref _builds);
            var observer = new ObservingCompilerFileSystem();
            CheckedNavigationWorkspace? workspace = null;
            CheckedNavigationModel? model;
            var aliases = new List<string>();
            using (CompilerFileSystem.Use(observer, ct))
            {
                if (state.Key.CursorTarget is not null && seed is not null)
                {
                    // Reconstruct configuration/resolution from the exact captured reads, never
                    // from a published AST. The fresh check owns every parser/checker object.
                    if (!seed.IsCurrent(ct)) return null;
                    observer.Include(seed.Inputs);
                }
                metadata = _metadata?.Capture();
                using var metadataScope = metadata?.EnterScope();
                void BeforeActualCheck()
                {
                    BeforeCheck?.Invoke();
                    ct.ThrowIfCancellationRequested();
                    Interlocked.Increment(ref _checks);
                }
                if (state.Key.Workspace)
                {
                    if (seed is null || !seed.Model.Snapshot.TryGetDocument(selectedPath, out var anchor)) return null;
                    if (!seed.IsCurrent(ct)) return null;
                    observer.Include(seed.Inputs);
                    workspace = NavigationModelBuilder.BuildWorkspace(selectedPath, anchor!.Document.Text,
                        request.Overlay, request.Roots, ct, BeforeActualCheck,
                        [seed.Model], preserveCapturedOverlay: true,
                        hasPartialMetadata: metadata?.IsComplete == false);
                    model = workspace.Models.FirstOrDefault();
                }
                else
                {
                    model = NavigationModelBuilder.TryBuild(request.Path, request.Text,
                        request.Overlay, ct, BeforeActualCheck, hasPartialMetadata: metadata?.IsComplete == false,
                        editorParseTarget: state.Key.CursorTarget);
                    if (model is not null && state.Key.CursorTarget is null)
                    {
                        foreach (AnalysisDocument document in model.Snapshot.Documents)
                        {
                            ct.ThrowIfCancellationRequested();
                            string path = document.Document.Path;
                            if (Path.IsPathFullyQualified(path) && string.Equals(model.Scope.ConfigPath,
                                TsConfigLoader.Discover(Path.GetDirectoryName(path)!), StringComparison.OrdinalIgnoreCase))
                                aliases.Add(path);
                        }
                    }
                }
            }
            if (model is null) return null;
            ObservedCompilerInputs inputs = observer.Complete();
            int? metadataGeneration = metadata?.Generation;
            var validation = new AnalysisValidation(this, state.Key.Generation, inputs,
                _metadata, metadataGeneration, state.WorkspaceVersion, _workspace);
            if (seed is not null && !seed.IsCurrent(state.Cancellation.Token)) return null;
            if (!validation.IsCurrent(state.Cancellation.Token)) return null;
            data = new AnalysisData(model, workspace, inputs, validation, metadata, request.Stamp);
            metadata = null; // ownership transferred
            lock (_gate)
            {
                if (!_disposed && state.Key.Generation == _generation && inputs.CanCache &&
                    _maxSnapshots > 0 && data.EstimatedBytes <= _maxRetainedBytes)
                {
                    var keys = aliases.Select(path => state.Key with { Path = Path.GetFullPath(path) })
                        .Append(state.Key).Distinct().ToArray();
                    foreach (RequestKey key in keys)
                        if (_cache.TryGetValue(key, out CacheEntry? old)) Remove(old);
                    var entry = new CacheEntry(data, keys);
                    data.Retain();
                    entry.Node = _lru.AddLast(entry);
                    foreach (RequestKey key in keys) _cache.Add(key, entry);
                    _bytes += data.EstimatedBytes;
                    while (_lru.Count > _maxSnapshots || _bytes > _maxRetainedBytes) Remove(_lru.First!.Value);
                    TrimCursorAnalyses();
                    TrimEditorSyntax();
                }
            }
            return data;
        }
        catch (AnalysisInputsChangedException) { return null; }
        catch (OperationCanceledException) when (state.Cancellation.IsCancellationRequested) { return null; }
        finally
        {
            metadata?.Dispose();
            seed?.Dispose();
            if (slot) _buildSlots.Release();
            lock (_gate)
            {
                state.Data = data;
                state.Finished = true;
                ReleaseFinished(state);
            }
            _admission.Release();
        }
    }

    private void ReleaseFinished(BuildState state)
    {
        if (!state.Finished || state.Waiters != 0 || state.Released) return;
        state.Released = true;
        _inflight.Remove(state.Key);
        state.Data?.Release();
        state.Cancellation.Dispose();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003",
        Justification = "Observes the service-owned build task even when every request waiter cancels.")]
    private static async Task ObserveBuildFailureAsync(Task<AnalysisData?> task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            // A cancelled waiter must not hide an unexpected failure in independently owned work.
            await Console.Error.WriteLineAsync($"SharpTS semantic analysis failed: {exception}").ConfigureAwait(false);
        }
    }

    internal bool IsGenerationCurrent(long generation)
    {
        lock (_gate) return !_disposed && generation == _generation;
    }

    public void InvalidateAll()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _generation++;
            ClearEditorSyntax();
            foreach (BuildState state in _inflight.Values) state.Cancellation.Cancel();
            while (_lru.First is { } node) Remove(node.Value);
        }
    }

    private void Touch(CacheEntry entry)
    {
        _lru.Remove(entry.Node!);
        entry.Node = _lru.AddLast(entry);
    }

    private void Remove(CacheEntry entry)
    {
        foreach (RequestKey key in entry.Keys) _cache.Remove(key);
        _lru.Remove(entry.Node!);
        _bytes -= entry.Data.EstimatedBytes;
        entry.Data.Release();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            InvalidateAll();
            _disposed = true;
        }
        // Semaphores remain alive until any already-running waiters/builds finish.
    }

    private sealed record RequestKey(string Path, string Stamp, bool Workspace, long Generation,
        EditorParseTarget? CursorTarget = null, object? SeedIdentity = null);
    private sealed class BuildState(RequestKey key, long workspaceVersion)
    {
        public RequestKey Key { get; } = key;
        public long WorkspaceVersion { get; } = workspaceVersion;
        public CancellationTokenSource Cancellation { get; } = new();
        public Task<AnalysisData?> Task { get; set; } = null!;
        public int Waiters;
        public bool Finished, Released;
        public AnalysisData? Data;
    }
    private sealed class CacheEntry(AnalysisData data, RequestKey[] keys)
    {
        public AnalysisData Data { get; } = data;
        public RequestKey[] Keys { get; } = keys;
        public LinkedListNode<CacheEntry>? Node;
    }

    private sealed record AnalysisRequest(string Path, string Text,
        IReadOnlyDictionary<string, string> Overlay, string[] Roots, string Stamp)
    {
        public static AnalysisRequest From(DocumentRequestSnapshot request, IReadOnlyList<string> roots) =>
            Create(request.Document.FilePath!, request.Document.Text, request.TextOverlay, roots,
                request.WorkspaceVersion.ToString(), request.FileSystemDocuments.OrderBy(pair => pair.Key,
                    StringComparer.OrdinalIgnoreCase).Select(pair => $"{pair.Key}:{pair.Value.Version}"));

        public static AnalysisRequest From(string path, string text,
            IReadOnlyDictionary<string, string>? overlay, IReadOnlyList<string> roots) =>
            Create(path, text, overlay, roots, "legacy", []);

        private static AnalysisRequest Create(string path, string text,
            IReadOnlyDictionary<string, string>? overlay, IReadOnlyList<string> roots,
            string version, IEnumerable<string> versions)
        {
            string absolute = System.IO.Path.GetFullPath(path);
            var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in overlay ?? new Dictionary<string, string>())
                copy[System.IO.Path.GetFullPath(pair.Key)] = pair.Value;
            copy[absolute] = text;
            string[] rootCopy = roots.Select(System.IO.Path.GetFullPath).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            void Add(string value)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                hash.AppendData(BitConverter.GetBytes(bytes.Length));
                hash.AppendData(bytes);
            }
            Add(version);
            foreach (string item in versions) Add(item);
            foreach (string root in rootCopy) Add(root);
            foreach (var pair in copy.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                Add(pair.Key); Add(pair.Value);
            }
            return new(absolute, text, copy, rootCopy, Convert.ToHexString(hash.GetHashAndReset()));
        }
    }
}

internal sealed class AnalysisValidation(SemanticAnalysisService owner, long generation,
    ObservedCompilerInputs inputs, AnalysisMetadataProvider? metadata, int? metadataGeneration,
    long workspaceVersion, NavigationWorkspaceContext workspace)
{
    internal bool IsOwnedBy(SemanticAnalysisService service) => ReferenceEquals(owner, service);

    public bool IsCurrent(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (workspace.Version != workspaceVersion || !owner.IsGenerationCurrent(generation) ||
            !inputs.IsCurrent(cancellationToken)) return false;
        using var physicalScope = CompilerFileSystem.Use(CompilerFileSystem.Physical, cancellationToken);
        bool metadataCurrent = metadata is null || metadata.RefreshGeneration() == metadataGeneration;
        return metadataCurrent && workspace.Version == workspaceVersion && owner.IsGenerationCurrent(generation);
    }
}

internal sealed class AnalysisData
{
    private int _references = 1;
    private readonly AnalysisMetadataView? _metadata;
    public AnalysisData(CheckedNavigationModel model, CheckedNavigationWorkspace? workspace,
        ObservedCompilerInputs inputs, AnalysisValidation validation, AnalysisMetadataView? metadata,
        string requestStamp)
    {
        Model = model; Workspace = workspace; Inputs = inputs; Validation = validation; _metadata = metadata;
        RequestStamp = requestStamp;
        var snapshots = (workspace?.Models ?? [model]).Select(item => item.Snapshot).Distinct().ToArray();
        EstimatedBytes = inputs.EstimatedBytes + snapshots.Sum(snapshot =>
            snapshot.Documents.Sum(document => (long)document.Document.Text.Length * 2 + document.Tokens.Count * 96L +
                (document.Syntax?.EstimatedBytes ?? 0)) +
            snapshot.TypeCount * 128L + snapshot.Members.EstimatedBytes + snapshot.EditorFacts.EstimatedBytes);
    }
    public CheckedNavigationModel Model { get; }
    public CheckedNavigationWorkspace? Workspace { get; }
    public ObservedCompilerInputs Inputs { get; }
    public AnalysisValidation Validation { get; }
    public long EstimatedBytes { get; }
    public string RequestStamp { get; }
    public void Retain()
    {
        int current;
        do
        {
            current = Volatile.Read(ref _references);
            ObjectDisposedException.ThrowIf(current == 0, this);
        } while (Interlocked.CompareExchange(ref _references, checked(current + 1), current) != current);
    }
    public void Release() { if (Interlocked.Decrement(ref _references) == 0) _metadata?.Dispose(); }
    public AnalysisLease? Acquire(string path)
    {
        if (!Model.Snapshot.TryGetDocument(path, out var document)) return null;
        Retain();
        return new(this, new CheckedNavigationModel(Model.Snapshot, document!.Document));
    }
    public IDisposable EnterMetadataScope() => _metadata?.EnterScope() ?? EmptyScope.Instance;
    private sealed class EmptyScope : IDisposable
    {
        public static EmptyScope Instance { get; } = new();
        public void Dispose() { }
    }
}

internal sealed class AnalysisLease : IDisposable
{
    private readonly AnalysisData _data;
    private AnalysisData? _owned;
    public AnalysisLease(AnalysisData data, CheckedNavigationModel model)
    {
        _data = data; _owned = data; Model = model;
    }
    public CheckedNavigationModel Model { get; }
    public CheckedNavigationWorkspace? Workspace => _data.Workspace;
    public ObservedCompilerInputs Inputs => _data.Inputs;
    public AnalysisValidation Validation => _data.Validation;
    internal string RequestStamp => _data.RequestStamp;
    public bool IsCurrent(CancellationToken cancellationToken = default) => Validation.IsCurrent(cancellationToken);
    public IDisposable EnterMetadataScope()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _owned) is null, this);
        return _data.EnterMetadataScope();
    }
    public AnalysisLease Retain()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _owned) is null, this);
        _data.Retain();
        return new(_data, Model);
    }
    public void Dispose() => Interlocked.Exchange(ref _owned, null)?.Release();
}
