using System.Reflection;

namespace SharpTS.Declaration;

/// <summary>
/// Generates TypeScript declaration files from .NET types or assemblies.
/// </summary>
public class DeclarationGenerator
{
    private readonly TypeInspector _inspector = new();
    private readonly TypeScriptEmitter _emitter = new();

    /// <summary>
    /// Generates a TypeScript declaration for a single type by name.
    /// </summary>
    /// <param name="typeName">The fully-qualified type name (e.g., "System.Console")</param>
    /// <returns>TypeScript declaration code</returns>
    public string GenerateForType(string typeName)
    {
        // Try to resolve the type
        Type? type = ResolveType(typeName);
        if (type == null)
        {
            throw new ArgumentException($"Type '{typeName}' could not be found.");
        }

        var metadata = _inspector.Inspect(type);
        return _emitter.Emit(metadata);
    }

    /// <summary>
    /// Generates TypeScript declarations for all public types in an assembly.
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly file</param>
    /// <returns>TypeScript declaration code</returns>
    public string GenerateForAssembly(string assemblyPath)
    {
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"Assembly not found: {assemblyPath}");
        }

        Assembly assembly;
        try
        {
            assembly = Assembly.LoadFrom(assemblyPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load assembly: {ex.Message}", ex);
        }

        return GenerateForAssembly(assembly);
    }

    /// <summary>
    /// Generates TypeScript declarations for all public types in an assembly.
    /// </summary>
    public string GenerateForAssembly(Assembly assembly)
    {
        var metadataList = new List<TypeMetadata>();

        foreach (var type in assembly.GetExportedTypes())
        {
            // Skip compiler-generated types
            if (type.Name.StartsWith("<"))
                continue;

            // Skip nested types
            if (type.IsNested)
                continue;

            // Skip generic type definitions for MVP
            if (type.IsGenericTypeDefinition)
                continue;

            try
            {
                var metadata = _inspector.Inspect(type);
                metadataList.Add(metadata);
            }
            catch
            {
                // Skip types that fail inspection
            }
        }

        return _emitter.EmitAll(metadataList);
    }

    private static Type? ResolveType(string typeName)
    {
        // First try direct resolution
        Type? type = Type.GetType(typeName);
        if (type != null)
            return type;

        // Try with common assembly qualifiers
        var commonAssemblies = new[]
        {
            "mscorlib",
            "System",
            "System.Core",
            "System.Runtime",
            "System.Console",
            "System.Collections",
            "System.Linq",
            "System.IO",
            "System.Net.Http"
        };

        foreach (var asmName in commonAssemblies)
        {
            try
            {
                type = Type.GetType($"{typeName}, {asmName}");
                if (type != null)
                    return type;
            }
            catch
            {
                // Continue trying other assemblies
            }
        }

        // Try loading assemblies from the current domain
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                type = assembly.GetType(typeName);
                if (type != null)
                    return type;
            }
            catch
            {
                // Continue with other assemblies
            }
        }

        return null;
    }

}
