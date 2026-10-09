using SharpTS.IO;

namespace SharpTS.LanguageServer.Services;

/// <summary>
/// Memoizes every disk read/probe made by one build. Repeated reads see the same captured value;
/// publication and reuse compare those values with disk, including failed resolution probes.
/// </summary>
internal sealed class ObservingCompilerFileSystem : ICompilerFileSystem
{
    private readonly Dictionary<InputKey, Observation> _inputs = [];
    private bool _canCache = true;

    public bool FileExists(string path) => Capture(
        new(InputKind.File, Normalize(path)), () => CompilerFileSystem.Physical.FileExists(path));

    public bool DirectoryExists(string path) => Capture(
        new(InputKind.Directory, Normalize(path)), () => CompilerFileSystem.Physical.DirectoryExists(path));

    public string ReadAllText(string path) => Capture(
        new(InputKind.Text, Normalize(path)), () => CompilerFileSystem.Physical.ReadAllText(path));

    public FileAttributes GetAttributes(string path) => Capture(
        new(InputKind.Attributes, Normalize(path)), () => CompilerFileSystem.Physical.GetAttributes(path));

    public IReadOnlyList<string> EnumerateFiles(string path, string searchPattern, EnumerationOptions options) =>
        Enumerate(InputKind.Files, path, searchPattern, options);

    public IReadOnlyList<string> EnumerateDirectories(string path, string searchPattern, EnumerationOptions options) =>
        Enumerate(InputKind.Directories, path, searchPattern, options);

    public ObservedCompilerInputs Complete() => new(_inputs.Values.ToArray(), _canCache);

    public void Include(ObservedCompilerInputs inputs)
    {
        foreach (Observation observation in inputs.Observations)
        {
            if (_inputs.TryGetValue(observation.Key, out Observation? existing) &&
                !existing.SameValue(observation))
            {
                // A reused semantic component and this build observed different disk states.
                throw new AnalysisInputsChangedException();
            }
            _inputs[observation.Key] = observation;
        }
        _canCache &= inputs.CanCache;
    }

    private IReadOnlyList<string> Enumerate(
        InputKind kind, string path, string pattern, EnumerationOptions options)
    {
        var policy = EnumerationPolicy.From(options);
        return Capture(new(kind, Normalize(path), pattern, policy), () =>
        {
            IReadOnlyList<string> values = kind == InputKind.Files
                ? CompilerFileSystem.Physical.EnumerateFiles(path, pattern, policy.Create())
                : CompilerFileSystem.Physical.EnumerateDirectories(path, pattern, policy.Create());
            return Array.AsReadOnly(values.Order(StringComparer.Ordinal).ToArray());
        });
    }

    private T Capture<T>(InputKey key, Func<T> read) where T : notnull
    {
        CompilerFileSystem.ThrowIfCancellationRequested();
        if (_inputs.TryGetValue(key, out Observation? previous))
            return (T)previous.Value;
        try
        {
            T value = read();
            _inputs.Add(key, new Observation(key, value, () => read()));
            return value;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // A recovered access/IO failure must never become a permanently cached assumption.
            _canCache = false;
            throw;
        }
    }

    private static string Normalize(string path)
    {
        string absolute = Path.GetFullPath(path);
        return OperatingSystem.IsWindows() ? absolute.ToUpperInvariant() : absolute;
    }

    internal enum InputKind { File, Directory, Text, Attributes, Files, Directories }
    internal sealed record InputKey(InputKind Kind, string Path, string? Pattern = null, EnumerationPolicy? Policy = null);

    internal sealed record EnumerationPolicy(
        bool Recurse, bool IgnoreInaccessible, FileAttributes Skip, MatchType MatchType,
        MatchCasing MatchCasing, bool ReturnSpecial, int MaxDepth, int BufferSize)
    {
        public static EnumerationPolicy From(EnumerationOptions options) => new(
            options.RecurseSubdirectories, options.IgnoreInaccessible, options.AttributesToSkip,
            options.MatchType, options.MatchCasing, options.ReturnSpecialDirectories,
            options.MaxRecursionDepth, options.BufferSize);

        public EnumerationOptions Create() => new()
        {
            RecurseSubdirectories = Recurse, IgnoreInaccessible = IgnoreInaccessible,
            AttributesToSkip = Skip, MatchType = MatchType, MatchCasing = MatchCasing,
            ReturnSpecialDirectories = ReturnSpecial, MaxRecursionDepth = MaxDepth, BufferSize = BufferSize,
        };
    }

    internal sealed record Observation(InputKey Key, object Value, Func<object> ReadCurrent)
    {
        public bool IsCurrent() => Equal(Value, ReadCurrent());
        public bool SameValue(Observation other) => Equal(Value, other.Value);
        private static bool Equal(object left, object right) =>
            left is IReadOnlyList<string> paths && right is IReadOnlyList<string> otherPaths
                ? paths.SequenceEqual(otherPaths, StringComparer.Ordinal)
                : left.Equals(right);
    }
}

internal sealed class ObservedCompilerInputs(
    IReadOnlyList<ObservingCompilerFileSystem.Observation> observations,
    bool canCache)
{
    internal IReadOnlyList<ObservingCompilerFileSystem.Observation> Observations { get; } = observations;
    public bool CanCache { get; } = canCache;
    public int Count => Observations.Count;
    public long EstimatedBytes => Observations.Sum(observation => observation.Value switch
    {
        string text => (long)text.Length * sizeof(char),
        IReadOnlyList<string> paths => paths.Sum(path => (long)path.Length * sizeof(char)),
        _ => 32L,
    });

    public bool IsCurrent(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = CompilerFileSystem.Use(CompilerFileSystem.Physical, cancellationToken);
            foreach (var observation in Observations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!observation.IsCurrent()) return false;
            }
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}

internal sealed class AnalysisInputsChangedException : Exception { }
