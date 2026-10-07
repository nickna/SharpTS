namespace SharpTS.IO;

/// <summary>Read-only filesystem inputs consumed by configuration and module loading.</summary>
public interface ICompilerFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    string ReadAllText(string path);
    FileAttributes GetAttributes(string path);
    IReadOnlyList<string> EnumerateFiles(string path, string searchPattern, EnumerationOptions options);
    IReadOnlyList<string> EnumerateDirectories(string path, string searchPattern, EnumerationOptions options);
}

/// <summary>
/// Provides an execution-context-local filesystem for an analysis. Ordinary compiler calls
/// use the physical filesystem; editor analysis can observe and memoize its input reads.
/// </summary>
public static class CompilerFileSystem
{
    private static readonly AsyncLocal<ScopeState?> State = new();

    public static ICompilerFileSystem Physical { get; } = new PhysicalFileSystem();
    public static ICompilerFileSystem Current => State.Value?.FileSystem ?? Physical;
    public static CancellationToken CancellationToken => State.Value?.CancellationToken ?? default;

    /// <summary>Installs a reader for this asynchronous flow until the returned scope is disposed.</summary>
    public static IDisposable Use(ICompilerFileSystem fileSystem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        var previous = State.Value;
        State.Value = new ScopeState(fileSystem, cancellationToken);
        return new RestoreScope(previous);
    }

    public static void ThrowIfCancellationRequested() => CancellationToken.ThrowIfCancellationRequested();

    public static bool FileExists(string path)
    {
        ThrowIfCancellationRequested();
        return Current.FileExists(path);
    }

    public static bool DirectoryExists(string path)
    {
        ThrowIfCancellationRequested();
        return Current.DirectoryExists(path);
    }

    public static string ReadAllText(string path)
    {
        ThrowIfCancellationRequested();
        string text = Current.ReadAllText(path);
        ThrowIfCancellationRequested();
        return text;
    }

    public static FileAttributes GetAttributes(string path)
    {
        ThrowIfCancellationRequested();
        return Current.GetAttributes(path);
    }

    public static IReadOnlyList<string> EnumerateFiles(string path, string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly) =>
        EnumerateFiles(path, searchPattern, CompatibleOptions(searchOption));

    public static IReadOnlyList<string> EnumerateFiles(string path, string searchPattern, EnumerationOptions options)
    {
        ThrowIfCancellationRequested();
        var files = Current.EnumerateFiles(path, searchPattern, options);
        ThrowIfCancellationRequested();
        return files;
    }

    public static IReadOnlyList<string> EnumerateDirectories(string path, string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly) =>
        EnumerateDirectories(path, searchPattern, CompatibleOptions(searchOption));

    public static IReadOnlyList<string> EnumerateDirectories(string path, string searchPattern, EnumerationOptions options)
    {
        ThrowIfCancellationRequested();
        var directories = Current.EnumerateDirectories(path, searchPattern, options);
        ThrowIfCancellationRequested();
        return directories;
    }

    // Match the Directory APIs' SearchOption overloads, rather than EnumerationOptions'
    // different defaults for hidden/system entries and inaccessible directories.
    private static EnumerationOptions CompatibleOptions(SearchOption option) => new()
    {
        RecurseSubdirectories = option switch
        {
            SearchOption.TopDirectoryOnly => false,
            SearchOption.AllDirectories => true,
            _ => throw new ArgumentOutOfRangeException(nameof(option)),
        },
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        MatchType = MatchType.Win32,
    };

    private sealed record ScopeState(ICompilerFileSystem FileSystem, CancellationToken CancellationToken);

    private sealed class RestoreScope(ScopeState? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            State.Value = previous;
            _disposed = true;
        }
    }

    private sealed class PhysicalFileSystem : ICompilerFileSystem
    {
        private static StringComparer PathComparer => OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        public bool FileExists(string path) => File.Exists(path);
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public string ReadAllText(string path) => File.ReadAllText(path);
        public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
        public IReadOnlyList<string> EnumerateFiles(string path, string searchPattern, EnumerationOptions options) =>
            Materialize(Directory.EnumerateFiles(path, searchPattern, options));
        public IReadOnlyList<string> EnumerateDirectories(string path, string searchPattern, EnumerationOptions options) =>
            Materialize(Directory.EnumerateDirectories(path, searchPattern, options));

        private static IReadOnlyList<string> Materialize(IEnumerable<string> paths)
        {
            var result = new List<string>();
            foreach (string path in paths)
            {
                ThrowIfCancellationRequested();
                result.Add(path);
            }
            result.Sort(PathComparer);
            return result.ToArray();
        }
    }
}
