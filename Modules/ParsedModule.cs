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
    /// True if module uses 'export = value' syntax (CommonJS-style).
    /// When true, the module has a single export assignment instead of named exports.
    /// </summary>
    public bool HasExportAssignment { get; set; }

    /// <summary>
    /// Type of the export assignment value (for type checker).
    /// Only set when HasExportAssignment is true.
    /// </summary>
    public TypeInfo? ExportAssignmentType { get; set; }

    /// <summary>
    /// Runtime value of export assignment (for interpreter).
    /// Only set when HasExportAssignment is true.
    /// </summary>
    public object? ExportAssignmentValue { get; set; }

    /// <summary>
    /// Whether the module has been executed (for interpreter).
    /// </summary>
    public bool IsExecuted { get; set; }

    /// <summary>
    /// Whether the module has been type-checked.
    /// </summary>
    public bool IsTypeChecked { get; set; }

    /// <summary>
    /// Whether this is a built-in module (fs, path, os, etc.).
    /// Built-in modules are not loaded from files.
    /// </summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// True if file has no import/export statements (is a "script" file).
    /// Scripts share global scope; modules have isolated scope.
    /// </summary>
    public bool IsScript { get; set; }

    /// <summary>
    /// Triple-slash path references from this file.
    /// Only applicable for script files.
    /// </summary>
    public List<TripleSlashDirective> PathReferences { get; set; } = [];

    /// <summary>
    /// Files referenced via /// &lt;reference path="..." /&gt;.
    /// These are processed differently from module Dependencies.
    /// Referenced scripts merge into the global scope rather than having isolated scope.
    /// </summary>
    public List<ParsedModule> ReferencedScripts { get; set; } = [];

    /// <summary>
    /// Module augmentations declared in this file (declare module 'path' { ... }).
    /// Key: the module path string (as specified in the declare module statement).
    /// Value: list of member statements from the augmentation block.
    /// </summary>
    public Dictionary<string, List<Stmt>> ModuleAugmentations { get; } = [];

    /// <summary>
    /// Global augmentation declarations in this file (declare global { ... }).
    /// Contains statements to merge into the global scope.
    /// </summary>
    public List<Stmt> GlobalAugmentations { get; } = [];

    /// <summary>
    /// Ambient module declarations (for packages without source).
    /// Key: module specifier (e.g., 'lodash'), Value: member statements.
    /// These provide type information for external packages.
    /// </summary>
    public Dictionary<string, List<Stmt>> AmbientModules { get; } = [];

    /// <summary>
    /// Reverse reference - which modules augment this one.
    /// Populated during type checking when augmentations are applied.
    /// </summary>
    public List<ParsedModule> AugmentedBy { get; } = [];

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
