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
            if (type.Name.StartsWith("<") || type.Name.Contains("+"))
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

    /// <summary>
    /// Generates TypeScript declarations for specified types in an assembly.
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly file</param>
    /// <param name="typeNames">List of type names to include (supports wildcards like "System.IO.*")</param>
    /// <returns>TypeScript declaration code</returns>
    public string GenerateForTypes(string assemblyPath, IEnumerable<string> typeNames)
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

        var metadataList = new List<TypeMetadata>();
        var patterns = typeNames.ToList();

        foreach (var type in assembly.GetExportedTypes())
        {
            // Skip compiler-generated types
            if (type.Name.StartsWith("<") || type.Name.Contains("+"))
                continue;

            // Skip generic type definitions for MVP
            if (type.IsGenericTypeDefinition)
                continue;

            // Check if type matches any pattern
            if (!MatchesAnyPattern(type.FullName ?? type.Name, patterns))
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

    private static bool MatchesAnyPattern(string typeName, List<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (MatchesPattern(typeName, pattern))
                return true;
        }
        return false;
    }

    private static bool MatchesPattern(string typeName, string pattern)
    {
        // Exact match
        if (pattern == typeName)
            return true;

        // Wildcard at end (e.g., "System.IO.*")
        if (pattern.EndsWith(".*"))
        {
            string prefix = pattern[..^2]; // Remove ".*"
            return typeName.StartsWith(prefix + ".");
        }

        // Wildcard anywhere (basic glob support)
        if (pattern.Contains('*'))
        {
            var regex = new System.Text.RegularExpressions.Regex(
                "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$");
            return regex.IsMatch(typeName);
        }

        return false;
    }
}
