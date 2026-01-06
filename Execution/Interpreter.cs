using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using SharpTS.TypeSystem;

namespace SharpTS.Execution;

/// <summary>
/// Tree-walking interpreter that executes the AST.
/// </summary>
/// <remarks>
/// One of two execution paths after type checking (the other being <see cref="ILCompiler"/>).
/// Traverses the AST recursively, evaluating expressions and executing statements. Uses
/// <see cref="RuntimeEnvironment"/> for variable scopes and control flow exceptions
/// (<see cref="ReturnException"/>, <see cref="BreakException"/>, <see cref="ContinueException"/>)
/// for unwinding. Runtime values include <see cref="SharpTSClass"/>, <see cref="SharpTSInstance"/>,
/// <see cref="SharpTSFunction"/>, <see cref="SharpTSArray"/>, and <see cref="SharpTSObject"/>.
///
/// This class is split across multiple partial class files:
/// <list type="bullet">
///   <item><description>Interpreter.cs - Core infrastructure and statement dispatch</description></item>
///   <item><description>Interpreter.Statements.cs - Statement execution helpers (block, switch, try/catch, loops)</description></item>
///   <item><description>Interpreter.Expressions.cs - Expression dispatch and basic evaluators</description></item>
///   <item><description>Interpreter.Properties.cs - Property/member access (Get, Set, New, This)</description></item>
///   <item><description>Interpreter.Calls.cs - Function calls and binary/logical operators</description></item>
///   <item><description>Interpreter.Operators.cs - Compound assignment, increment, and utility methods</description></item>
/// </list>
/// </remarks>
/// <seealso cref="RuntimeEnvironment"/>
/// <seealso cref="ILCompiler"/>
public partial class Interpreter
{
    private RuntimeEnvironment _environment = new();
    private TypeMap? _typeMap;

    // Module support
    private readonly Dictionary<string, ModuleInstance> _loadedModules = [];
    private ModuleResolver? _moduleResolver;
    private ParsedModule? _currentModule;
    private ModuleInstance? _currentModuleInstance;

    internal RuntimeEnvironment Environment => _environment;
    internal TypeMap? TypeMap => _typeMap;
    internal void SetEnvironment(RuntimeEnvironment env) => _environment = env;

    /// <summary>
    /// Executes a list of statements as the main entry point for interpretation.
    /// </summary>
    /// <param name="statements">The list of parsed statements to execute.</param>
    /// <param name="typeMap">Optional type map from static analysis for type-aware dispatch.</param>
    /// <remarks>
    /// Catches and reports runtime errors to the console. Each statement is executed
    /// sequentially via <see cref="Execute"/>.
    /// </remarks>
    public void Interpret(List<Stmt> statements, TypeMap? typeMap = null)
    {
        _typeMap = typeMap;
        try
        {
            foreach (Stmt statement in statements)
            {
                // For expression statements, we may get a Promise that needs to be awaited
                // This provides "top-level await" behavior for the interpreter
                if (statement is Stmt.Expression exprStmt)
                {
                    object? result = Evaluate(exprStmt.Expr);
                    // Wait for top-level Promises to complete before continuing
                    if (result is SharpTSPromise promise)
                    {
                        promise.Task.GetAwaiter().GetResult();
                    }
                }
                else
                {
                    Execute(statement);
                }
            }
        }
        catch (Exception error)
        {
            Console.WriteLine($"Runtime Error: {error.Message}");
        }
    }

    /// <summary>
    /// Interprets multiple modules in dependency order.
    /// </summary>
    /// <param name="modules">Modules in dependency order (dependencies first)</param>
    /// <param name="resolver">Module resolver for path resolution</param>
    /// <param name="typeMap">Optional type map from static analysis</param>
    public void InterpretModules(List<ParsedModule> modules, ModuleResolver resolver, TypeMap? typeMap = null)
    {
        _typeMap = typeMap;
        _moduleResolver = resolver;

        try
        {
            foreach (var module in modules)
            {
                ExecuteModule(module);
            }
        }
        catch (Exception error)
        {
            Console.WriteLine($"Runtime Error: {error.Message}");
        }
    }

    /// <summary>
    /// Executes a single module, caching its exports.
    /// </summary>
    private void ExecuteModule(ParsedModule module)
    {
        // Skip if already executed
        if (_loadedModules.ContainsKey(module.Path))
        {
            return;
        }

        // Create module instance to track exports
        var moduleInstance = new ModuleInstance();
        _loadedModules[module.Path] = moduleInstance;

        // Create module-scoped environment
        var moduleEnv = new RuntimeEnvironment(_environment);

        // Bind imports from dependencies
        BindModuleImports(module, moduleEnv);

        // Save context
        var savedEnv = _environment;
        var savedModule = _currentModule;
        var savedModuleInstance = _currentModuleInstance;

        _environment = moduleEnv;
        _currentModule = module;
        _currentModuleInstance = moduleInstance;

        try
        {
            foreach (var stmt in module.Statements)
            {
                // For expression statements, we may get a Promise that needs to be awaited
                // This provides "top-level await" behavior for modules
                if (stmt is Stmt.Expression exprStmt)
                {
                    object? result = Evaluate(exprStmt.Expr);
                    // Wait for top-level Promises to complete before continuing
                    if (result is SharpTSPromise promise)
                    {
                        promise.Task.GetAwaiter().GetResult();
                    }
                }
                else
                {
                    Execute(stmt);
                }
            }
            moduleInstance.IsExecuted = true;
        }
        finally
        {
            _environment = savedEnv;
            _currentModule = savedModule;
            _currentModuleInstance = savedModuleInstance;
        }
    }

    /// <summary>
    /// Binds imported values into the module's environment.
    /// </summary>
    private void BindModuleImports(ParsedModule module, RuntimeEnvironment env)
    {
        foreach (var stmt in module.Statements)
        {
            if (stmt is Stmt.Import import)
            {
                string importedPath = _moduleResolver!.ResolveModulePath(import.ModulePath, module.Path);
                var importedModuleInstance = _loadedModules.GetValueOrDefault(importedPath);

                if (importedModuleInstance == null)
                {
                    throw new Exception($"Runtime Error: Module '{import.ModulePath}' not loaded.");
                }

                // Default import
                if (import.DefaultImport != null)
                {
                    env.Define(import.DefaultImport.Lexeme, importedModuleInstance.DefaultExport);
                }

                // Namespace import: import * as Module from './file'
                if (import.NamespaceImport != null)
                {
                    env.Define(import.NamespaceImport.Lexeme, importedModuleInstance.ExportsAsObject());
                }

                // Named imports: import { x, y as z } from './file'
                if (import.NamedImports != null)
                {
                    foreach (var spec in import.NamedImports)
                    {
                        string importedName = spec.Imported.Lexeme;
                        string localName = spec.LocalName?.Lexeme ?? importedName;
                        var value = importedModuleInstance.GetExport(importedName);
                        env.Define(localName, value);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Executes an export statement, registering exports in the current module.
    /// </summary>
    private void ExecuteExport(Stmt.Export export)
    {
        if (export.IsDefaultExport)
        {
            if (export.Declaration != null)
            {
                Execute(export.Declaration);
                if (_currentModuleInstance != null)
                {
                    _currentModuleInstance.DefaultExport = GetDeclaredValue(export.Declaration);
                }
            }
            else if (export.DefaultExpr != null)
            {
                var value = Evaluate(export.DefaultExpr);
                if (_currentModuleInstance != null)
                {
                    _currentModuleInstance.DefaultExport = value;
                }
            }
        }
        else if (export.Declaration != null)
        {
            Execute(export.Declaration);
            // Skip type-only declarations (interface, type alias) - they have no runtime value
            if (_currentModuleInstance != null && !IsTypeOnlyDeclaration(export.Declaration))
            {
                string name = GetDeclaredName(export.Declaration);
                _currentModuleInstance.SetExport(name, GetDeclaredValue(export.Declaration));
            }
        }
        else if (export.NamedExports != null && export.FromModulePath == null)
        {
            // export { x, y }
            foreach (var spec in export.NamedExports)
            {
                string localName = spec.LocalName.Lexeme;
                string exportedName = spec.ExportedName?.Lexeme ?? localName;
                var value = _environment.Get(spec.LocalName);
                if (_currentModuleInstance != null)
                {
                    _currentModuleInstance.SetExport(exportedName, value);
                }
            }
        }
        else if (export.FromModulePath != null)
        {
            // Re-export: export { x } from './module' or export * from './module'
            string sourcePath = _moduleResolver!.ResolveModulePath(export.FromModulePath, _currentModule!.Path);
            var sourceModuleInstance = _loadedModules.GetValueOrDefault(sourcePath);

            if (sourceModuleInstance != null && _currentModuleInstance != null)
            {
                if (export.NamedExports != null)
                {
                    // Re-export specific names
                    foreach (var spec in export.NamedExports)
                    {
                        string importedName = spec.LocalName.Lexeme;
                        string exportedName = spec.ExportedName?.Lexeme ?? importedName;
                        var value = sourceModuleInstance.GetExport(importedName);
                        _currentModuleInstance.SetExport(exportedName, value);
                    }
                }
                else
                {
                    // Re-export all: export * from './module'
                    foreach (var (name, value) in sourceModuleInstance.Exports)
                    {
                        _currentModuleInstance.SetExport(name, value);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Checks if a declaration is type-only (interface or type alias) with no runtime value.
    /// </summary>
    private bool IsTypeOnlyDeclaration(Stmt decl) =>
        decl is Stmt.Interface or Stmt.TypeAlias;

    /// <summary>
    /// Gets the name of a declaration.
    /// </summary>
    private string GetDeclaredName(Stmt decl)
    {
        return decl switch
        {
            Stmt.Function f => f.Name.Lexeme,
            Stmt.Class c => c.Name.Lexeme,
            Stmt.Var v => v.Name.Lexeme,
            Stmt.Enum e => e.Name.Lexeme,
            _ => throw new Exception($"Runtime Error: Cannot get name of declaration type {decl.GetType().Name}")
        };
    }

    /// <summary>
    /// Gets the value of a declaration from the environment.
    /// </summary>
    private object? GetDeclaredValue(Stmt decl)
    {
        string name = GetDeclaredName(decl);
        var token = decl switch
        {
            Stmt.Function f => f.Name,
            Stmt.Class c => c.Name,
            Stmt.Var v => v.Name,
            Stmt.Enum e => e.Name,
            _ => throw new Exception($"Runtime Error: Cannot get value of declaration type {decl.GetType().Name}")
        };
        return _environment.Get(token);
    }

    /// <summary>
    /// Dispatches a statement to the appropriate execution handler using pattern matching.
    /// </summary>
    /// <param name="stmt">The statement AST node to execute.</param>
    /// <remarks>
    /// Handles all statement types including control flow (if, while, for, switch),
    /// declarations (var, function, class, enum), and control transfer (return, break, continue, throw).
    /// Control flow uses exceptions (<see cref="ReturnException"/>, <see cref="BreakException"/>,
    /// <see cref="ContinueException"/>, <see cref="ThrowException"/>) for stack unwinding.
    /// </remarks>
    private void Execute(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Block block:
                ExecuteBlock(block.Statements, new RuntimeEnvironment(_environment));
                break;
            case Stmt.LabeledStatement labeledStmt:
                ExecuteLabeledStatement(labeledStmt);
                break;
            case Stmt.Sequence seq:
                // Execute in current scope (no new environment)
                foreach (var s in seq.Statements)
                    Execute(s);
                break;
            case Stmt.Expression exprStmt:
                Evaluate(exprStmt.Expr);
                break;
            case Stmt.If ifStmt:
                if (IsTruthy(Evaluate(ifStmt.Condition)))
                {
                    Execute(ifStmt.ThenBranch);
                }
                else if (ifStmt.ElseBranch != null)
                {
                    Execute(ifStmt.ElseBranch);
                }
                break;
            case Stmt.While whileStmt:
                while (IsTruthy(Evaluate(whileStmt.Condition)))
                {
                    try
                    {
                        Execute(whileStmt.Body);
                    }
                    catch (BreakException ex) when (ex.TargetLabel == null)
                    {
                        break;
                    }
                    catch (ContinueException ex) when (ex.TargetLabel == null)
                    {
                        continue;
                    }
                }
                break;
            case Stmt.DoWhile doWhileStmt:
                do
                {
                    try
                    {
                        Execute(doWhileStmt.Body);
                    }
                    catch (BreakException ex) when (ex.TargetLabel == null)
                    {
                        break;
                    }
                    catch (ContinueException ex) when (ex.TargetLabel == null)
                    {
                        continue;
                    }
                } while (IsTruthy(Evaluate(doWhileStmt.Condition)));
                break;
            case Stmt.ForOf forOf:
                ExecuteForOf(forOf);
                break;
            case Stmt.ForIn forIn:
                ExecuteForIn(forIn);
                break;
            case Stmt.Break breakStmt:
                throw new BreakException(breakStmt.Label?.Lexeme);
            case Stmt.Continue continueStmt:
                throw new ContinueException(continueStmt.Label?.Lexeme);
            case Stmt.Switch switchStmt:
                ExecuteSwitch(switchStmt);
                break;
            case Stmt.TryCatch tryCatch:
                ExecuteTryCatch(tryCatch);
                break;
            case Stmt.Throw throwStmt:
                throw new ThrowException(Evaluate(throwStmt.Value));
            case Stmt.Var varStmt:
                object? value = null;
                if (varStmt.Initializer != null)
                {
                    value = Evaluate(varStmt.Initializer);
                }
                _environment.Define(varStmt.Name.Lexeme, value);
                break;
            case Stmt.Function functionStmt:
                // Skip overload signatures (no body) - they're type-checking only
                if (functionStmt.Body == null) break;
                if (functionStmt.IsGenerator)
                {
                    SharpTSGeneratorFunction generatorFunction = new(functionStmt, _environment);
                    _environment.Define(functionStmt.Name.Lexeme, generatorFunction);
                }
                else if (functionStmt.IsAsync)
                {
                    SharpTSAsyncFunction asyncFunction = new(functionStmt, _environment);
                    _environment.Define(functionStmt.Name.Lexeme, asyncFunction);
                }
                else
                {
                    SharpTSFunction function = new(functionStmt, _environment);
                    _environment.Define(functionStmt.Name.Lexeme, function);
                }
                break;
            case Stmt.Class classStmt:
                object? superclass = null;
                if (classStmt.Superclass != null)
                {
                    superclass = _environment.Get(classStmt.Superclass);
                    if (superclass is not SharpTSClass)
                    {
                        throw new Exception("Superclass must be a class.");
                    }
                }

                _environment.Define(classStmt.Name.Lexeme, null);

                if (classStmt.Superclass != null)
                {
                    _environment = new RuntimeEnvironment(_environment);
                    _environment.Define("super", superclass);
                }

                Dictionary<string, SharpTSFunction> methods = [];
                Dictionary<string, SharpTSFunction> staticMethods = [];
                Dictionary<string, object?> staticProperties = [];
                List<Stmt.Field> instanceFields = [];

                // Process fields: evaluate static initializers now, collect instance fields for later
                foreach (Stmt.Field field in classStmt.Fields)
                {
                    if (field.IsStatic)
                    {
                        object? fieldValue = field.Initializer != null
                            ? Evaluate(field.Initializer)
                            : null;
                        staticProperties[field.Name.Lexeme] = fieldValue;
                    }
                    else
                    {
                        // Collect instance fields - they'll be initialized when instances are created
                        instanceFields.Add(field);
                    }
                }

                // Separate static and instance methods (skip overload signatures with no body)
                foreach (Stmt.Function method in classStmt.Methods.Where(m => m.Body != null))
                {
                    SharpTSFunction func = new(method, _environment);
                    if (method.IsStatic)
                    {
                        staticMethods[method.Name.Lexeme] = func;
                    }
                    else
                    {
                        methods[method.Name.Lexeme] = func;
                    }
                }

                // Create accessor functions
                Dictionary<string, SharpTSFunction> getters = [];
                Dictionary<string, SharpTSFunction> setters = [];

                if (classStmt.Accessors != null)
                {
                    foreach (var accessor in classStmt.Accessors)
                    {
                        // Create a synthetic function for the accessor
                        var funcStmt = new Stmt.Function(
                            accessor.Name,
                            null,  // No type parameters for accessor
                            null,  // No this type annotation
                            accessor.SetterParam != null ? [accessor.SetterParam] : [],
                            accessor.Body,
                            accessor.ReturnType);

                        SharpTSFunction func = new(funcStmt, _environment);

                        if (accessor.Kind.Type == TokenType.GET)
                        {
                            getters[accessor.Name.Lexeme] = func;
                        }
                        else
                        {
                            setters[accessor.Name.Lexeme] = func;
                        }
                    }
                }

                SharpTSClass klass = new(
                    classStmt.Name.Lexeme,
                    (SharpTSClass?)superclass,
                    methods,
                    staticMethods,
                    staticProperties,
                    getters,
                    setters,
                    classStmt.IsAbstract,
                    instanceFields);

                // Apply decorators in the correct order
                klass = ApplyAllDecorators(classStmt, klass, methods, staticMethods, getters, setters);

                if (classStmt.Superclass != null)
                {
                    _environment = _environment.Enclosing!;
                }

                _environment.Assign(classStmt.Name, klass);
                break;
            case Stmt.TypeAlias:
                // Type aliases are compile-time only, no runtime effect
                break;
            case Stmt.Enum enumStmt:
                ExecuteEnumDeclaration(enumStmt);
                break;
            case Stmt.Return returnStmt:
                object? returnValue = null;
                if (returnStmt.Value != null) returnValue = Evaluate(returnStmt.Value);
                throw new ReturnException(returnValue);
            case Stmt.Print printStmt:
                Console.WriteLine(Stringify(Evaluate(printStmt.Expr)));
                break;
            case Stmt.Import:
                // Imports are handled in BindModuleImports before execution
                // In single-file mode, imports are a no-op (type checker would have errored)
                break;
            case Stmt.Export exportStmt:
                ExecuteExport(exportStmt);
                break;
        }
    }
}
