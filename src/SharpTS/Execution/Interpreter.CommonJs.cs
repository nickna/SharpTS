using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns.Modules;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

/// <summary>
/// CommonJS execution support for the interpreter.
/// </summary>
/// <remarks>
/// Implements the <c>require</c> function and CJS module execution path. CJS modules are
/// wrapped in a synthetic function scope binding <c>require</c>, <c>module</c>, <c>exports</c>,
/// <c>__filename</c>, <c>__dirname</c>, and <c>global</c> as locals.
///
/// Circular requires are handled by storing a live <see cref="ModuleInstance.CommonJsModuleObject"/>
/// at the moment the module starts executing, so a re-entrant require sees the partially-populated
/// exports object (matching Node's semantics).
/// </remarks>
public partial class Interpreter
{
    /// <summary>
    /// Resolves and loads a CommonJS module by specifier, returning its <c>module.exports</c> value.
    /// Called by the <c>require()</c> global handler.
    /// </summary>
    /// <param name="specifier">The require specifier (e.g., "./util", "lodash").</param>
    /// <returns>The module's exports value.</returns>
    /// <exception cref="InterpreterException">If the module cannot be resolved or fails to load.</exception>
    internal object? RequireCommonJsModule(string specifier)
    {
        string callerPath = _currentModule?.Path ?? _moduleResolver?.GetType().Name ?? "";
        return RequireCommonJsModule(specifier, callerPath);
    }

    /// <summary>
    /// Resolves a CommonJS module relative to an explicit caller path. Used by
    /// <c>module.createRequire(filename)</c>.
    /// </summary>
    internal object? RequireCommonJsModule(string specifier, string callerPath)
    {
        if (_moduleResolver == null)
        {
            throw new InterpreterException(
                $"Cannot find module '{specifier}': require() is only available when running with module resolution enabled.");
        }

        // Resolve relative to the calling module's directory.
        string resolvedPath;
        try
        {
            resolvedPath = _moduleResolver.ResolveRuntimeModulePath(
                specifier, callerPath, ResolutionKind.Cjs);
        }
        catch (Exception ex) when (ex is not Runtime.Exceptions.ThrowException)
        {
            // Throw a Node-style MODULE_NOT_FOUND so user code in try/catch can detect it.
            var err = new SharpTSError(ex.Message) { Code = "MODULE_NOT_FOUND" };
            throw new Runtime.Exceptions.ThrowException(err);
        }

        // Built-in modules: delegate to the existing built-in module path.
        if (resolvedPath.StartsWith(BuiltInModuleRegistry.BuiltInPrefix))
        {
            var builtInName = BuiltInModuleRegistry.GetModuleName(resolvedPath);
            if (builtInName != null && BuiltInModuleValues.HasInterpreterSupport(builtInName))
            {
                var exports = BuiltInModuleValues.GetModuleExports(builtInName);
                return BuiltInModuleValues.TryCreateNamespaceOverride(builtInName, exports)
                    ?? new SharpTSObject(new Dictionary<string, object?>(exports));
            }
            throw new InterpreterException($"Built-in module '{builtInName}' is not supported in interpreter mode.");
        }

        // Already loaded? Return the live exports value (handles circular and completed cases).
        if (_loadedModules.TryGetValue(resolvedPath, out var existing))
        {
            return GetCurrentExports(existing);
        }

        // Need to load + execute. LoadModule parses the file and runs CJS detection.
        ParsedModule parsed;
        try
        {
            parsed = _moduleResolver.LoadModule(resolvedPath);
        }
        catch (Exception ex) when (ex is not Runtime.Exceptions.ThrowException)
        {
            var err = new SharpTSError(ex.Message) { Code = "MODULE_NOT_FOUND" };
            throw new Runtime.Exceptions.ThrowException(err);
        }

        if (parsed.IsCommonJs)
        {
            return ExecuteCommonJsModule(parsed);
        }

        // ESM file required from CJS — execute as ESM and return its namespace object.
        // ExportsAsObject() mirrors Node's ESM→CJS interop: the require() caller gets
        // an object of all named exports (not the default export alone). This matters
        // for stdlib modules (path, os, querystring) whose consumers write
        // `const path = require('path'); path.join(...)` — path.join must resolve to
        // the named export, which DefaultExport wouldn't expose for pure-TS stdlib.
        ExecuteModule(parsed);
        var instance = _loadedModules.GetValueOrDefault(resolvedPath);
        if (instance == null)
        {
            return null;
        }
        // Node's assert entry points are callable CommonJS exports. Their ESM
        // facades expose the callable object as the default export while named
        // imports remain available to ESM consumers.
        if (resolvedPath is "stdlib:node/assert.ts" or "stdlib:node/assert/strict.ts" &&
            instance.DefaultExport != null)
        {
            return instance.DefaultExport;
        }
        return instance.CommonJsModuleObject != null
            ? instance.CommonJsModuleObject.GetProperty("exports")
            : instance.ExportsAsObject();
    }

    /// <summary>
    /// Executes a parsed CommonJS module, populating its exports. Idempotent — re-entrant calls
    /// (circular requires) return the partially-populated exports value.
    /// </summary>
    private object? ExecuteCommonJsModule(ParsedModule module)
    {
        // Re-entrant call: return the live exports.
        if (_loadedModules.TryGetValue(module.Path, out var existing))
        {
            return GetCurrentExports(existing);
        }

        // Create the module instance and the live module/exports objects BEFORE execution
        // so circular requires can see partial state.
        var moduleInstance = new ModuleInstance();
        var exportsObj = new SharpTSObject([]);
        var moduleObj = new SharpTSObject(new Dictionary<string, object?> { ["exports"] = exportsObj });
        moduleInstance.CommonJsModuleObject = moduleObj;
        moduleInstance.DefaultExport = exportsObj;
        _loadedModules[module.Path] = moduleInstance;

        // Build the CJS module-scoped environment.
        var moduleEnv = new RuntimeEnvironment(_environment);
        moduleEnv.Define("module", moduleObj);
        moduleEnv.Define("exports", exportsObj);
        // `global` in Node is a reference to the global object (same as
        // `globalThis`). Bind this realm's global object so `global.Object`,
        // `global.process`, etc. resolve and `global.x = …` stays realm-local.
        moduleEnv.Define("global", GlobalThis);
        // In Node CommonJS, top-level `this` === `module.exports` (initially
        // an empty object). UMD wrappers depend on this.
        moduleEnv.Define("this", exportsObj);
        // __filename and __dirname are resolved via _currentModule in the variable lookup path,
        // and `require` is a registered global function — both are available without local bindings.

        using (PushModuleContext(moduleEnv, module, moduleInstance))
        {
            HoistFunctionDeclarations(module.Statements);

            foreach (var stmt in module.Statements)
            {
                if (stmt is Stmt.Expression exprStmt)
                {
                    try
                    {
                        object? result = Evaluate(exprStmt.Expr);
                        if (_waitForTopLevelPromises && result is SharpTSPromise promise)
                        {
                            // Pump the event loop while waiting: a bare GetResult()
                            // here hard-hangs when the promise needs a timer or I/O
                            // continuation to settle, because those continuations
                            // only ever run on this thread.
                            WaitForPromise(promise);
                        }
                    }
                    catch (ThrowException tex)
                    {
                        throw new InterpreterException(Stringify(tex.Value));
                    }
                }
                else
                {
                    var result = Execute(stmt);
                    if (result.Type == ExecutionResult.ResultType.Throw)
                    {
                        throw new InterpreterException(Stringify(result.Value.ToObject()));
                    }
                    if (result.IsAbrupt) break;
                }
            }

            // Snapshot the final module.exports value.
            moduleInstance.DefaultExport = moduleObj.GetProperty("exports");
            moduleInstance.IsExecuted = true;
        }

        return moduleInstance.DefaultExport;
    }

    /// <summary>
    /// Returns the current exports value of a module instance, accounting for CJS module.exports
    /// reassignment (which may happen mid-execution and must be visible to circular requires).
    /// </summary>
    private static object? GetCurrentExports(ModuleInstance instance)
    {
        if (instance.CommonJsModuleObject != null)
        {
            return instance.CommonJsModuleObject.GetProperty("exports");
        }
        // ESM→CJS interop: when a CJS caller requires an ESM module, it expects a namespace
        // object of named exports (Node's semantics), not the default export alone. Stdlib
        // modules like path.ts have named exports but no default export — returning just
        // DefaultExport would yield null.
        return instance.DefaultExport ?? instance.ExportsAsObject();
    }
}
