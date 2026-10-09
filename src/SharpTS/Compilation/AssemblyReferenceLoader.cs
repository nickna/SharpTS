using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using SharpTS.IO;

namespace SharpTS.Compilation;

/// <summary>
/// Loads and provides access to types from referenced .NET assemblies.
/// Uses MetadataLoadContext for safe inspection without runtime loading.
/// </summary>
public sealed class AssemblyReferenceLoader : IDisposable
{
    private static readonly ConditionalWeakTable<Assembly, SourceAssemblyPath> SourcePaths = new();
    private const string MetadataLoadContextJustification =
        "The reflected assembly is a MetadataLoadContext input loaded from an external file, not code linked into the native host. Only its metadata is inspected.";

    private readonly MetadataLoadContext _mlc;
    private readonly List<Assembly> _loadedAssemblies = [];
    private readonly ConcurrentDictionary<string, Type?> _typeCache = new();
    private readonly ConcurrentDictionary<string, string> _metadataFailures = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>
    /// Creates a new loader with the specified assembly paths.
    /// </summary>
    /// <param name="assemblyPaths">Paths to referenced assemblies.</param>
    /// <param name="sdkPath">Optional explicit path to SDK reference assemblies.</param>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "SingleFile",
        "IL3000",
        Justification = "Assembly.Location is only a managed-build fallback after SDK reference resolution; an empty single-file location is explicitly ignored.")]
    public AssemblyReferenceLoader(IEnumerable<string> assemblyPaths, string? sdkPath = null)
    {
        string[] references = assemblyPaths.ToArray();
        var paths = references.ToList();
        var frameworkPaths = new List<string>();

        // Add SDK reference assemblies for complete resolution
        var refAsmPath = sdkPath ?? SdkResolver.FindReferenceAssembliesPath();
        if (refAsmPath != null && Directory.Exists(refAsmPath))
        {
            frameworkPaths.AddRange(Directory.GetFiles(refAsmPath, "*.dll"));
        }
        else
        {
            // Fallback to runtime assemblies if SDK not found
            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            if (runtimeDir != null)
            {
                frameworkPaths.AddRange(Directory.GetFiles(runtimeDir, "*.dll"));
            }
        }

        paths.AddRange(frameworkPaths);
        _mlc = new MetadataLoadContext(new PathAssemblyResolver(paths.Distinct()), "System.Runtime");

        // Pre-load referenced assemblies (user-provided ones, not runtime)
        foreach (var path in references)
        {
            if (!File.Exists(path)) continue;

            try
            {
                var asm = _mlc.LoadFromAssemblyPath(path);
                _loadedAssemblies.Add(asm);
            }
            catch
            {
                // Skip assemblies that fail to load
            }
        }
    }

    internal AssemblyReferenceLoader(IEnumerable<string> assemblyPaths, CapturedAssemblySet images)
    {
        // This constructor performs no filesystem reads: the manager fingerprints precisely
        // these owned bytes before publishing the corresponding metadata generation.
        foreach (var (path, error) in images.ReadErrors)
            _metadataFailures[path] = error;
        _mlc = new MetadataLoadContext(new CapturedAssemblyResolver(images,
            (path, error) => _metadataFailures[path] = $"metadata unavailable: {path}: {error.Message}"), "System.Runtime");
        foreach (string path in assemblyPaths)
        {
            if (!images.Contains(path)) continue;
            try { _loadedAssemblies.Add(images.Load(_mlc, path)); }
            catch (Exception error) when (IsMetadataInputFailure(error))
            { _metadataFailures[path] = $"metadata unavailable: {path}: {error.Message}"; }
        }
    }

    internal IReadOnlyList<string> MetadataFailures => _metadataFailures.Values.Order(StringComparer.Ordinal).ToArray();

    internal static bool IsMetadataInputFailure(Exception error) => error is IOException or
        UnauthorizedAccessException or BadImageFormatException;

    /// <summary>The original assembly path, including byte-backed editor metadata captures.</summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "SingleFile", "IL3000", Justification = MetadataLoadContextJustification)]
    public static string GetSourcePath(Assembly assembly) =>
        SourcePaths.TryGetValue(assembly, out SourceAssemblyPath? source) ? source.Path : assembly.Location;

    private sealed record SourceAssemblyPath(string Path);

    /// <summary>Private byte ownership prevents rebuilds from locking or mutating a retained view.</summary>
    internal sealed class CapturedAssemblySet
    {
        private readonly Dictionary<string, byte[]> _images;
        private readonly Dictionary<string, string> _readErrors;

        private CapturedAssemblySet(Dictionary<string, byte[]> images, Dictionary<string, string> readErrors)
        { _images = images; _readErrors = readErrors; }

        public static CapturedAssemblySet Capture(IEnumerable<string> paths)
        {
            var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                CompilerFileSystem.ThrowIfCancellationRequested();
                try
                {
                    if (!CompilerFileSystem.FileExists(path))
                    { errors[path] = $"reference not found: {path}"; continue; }
                    using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var bytes = new MemoryStream();
                    input.CopyTo(bytes);
                    images[Path.GetFullPath(path)] = bytes.ToArray();
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { errors[path] = ReadFailure(path, error); }
            }
            CompilerFileSystem.ThrowIfCancellationRequested();
            return new CapturedAssemblySet(images, errors);
        }

        public CapturedAssemblySet WithFramework(CapturedAssemblySet framework)
        {
            var images = new Dictionary<string, byte[]>(_images, StringComparer.OrdinalIgnoreCase);
            foreach (var (path, bytes) in framework._images)
                images.TryAdd(path, bytes);
            var errors = new Dictionary<string, string>(_readErrors, StringComparer.OrdinalIgnoreCase);
            foreach (var (path, error) in framework._readErrors) errors.TryAdd(path, error);
            return new CapturedAssemblySet(images, errors);
        }

        public bool Contains(string path) => _images.ContainsKey(Path.GetFullPath(path));

        public CapturedAssemblySet WithFailure(string path, string error)
        {
            var errors = new Dictionary<string, string>(_readErrors, StringComparer.OrdinalIgnoreCase)
            { [path] = error };
            return new CapturedAssemblySet(_images, errors);
        }

        public string? ContentHash(string path) => _images.TryGetValue(Path.GetFullPath(path), out byte[]? bytes)
            ? Convert.ToHexString(SHA256.HashData(bytes))
            : _readErrors.TryGetValue(path, out string? error) && !error.StartsWith("reference not found:", StringComparison.Ordinal)
                ? error : null;

        public IReadOnlyDictionary<string, string> ReadErrors => _readErrors;

        internal static string ReadFailure(string path, Exception error) =>
            $"reference unreadable: {path}: {error.GetType().Name}: {error.HResult}: {error.Message}";

        public Assembly Load(MetadataLoadContext context, string path)
        {
            string absolute = Path.GetFullPath(path);
            Assembly assembly = context.LoadFromByteArray(_images[absolute]);
            SourcePaths.GetValue(assembly, _ => new SourceAssemblyPath(absolute));
            return assembly;
        }

        public IEnumerable<string> Paths => _images.Keys;
    }

    private sealed class CapturedAssemblyResolver(CapturedAssemblySet images,
        Action<string, Exception> reportFailure) : MetadataAssemblyResolver
    {
        public override Assembly? Resolve(MetadataLoadContext context, AssemblyName requested)
        {
            byte[] token = requested.GetPublicKeyToken() ?? [];
            // Match PathAssemblyResolver's name/token/version policy while loading exclusively
            // from captured bytes, including dependencies not yet inspected by the request.
            var candidates = images.Paths
                .Where(path => string.Equals(Path.GetFileNameWithoutExtension(path), requested.Name,
                    StringComparison.OrdinalIgnoreCase))
                .Select(path => TryLoad(context, path)).OfType<Assembly>()
                .Select(assembly => (Assembly: assembly, Name: assembly.GetName()))
                .Where(candidate => string.Equals(candidate.Name.Name, requested.Name,
                    StringComparison.OrdinalIgnoreCase))
                .Select(candidate => (candidate.Assembly, candidate.Name,
                    ExactToken: token.AsSpan().SequenceEqual(candidate.Name.GetPublicKeyToken() ?? [])))
                .Where(candidate => candidate.ExactToken || token.Length == 0 ||
                    requested.Flags.HasFlag(AssemblyNameFlags.Retargetable))
                .OrderByDescending(candidate => candidate.ExactToken)
                .ThenByDescending(candidate => candidate.Name.Version);
            return candidates.Select(candidate => candidate.Assembly).FirstOrDefault();
        }

        private Assembly? TryLoad(MetadataLoadContext context, string path)
        {
            try { return images.Load(context, path); }
            catch (Exception error) when (IsMetadataInputFailure(error))
            { reportFailure(path, error); return null; }
        }
    }

    /// <summary>
    /// Gets all loaded reference assemblies (user-provided, excluding runtime).
    /// </summary>
    public IReadOnlyList<Assembly> LoadedAssemblies => _loadedAssemblies;

    /// <summary>
    /// Attempts to resolve a type by its full name across all loaded assemblies.
    /// </summary>
    /// <param name="fullName">The fully-qualified type name (e.g., "System.Console").</param>
    /// <returns>The Type if found, null otherwise.</returns>
    public Type? TryResolve(string fullName)
    {
        return _typeCache.GetOrAdd(fullName, ResolveCore);
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = MetadataLoadContextJustification)]
    private Type? ResolveCore(string fullName)
    {
        // Search user references first
        foreach (var asm in _loadedAssemblies)
        {
            var type = asm.GetType(fullName);
            if (type != null) return type;
        }

        // Try loading from MetadataLoadContext (includes runtime assemblies)
        try
        {
            // Try common assemblies
            foreach (var asmName in new[] { "System.Runtime", "System.Console", "System.Collections", "mscorlib" })
            {
                try
                {
                    var asm = _mlc.LoadFromAssemblyName(asmName);
                    var type = asm.GetType(fullName);
                    if (type != null) return type;
                }
                catch
                {
                    // Assembly not available, continue
                }
            }
        }
        catch
        {
            // MetadataLoadContext resolution failed
        }

        return null;
    }

    /// <summary>
    /// Gets all public types from loaded reference assemblies.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = MetadataLoadContextJustification)]
    public IEnumerable<Type> GetAllPublicTypes()
    {
        foreach (var asm in _loadedAssemblies)
        {
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch
            {
                continue;
            }

            foreach (var type in types)
            {
                if (type.IsPublic || type.IsNestedPublic)
                    yield return type;
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _mlc.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Metadata about a .NET type for validation and code generation.
/// </summary>
public record TypeMetadata(
    Type Type,
    List<System.Reflection.ConstructorInfo> Constructors,
    List<MethodInfo> Methods,
    List<PropertyInfo> Properties
);
