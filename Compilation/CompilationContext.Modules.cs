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

    // Maps module path to qualified class name when module uses `export = ClassName`
    public Dictionary<string, string>? ExportAssignmentClasses { get; set; }

    // Maps local variable name to qualified class name for imported classes
    // Populated during import processing when an import alias refers to an exported class
    public Dictionary<string, string>? ImportedClassAliases { get; set; }

    /// <summary>
    /// Maps module path to a dictionary of export name to qualified class name.
    /// Used for resolving named class imports to direct constructor calls.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>>? ExportedClasses { get; set; }

    /// <summary>
    /// Maps module path to the qualified class name for default class exports.
    /// Used for resolving default class imports to direct constructor calls.
    /// </summary>
    public Dictionary<string, string>? DefaultExportClasses { get; set; }

    /// <summary>
    /// Maps namespace import alias to the module path.
    /// Used for resolving namespace-qualified class construction (e.g., new Utils.Person()).
    /// Example: NamespaceImports["Utils"] = "./utils.ts"
    /// </summary>
    public Dictionary<string, string>? NamespaceImports { get; set; }

    // Cache for sanitized module names to avoid repeated string operations
    private readonly Dictionary<string, string> _sanitizedModuleNameCache = [];

    /// <summary>
    /// Resolves a simple enum name to its qualified name for lookup in the EnumMembers dictionary.
    /// </summary>
    public string ResolveEnumName(string simpleEnumName)
    {
        if (EnumToModule != null && EnumToModule.TryGetValue(simpleEnumName, out var modulePath))
        {
            string sanitizedModule = GetSanitizedModuleName(modulePath);
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

        string sanitizedModule = GetSanitizedModuleName(CurrentModulePath);
        return $"$M_{sanitizedModule}_{simpleEnumName}";
    }

    /// <summary>
    /// Gets the sanitized module name with caching to avoid repeated string operations.
    /// </summary>
    private string GetSanitizedModuleName(string modulePath)
    {
        string filename = Path.GetFileNameWithoutExtension(modulePath);
        if (!_sanitizedModuleNameCache.TryGetValue(filename, out var sanitized))
        {
            sanitized = SanitizeModuleName(filename);
            _sanitizedModuleNameCache[filename] = sanitized;
        }
        return sanitized;
    }

    public static string SanitizeModuleName(string name)
    {
        return name.Replace("/", "_").Replace("\\", "_").Replace(".", "_").Replace("-", "_");
    }
}
