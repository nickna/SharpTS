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

    // Module initialization methods (module path -> init method)
    // Used to ensure modules are initialized before their exports are accessed
    public Dictionary<string, MethodBuilder>? ModuleInitMethods { get; set; }

    // Module import fields (module path -> import name -> static field)
    // Used to store imported values so they're accessible from module functions
    public Dictionary<string, Dictionary<string, FieldBuilder>>? ModuleImportFields { get; set; }

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

    // ============================================
    // CommonJS Support
    // ============================================

    /// <summary>
    /// When emitting a CommonJS module's $Initialize body, points at the module's $exports
    /// static field. Used by ILEmitter to lower `module.exports`/`exports` reads and writes.
    /// Null when not currently inside a CJS module body.
    /// </summary>
    public FieldBuilder? CurrentCjsExportsField { get; set; }

    /// <summary>
    /// Maps each CommonJS module path → its $exports static field.
    /// Populated in Phase 4 (DefineCommonJsModuleType).
    /// </summary>
    public Dictionary<string, FieldBuilder>? CommonJsExportFields { get; set; }

    /// <summary>
    /// Maps each CommonJS module path → its $GetExports static method.
    /// Used by `require('./literal')` lowering to emit a direct static call.
    /// </summary>
    public Dictionary<string, MethodBuilder>? CommonJsGetExportsMethods { get; set; }

    // Protected path-keyed declaration names shared by all contexts in a compilation.
    internal IReadOnlyDictionary<string, string> ModuleNames { get; set; } = new Dictionary<string, string>();

    // Cache for standalone contexts without a collected module graph.
    private readonly Dictionary<string, string> _sanitizedModuleNameCache = [];

    /// <summary>
    /// Resolves a simple enum name to its qualified name for lookup in the EnumMembers dictionary.
    /// </summary>
    public string ResolveEnumName(string simpleEnumName)
    {
        if (CurrentNamespacePath != null)
        {
            var parts = CurrentNamespacePath.Split('.');
            for (int i = parts.Length; i >= 1; i--)
            {
                string key = QualifyNamespaceEnum(string.Join('.', parts.Take(i)), EnumModuleQualify(simpleEnumName));
                if (EnumMembers?.ContainsKey(key) == true) return key;
            }
        }
        string local = EnumModuleQualify(simpleEnumName);
        if (EnumMembers?.ContainsKey(local) == true) return local;
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
        string local = EnumModuleQualify(simpleEnumName);
        return CurrentNamespacePath == null ? local : QualifyNamespaceEnum(CurrentNamespacePath, local);
    }

    private string EnumModuleQualify(string name) => CurrentModulePath == null || IsScriptTopLevel
        ? name : $"$M_{GetSanitizedModuleName(CurrentModulePath)}_{name}";

    private static string QualifyNamespaceEnum(string path, string name) => $"$nsenum_{path.Replace('.', '_')}_{name}";

    public FieldBuilder? ResolveNamespaceEnumField(string name)
    {
        if (CurrentNamespacePath == null || NamespaceFields == null) return null;
        var parts = CurrentNamespacePath.Split('.');
        for (int i = parts.Length; i >= 1; i--)
        {
            string path = string.Join('.', parts.Take(i));
            if (EnumMembers?.ContainsKey(QualifyNamespaceEnum(path, EnumModuleQualify(name))) == true
                && NamespaceFields.TryGetValue(path, out var field)) return field;
        }
        return null;
    }

    /// <summary>
    /// Gets the sanitized module name with caching to avoid repeated string operations.
    /// </summary>
    internal string GetSanitizedModuleName(string modulePath)
    {
        if (ModuleNames.TryGetValue(modulePath, out var name))
            return name;
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
