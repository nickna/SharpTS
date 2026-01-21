using System.Collections.Frozen;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

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
///   <item><description><c>TypeChecker.Statements.cs</c> - Main CheckStmt dispatch and simple statement handlers</description></item>
///   <item><description><c>TypeChecker.Statements.Classes.cs</c> - Class declaration checking</description></item>
///   <item><description><c>TypeChecker.Statements.Interfaces.cs</c> - Interface declaration checking</description></item>
///   <item><description><c>TypeChecker.Statements.Functions.cs</c> - Function declaration and overload handling</description></item>
///   <item><description><c>TypeChecker.Statements.Enums.cs</c> - Enum declaration with const enum support</description></item>
///   <item><description><c>TypeChecker.Statements.ControlFlow.cs</c> - Block, switch, try/catch checking</description></item>
///   <item><description><c>TypeChecker.Statements.Modules.cs</c> - Export statement checking</description></item>
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
    private bool _inStaticBlock = false;
    // Track the declared 'this' type for explicit this parameter (e.g., function f(this: MyType) {})
    private TypeInfo? _currentFunctionThisType = null;

    // Memoization cache for IsCompatible checks - cleared per Check() call
    private Dictionary<(TypeInfo Expected, TypeInfo Actual), bool>? _compatibilityCache;

    // Error recovery support
    private readonly List<TypeCheckError> _errors = [];
    private const int MaxErrors = 10;

    /// <summary>
    /// RAII-style helper for safely managing TypeEnvironment scope changes.
    /// Automatically restores the previous environment on disposal, even if an exception is thrown.
    /// </summary>
    /// <remarks>
    /// Usage: using var _ = new EnvironmentScope(this, newEnvironment);
    /// This ensures _environment is always restored when the scope exits, preventing corruption
    /// if type checking throws an exception during the scope's lifetime.
    /// </remarks>
    private readonly struct EnvironmentScope : IDisposable
    {
        private readonly TypeChecker _checker;
        private readonly TypeEnvironment _previous;

        public EnvironmentScope(TypeChecker checker, TypeEnvironment newEnv)
        {
            _checker = checker;
            _previous = checker._environment;
            checker._environment = newEnv;
        }

        public void Dispose() => _checker._environment = _previous;
    }

    /// <summary>
    /// Builds a function signature by parsing parameters and validating optional/required ordering.
    /// </summary>
    /// <param name="parameters">Function/method parameters to parse</param>
    /// <param name="validateDefaults">Whether to type-check default parameter values</param>
    /// <param name="contextName">Context name for error messages (e.g., "method 'foo'" or "function 'bar'")</param>
    /// <returns>Tuple of (parameter types, required parameter count, has rest parameter, parameter names)</returns>
    private (List<TypeInfo> paramTypes, int requiredParams, bool hasRest, List<string> paramNames) BuildFunctionSignature(
        List<Stmt.Parameter> parameters,
        bool validateDefaults,
        string contextName)
    {
        List<TypeInfo> paramTypes = [];
        List<string> paramNames = [];
        int requiredParams = 0;
        bool seenDefault = false;

        foreach (var param in parameters)
        {
            TypeInfo paramType = param.Type != null ? ToTypeInfo(param.Type) : new TypeInfo.Any();
            paramTypes.Add(paramType);
            paramNames.Add(param.Name.Lexeme);

            if (param.IsRest) continue;

            bool isOptional = param.DefaultValue != null || param.IsOptional;

            if (param.DefaultValue != null)
            {
                seenDefault = true;
                if (validateDefaults)
                {
                    TypeInfo defaultType = CheckExpr(param.DefaultValue);
                    if (!IsCompatible(paramType, defaultType))
                    {
                        throw new TypeMismatchException($"Default value type is not assignable to parameter type in {contextName}", paramType, defaultType);
                    }
                }
            }
            else if (param.IsOptional)
            {
                seenDefault = true;
            }
            else
            {
                if (seenDefault)
                {
                    throw new TypeCheckException($"Required parameter cannot follow optional parameter in {contextName}");
                }
                requiredParams++;
            }
        }

        bool hasRest = parameters.Any(p => p.IsRest);
        return (paramTypes, requiredParams, hasRest, paramNames);
    }

    /// <summary>
    /// Widens literal types to their base primitive types for mutable variable inference.
    /// In TypeScript, `let x = 1` infers `number`, not literal type `1`.
    /// Const bindings preserve narrower types (handled separately).
    /// </summary>
    /// <param name="type">The type to potentially widen</param>
    /// <param name="isConst">Whether this is a const binding (preserves literals)</param>
    /// <returns>Widened type or original if not a literal</returns>
    private TypeInfo WidenLiteralType(TypeInfo type, bool isConst = false)
    {
        // Const bindings preserve literal types
        if (isConst) return type;

        return type switch
        {
            // Widen literal primitives to base types
            TypeInfo.StringLiteral => new TypeInfo.String(),
            TypeInfo.NumberLiteral => new TypeInfo.Primitive(TokenType.TYPE_NUMBER),
            TypeInfo.BooleanLiteral => new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN),

            // Widen array element types recursively
            TypeInfo.Array a => new TypeInfo.Array(WidenLiteralType(a.ElementType, false)),

            // Widen object property types recursively
            TypeInfo.Record r => new TypeInfo.Record(
                r.Fields.ToFrozenDictionary(
                    kv => kv.Key,
                    kv => WidenLiteralType(kv.Value, false)
                ),
                r.StringIndexType != null ? WidenLiteralType(r.StringIndexType, false) : null,
                r.NumberIndexType != null ? WidenLiteralType(r.NumberIndexType, false) : null,
                r.SymbolIndexType
            ),

            // Widen union members and collapse single-element unions
            TypeInfo.Union u => CollapseOrCreateUnion(
                u.Types.Select(t => WidenLiteralType(t, false)).Distinct(TypeInfoEqualityComparer.Instance).ToList()
            ),

            // Other types pass through unchanged
            _ => type
        };
    }

    /// <summary>
    /// Collapses a list of types into a single type or union.
    /// If the list has only one element, returns that element directly.
    /// Otherwise, creates a Union type.
    /// </summary>
    private static TypeInfo CollapseOrCreateUnion(List<TypeInfo> types)
    {
        return types.Count == 1 ? types[0] : new TypeInfo.Union(types);
    }

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

    // Track type parameters for generic overloaded functions
    private readonly Dictionary<string, List<TypeInfo.TypeParameter>> _pendingOverloadTypeParams = [];

    // Decorator mode configuration
    private DecoratorMode _decoratorMode = DecoratorMode.None;

    /// <summary>
    /// Sets the decorator mode for type checking decorators.
    /// </summary>
    public void SetDecoratorMode(DecoratorMode mode) => _decoratorMode = mode;

    // Module support - track the current module being type-checked
    private ParsedModule? _currentModule = null;
    private ModuleResolver? _moduleResolver = null;

    // Track dynamic import paths discovered during type checking
    // Used for module discovery - ensures dynamically imported modules are compiled
    private readonly HashSet<string> _dynamicImportPaths = [];

    /// <summary>
    /// Gets the set of module paths discovered in dynamic import expressions with string literal paths.
    /// These paths are relative to the importing module and should be resolved before use.
    /// </summary>
    public IReadOnlySet<string> DynamicImportPaths => _dynamicImportPaths;

    /// <summary>
    /// Type-checks the given statements and returns a TypeMap with resolved types for all expressions.
    /// </summary>
    /// <param name="statements">The AST statements to check.</param>
    /// <returns>A TypeMap containing the resolved type for each expression.</returns>
    public TypeMap Check(List<Stmt> statements)
    {
        // Clear compatibility cache for fresh check
        _compatibilityCache = null;

        // Pre-define built-ins
        _environment.Define("console", new TypeInfo.Any());
        _environment.Define("Reflect", new TypeInfo.Any());
        _environment.Define("process", new TypeInfo.Any());

        // Pre-register type declarations (interfaces, classes, enums, type aliases)
        // This ensures types are available when parsing function signatures during hoisting
        PreRegisterTypeDeclarations(statements);

        // Hoist function declarations (now type references will resolve correctly)
        HoistFunctionDeclarations(statements);

        foreach (Stmt statement in statements)
        {
            CheckStmt(statement);
        }

        return _typeMap;
    }

    /// <summary>
    /// Type-checks the given statements with error recovery, collecting multiple errors.
    /// </summary>
    /// <param name="statements">The AST statements to check.</param>
    /// <returns>A TypeCheckResult containing the type map and any errors encountered.</returns>
    public TypeCheckResult CheckWithRecovery(List<Stmt> statements)
    {
        _errors.Clear();
        _compatibilityCache = null;

        // Pre-define built-ins
        _environment.Define("console", new TypeInfo.Any());
        _environment.Define("Reflect", new TypeInfo.Any());
        _environment.Define("process", new TypeInfo.Any());

        // Pre-register type declarations
        PreRegisterTypeDeclarations(statements);

        // Hoist function declarations
        HoistFunctionDeclarations(statements);

        foreach (Stmt statement in statements)
        {
            if (_errors.Count >= MaxErrors)
                return new TypeCheckResult(_typeMap, _errors) { HitErrorLimit = true };

            try
            {
                CheckStmt(statement);
            }
            catch (TypeMismatchException ex)
            {
                RecordTypeError(ex);
            }
            catch (TypeCheckException ex)
            {
                RecordTypeError(ex);
            }
            catch (Exception ex)
            {
                RecordTypeError(ex.Message);
            }
        }

        return new TypeCheckResult(_typeMap, _errors);
    }

    /// <summary>
    /// Records a type checking error from a TypeCheckException.
    /// </summary>
    private void RecordTypeError(TypeCheckException ex)
    {
        TypeInfo? expected = null, actual = null;
        if (ex is TypeMismatchException m)
        {
            expected = m.Expected;
            actual = m.Actual;
        }

        // Extract the core message by removing the "Type Error: " or "Type Error at line X: " prefix
        string message = ex.Message;
        if (message.StartsWith("Type Error at line"))
        {
            var colonIndex = message.IndexOf(": ", 15); // Skip past "Type Error at line X"
            if (colonIndex > 0)
                message = message[(colonIndex + 2)..];
        }
        else if (message.StartsWith("Type Error: "))
        {
            message = message["Type Error: ".Length..];
        }

        _errors.Add(new TypeCheckError(message, ex.Line, ex.Column, expected, actual));
    }

    /// <summary>
    /// Records a type checking error from a raw message.
    /// </summary>
    private void RecordTypeError(string message, int? line = null)
    {
        _errors.Add(new TypeCheckError(message, line));
    }

    /// <summary>
    /// Pre-registers type declarations (interfaces, enums, type aliases) before function hoisting.
    /// This ensures type names are available when parsing function signatures.
    /// Full validation happens later during CheckStmt.
    /// Note: Classes are NOT pre-registered to avoid breaking inheritance checking with MutableClass.
    /// </summary>
    private void PreRegisterTypeDeclarations(IEnumerable<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case Stmt.Interface itf:
                    PreRegisterInterface(itf);
                    break;
                case Stmt.TypeAlias alias:
                    PreRegisterTypeAlias(alias);
                    break;
                case Stmt.Enum enumStmt:
                    PreRegisterEnum(enumStmt);
                    break;
                // Note: Classes are not pre-registered here because doing so creates MutableClass
                // objects that break inheritance checking. Classes are properly registered during
                // CheckClassDeclaration which handles inheritance correctly.
                case Stmt.Export export when export.Declaration != null:
                    // Handle exported type declarations
                    PreRegisterTypeDeclarations([export.Declaration]);
                    break;
                case Stmt.Namespace ns:
                    // Pre-register namespace contents
                    PreRegisterTypeDeclarations(ns.Members);
                    break;
            }
        }
    }

    /// <summary>
    /// Pre-registers a type alias before function hoisting.
    /// Type aliases are just stored as string definitions, so pre-registration is the same as full registration.
    /// </summary>
    private void PreRegisterTypeAlias(Stmt.TypeAlias typeAlias)
    {
        // Skip if already registered
        if (_environment.GetTypeAlias(typeAlias.Name.Lexeme) != null)
            return;

        if (typeAlias.TypeParameters != null && typeAlias.TypeParameters.Count > 0)
        {
            var typeParamNames = typeAlias.TypeParameters.Select(tp => tp.Name.Lexeme).ToList();
            _environment.DefineGenericTypeAlias(typeAlias.Name.Lexeme, typeAlias.TypeDefinition, typeParamNames);
        }
        else
        {
            _environment.DefineTypeAlias(typeAlias.Name.Lexeme, typeAlias.TypeDefinition);
        }
    }

    /// <summary>
    /// Pre-registers an enum before function hoisting.
    /// Creates a basic enum type with placeholder values. Full validation happens in CheckEnumDeclaration.
    /// </summary>
    private void PreRegisterEnum(Stmt.Enum enumStmt)
    {
        // Skip if already registered
        if (_environment.IsDefinedLocally(enumStmt.Name.Lexeme))
            return;

        // Create a basic enum with member names (values will be computed during full check)
        Dictionary<string, object> members = [];
        double value = 0;

        foreach (var member in enumStmt.Members)
        {
            // During pre-registration, just assign sequential numeric values as placeholders
            // The full check will compute actual values
            members[member.Name.Lexeme] = value++;
        }

        _environment.Define(enumStmt.Name.Lexeme, new TypeInfo.Enum(
            enumStmt.Name.Lexeme,
            members.ToFrozenDictionary(),
            EnumKind.Numeric,
            enumStmt.IsConst
        ));
    }

    /// <summary>
    /// Pre-registers a class before function hoisting.
    /// Creates a basic class type structure so the class name is available for type references.
    /// Full validation happens in CheckClassDeclaration.
    /// </summary>
    private void PreRegisterClass(Stmt.Class classStmt)
    {
        // Skip if already registered
        if (_environment.IsDefinedLocally(classStmt.Name.Lexeme))
            return;

        // Create a mutable class placeholder for forward references
        // MutableClass supports forward references and will be replaced during full check
        var mutableClass = new TypeInfo.MutableClass(classStmt.Name.Lexeme);

        // Try to resolve superclass if present (may fail if superclass not yet defined)
        if (classStmt.Superclass != null)
        {
            try
            {
                var superType = _environment.Get(classStmt.Superclass.Lexeme);
                if (superType is TypeInfo.Class c)
                {
                    mutableClass.Superclass = c;
                }
                else if (superType is TypeInfo.MutableClass mc && mc.Frozen != null)
                {
                    mutableClass.Superclass = mc.Frozen;
                }
            }
            catch
            {
                // Ignore superclass resolution errors during pre-registration
            }
        }

        _environment.Define(classStmt.Name.Lexeme, mutableClass);
    }

    /// <summary>
    /// Type-checks multiple modules in dependency order.
    /// </summary>
    /// <param name="modules">Modules in dependency order (dependencies first)</param>
    /// <param name="resolver">Module resolver for path resolution</param>
    /// <returns>A TypeMap containing resolved types for all expressions across all modules</returns>
    public TypeMap CheckModules(List<ParsedModule> modules, ModuleResolver resolver)
    {
        // Clear compatibility cache for fresh check
        _compatibilityCache = null;

        _moduleResolver = resolver;

        // Pre-define built-ins in the global environment
        _environment.Define("console", new TypeInfo.Any());
        _environment.Define("Reflect", new TypeInfo.Any());
        _environment.Define("process", new TypeInfo.Any());

        // Create a shared script environment for script files (they share global scope)
        var scriptEnv = new TypeEnvironment(_environment);

        // First pass: collect all exports from each module
        foreach (var module in modules)
        {
            _currentModule = module;
            if (module.IsScript)
            {
                // Scripts use shared environment and don't export
                CollectScriptDeclarations(module, scriptEnv);
            }
            else
            {
                CollectModuleExports(module);
            }
        }

        // Second pass: type-check each module with imports resolved
        foreach (var module in modules)
        {
            if (module.IsTypeChecked)
            {
                continue;
            }

            _currentModule = module;

            if (module.IsScript)
            {
                // Script files share the global script environment
                // Type-check in the shared script environment
                using (new EnvironmentScope(this, scriptEnv))
                {
                    // Pre-register type declarations (may have been done in first pass, but safe to repeat)
                    PreRegisterTypeDeclarations(module.Statements);

                    // Hoist function declarations
                    HoistFunctionDeclarations(module.Statements);

                    // Check all statements
                    foreach (var stmt in module.Statements)
                    {
                        CheckStmt(stmt);
                    }
                }
            }
            else
            {
                // Module files get isolated scope
                var moduleEnv = new TypeEnvironment(_environment);

                // Bind imports from dependencies
                BindModuleImports(module, moduleEnv);

                // Type-check module body
                using (new EnvironmentScope(this, moduleEnv))
                {
                    // First pass: pre-register type declarations
                    PreRegisterTypeDeclarations(module.Statements);

                    // Second pass: hoist function declarations (now types are available)
                    HoistFunctionDeclarations(module.Statements);

                    // Third pass: check all statements
                    foreach (var stmt in module.Statements)
                    {
                        CheckStmt(stmt);
                    }
                }
            }

            module.IsTypeChecked = true;
        }

        _currentModule = null;
        return _typeMap;
    }

    /// <summary>
    /// Collects declarations from a script file into the shared script environment.
    /// Scripts share global scope, so all declarations are visible to other scripts.
    /// </summary>
    private void CollectScriptDeclarations(ParsedModule script, TypeEnvironment scriptEnv)
    {
        using (new EnvironmentScope(this, scriptEnv))
        {
            // Pre-register type declarations (interfaces, enums, type aliases)
            PreRegisterTypeDeclarations(script.Statements);

            // Hoist function declarations
            HoistFunctionDeclarations(script.Statements);

            // Process all declarations to populate the environment
            foreach (var stmt in script.Statements)
            {
                // For scripts, just check the statements to register types
                // Skip actual runtime statements during collection phase
                switch (stmt)
                {
                    case Stmt.Function func when func.Body != null:
                    case Stmt.Class:
                    case Stmt.Interface:
                    case Stmt.TypeAlias:
                    case Stmt.Enum:
                    case Stmt.Namespace:
                        CheckStmt(stmt);
                        break;
                    case Stmt.Var:
                    case Stmt.Const:
                        // Register variable types
                        CheckStmt(stmt);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Collects exports from a module (first pass - just register export types).
    /// </summary>
    private void CollectModuleExports(ParsedModule module)
    {
        var moduleEnv = new TypeEnvironment(_environment);

        using (new EnvironmentScope(this, moduleEnv))
        {
            // First, bind imports so we can reference imported types in our declarations
            BindModuleImports(module, moduleEnv);

            // Pre-register type declarations first
            PreRegisterTypeDeclarations(module.Statements);

            // Hoist function declarations (now types are available)
            HoistFunctionDeclarations(module.Statements);

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
                if (export.ExportAssignment != null)
                {
                    // CommonJS-style export = value
                    var type = CheckExpr(export.ExportAssignment);
                    module.HasExportAssignment = true;
                    module.ExportAssignmentType = type;
                }
                else if (export.Declaration != null)
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
        }
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
                    throw new TypeCheckException($"Cannot find module '{import.ModulePath}'", import.Keyword.Line);
                }

                // Default import
                if (import.DefaultImport != null)
                {
                    if (importedModule.DefaultExportType == null)
                    {
                        throw new TypeCheckException($"Module '{import.ModulePath}' has no default export", import.Keyword.Line);
                    }
                    env.Define(import.DefaultImport.Lexeme, importedModule.DefaultExportType);
                }

                // Namespace import: import * as Module from './file'
                if (import.NamespaceImport != null)
                {
                    // Create a record type with all exports
                    var namespaceType = new TypeInfo.Record(
                        importedModule.ExportedTypes.ToFrozenDictionary()
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
                            throw new TypeCheckException($"Module '{import.ModulePath}' has no export named '{importedName}'", import.Keyword.Line);
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
            _ => throw new TypeCheckException($" Cannot get name of declaration type {decl.GetType().Name}")
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
