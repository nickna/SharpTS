using System.Reflection.Emit;
using SharpTS.Modules;

namespace SharpTS.Compilation;

public partial class CompilationContext
{
    // ============================================
    // Module Support
    // ============================================

    // Current module path being compiled
    public string? CurrentModulePath { get; set; }

    // Module export fields (module path -> export name -> FieldBuilder)
    public Dictionary<string, Dictionary<string, FieldBuilder>>? ModuleExportFields { get; set; }

    // Module types (module path -> TypeBuilder)
    public Dictionary<string, TypeBuilder>? ModuleTypes { get; set; }

    // Module resolver for import path resolution
    public ModuleResolver? ModuleResolver { get; set; }

    // .NET namespace from @Namespace directive (for typed interop)
    public string? DotNetNamespace { get; set; }

    // Class to module mapping (simple class name -> module path)
    // Used to resolve qualified class names in multi-module compilation
    public Dictionary<string, string>? ClassToModule { get; set; }

    // Function to module mapping (simple function name -> module path)
    public Dictionary<string, string>? FunctionToModule { get; set; }

    // Enum to module mapping (simple enum name -> module path)
    public Dictionary<string, string>? EnumToModule { get; set; }

    /// <summary>
    /// Resolves a simple enum name to its qualified name for lookup in the EnumMembers dictionary.
    /// </summary>
    public string ResolveEnumName(string simpleEnumName)
    {
        if (EnumToModule != null && EnumToModule.TryGetValue(simpleEnumName, out var modulePath))
        {
            string sanitizedModule = SanitizeModuleName(Path.GetFileNameWithoutExtension(modulePath));
            return $"$M_{sanitizedModule}_{simpleEnumName}";
        }
        return simpleEnumName;
    }

    /// <summary>
    /// Gets the qualified enum name for the current module context.
    /// </summary>
    public string GetQualifiedEnumName(string simpleEnumName)
    {
        if (CurrentModulePath == null)
            return simpleEnumName;

        string sanitizedModule = SanitizeModuleName(Path.GetFileNameWithoutExtension(CurrentModulePath));
        return $"$M_{sanitizedModule}_{simpleEnumName}";
    }

    public static string SanitizeModuleName(string name)
    {
        return name.Replace("/", "_").Replace("\\", "_").Replace(".", "_").Replace("-", "_");
    }
}
