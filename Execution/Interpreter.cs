using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
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
/// <see cref="RuntimeEnvironment"/> for variable scopes and <see cref="ExecutionResult"/>
/// for lightweight flow control (return, break, continue, throw). Runtime values include
/// <see cref="SharpTSClass"/>, <see cref="SharpTSInstance"/>, <see cref="SharpTSFunction"/>,
/// <see cref="SharpTSArray"/>, and <see cref="SharpTSObject"/>.
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
public partial class Interpreter : IDisposable
{
    private RuntimeEnvironment _environment = new();
    private readonly Dictionary<Expr, int> _locals = []; // Depth for resolved variables
    private TypeMap? _typeMap;

    // Module support
    private readonly Dictionary<string, ModuleInstance> _loadedModules = [];
    private ModuleResolver? _moduleResolver;
    private ParsedModule? _currentModule;
    private ModuleInstance? _currentModuleInstance;

    // Lock for synchronizing timer callback access with main interpretation thread
    internal readonly object InterpreterLock = new();

    // Flag to indicate interpreter has been disposed - timer callbacks should not execute
    private volatile bool _isDisposed;

    /// <summary>
    /// Gets whether this interpreter has been disposed.
    /// Timer callbacks check this before executing to prevent race conditions.
    /// </summary>
    internal bool IsDisposed => _isDisposed;

    internal RuntimeEnvironment Environment => _environment;
    internal TypeMap? TypeMap => _typeMap;
    internal void SetEnvironment(RuntimeEnvironment env) => _environment = env;

    /// <summary>
    /// Disposes the interpreter, marking it as disposed so timer callbacks won't execute.
    /// This prevents race conditions where timer callbacks fire after the test/execution context has ended.
    /// </summary>
    public void Dispose()
    {
        _isDisposed = true;
        GC.SuppressFinalize(this);
    }

    public void Resolve(Expr expr, int depth)
    {
        _locals[expr] = depth;
    }

    private object? LookupVariable(Token name, Expr expr)
    {
        if (_locals.TryGetValue(expr, out int distance))
        {
            return _environment.GetAt(distance, name.Lexeme);
        }

        // Single-pass scope chain traversal (more efficient than IsDefined + Get)
        if (_environment.TryGet(name.Lexeme, out object? value))
        {
            return value;
        }

        // Check for built-in singleton namespaces (e.g., Math, process)
        var singleton = BuiltInRegistry.Instance.GetSingleton(name.Lexeme);
        if (singleton != null)
        {
            return singleton;
        }

        // Check for global constants (NaN, Infinity, undefined)
        if (name.Lexeme == "NaN") return double.NaN;
        if (name.Lexeme == "Infinity") return double.PositiveInfinity;
        if (name.Lexeme == "undefined") return Runtime.Types.SharpTSUndefined.Instance;

        throw new Exception($"Undefined variable '{name.Lexeme}'.");
    }

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
            // Check for "use strict" directive at file level
            bool isStrict = CheckForUseStrict(statements);
            if (isStrict)
            {
                // Wrap the current environment with strict mode enabled
                _environment = new RuntimeEnvironment(_environment, strictMode: true);
            }

            // Hoist function declarations first
            HoistFunctionDeclarations(statements);

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
                    var result = Execute(statement);
                    if (result.Type == ExecutionResult.ResultType.Throw)
                    {
                        Console.WriteLine($"Runtime Error: {Stringify(result.Value)}");
                        return;
                    }
                    if (result.IsAbrupt)
                    {
                        // Top-level break/continue/return is usually a syntax error handled by parser
                        // but if it reaches here, we stop execution.
                        return;
                    }
                }
            }

            // After executing all statements, check for a main() function and call it
            TryCallMainWithExitCode(statements);
        }
        catch (Exception error)
        {
            Console.WriteLine($"Runtime Error: {error.Message}");
            throw;
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
            // Create a shared script environment for script files (they share global scope)
            var scriptEnv = new RuntimeEnvironment(_environment);

            foreach (var module in modules)
            {
                if (module.IsScript)
                {
                    ExecuteScriptFile(module, scriptEnv);
                }
                else
                {
                    ExecuteModule(module);
                }
            }

            // After executing all modules, check for main() in the entry module (last one)
            if (modules.Count > 0)
            {
                TryCallMainWithExitCode(modules[^1].Statements);
            }
        }
        catch (Exception error)
        {
            Console.WriteLine($"Runtime Error: {error.Message}");
            throw;
        }
    }

    /// <summary>
    /// Executes a script file in the shared script environment.
    /// Scripts share global scope, so all declarations are visible to other scripts.
    /// </summary>
    private void ExecuteScriptFile(ParsedModule script, RuntimeEnvironment scriptEnv)
    {
        // Skip if already executed
        if (script.IsExecuted)
        {
            return;
        }

        // Save context
        var savedEnv = _environment;
        var savedModule = _currentModule;

        _environment = scriptEnv;
        _currentModule = script;

        try
        {
            // Check for "use strict" directive
            bool isStrict = CheckForUseStrict(script.Statements);
            if (isStrict && !_environment.IsStrictMode)
            {
                _environment = new RuntimeEnvironment(_environment, strictMode: true);
            }

            // Hoist function declarations first
            HoistFunctionDeclarations(script.Statements);

            // Execute all statements in the shared environment
            foreach (var stmt in script.Statements)
            {
                if (stmt is Stmt.Expression exprStmt)
                {
                    object? result = Evaluate(exprStmt.Expr);
                    if (result is SharpTSPromise promise)
                    {
                        promise.Task.GetAwaiter().GetResult();
                    }
                }
                else
                {
                    var result = Execute(stmt);
                    if (result.Type == ExecutionResult.ResultType.Throw)
                    {
                        throw new Exception(Stringify(result.Value));
                    }
                    if (result.IsAbrupt) break;
                }
            }

            script.IsExecuted = true;
        }
        finally
        {
            _environment = savedEnv;
            _currentModule = savedModule;
        }
    }

    /// <summary>
    /// Checks for a main(args: string[]) function in the statements and calls it if found.
    /// If main() returns a number, calls Environment.Exit with that number as the exit code.
    /// </summary>
    private void TryCallMainWithExitCode(List<Stmt> statements)
    {
        // Look for a function named "main" with the expected signature
        Stmt.Function? mainFunc = null;
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Function func && func.Name.Lexeme == "main" && func.Body != null)
            {
                // Check signature: exactly one parameter (args: string[])
                if (func.Parameters.Count == 1 && func.Parameters[0].Type == "string[]")
                {
                    // Accept return types: void, null (implicit), number, Promise<void>, Promise<number>
                    var rt = func.ReturnType;
                    if (rt == null || rt == "void" || rt == "number" ||
                        rt == "Promise<void>" || rt == "Promise<number>")
                    {
                        mainFunc = func;
                        break;
                    }
                }
            }
        }

        if (mainFunc == null)
            return;

        // Get the main function from the environment (single scope traversal)
        if (!_environment.TryGet(mainFunc.Name.Lexeme, out object? mainValue))
            return;

        if (mainValue is not SharpTSFunction mainFn)
            return;

        // Call main with process.argv
        var argv = ProcessBuiltIns.GetArgv();
        object? result;
        try
        {
            result = mainFn.Call(this, [argv]);
        }
        catch (Runtime.Exceptions.ReturnException ret)
        {
            result = ret.Value;
        }

        // If result is a Promise, await it
        if (result is SharpTSPromise promise)
        {
            result = promise.Task.GetAwaiter().GetResult();
        }

        // If result is a number, use it as exit code
        if (result is double exitCode)
        {
            System.Environment.Exit((int)exitCode);
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

        // Handle built-in modules specially - populate exports from interpreter implementations
        if (module.IsBuiltIn)
        {
            var moduleName = BuiltInModuleRegistry.GetModuleName(module.Path);
            if (moduleName != null && BuiltInModuleValues.HasInterpreterSupport(moduleName))
            {
                var exports = BuiltInModuleValues.GetModuleExports(moduleName);
                foreach (var (name, value) in exports)
                {
                    moduleInstance.SetExport(name, value);
                }
                // Set default export to all exports, enabling: import fs from 'fs'
                moduleInstance.DefaultExport = moduleInstance.ExportsAsObject();
            }
            moduleInstance.IsExecuted = true;
            return;
        }

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
            // First pass: hoist function declarations
            HoistFunctionDeclarations(module.Statements);

            // Second pass: execute all statements
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
                    var result = Execute(stmt);
                    if (result.Type == ExecutionResult.ResultType.Throw)
                    {
                        throw new Exception(Stringify(result.Value));
                    }
                    if (result.IsAbrupt) break;
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
                // Skip type-only imports entirely - they have no runtime binding
                if (import.IsTypeOnly)
                    continue;

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
                // Skip individual type-only specifiers
                if (import.NamedImports != null)
                {
                    foreach (var spec in import.NamedImports.Where(s => !s.IsTypeOnly))
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
    private ExecutionResult ExecuteExport(Stmt.Export export)
    {
        // Handle export = assignment (CommonJS-style)
        if (export.ExportAssignment != null)
        {
            var value = Evaluate(export.ExportAssignment);
            if (_currentModule != null)
            {
                _currentModule.HasExportAssignment = true;
                _currentModule.ExportAssignmentValue = value;
            }
            return ExecutionResult.Success();
        }

        if (export.IsDefaultExport)
        {
            if (export.Declaration != null)
            {
                var result = Execute(export.Declaration);
                if (result.IsAbrupt) return result;
                
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
            var result = Execute(export.Declaration);
            if (result.IsAbrupt) return result;

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
        
        return ExecutionResult.Success();
    }

    /// <summary>
    /// Checks if a declaration is type-only (interface or type alias) with no runtime value.
    /// </summary>
    private bool IsTypeOnlyDeclaration(Stmt decl) =>
        decl is Stmt.Interface or Stmt.TypeAlias;

    /// <summary>
    /// Executes a CommonJS-style require import: import x = require('path')
    /// </summary>
    private ExecutionResult ExecuteImportRequire(Stmt.ImportRequire importReq)
    {
        // Check if it's a built-in module (fs, path, os, etc.)
        string? builtInModuleName = BuiltInModuleRegistry.GetModuleName(importReq.ModulePath);
        if (builtInModuleName != null && BuiltInModuleValues.HasInterpreterSupport(builtInModuleName))
        {
            // Get the built-in module exports and create a namespace object
            var exports = BuiltInModuleValues.GetModuleExports(builtInModuleName);
            var builtInModule = new SharpTSObject(exports);
            _environment.Define(importReq.AliasName.Lexeme, builtInModule);

            // If this is a re-export, register the export
            if (importReq.IsExported && _currentModuleInstance != null)
            {
                _currentModuleInstance.SetExport(importReq.AliasName.Lexeme, builtInModule);
            }
            return ExecutionResult.Success();
        }

        // Not in module context - define as null
        if (_currentModule == null || _moduleResolver == null)
        {
            _environment.Define(importReq.AliasName.Lexeme, null);
            return ExecutionResult.Success();
        }

        // Resolve the module path
        string resolvedPath = _moduleResolver.ResolveModulePath(importReq.ModulePath, _currentModule.Path);

        // Find the loaded module instance
        var moduleInstance = _loadedModules.GetValueOrDefault(resolvedPath);
        var importedModule = _moduleResolver.GetCachedModule(resolvedPath);

        object? importedValue;
        if (importedModule?.HasExportAssignment == true)
        {
            // Module uses export = value - import the assignment value directly
            importedValue = importedModule.ExportAssignmentValue;
        }
        else if (moduleInstance != null)
        {
            // ES6-style module - create a namespace object with all exports
            var exports = new Dictionary<string, object?>(moduleInstance.Exports);
            importedValue = new SharpTSObject(exports);
        }
        else
        {
            // Module not found - define as null
            importedValue = null;
        }

        _environment.Define(importReq.AliasName.Lexeme, importedValue);

        // If this is a re-export, register the export
        if (importReq.IsExported && _currentModuleInstance != null)
        {
            _currentModuleInstance.SetExport(importReq.AliasName.Lexeme, importedValue);
        }

        return ExecutionResult.Success();
    }

    /// <summary>
    /// Checks if the statements begin with a "use strict" directive.
    /// </summary>
    /// <param name="statements">The list of statements to check.</param>
    /// <returns>True if "use strict" directive is found at the beginning.</returns>
    private static bool CheckForUseStrict(List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Directive directive)
            {
                if (directive.Value == "use strict")
                {
                    return true;
                }
                // Continue checking other directives at the start
            }
            else
            {
                // Non-directive statement encountered, stop checking
                break;
            }
        }
        return false;
    }

    /// <summary>
    /// Hoists function declarations by defining them before other statements execute.
    /// This enables functions to call each other regardless of declaration order.
    /// </summary>
    private void HoistFunctionDeclarations(IEnumerable<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            Stmt.Function? funcStmt = null;

            // Handle top-level functions
            if (stmt is Stmt.Function f && f.Body != null)
            {
                funcStmt = f;
            }
            // Handle exported functions
            else if (stmt is Stmt.Export export && export.Declaration is Stmt.Function ef && ef.Body != null)
            {
                funcStmt = ef;
            }

            if (funcStmt != null)
            {
                // Skip if already defined
                if (_environment.IsDefinedLocally(funcStmt.Name.Lexeme))
                    continue;

                // Create the appropriate function type and define it
                if (funcStmt.IsGenerator && funcStmt.IsAsync)
                {
                    _environment.Define(funcStmt.Name.Lexeme, new SharpTSAsyncGeneratorFunction(funcStmt, _environment));
                }
                else if (funcStmt.IsGenerator)
                {
                    _environment.Define(funcStmt.Name.Lexeme, new SharpTSGeneratorFunction(funcStmt, _environment));
                }
                else if (funcStmt.IsAsync)
                {
                    _environment.Define(funcStmt.Name.Lexeme, new SharpTSAsyncFunction(funcStmt, _environment));
                }
                else
                {
                    _environment.Define(funcStmt.Name.Lexeme, new SharpTSFunction(funcStmt, _environment));
                }
            }
        }
    }

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
    /// Control flow uses <see cref="ExecutionResult"/> for non-local jumps.
    /// </remarks>
    private ExecutionResult Execute(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Block block:
                return ExecuteBlock(block.Statements, new RuntimeEnvironment(_environment));
            case Stmt.LabeledStatement labeledStmt:
                return ExecuteLabeledStatement(labeledStmt);
            case Stmt.Sequence seq:
                // Execute in current scope (no new environment)
                foreach (var s in seq.Statements)
                {
                    var result = Execute(s);
                    if (result.IsAbrupt) return result;
                }
                return ExecutionResult.Success();
            case Stmt.Expression exprStmt:
                Evaluate(exprStmt.Expr);
                return ExecutionResult.Success();
            case Stmt.If ifStmt:
                if (IsTruthy(Evaluate(ifStmt.Condition)))
                {
                    return Execute(ifStmt.ThenBranch);
                }
                else if (ifStmt.ElseBranch != null)
                {
                    return Execute(ifStmt.ElseBranch);
                }
                return ExecutionResult.Success();
            case Stmt.While whileStmt:
                return ExecuteWhileCore(
                    () => IsTruthy(Evaluate(whileStmt.Condition)),
                    () => Execute(whileStmt.Body));
            case Stmt.DoWhile doWhileStmt:
                do
                {
                    var result = Execute(doWhileStmt.Body);
                    var (shouldBreak, shouldContinue, abruptResult) = HandleLoopResult(result, null);
                    if (shouldBreak) return ExecutionResult.Success();
                    if (shouldContinue) continue;
                    if (abruptResult.HasValue) return abruptResult.Value;
                } while (IsTruthy(Evaluate(doWhileStmt.Condition)));
                return ExecutionResult.Success();
            case Stmt.For forStmt:
                // Execute initializer once
                if (forStmt.Initializer != null)
                    Execute(forStmt.Initializer);
                // Loop with proper continue handling - increment always runs
                while (forStmt.Condition == null || IsTruthy(Evaluate(forStmt.Condition)))
                {
                    var result = Execute(forStmt.Body);
                    if (result.Type == ExecutionResult.ResultType.Break && result.TargetLabel == null) break;
                    // On continue, execute increment then continue the loop
                    if (result.Type == ExecutionResult.ResultType.Continue && result.TargetLabel == null)
                    {
                        if (forStmt.Increment != null)
                            Evaluate(forStmt.Increment);
                        continue;
                    }
                    if (result.IsAbrupt) return result;
                    // Normal completion: execute increment
                    if (forStmt.Increment != null)
                        Evaluate(forStmt.Increment);
                }
                return ExecutionResult.Success();
            case Stmt.ForOf forOf:
                return ExecuteForOf(forOf);
            case Stmt.ForIn forIn:
                return ExecuteForIn(forIn);
            case Stmt.Break breakStmt:
                return ExecutionResult.Break(breakStmt.Label?.Lexeme);
            case Stmt.Continue continueStmt:
                return ExecutionResult.Continue(continueStmt.Label?.Lexeme);
            case Stmt.Switch switchStmt:
                return ExecuteSwitch(switchStmt);
            case Stmt.TryCatch tryCatch:
                return ExecuteTryCatch(tryCatch);
            case Stmt.Throw throwStmt:
                return ExecutionResult.Throw(Evaluate(throwStmt.Value));
            case Stmt.Var varStmt:
                object? value = null;
                if (varStmt.Initializer != null)
                {
                    value = Evaluate(varStmt.Initializer);
                }
                _environment.Define(varStmt.Name.Lexeme, value);
                return ExecutionResult.Success();
            case Stmt.Const constStmt:
                // Const declarations always have an initializer (enforced by parser)
                object? constValue = Evaluate(constStmt.Initializer);
                _environment.Define(constStmt.Name.Lexeme, constValue);
                return ExecutionResult.Success();
            case Stmt.Function functionStmt:
                // Skip overload signatures (no body) - they're type-checking only
                if (functionStmt.Body == null) return ExecutionResult.Success();
                // Skip if already hoisted
                if (_environment.IsDefinedLocally(functionStmt.Name.Lexeme)) return ExecutionResult.Success();
                if (functionStmt.IsGenerator && functionStmt.IsAsync)
                {
                    // Async generator: async function* foo() { yield await ... }
                    SharpTSAsyncGeneratorFunction asyncGenFunction = new(functionStmt, _environment);
                    _environment.Define(functionStmt.Name.Lexeme, asyncGenFunction);
                }
                else if (functionStmt.IsGenerator)
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
                return ExecutionResult.Success();
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
                // ES2022 private class elements
                List<Stmt.Field> instancePrivateFields = [];
                Dictionary<string, SharpTSFunction> privateMethods = [];
                Dictionary<string, object?> staticPrivateFields = [];
                Dictionary<string, SharpTSFunction> staticPrivateMethods = [];

                // Process fields: collect instance fields, defer static field initialization if using StaticInitializers
                // Note: Declare fields are processed normally - they can't have initializers (enforced by parser),
                // so they'll be added with null/undefined values and can be set externally later.
                bool hasStaticInitializers = classStmt.StaticInitializers != null && classStmt.StaticInitializers.Count > 0;

                foreach (Stmt.Field field in classStmt.Fields)
                {
                    if (field.IsPrivate)
                    {
                        // ES2022 private fields
                        if (field.IsStatic)
                        {
                            if (!hasStaticInitializers)
                            {
                                // Old behavior: evaluate immediately
                                object? fieldValue = field.Initializer != null
                                    ? Evaluate(field.Initializer)
                                    : null;
                                staticPrivateFields[field.Name.Lexeme] = fieldValue;
                            }
                            // else: will be evaluated via StaticInitializers with proper 'this' binding
                        }
                        else
                        {
                            // Collect instance private fields - they'll be initialized when instances are created
                            instancePrivateFields.Add(field);
                        }
                    }
                    else if (field.IsStatic)
                    {
                        if (!hasStaticInitializers)
                        {
                            // Old behavior: evaluate immediately
                            object? fieldValue = field.Initializer != null
                                ? Evaluate(field.Initializer)
                                : null;
                            staticProperties[field.Name.Lexeme] = fieldValue;
                        }
                        // else: will be evaluated via StaticInitializers with proper 'this' binding
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
                    if (method.IsPrivate)
                    {
                        // ES2022 private methods
                        if (method.IsStatic)
                        {
                            staticPrivateMethods[method.Name.Lexeme] = func;
                        }
                        else
                        {
                            privateMethods[method.Name.Lexeme] = func;
                        }
                    }
                    else if (method.IsStatic)
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

                // Process auto-accessors (TypeScript 4.9+)
                List<Stmt.AutoAccessor> instanceAutoAccessors = [];
                Dictionary<string, object?> staticAutoAccessors = [];

                if (classStmt.AutoAccessors != null)
                {
                    foreach (var autoAccessor in classStmt.AutoAccessors)
                    {
                        if (autoAccessor.IsStatic)
                        {
                            // Evaluate static auto-accessor initializer now
                            object? initValue = autoAccessor.Initializer != null
                                ? Evaluate(autoAccessor.Initializer)
                                : null;
                            staticAutoAccessors[autoAccessor.Name.Lexeme] = initValue;
                        }
                        else
                        {
                            // Collect instance auto-accessors for later initialization
                            instanceAutoAccessors.Add(autoAccessor);
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
                    instanceFields,
                    instancePrivateFields,
                    privateMethods,
                    staticPrivateFields,
                    staticPrivateMethods,
                    instanceAutoAccessors.Count > 0 ? instanceAutoAccessors : null,
                    staticAutoAccessors.Count > 0 ? staticAutoAccessors : null);

                // Execute static initializers in declaration order (if present)
                if (hasStaticInitializers)
                {
                    // Create temporary environment with 'this' bound to the class
                    // Also make the class name available so code like Foo.x works
                    var staticEnv = new RuntimeEnvironment(_environment);
                    staticEnv.Define("this", klass);
                    staticEnv.Define(classStmt.Name.Lexeme, klass);

                    var prevEnv = _environment;
                    _environment = staticEnv;

                    try
                    {
                        foreach (var initializer in classStmt.StaticInitializers!)
                        {
                            switch (initializer)
                            {
                                case Stmt.Field field when field.IsStatic:
                                    object? fieldValue = field.Initializer != null
                                        ? Evaluate(field.Initializer)
                                        : null;
                                    if (field.IsPrivate)
                                        klass.SetStaticPrivateField(field.Name.Lexeme, fieldValue);
                                    else
                                        klass.SetStaticProperty(field.Name.Lexeme, fieldValue);
                                    break;

                                case Stmt.StaticBlock block:
                                    foreach (var blockStmt in block.Body)
                                    {
                                        var result = Execute(blockStmt);
                                        if (result.IsAbrupt)
                                        {
                                            // Handle throw from static block
                                            if (result.Type == ExecutionResult.ResultType.Throw)
                                            {
                                                throw new Exception($"Error in static block: {Stringify(result.Value)}");
                                            }
                                            // Return, break, continue are not allowed (validated by type checker)
                                        }
                                    }
                                    break;
                            }
                        }
                    }
                    finally
                    {
                        _environment = prevEnv;
                    }
                }

                // Apply decorators in the correct order
                klass = ApplyAllDecorators(classStmt, klass, methods, staticMethods, getters, setters);

                if (classStmt.Superclass != null)
                {
                    _environment = _environment.Enclosing!;
                }

                _environment.Assign(classStmt.Name, klass);
                return ExecutionResult.Success();
            case Stmt.TypeAlias:
                // Type aliases are compile-time only, no runtime effect
                return ExecutionResult.Success();
            case Stmt.Enum enumStmt:
                ExecuteEnumDeclaration(enumStmt);
                return ExecutionResult.Success();
            case Stmt.Namespace ns:
                return ExecuteNamespace(ns);
            case Stmt.ImportAlias importAlias:
                return ExecuteImportAlias(importAlias);
            case Stmt.Return returnStmt:
                object? returnValue = null;
                if (returnStmt.Value != null) returnValue = Evaluate(returnStmt.Value);
                return ExecutionResult.Return(returnValue);
            case Stmt.Print printStmt:
                Console.WriteLine(Stringify(Evaluate(printStmt.Expr)));
                return ExecutionResult.Success();
            case Stmt.Import:
                // Imports are handled in BindModuleImports before execution
                // In single-file mode, imports are a no-op (type checker would have errored)
                return ExecutionResult.Success();
            case Stmt.ImportRequire importReq:
                return ExecuteImportRequire(importReq);
            case Stmt.Export exportStmt:
                return ExecuteExport(exportStmt);
            case Stmt.Directive:
                // Directives are processed at the start of interpretation for their side effects (strict mode)
                // When encountered during execution, they are a no-op
                return ExecutionResult.Success();
            case Stmt.DeclareModule:
            case Stmt.DeclareGlobal:
                // Module/global augmentations and ambient declarations are type-only
                // No runtime effect - types were merged during type checking
                return ExecutionResult.Success();
            default:
                return ExecutionResult.Success();
        }
    }

}
