using SharpTS.Diagnostics;
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
    /// Recoverable syntax diagnostics produced while parsing this module. Empty during
    /// normal product loading, which remains fail-fast; conformance and editor-style
    /// callers can opt into retaining the partial AST and these diagnostics.
    /// </summary>
    public List<Diagnostic> ParseDiagnostics { get; } = [];

    /// <summary>
    /// True for a synthetic module created from <c>declare module "name"</c>.
    /// Declarations inside these modules are implicitly exported by TypeScript.
    /// </summary>
    public bool IsAmbientModule { get; set; }

    /// <summary>
    /// The text this module was parsed from, together with its line index, content checksum, and
    /// the source spans of <see cref="Statements"/>.
    /// </summary>
    /// <remarks>
    /// Null for modules that have no text of their own — built-in and <c>dotnet:</c> interop
    /// modules — and for callers that parsed without retaining the source. Consumers that need a
    /// position must handle its absence rather than assume every module is source-backed.
    /// </remarks>
    public SourceDocument? Document { get; set; }

    /// <summary>
    /// Direct dependencies of this module (modules it imports).
    /// </summary>
    public List<ParsedModule> Dependencies { get; } = [];

    /// <summary>
    /// Executable counterparts of declaration dependencies. TypeScript resolves a package
    /// import through its <c>types</c> entry for checking while Node resolves the same
    /// specifier through <c>import</c>/<c>require</c> for execution.
    /// </summary>
    public List<ParsedModule> RuntimeDependencies { get; } = [];

    /// <summary>
    /// Named exports from this module (name -> type).
    /// Populated during type checking.
    /// </summary>
    public Dictionary<string, TypeInfo> ExportedTypes { get; } = [];

    /// <summary>
    /// Checker binding identities for named value exports. These preserve declaration provenance
    /// through imports and re-exports for editor features; runtime export behavior remains in
    /// <see cref="ExportedValues"/>.
    /// </summary>
    public Dictionary<string, BindingSymbol> ExportedValueBindings { get; } = [];

    /// <summary>
    /// Checker binding identities for named type exports. TypeScript's type and value facets are
    /// tracked independently because declarations such as classes and enums participate in both.
    /// </summary>
    public Dictionary<string, BindingSymbol> ExportedTypeBindings { get; } = [];

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

    /// <summary>Binding provenance for the default export's value facet, when source-backed.</summary>
    public BindingSymbol? DefaultValueBinding { get; set; }

    /// <summary>Binding provenance for the default export's type facet, when source-backed.</summary>
    public BindingSymbol? DefaultTypeBinding { get; set; }

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
    /// True when the module was discovered only through a literal dynamic import
    /// and is compiled for on-demand initialization rather than the entry graph.
    /// </summary>
    public bool IsDynamicImportOnly { get; set; }

    /// <summary>
    /// Whether the module has been type-checked.
    /// </summary>
    public bool IsTypeChecked { get; set; }

    /// <summary>
    /// Whether this is a built-in module (fs, path, os, etc.).
    /// Built-in modules are not loaded from files.
    /// </summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>True for a <c>.d.ts</c>/<c>.d.mts</c>/<c>.d.cts</c> input.</summary>
    public bool IsDeclarationFile { get; set; }

    /// <summary>
    /// True for an embedded TypeScript <c>lib.*.d.ts</c>. These files are trusted
    /// compiler inputs and never emitted or executed.
    /// </summary>
    public bool IsDefaultLibrary { get; set; }

    /// <summary>Whether this file carries <c>/// &lt;reference no-default-lib="true"/&gt;</c>.</summary>
    public bool NoDefaultLib { get; set; }

    /// <summary>
    /// For <c>dotnet:</c>-scheme modules only: exported names mapped to their resolved CLR
    /// types. Null for every other module kind. Populated incrementally by
    /// <see cref="DotNetImports.EnsureImports"/> as import statements naming this module are
    /// discovered — the export surface of a <c>dotnet:</c> namespace module is defined by what
    /// programs import from it, not by enumerating the namespace.
    /// </summary>
    public Dictionary<string, Type>? DotNetExports { get; set; }

    /// <summary>True if this is a synthesized <c>dotnet:</c>-scheme interop module.</summary>
    public bool IsDotNetModule => DotNetExports != null;

    /// <summary>
    /// For a <c>dotnet-extensions:</c> virtual module, the public static container whose
    /// extension methods the importing source module activates.
    /// </summary>
    public Type? DotNetExtensionContainer { get; set; }

    /// <summary>Extension containers activated only within this source module.</summary>
    public List<Type> DotNetExtensionTypes { get; } = [];

    public bool IsDotNetExtensionModule => DotNetExtensionContainer != null;

    /// <summary>
    /// True if file has no import/export statements (is a "script" file).
    /// Scripts share global scope; modules have isolated scope.
    /// </summary>
    public bool IsScript { get; set; }

    /// <summary>
    /// True if file is a CommonJS module (uses require/module.exports semantics).
    /// Determined by <see cref="CommonJsDetector"/> at load time.
    /// </summary>
    public bool IsCommonJs { get; set; }

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

    public override string ToString() => $"Module({ModuleName})";
}
