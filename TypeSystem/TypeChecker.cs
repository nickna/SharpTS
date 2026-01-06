using SharpTS.Modules;
using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Static type analyzer that validates the AST before execution.
/// </summary>
/// <remarks>
/// Third stage of the compiler pipeline. Traverses the AST from <see cref="Parser"/> and
/// validates type compatibility, function signatures, class inheritance, and interface
/// implementations. Uses <see cref="TypeEnvironment"/> for scope tracking and <see cref="TypeInfo"/>
/// records for type representations. Supports both structural typing (interfaces) and nominal
/// typing (classes). Type checking runs at compile-time, completely separate from runtime
/// execution. Errors throw exceptions with "Type Error:" prefix.
///
/// This class is split across partial files:
/// <list type="bullet">
///   <item><description><c>TypeChecker.cs</c> - Core infrastructure, fields, entry points, module helpers</description></item>
///   <item><description><c>TypeChecker.Statements.cs</c> - Statement checking (CheckStmt, blocks, loops, functions, classes, enums)</description></item>
///   <item><description><c>TypeChecker.Expressions.cs</c> - Expression checking (CheckExpr, literals, arrays, objects, arrow functions)</description></item>
///   <item><description><c>TypeChecker.Properties.cs</c> - Property access (CheckGet, CheckSet, CheckNew, CheckThis, CheckSuper, indexing)</description></item>
///   <item><description><c>TypeChecker.Calls.cs</c> - Function calls (CheckCall, overload resolution)</description></item>
///   <item><description><c>TypeChecker.Operators.cs</c> - Operators (binary, unary, logical, compound assignment)</description></item>
///   <item><description><c>TypeChecker.Compatibility.cs</c> - Type compatibility (IsCompatible, structural typing, type guards)</description></item>
///   <item><description><c>TypeChecker.Generics.cs</c> - Generic types (instantiation, substitution, type inference)</description></item>
///   <item><description><c>TypeChecker.TypeParsing.cs</c> - Type string parsing (ToTypeInfo, union/intersection/tuple/function parsing)</description></item>
///   <item><description><c>TypeChecker.Validation.cs</c> - Validation (interface implementation, abstract members, override checking)</description></item>
/// </list>
/// </remarks>
/// <seealso cref="TypeEnvironment"/>
/// <seealso cref="TypeInfo"/>
public partial class TypeChecker
{
    private TypeEnvironment _environment = new();
    private TypeMap _typeMap = new();

    // We need to track the current function's expected return type to validate 'return' statements
    private TypeInfo? _currentFunctionReturnType = null;
    private TypeInfo.Class? _currentClass = null;
    private bool _inStaticMethod = false;
    // Track the declared 'this' type for explicit this parameter (e.g., function f(this: MyType) {})
    private TypeInfo? _currentFunctionThisType = null;
    private int _loopDepth = 0;
    private int _switchDepth = 0;
    // Track if we're inside an async function (for validating 'await' usage)
    private bool _inAsyncFunction = false;
    // Track if we're inside a generator function (for validating 'yield' usage)
    private bool _inGeneratorFunction = false;

    // Track active labels for labeled statements (label name -> isOnLoop)
    private readonly Dictionary<string, bool> _activeLabels = [];

    // Track pending overload signatures for top-level functions
    private readonly Dictionary<string, List<TypeInfo.Function>> _pendingOverloadSignatures = [];

    // Decorator mode configuration
    private DecoratorMode _decoratorMode = DecoratorMode.None;

    /// <summary>
    /// Sets the decorator mode for type checking decorators.
    /// </summary>
    public void SetDecoratorMode(DecoratorMode mode) => _decoratorMode = mode;

    // Module support - track the current module being type-checked
    private ParsedModule? _currentModule = null;
    private ModuleResolver? _moduleResolver = null;

    /// <summary>
    /// Type-checks the given statements and returns a TypeMap with resolved types for all expressions.
    /// </summary>
    /// <param name="statements">The AST statements to check.</param>
    /// <returns>A TypeMap containing the resolved type for each expression.</returns>
    public TypeMap Check(List<Stmt> statements)
    {
        // Pre-define built-ins
        _environment.Define("console", new TypeInfo.Any());
        _environment.Define("Reflect", new TypeInfo.Any());

        foreach (Stmt statement in statements)
        {
            CheckStmt(statement);
        }

        return _typeMap;
    }

    /// <summary>
    /// Type-checks multiple modules in dependency order.
    /// </summary>
    /// <param name="modules">Modules in dependency order (dependencies first)</param>
    /// <param name="resolver">Module resolver for path resolution</param>
    /// <returns>A TypeMap containing resolved types for all expressions across all modules</returns>
    public TypeMap CheckModules(List<ParsedModule> modules, ModuleResolver resolver)
    {
        _moduleResolver = resolver;

        // Pre-define built-ins in the global environment
        _environment.Define("console", new TypeInfo.Any());
        _environment.Define("Reflect", new TypeInfo.Any());

        // First pass: collect all exports from each module
        foreach (var module in modules)
        {
            _currentModule = module;
            CollectModuleExports(module);
        }

        // Second pass: type-check each module with imports resolved
        foreach (var module in modules)
        {
            if (module.IsTypeChecked)
            {
                continue;
            }

            _currentModule = module;
            var moduleEnv = new TypeEnvironment(_environment);

            // Bind imports from dependencies
            BindModuleImports(module, moduleEnv);

            // Type-check module body
            var savedEnv = _environment;
            _environment = moduleEnv;

            foreach (var stmt in module.Statements)
            {
                CheckStmt(stmt);
            }

            _environment = savedEnv;
            module.IsTypeChecked = true;
        }

        _currentModule = null;
        return _typeMap;
    }

    /// <summary>
    /// Collects exports from a module (first pass - just register export types).
    /// </summary>
    private void CollectModuleExports(ParsedModule module)
    {
        var moduleEnv = new TypeEnvironment(_environment);
        var savedEnv = _environment;
        _environment = moduleEnv;

        // First, bind imports so we can reference imported types in our declarations
        BindModuleImports(module, moduleEnv);

        // Then, process all declarations to populate the environment
        foreach (var stmt in module.Statements)
        {
            // Skip imports - already bound above
            if (stmt is Stmt.Import)
            {
                continue;
            }

            // For exports, process the underlying declaration
            if (stmt is Stmt.Export export)
            {
                if (export.Declaration != null)
                {
                    CheckStmt(export.Declaration);
                }
                else if (export.DefaultExpr != null)
                {
                    var type = CheckExpr(export.DefaultExpr);
                    module.DefaultExportType = type;
                }
                else if (export.NamedExports != null && export.FromModulePath == null)
                {
                    // Named exports like `export { x, y }` need the declarations to be processed first
                    // They'll be resolved in the second pass
                }
            }
            else
            {
                // Regular declarations
                CheckStmt(stmt);
            }
        }

        // Now collect exports
        foreach (var stmt in module.Statements)
        {
            if (stmt is Stmt.Export export)
            {
                if (export.IsDefaultExport)
                {
                    if (export.Declaration != null)
                    {
                        module.DefaultExportType = GetDeclaredType(export.Declaration);
                    }
                    // DefaultExpr already handled above
                }
                else if (export.Declaration != null)
                {
                    string name = GetDeclarationName(export.Declaration);
                    var type = GetDeclaredType(export.Declaration);
                    module.ExportedTypes[name] = type;
                }
                else if (export.NamedExports != null && export.FromModulePath == null)
                {
                    foreach (var spec in export.NamedExports)
                    {
                        var type = _environment.Get(spec.LocalName.Lexeme);
                        if (type != null)
                        {
                            string exportedName = spec.ExportedName?.Lexeme ?? spec.LocalName.Lexeme;
                            module.ExportedTypes[exportedName] = type;
                        }
                    }
                }
                else if (export.FromModulePath != null)
                {
                    // Re-export - resolve from the source module
                    string sourcePath = _moduleResolver!.ResolveModulePath(export.FromModulePath, module.Path);
                    var sourceModule = _moduleResolver.GetCachedModule(sourcePath);

                    if (sourceModule != null)
                    {
                        if (export.NamedExports != null)
                        {
                            // Re-export specific names
                            foreach (var spec in export.NamedExports)
                            {
                                if (sourceModule.ExportedTypes.TryGetValue(spec.LocalName.Lexeme, out var type))
                                {
                                    string exportedName = spec.ExportedName?.Lexeme ?? spec.LocalName.Lexeme;
                                    module.ExportedTypes[exportedName] = type;
                                }
                            }
                        }
                        else
                        {
                            // Re-export all: export * from './module'
                            foreach (var (name, type) in sourceModule.ExportedTypes)
                            {
                                module.ExportedTypes[name] = type;
                            }
                        }
                    }
                }
            }
        }

        _environment = savedEnv;
    }

    /// <summary>
    /// Binds imported symbols from dependencies into the module's environment.
    /// </summary>
    private void BindModuleImports(ParsedModule module, TypeEnvironment env)
    {
        foreach (var stmt in module.Statements)
        {
            if (stmt is Stmt.Import import)
            {
                string importedPath = _moduleResolver!.ResolveModulePath(import.ModulePath, module.Path);
                var importedModule = _moduleResolver.GetCachedModule(importedPath);

                if (importedModule == null)
                {
                    throw new Exception($"Type Error at line {import.Keyword.Line}: Cannot find module '{import.ModulePath}'.");
                }

                // Default import
                if (import.DefaultImport != null)
                {
                    if (importedModule.DefaultExportType == null)
                    {
                        throw new Exception($"Type Error at line {import.Keyword.Line}: Module '{import.ModulePath}' has no default export.");
                    }
                    env.Define(import.DefaultImport.Lexeme, importedModule.DefaultExportType);
                }

                // Namespace import: import * as Module from './file'
                if (import.NamespaceImport != null)
                {
                    // Create a record type with all exports
                    var namespaceType = new TypeInfo.Record(
                        new Dictionary<string, TypeInfo>(importedModule.ExportedTypes)
                    );
                    env.Define(import.NamespaceImport.Lexeme, namespaceType);
                }

                // Named imports: import { x, y as z } from './file'
                if (import.NamedImports != null)
                {
                    foreach (var spec in import.NamedImports)
                    {
                        string importedName = spec.Imported.Lexeme;
                        string localName = spec.LocalName?.Lexeme ?? importedName;

                        if (!importedModule.ExportedTypes.TryGetValue(importedName, out var type))
                        {
                            throw new Exception($"Type Error at line {import.Keyword.Line}: Module '{import.ModulePath}' has no export named '{importedName}'.");
                        }

                        env.Define(localName, type);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets the name of a declaration (function, class, variable, etc.)
    /// </summary>
    private string GetDeclarationName(Stmt decl)
    {
        return decl switch
        {
            Stmt.Function f => f.Name.Lexeme,
            Stmt.Class c => c.Name.Lexeme,
            Stmt.Var v => v.Name.Lexeme,
            Stmt.Interface i => i.Name.Lexeme,
            Stmt.TypeAlias t => t.Name.Lexeme,
            Stmt.Enum e => e.Name.Lexeme,
            _ => throw new Exception($"Type Error: Cannot get name of declaration type {decl.GetType().Name}")
        };
    }

    /// <summary>
    /// Gets the type of a declaration.
    /// </summary>
    private TypeInfo GetDeclaredType(Stmt decl)
    {
        return decl switch
        {
            Stmt.Function f => _environment.Get(f.Name.Lexeme) ?? new TypeInfo.Any(),
            Stmt.Class c => _environment.Get(c.Name.Lexeme) ?? new TypeInfo.Any(),
            Stmt.Var v => _environment.Get(v.Name.Lexeme) ?? new TypeInfo.Any(),
            Stmt.Interface i => _environment.Get(i.Name.Lexeme) ?? new TypeInfo.Any(),
            Stmt.TypeAlias t => ToTypeInfo(t.TypeDefinition),
            Stmt.Enum e => _environment.Get(e.Name.Lexeme) ?? new TypeInfo.Any(),
            _ => new TypeInfo.Any()
        };
    }
}
