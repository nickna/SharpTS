namespace SharpTS.Compilation;

/// <summary>
/// Atomically replaces metadata loaders when configured assembly files change. Retired metadata
/// contexts remain alive until shutdown so a <see cref="Type"/> returned to an in-flight request
/// can never be invalidated by a concurrent reload.
/// </summary>
public sealed class ReloadingAssemblyReferenceLoader : IDisposable
{
    private readonly object _gate = new();
    private readonly string[] _assemblyPaths;
    private readonly string? _sdkPath;
    private readonly List<AssemblyReferenceLoader> _retired = [];
    private AssemblyReferenceLoader _current;
    private FileStamp[] _stamps;
    private bool _disposed;

    public ReloadingAssemblyReferenceLoader(
        IEnumerable<string> assemblyPaths,
        string? sdkPath = null)
    {
        _assemblyPaths = assemblyPaths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _sdkPath = sdkPath;
        _stamps = CaptureStamps();
        _current = new AssemblyReferenceLoader(_assemblyPaths, _sdkPath);
    }

    public int Generation { get; private set; }

    public Type? TryResolve(string fullName)
    {
        lock (_gate)
        {
            ReloadIfChanged();
            return _current.TryResolve(fullName);
        }
    }

    public IReadOnlyList<Type> GetAllPublicTypes()
    {
        lock (_gate)
        {
            ReloadIfChanged();
            return _current.GetAllPublicTypes().ToArray();
        }
    }

    private void ReloadIfChanged()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        FileStamp[] currentStamps = CaptureStamps();
        if (currentStamps.SequenceEqual(_stamps))
            return;

        var replacement = new AssemblyReferenceLoader(_assemblyPaths, _sdkPath);
        _retired.Add(_current);
        _current = replacement;
        _stamps = currentStamps;
        Generation++;
    }

    private FileStamp[] CaptureStamps() =>
        _assemblyPaths.Select(FileStamp.Capture).ToArray();

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _current.Dispose();
            foreach (AssemblyReferenceLoader loader in _retired)
                loader.Dispose();
            _retired.Clear();
        }
    }

    private readonly record struct FileStamp(
        bool Exists,
        long Length,
        long LastWriteUtcTicks)
    {
        public static FileStamp Capture(string path)
        {
            var file = new FileInfo(path);
            return file.Exists
                ? new FileStamp(
                    Exists: true,
                    file.Length,
                    file.LastWriteTimeUtc.Ticks)
                : default;
        }
    }
}
