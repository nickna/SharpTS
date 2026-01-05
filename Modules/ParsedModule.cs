using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Modules;

/// <summary>
/// Represents a parsed TypeScript module with its exports and dependencies.
/// </summary>
/// <remarks>
/// Used by <see cref="ModuleResolver"/> to track loaded modules and their relationships.
/// Populated with export information during type checking and used by the interpreter
/// and IL compiler for module execution and compilation.
/// </remarks>
public class ParsedModule
{
    /// <summary>
    /// Absolute file path to the module.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Module name derived from filename (without extension).
    /// </summary>
    public string ModuleName { get; }

    /// <summary>
    /// Parsed AST statements from the module.
    /// </summary>
    public List<Stmt> Statements { get; }

    /// <summary>
    /// Direct dependencies of this module (modules it imports).
    /// </summary>
    public List<ParsedModule> Dependencies { get; } = [];

    /// <summary>
    /// Named exports from this module (name -> type).
    /// Populated during type checking.
    /// </summary>
    public Dictionary<string, TypeInfo> ExportedTypes { get; } = [];

    /// <summary>
    /// Named exports from this module (name -> runtime value).
    /// Populated during interpretation.
    /// </summary>
    public Dictionary<string, object?> ExportedValues { get; } = [];

    /// <summary>
    /// Default export type, if any.
    /// Populated during type checking.
    /// </summary>
    public TypeInfo? DefaultExportType { get; set; }

    /// <summary>
    /// Default export value, if any.
    /// Populated during interpretation.
    /// </summary>
    public object? DefaultExportValue { get; set; }

    /// <summary>
    /// Whether the module has been executed (for interpreter).
    /// </summary>
    public bool IsExecuted { get; set; }

    /// <summary>
    /// Whether the module has been type-checked.
    /// </summary>
    public bool IsTypeChecked { get; set; }

    public ParsedModule(string path, List<Stmt> statements)
    {
        Path = path;
        ModuleName = System.IO.Path.GetFileNameWithoutExtension(path);
        Statements = statements;
    }

    /// <summary>
    /// Creates a sanitized name suitable for use as a .NET type name.
    /// </summary>
    public string GetSanitizedTypeName()
    {
        return new string(ModuleName.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    }

    public override string ToString() => $"Module({ModuleName})";
}
