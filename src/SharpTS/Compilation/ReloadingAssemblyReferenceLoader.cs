using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SharpTS.IO;

namespace SharpTS.Compilation;

/// <summary>
/// Publishes stable metadata generations when configured assembly contents change. A captured
/// snapshot keeps its metadata context alive until its last lease is released.
/// </summary>
public sealed class ReloadingAssemblyReferenceLoader : IDisposable
{
    private static readonly ConditionalWeakTable<Type, LoaderGeneration> LegacyTypeOwners = new();
    private readonly object _gate = new();
    private readonly string _sdkPath;
    private readonly bool _observeSdkContents;
    private AssemblyReferenceLoader.CapturedAssemblySet? _fixedFramework;
    private string[] _assemblyPaths;
    private LoaderGeneration? _current;
    private FileStamp[] _stamps;
    private int _generation;
    private bool _disposed;

    public ReloadingAssemblyReferenceLoader(IEnumerable<string> assemblyPaths, string? sdkPath = null)
    {
        _assemblyPaths = NormalizePaths(assemblyPaths);
        // The installed default SDK/runtime is a host input fixed for this server lifetime.
        // An explicitly configured SDK directory is a mutable project input and is refreshed.
        _sdkPath = sdkPath is null
            ? SdkResolver.FindReferenceAssembliesPath() ?? RuntimeEnvironment.GetRuntimeDirectory()
            : Path.GetFullPath(sdkPath);
        _observeSdkContents = sdkPath is not null;
        _fixedFramework = _observeSdkContents ? null : CaptureFixedFramework(_sdkPath);
        CapturedGeneration captured = CaptureGeneration();
        _stamps = captured.Stamps;
        _current = new LoaderGeneration(CreateLoader(captured.Images), 0);
    }

    public int Generation => Volatile.Read(ref _generation);

    private static AssemblyReferenceLoader.CapturedAssemblySet CaptureFixedFramework(string sdkPath)
    {
        // Host framework bytes are fixed, rather than re-entering the project input manifest on
        // only the first capture and spuriously changing its fingerprint on the second request.
        using var scope = CompilerFileSystem.Use(CompilerFileSystem.Physical, CompilerFileSystem.CancellationToken);
        return AssemblyReferenceLoader.CapturedAssemblySet.Capture(
            CompilerFileSystem.EnumerateFiles(sdkPath, "*.dll"));
    }

    /// <summary>Refreshes reference paths as well as contents, without resolving a type.</summary>
    public void UpdateReferences(IEnumerable<string> assemblyPaths)
    {
        string[] paths = NormalizePaths(assemblyPaths);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_assemblyPaths.SequenceEqual(paths, StringComparer.OrdinalIgnoreCase))
            {
                string[] previous = _assemblyPaths;
                _assemblyPaths = paths;
                try
                {
                    ReplaceCurrent();
                }
                catch
                {
                    _assemblyPaths = previous;
                    throw;
                }
            }
        }
    }

    /// <summary>Observes content changes without requiring an arbitrary type lookup.</summary>
    public int RefreshGeneration()
    {
        lock (_gate)
        {
            ReloadIfChanged();
            return _generation;
        }
    }

    public AssemblyReferenceSnapshot AcquireSnapshot()
    {
        lock (_gate)
        {
            ReloadIfChanged();
            return Retain(_current!);
        }
    }

    /// <summary>
    /// Compatibility API for callers that return raw Types without a lease. Weak associations
    /// keep exactly the contexts whose returned Types remain reachable; no retired list grows
    /// for the lifetime of the server. New analysis callers should use <see cref="AcquireSnapshot"/>.
    /// </summary>
    public Type? TryResolve(string fullName)
    {
        using AssemblyReferenceSnapshot snapshot = AcquireSnapshot();
        Type? type = snapshot.TryResolve(fullName);
        if (type is not null)
            PreserveLegacyType(type, snapshot.State);
        return type;
    }

    public IReadOnlyList<Type> GetAllPublicTypes()
    {
        using AssemblyReferenceSnapshot snapshot = AcquireSnapshot();
        IReadOnlyList<Type> types = snapshot.GetAllPublicTypes();
        foreach (Type type in types)
            PreserveLegacyType(type, snapshot.State);
        return types;
    }

    private void PreserveLegacyType(Type type, LoaderGeneration state)
    {
        lock (_gate)
        {
            state.HasLegacyTypes = true;
            LegacyTypeOwners.GetValue(type, _ => state);
        }
    }

    private void ReloadIfChanged()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        FileStamp[] stamps = CaptureStamps();
        if (!stamps.SequenceEqual(_stamps))
            ReplaceCurrent();
    }

    private void ReplaceCurrent()
    {
        CapturedGeneration captured = CaptureGeneration();
        var replacement = new LoaderGeneration(
            CreateLoader(captured.Images),
            checked(_generation + 1));
        LoaderGeneration previous = _current!;
        _current = replacement;
        _stamps = captured.Stamps;
        Volatile.Write(ref _generation, replacement.Generation);
        Release(previous);
    }

    private AssemblyReferenceLoader CreateLoader(AssemblyReferenceLoader.CapturedAssemblySet images)
    {
        try { return new AssemblyReferenceLoader(_assemblyPaths, images); }
        catch (Exception error) when (_observeSdkContents && AssemblyReferenceLoader.IsMetadataInputFailure(error))
        {
            // An unreadable/incomplete explicit SDK must not discard otherwise safe user/BCL
            // analysis. Keep its failed inputs in the generation and mark the fallback partial.
            _fixedFramework ??= CaptureFixedFramework(RuntimeEnvironment.GetRuntimeDirectory());
            return new AssemblyReferenceLoader(_assemblyPaths,
                images.WithFramework(_fixedFramework).WithFailure(_sdkPath,
                    $"SDK metadata unavailable: {_sdkPath}: {error.Message}"));
        }
    }

    private (string[] Paths, List<FileStamp> Stamps, bool HasSdkDirectory, string? SdkFailure) CaptureInputPaths()
    {
        IEnumerable<string> paths = _assemblyPaths;
        var stamps = new List<FileStamp>();
        bool hasSdkDirectory = false;
        string? sdkFailure = null;
        if (_observeSdkContents)
        {
            // Record both a missing explicit directory and its complete DLL inventory.
            try
            {
                hasSdkDirectory = CompilerFileSystem.DirectoryExists(_sdkPath);
                stamps.Add(new FileStamp("sdk-directory:" + _sdkPath, hasSdkDirectory ? "exists" : "missing"));
                if (hasSdkDirectory) paths = paths.Concat(CompilerFileSystem.EnumerateFiles(_sdkPath, "*.dll"));
                else sdkFailure = $"SDK reference directory not found: {_sdkPath}";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                hasSdkDirectory = false;
                sdkFailure = $"SDK reference directory unreadable: {_sdkPath}: {error.Message}";
                stamps.Add(new FileStamp("sdk-directory:" + _sdkPath, sdkFailure));
            }
        }
        return (paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), stamps, hasSdkDirectory, sdkFailure);
    }

    private CapturedGeneration CaptureGeneration()
    {
        var (paths, stamps, hasSdkDirectory, sdkFailure) = CaptureInputPaths();
        AssemblyReferenceLoader.CapturedAssemblySet images = AssemblyReferenceLoader.CapturedAssemblySet.Capture(paths);
        // A stream-hash followed by another file read can join two versions of a rebuilding
        // assembly. Fingerprint the exact bytes the captured resolver will subsequently load.
        stamps.AddRange(paths.Select(path => new FileStamp(path, images.ContentHash(path))));
        if (!_observeSdkContents || !hasSdkDirectory)
        {
            _fixedFramework ??= CaptureFixedFramework(RuntimeEnvironment.GetRuntimeDirectory());
            images = images.WithFramework(_fixedFramework);
        }
        if (sdkFailure is not null) images = images.WithFailure(_sdkPath, sdkFailure);
        return new CapturedGeneration(stamps.ToArray(), images);
    }

    private FileStamp[] CaptureStamps()
    {
        var (paths, stamps, _, _) = CaptureInputPaths();
        stamps.AddRange(paths
            .Select(path =>
            {
                CompilerFileSystem.ThrowIfCancellationRequested();
                try
                {
                    if (!CompilerFileSystem.FileExists(path))
                        return new FileStamp(path, null);
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    string hash = Convert.ToHexString(SHA256.HashData(stream));
                    CompilerFileSystem.ThrowIfCancellationRequested();
                    return new FileStamp(path, hash);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    return new FileStamp(path, AssemblyReferenceLoader.CapturedAssemblySet.ReadFailure(path, error));
                }
            }));
        return stamps.ToArray();
    }

    private static string[] NormalizePaths(IEnumerable<string> paths) => paths
        .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private AssemblyReferenceSnapshot Retain(LoaderGeneration state)
    {
        state.LeaseCount++;
        return new AssemblyReferenceSnapshot(this, state);
    }

    private void Release(LoaderGeneration state)
    {
        state.LeaseCount--;
        if (state.LeaseCount == 0 && !state.HasLegacyTypes)
            state.Dispose();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            LoaderGeneration previous = _current!;
            _current = null;
            _fixedFramework = null;
            _assemblyPaths = [];
            _stamps = [];
            Release(previous);
        }
    }

    private readonly record struct FileStamp(string Path, string? ContentHash);
    private readonly record struct CapturedGeneration(FileStamp[] Stamps,
        AssemblyReferenceLoader.CapturedAssemblySet Images);

    internal sealed class LoaderGeneration(AssemblyReferenceLoader loader, int generation) : IDisposable
    {
        public object Gate { get; } = new();
        public AssemblyReferenceLoader Loader { get; } = loader;
        public int Generation { get; } = generation;
        public int LeaseCount { get; set; } = 1; // Manager ownership, plus captured leases.
        public bool HasLegacyTypes { get; set; }
        private int _disposed;

        ~LoaderGeneration() => Dispose();

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            lock (Gate)
                Loader.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>A reference-counted view of one completed metadata generation.</summary>
    public sealed class AssemblyReferenceSnapshot : IDisposable
    {
        private readonly ReloadingAssemblyReferenceLoader _owner;
        private bool _disposed;
        internal LoaderGeneration State { get; }

        internal AssemblyReferenceSnapshot(ReloadingAssemblyReferenceLoader owner, LoaderGeneration state)
        {
            _owner = owner;
            State = state;
        }

        public int Generation => State.Generation;

        public IReadOnlyList<string> Failures
        {
            get
            {
                lock (State.Gate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    return State.Loader.MetadataFailures;
                }
            }
        }

        public Type? TryResolve(string fullName)
        {
            lock (State.Gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return State.Loader.TryResolve(fullName);
            }
        }

        public IReadOnlyList<Type> GetAllPublicTypes()
        {
            lock (State.Gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return State.Loader.GetAllPublicTypes().ToArray();
            }
        }

        public AssemblyReferenceSnapshot Retain()
        {
            lock (_owner._gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _owner.Retain(State);
            }
        }

        public void Dispose()
        {
            lock (_owner._gate)
            {
                if (_disposed)
                    return;
                // Synchronize with queries on this lease before releasing its last context.
                lock (State.Gate)
                    _disposed = true;
                _owner.Release(State);
            }
        }
    }
}
