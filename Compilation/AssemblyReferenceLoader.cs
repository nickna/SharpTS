using System.Collections.Concurrent;
using System.Reflection;

namespace SharpTS.Compilation;

/// <summary>
/// Loads and provides access to types from referenced .NET assemblies.
/// Uses MetadataLoadContext for safe inspection without runtime loading.
/// </summary>
public sealed class AssemblyReferenceLoader : IDisposable
{
    private readonly MetadataLoadContext _mlc;
    private readonly List<Assembly> _loadedAssemblies = [];
    private readonly ConcurrentDictionary<string, Type?> _typeCache = new();
    private bool _disposed;

    /// <summary>
    /// Creates a new loader with the specified assembly paths.
    /// </summary>
    /// <param name="assemblyPaths">Paths to referenced assemblies.</param>
    /// <param name="sdkPath">Optional explicit path to SDK reference assemblies.</param>
    public AssemblyReferenceLoader(IEnumerable<string> assemblyPaths, string? sdkPath = null)
    {
        var paths = assemblyPaths.ToList();

        // Add SDK reference assemblies for complete resolution
        var refAsmPath = sdkPath ?? SdkResolver.FindReferenceAssembliesPath();
        if (refAsmPath != null && Directory.Exists(refAsmPath))
        {
            paths.AddRange(Directory.GetFiles(refAsmPath, "*.dll"));
        }
        else
        {
            // Fallback to runtime assemblies if SDK not found
            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            if (runtimeDir != null)
            {
                paths.AddRange(Directory.GetFiles(runtimeDir, "*.dll"));
            }
        }

        var resolver = new PathAssemblyResolver(paths.Distinct());
        _mlc = new MetadataLoadContext(resolver, "System.Runtime");

        // Pre-load referenced assemblies (user-provided ones, not runtime)
        foreach (var path in assemblyPaths)
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

    /// <summary>
    /// Checks if a type with the given name exists in any loaded assembly.
    /// </summary>
    public bool TypeExists(string fullName)
    {
        return TryResolve(fullName) != null;
    }

    /// <summary>
    /// Gets type metadata for validation purposes (methods, properties, etc.).
    /// </summary>
    public TypeMetadata? GetTypeMetadata(string fullName)
    {
        var type = TryResolve(fullName);
        if (type == null) return null;

        return new TypeMetadata(
            type,
            type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).ToList(),
            type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .ToList(),
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).ToList()
        );
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
