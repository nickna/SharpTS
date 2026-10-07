using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using SharpTS.Compilation;
using SharpTS.IO;
using SharpTS.References;

namespace SharpTS.LanguageServer.Project;

/// <summary>
/// Captures read-only project/manifest inputs and one stable CLR metadata generation. Refreshing
/// never loads executable assemblies, restores packages, or launches an external process.
/// </summary>
public sealed class AnalysisMetadataProvider : IDisposable
{
    private readonly object _gate = new();
    private readonly AsyncLocal<AnalysisMetadataView?> _ambient = new();
    private readonly string? _projectFile;
    private readonly string[] _references;
    private readonly string? _sdkPath;
    private readonly string _startDirectory;
    private readonly Action<string>? _log;
    private ReloadingAssemblyReferenceLoader? _loader;
    private string? _inputKey;
    private string? _lastErrors;
    private int _generation;
    private bool _disposed;

    public AnalysisMetadataProvider(
        string? projectFile = null,
        IEnumerable<string>? references = null,
        string? sdkPath = null,
        string? startDirectory = null,
        Action<string>? log = null)
    {
        _projectFile = projectFile is null ? null : Path.GetFullPath(projectFile);
        _references = (references ?? []).Select(Path.GetFullPath).ToArray();
        _sdkPath = sdkPath;
        _startDirectory = Path.GetFullPath(startDirectory ?? Environment.CurrentDirectory);
        _log = log;
    }

    /// <summary>The ambient captured generation, suitable for a request-local query cache.</summary>
    public int CurrentGeneration => _ambient.Value?.Generation ?? RefreshGeneration();

    public AnalysisMetadataView Capture()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var inputs = new MetadataInputCapture(CompilerFileSystem.Current);
            using var fileSystemScope = CompilerFileSystem.Use(inputs, CompilerFileSystem.CancellationToken);
            var paths = new List<string>(_references);
            var errors = new List<string>();
            if (_projectFile is not null)
            {
                try
                {
                    paths.AddRange(CsprojParser.Parse(_projectFile));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { errors.Add($"project references unavailable: {ex.Message}"); }
            }
            try
            {
                ReferenceSet set = DotNetReferences.ResolveReadOnly(_startDirectory, []);
                paths.AddRange(set.References.Select(reference => reference.Path));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { errors.Add($"sharpts.json references unavailable: {ex.Message}"); }

            string[] distinctPaths = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (string path in distinctPaths)
            {
                try
                {
                    if (!CompilerFileSystem.FileExists(path))
                        errors.Add($"reference not found: {path}");
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { errors.Add($"reference unreadable: {path}: {error.Message}"); }
            }
            _loader ??= new ReloadingAssemblyReferenceLoader(distinctPaths, _sdkPath);
            _loader.UpdateReferences(distinctPaths);
            ReloadingAssemblyReferenceLoader.AssemblyReferenceSnapshot snapshot = _loader.AcquireSnapshot();
            try
            {
                errors.AddRange(snapshot.Failures);
                string errorText = string.Join("\n", errors.Distinct(StringComparer.Ordinal));
                string key = $"{snapshot.Generation}:{inputs.Fingerprint()}:{errorText}";
                if (_inputKey is not null && !string.Equals(_inputKey, key, StringComparison.Ordinal))
                    _generation = checked(_generation + 1);
                _inputKey = key;
                if (!string.Equals(_lastErrors, errorText, StringComparison.Ordinal))
                {
                    _lastErrors = errorText;
                    foreach (string error in errors)
                        _log?.Invoke(error);
                }
                return new AnalysisMetadataView(this, snapshot, _generation, errors.Count == 0, distinctPaths);
            }
            catch
            {
                snapshot.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// Revalidates current configuration and assembly content even inside an older metadata scope.
    /// Call outside any memoized compiler filesystem scope when checking physical currentness.
    /// </summary>
    public int RefreshGeneration()
    {
        using AnalysisMetadataView view = Capture();
        return view.Generation;
    }

    /// <summary>Existing editor callbacks resolve through their ambient captured view.</summary>
    public Type? Resolve(string fullName)
    {
        if (_ambient.Value is { } view)
            return view.Resolve(fullName);
        lock (_gate)
        {
            // Preserve raw-Type compatibility for unscoped consumers. Production requests enter
            // a view scope and release their contexts explicitly instead of taking this path.
            using AnalysisMetadataView capture = Capture();
            return _loader!.TryResolve(fullName);
        }
    }

    public IEnumerable<string> GetTypeNames()
    {
        if (_ambient.Value is { } view)
            return view.GetTypeNames();
        using AnalysisMetadataView capture = Capture();
        return capture.GetTypeNames().ToArray();
    }

    internal IDisposable EnterScope(AnalysisMetadataView view)
    {
        AnalysisMetadataView? previous = _ambient.Value;
        AnalysisMetadataView retained = view.Retain();
        _ambient.Value = retained;
        return new MetadataScope(this, previous, retained);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _loader?.Dispose();
        }
    }

    private sealed class MetadataScope(
        AnalysisMetadataProvider owner,
        AnalysisMetadataView? previous,
        AnalysisMetadataView retained) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner._ambient.Value = previous;
            retained.Dispose();
        }
    }

    /// <summary>
    /// Memoizes only one capture's read-only inputs so repeated parser probes cannot join two
    /// configurations. Its digest includes failed probes and inventories as well as text reads.
    /// The enclosing analysis recorder still observes every underlying read.
    /// </summary>
    private sealed class MetadataInputCapture(ICompilerFileSystem inner) : ICompilerFileSystem
    {
        private readonly Dictionary<string, bool> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _directories = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _text = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FileAttributes> _attributes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<string>> _inventories = new(StringComparer.Ordinal);

        public bool FileExists(string path) => Memoize(_files, Path.GetFullPath(path), inner.FileExists);
        public bool DirectoryExists(string path) => Memoize(_directories, Path.GetFullPath(path), inner.DirectoryExists);
        public string ReadAllText(string path) => Memoize(_text, Path.GetFullPath(path), inner.ReadAllText);
        public FileAttributes GetAttributes(string path) => Memoize(_attributes, Path.GetFullPath(path), inner.GetAttributes);

        public IReadOnlyList<string> EnumerateFiles(string path, string searchPattern, EnumerationOptions options) =>
            Inventory("files", path, searchPattern, options, inner.EnumerateFiles);
        public IReadOnlyList<string> EnumerateDirectories(string path, string searchPattern, EnumerationOptions options) =>
            Inventory("directories", path, searchPattern, options, inner.EnumerateDirectories);

        private IReadOnlyList<string> Inventory(string kind, string path, string pattern, EnumerationOptions options,
            Func<string, string, EnumerationOptions, IReadOnlyList<string>> read)
        {
            string absolute = Path.GetFullPath(path);
            string key = $"{kind}:{absolute}:{pattern}:{options.RecurseSubdirectories}:" +
                $"{options.IgnoreInaccessible}:{options.AttributesToSkip}:{options.MatchType}:" +
                $"{options.MatchCasing}:{options.ReturnSpecialDirectories}:{options.MaxRecursionDepth}";
            if (!_inventories.TryGetValue(key, out IReadOnlyList<string>? paths))
            {
                paths = read(absolute, pattern, options).ToImmutableArray();
                _inventories.Add(key, paths);
            }
            return paths;
        }

        public string Fingerprint()
        {
            IEnumerable<string> records = _files.Select(pair => $"file:{pair.Key}:{pair.Value}")
                .Concat(_directories.Select(pair => $"directory:{pair.Key}:{pair.Value}"))
                .Concat(_text.Select(pair => $"text:{pair.Key}:{Hash(pair.Value)}"))
                .Concat(_attributes.Select(pair => $"attributes:{pair.Key}:{pair.Value}"))
                .Concat(_inventories.Select(pair => $"inventory:{pair.Key}:{Hash(string.Join('\n', pair.Value))}"));
            return Hash(string.Join('\n', records.Order(StringComparer.Ordinal)));
        }

        private static TValue Memoize<TValue>(Dictionary<string, TValue> values, string path, Func<string, TValue> read)
        {
            if (!values.TryGetValue(path, out TValue? value))
            {
                value = read(path);
                values.Add(path, value);
            }
            return value;
        }

        private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}

/// <summary>
/// One immutable CLR metadata view. Retain a lease for every cache entry or active operation that
/// may still use a reflected Type; dispose it when that operation or cache ownership ends.
/// </summary>
public sealed class AnalysisMetadataView : IDisposable
{
    private readonly AnalysisMetadataProvider _owner;
    private readonly ReloadingAssemblyReferenceLoader.AssemblyReferenceSnapshot _snapshot;

    internal AnalysisMetadataView(AnalysisMetadataProvider owner,
        ReloadingAssemblyReferenceLoader.AssemblyReferenceSnapshot snapshot,
        int generation, bool isComplete, IReadOnlyList<string> referencePaths)
    {
        _owner = owner;
        _snapshot = snapshot;
        Generation = generation;
        IsComplete = isComplete;
        ReferencePaths = referencePaths.ToImmutableArray();
    }

    public int Generation { get; }
    public bool IsComplete { get; }
    public IReadOnlyList<string> ReferencePaths { get; }
    public Type? Resolve(string fullName) => _snapshot.TryResolve(fullName);
    public IEnumerable<string> GetTypeNames() => _snapshot.GetAllPublicTypes()
        .Select(type => type.FullName).OfType<string>().ToImmutableArray();
    public IDisposable EnterScope() => _owner.EnterScope(this);
    public AnalysisMetadataView Retain() => new(_owner, _snapshot.Retain(), Generation, IsComplete, ReferencePaths);
    public void Dispose() => _snapshot.Dispose();
}
