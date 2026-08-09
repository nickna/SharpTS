#pragma warning disable SHARPTS_HOSTING001

using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Hosting;
using SharpTS.Modules;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Module compilation methods for the IL compiler.
/// </summary>
public partial class ILCompiler
{
    // Track $GetNamespace methods for module registration
    private readonly Dictionary<string, MethodBuilder> _moduleGetNamespaceMethods = [];

    /// <summary>
    /// Defines a module type with export fields.
    /// Script files (no import/export) are skipped - they share global scope.
    /// </summary>
    private void DefineModuleType(ParsedModule module)
    {
        // Skip script files - they share global scope and don't have module types
        if (module.IsScript)
        {
            return;
        }

        // dotnet: interop modules have no emitted module type — their imports compile to
        // direct external-interop IL (RegisterDotNetImports), never to export-field reads.
        // (Also avoids duplicate $Module_ names: distinct specifiers in one namespace share
        // a filename-derived ModuleName.)
        if (module.IsDotNetModule || module.IsDotNetExtensionModule)
        {
            return;
        }

        // Create module class: $Module_<name>
        string moduleTypeName = $"$Module_{CompilationContext.SanitizeModuleName(module.ModuleName)}";
        var moduleType = _moduleBuilder.DefineType(
            moduleTypeName,
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.Abstract
        );

        _modules.Types[module.Path] = moduleType;
        Dictionary<string, FieldBuilder> exportFields = [];
        bool hasExportAssignment = false;

        // First pass: check for export = syntax
        foreach (var stmt in module.Statements)
        {
            if (stmt is Stmt.Export export && export.ExportAssignment != null)
            {
                // Create $exportAssignment field for CommonJS export = syntax
                var field = moduleType.DefineField(
                    "$exportAssignment",
                    typeof(object),
                    FieldAttributes.Public | FieldAttributes.Static
                );
                exportFields["$exportAssignment"] = field;
                hasExportAssignment = true;

                // Track if this export = exports a class (for cross-module static member access)
                // We scan the module's statements directly since ClassToModule isn't populated yet
                if (export.ExportAssignment is Expr.Variable classVar)
                {
                    string className = classVar.Name.Lexeme;
                    // Check if this module contains a class with this name
                    bool hasMatchingClass = module.Statements.Any(s =>
                        s is Stmt.Class c && c.Name.Lexeme == className);

                    if (hasMatchingClass)
                    {
                        string qualifiedClassName = GetQualifiedClassName(className, module.Path);
                        _modules.ExportAssignmentClasses[module.Path] = qualifiedClassName;
                    }
                }

                break; // No other exports allowed with export =
            }
        }

        // Second pass: create standard export fields (only if no export assignment)
        if (!hasExportAssignment)
        foreach (var stmt in module.Statements)
        {
            if (stmt is Stmt.Export export)
            {
                if (export.IsTypeOnly)
                    continue;

                if (export.IsDefaultExport)
                {
                    // Default export field
                    var field = moduleType.DefineField(
                        "$default",
                        typeof(object),
                        FieldAttributes.Public | FieldAttributes.Static
                    );
                    exportFields["$default"] = field;
                }
                else if (export.Declaration != null)
                {
                    // Named export from declaration
                    string? exportName = GetExportDeclarationName(export.Declaration);
                    if (exportName != null)
                    {
                        var field = moduleType.DefineField(
                            exportName,
                            typeof(object),
                            FieldAttributes.Public | FieldAttributes.Static
                        );
                        exportFields[exportName] = field;
                    }
                }
                else if (export.NamedExports != null && export.FromModulePath == null)
                {
                    // Named exports like export { x, y as z }
                    foreach (var spec in export.NamedExports)
                    {
                        if (spec.IsTypeOnly)
                            continue;
                        string exportedName = spec.ExportedName?.Lexeme ?? spec.LocalName.Lexeme;
                        if (!exportFields.ContainsKey(exportedName))
                        {
                            var field = moduleType.DefineField(
                                exportedName,
                                typeof(object),
                                FieldAttributes.Public | FieldAttributes.Static
                            );
                            exportFields[exportedName] = field;
                        }
                    }
                }
                else if (export.FromModulePath != null && _modules.Resolver != null)
                {
                    // Re-export: export { x } from './module' or export * from './module'
                    string sourcePath = _modules.Resolver.ResolveRuntimeModulePath(
                        export.FromModulePath, module.Path);

                    if (export.NamedExports != null)
                    {
                        // export { x, y as z } from './module'
                        foreach (var spec in export.NamedExports)
                        {
                            if (spec.IsTypeOnly)
                                continue;
                            string exportedName = spec.ExportedName?.Lexeme ?? spec.LocalName.Lexeme;
                            if (!exportFields.ContainsKey(exportedName))
                            {
                                var field = moduleType.DefineField(
                                    exportedName,
                                    typeof(object),
                                    FieldAttributes.Public | FieldAttributes.Static
                                );
                                exportFields[exportedName] = field;
                            }
                        }
                    }
                    else
                    {
                        // export * from './module' - need source module's exports
                        // Source module is processed first (topological order)
                        if (_modules.ExportFields.TryGetValue(sourcePath, out var sourceFields))
                        {
                            foreach (var (name, _) in sourceFields)
                            {
                                if (name == "$default") continue;  // * doesn't include default
                                if (!exportFields.ContainsKey(name))
                                {
                                    var field = moduleType.DefineField(
                                        name,
                                        typeof(object),
                                        FieldAttributes.Public | FieldAttributes.Static
                                    );
                                    exportFields[name] = field;
                                }
                            }
                        }
                    }
                }
            }
        }

        _modules.ExportFields[module.Path] = exportFields;

        // Pre-scan imports and create static fields for imported values
        // This allows functions in this module to access imported values
        CreateModuleImportFields(module, moduleType);

        // Track which exports are classes (for direct constructor calls in importing modules)
        TrackClassExports(module);

        // Create $GetNamespace method that returns all exports as SharpTSObject
        EmitModuleGetNamespace(module, moduleType, exportFields);
    }

    /// <summary>
    /// Emits the $GetNamespace method that returns all module exports as a SharpTSObject.
    /// Used for dynamic import - returns the module namespace object.
    /// For modules using export =, returns { default: value } for ESM interop.
    /// </summary>
    private void EmitModuleGetNamespace(
        ParsedModule module,
        TypeBuilder moduleType,
        Dictionary<string, FieldBuilder> exportFields)
    {
        var method = moduleType.DefineMethod(
            "$GetNamespace",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            Type.EmptyTypes
        );
        _moduleGetNamespaceMethods[module.Path] = method;

        var il = method.GetILGenerator();

        // var dict = new Dictionary<string, object?>();
        var dictType = typeof(Dictionary<string, object?>);
        var dictLocal = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Newobj, dictType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, dictLocal);

        // Check if this module uses export = syntax
        if (exportFields.TryGetValue("$exportAssignment", out var exportAssignField))
        {
            // For export = modules, return { default: value } for ESM interop
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Ldstr, "default");
            il.Emit(OpCodes.Ldsfld, exportAssignField);
            il.Emit(OpCodes.Callvirt, dictType.GetMethod("set_Item")!);
        }
        else
        {
            // Standard ES6 module - add each export to the dictionary
            foreach (var (exportName, field) in exportFields)
            {
                // dict[exportName] = exportField;
                il.Emit(OpCodes.Ldloc, dictLocal);
                il.Emit(OpCodes.Ldstr, exportName == "$default" ? "default" : exportName);
                il.Emit(OpCodes.Ldsfld, field);
                il.Emit(OpCodes.Callvirt, dictType.GetMethod("set_Item")!);
            }
        }

        // return $Runtime.CreateObject(dict);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Call, _runtime.CreateObject);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Gets the name of an exported declaration.
    /// </summary>
    private string? GetExportDeclarationName(Stmt decl) => decl switch
    {
        Stmt.Function f => f.Name.Lexeme,
        Stmt.Class c => c.Name.Lexeme,
        Stmt.Var v => v.Name.Lexeme,
        Stmt.Const ct => ct.Name.Lexeme,
        Stmt.Enum e => e.Name.Lexeme,
        Stmt.Interface or Stmt.TypeAlias => null, // Type-only, no runtime export
        _ => null
    };

    /// <summary>
    /// Registers the external .NET types imported via <c>dotnet:</c> specifiers in this module,
    /// routing them through the same <see cref="TypeMapper"/>/ExternalTypes registries an
    /// <c>@DotNetType declare class</c> uses — so construction, member calls, and static access
    /// compile to the identical direct-IL external-interop paths (fully standalone output).
    /// Resolution happened at module-load time (<see cref="DotNetImports.EnsureImports"/>);
    /// this only transfers the resolved types into the compilation registries.
    /// </summary>
    private void RegisterDotNetImports(ParsedModule module)
    {
        foreach (var stmt in module.Statements)
        {
            if (stmt is not Stmt.Import import || !DotNetImports.IsDotNetSpecifier(import.ModulePath))
                continue;
            if (import.NamedImports == null)
                continue;

            var dotnetModule = _modules.Resolver?.GetCachedModule(import.ModulePath);
            if (dotnetModule?.DotNetExports == null)
                continue;

            foreach (var spec in import.NamedImports)
            {
                if (!dotnetModule.DotNetExports.TryGetValue(spec.Imported.Lexeme, out var externalType))
                    continue;

                string localName = spec.LocalName?.Lexeme ?? spec.Imported.Lexeme;
                // The local binding name drives `new X()` / static-access dispatch; the CLR
                // simple name drives receiver-typed instance dispatch (the checker's
                // synthesized class is named after the CLR type).
                RegisterDotNetImportedType(localName, externalType);
                RegisterDotNetImportedType(externalType.Name, externalType);
            }
        }
    }

    /// <summary>
    /// Registers one name → external type mapping unless a user-defined class already claims
    /// the name. ExternalTypes lookups run before user-class dispatch, so an unconditional
    /// registration would hijack same-named user classes program-wide.
    /// </summary>
    private void RegisterDotNetImportedType(string name, Type externalType)
    {
        if (_modules.ClassToModule.ContainsKey(name))
        {
            if (!_classes.ExternalTypes.TryGetValue(name, out var existing) || existing != externalType)
            {
                AddWarning(
                    $"dotnet: import '{name}' ({externalType.FullName}) conflicts with a " +
                    "user-defined class of the same name; the user class wins in compiled dispatch. " +
                    "Rename the import (import { X as Alias }) to use the .NET type.");
            }
            return;
        }

        _classes.ExternalTypes.TryAdd(name, externalType);
        _typeMapper.RegisterExternalType(name, externalType);
    }

    /// <summary>
    /// Creates static fields for imported values in this module.
    /// This allows functions in the module to access imported values.
    /// </summary>
    private void CreateModuleImportFields(ParsedModule module, TypeBuilder moduleType)
    {
        Dictionary<string, FieldBuilder> importFields = [];

        foreach (var stmt in module.Statements)
        {
            if (stmt is Stmt.Import import && !import.IsTypeOnly)
            {
                // Skip built-in modules - they have their own handling
                string? builtInModuleName = Runtime.BuiltIns.Modules.BuiltInModuleRegistry.GetModuleName(import.ModulePath);
                if (builtInModuleName != null)
                    continue;

                // dotnet: imports need no import fields — every use site compiles to direct
                // external-interop IL keyed off the ExternalTypes registry (RegisterDotNetImports).
                if (DotNetImports.IsDotNetSpecifier(import.ModulePath) ||
                    DotNetExtensionImports.IsSpecifier(import.ModulePath))
                    continue;

                // Default import: import x from './module'
                if (import.DefaultImport != null)
                {
                    string localName = import.DefaultImport.Lexeme;
                    if (!importFields.ContainsKey(localName))
                    {
                        var field = moduleType.DefineField(
                            $"$import_{localName}",
                            typeof(object),
                            FieldAttributes.Assembly | FieldAttributes.Static
                        );
                        importFields[localName] = field;
                    }
                }

                // Named imports: import { x, y as z } from './module'
                if (import.NamedImports != null)
                {
                    foreach (var spec in import.NamedImports.Where(s => !s.IsTypeOnly))
                    {
                        string localName = spec.LocalName?.Lexeme ?? spec.Imported.Lexeme;
                        if (!importFields.ContainsKey(localName))
                        {
                            var field = moduleType.DefineField(
                                $"$import_{localName}",
                                typeof(object),
                                FieldAttributes.Assembly | FieldAttributes.Static
                            );
                            importFields[localName] = field;
                        }
                    }
                }

                // Namespace import: import * as x from './module'
                if (import.NamespaceImport != null)
                {
                    string localName = import.NamespaceImport.Lexeme;
                    if (!importFields.ContainsKey(localName))
                    {
                        var field = moduleType.DefineField(
                            $"$import_{localName}",
                            typeof(object),
                            FieldAttributes.Assembly | FieldAttributes.Static
                        );
                        importFields[localName] = field;
                    }
                }
            }
            else if (stmt is Stmt.ImportRequire importReq)
            {
                // Skip built-in modules
                string? builtInModuleName = Runtime.BuiltIns.Modules.BuiltInModuleRegistry.GetModuleName(importReq.ModulePath);
                if (builtInModuleName != null)
                    continue;

                string localName = importReq.AliasName.Lexeme;
                if (!importFields.ContainsKey(localName))
                {
                    var field = moduleType.DefineField(
                        $"$import_{localName}",
                        typeof(object),
                        FieldAttributes.Assembly | FieldAttributes.Static
                    );
                    importFields[localName] = field;
                }
            }
        }

        _modules.ImportFields[module.Path] = importFields;
    }

    /// <summary>
    /// Emits a top-level expression statement plus "top-level await" handling: if the
    /// value is a <c>Task&lt;object&gt;</c> or <c>$Promise</c>, pump the event loop until
    /// it settles via <see cref="EmittedRuntime.EventLoopWaitForTask"/>, then GetResult to
    /// rethrow faults. Shared by the single-file entry point (<c>EmitDefaultEntryPoint</c>)
    /// and every module/script init body so both wait the same way.
    /// </summary>
    /// <remarks>
    /// The wait MUST pump (drain the loop queue + fire timers), not block on
    /// <c>GetResult()</c>: once <c>$EventLoopSyncContext</c> is installed (issues
    /// #319/#320/#381), await continuations are Posted to the loop queue, so a thread
    /// blocked in <c>GetResult()</c> would never drain them — the awaited promise could
    /// never settle and the program would deadlock. <c>WaitForTask</c> runs the queue and
    /// the timer processor on this thread until the task completes (or the loop proves
    /// quiescent, matching Node's "a forever-pending top-level promise doesn't block exit").
    /// </remarks>
    private void EmitExpressionWithAsyncWait(ILGenerator il, ILEmitter emitter, Stmt.Expression exprStmt)
    {
        // This path drives expression emission itself instead of going through EmitStatement, so it
        // has to mark the statement or every top-level expression would be unsteppable.
        emitter.MarkStatementStart(exprStmt);

        emitter.EmitExpression(exprStmt.Expr);

        // Box value types first (e.g., delete returns boolean)
        emitter.Helpers.EnsureBoxed();
        var exprResult = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, exprResult);

        var notTaskLabel = il.DefineLabel();
        var waitForTaskLabel = il.DefineLabel();
        var isTaskLabel = il.DefineLabel();

        // Check for Task<object> first
        il.Emit(OpCodes.Ldloc, exprResult);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Brtrue, isTaskLabel);

        // Check for $Promise (async function return type)
        il.Emit(OpCodes.Ldloc, exprResult);
        il.Emit(OpCodes.Isinst, _runtime.TSPromiseType);
        il.Emit(OpCodes.Brfalse, notTaskLabel);

        // It's a $Promise - extract its underlying Task
        il.Emit(OpCodes.Ldloc, exprResult);
        il.Emit(OpCodes.Castclass, _runtime.TSPromiseType);
        il.Emit(OpCodes.Callvirt, _runtime.TSPromiseTaskGetter);
        il.Emit(OpCodes.Br, waitForTaskLabel);

        // It's a Task<object> directly
        il.MarkLabel(isTaskLabel);
        il.Emit(OpCodes.Ldloc, exprResult);
        il.Emit(OpCodes.Castclass, _types.TaskOfObject);

        // Pump the event loop until the task settles (drains Posted await
        // continuations + fires timers). Returns false if the loop went quiescent
        // with the task still pending (never-settling promise) — then skip GetResult.
        il.MarkLabel(waitForTaskLabel);
        var taskLocal = il.DeclareLocal(_types.TaskOfObject);
        il.Emit(OpCodes.Stloc, taskLocal);

        il.Emit(OpCodes.Call, _runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Ldloc, taskLocal);
        il.Emit(OpCodes.Callvirt, _runtime.EventLoopWaitForTask);
        il.Emit(OpCodes.Brfalse, notTaskLabel);

        // Task is complete — GetResult() to rethrow if faulted
        il.Emit(OpCodes.Ldloc, taskLocal);
        var getAwaiter = _types.GetMethodNoParams(_types.TaskOfObject, "GetAwaiter");
        il.Emit(OpCodes.Call, getAwaiter);
        var awaiterLocal = il.DeclareLocal(_types.TaskAwaiterOfObject);
        il.Emit(OpCodes.Stloc, awaiterLocal);
        il.Emit(OpCodes.Ldloca, awaiterLocal);
        var getResult = _types.GetMethodNoParams(_types.TaskAwaiterOfObject, "GetResult");
        il.Emit(OpCodes.Call, getResult);
        il.Emit(OpCodes.Pop);  // Discard the result

        il.MarkLabel(notTaskLabel);
        // No pop needed - value is in local
    }

    /// <summary>
    /// Emits and discards a top-level expression for hosted initialization. An
    /// async call is allowed to start, but its returned task/promise is never
    /// synchronously observed or pumped from initialization.
    /// </summary>
    private void EmitHostedExpression(ILGenerator il, ILEmitter emitter, Stmt.Expression exprStmt)
    {
        emitter.MarkStatementStart(exprStmt);
        emitter.EmitExpression(exprStmt.Expr);
        emitter.Helpers.EnsureBoxed();
        il.Emit(OpCodes.Pop);
    }

    /// <summary>
    /// Emits the guarded private hosted initialization core on <c>$Program</c>.
    /// The versioned ABI factory owns the public surface.
    /// </summary>
    private MethodBuilder EmitHostedInitializationMethod(Action<ILGenerator, MethodBuilder> emitInitialization)
    {
        var initializedField = _programType.DefineField(
            "$hostedInitialized",
            typeof(bool),
            FieldAttributes.Private | FieldAttributes.Static);
        var initializeMethod = _programType.DefineMethod(
            "$InitializeHostedCore",
            MethodAttributes.Assembly | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes);

        var initializeIl = initializeMethod.GetILGenerator();
        var alreadyInitialized = initializeIl.DefineLabel();
        initializeIl.Emit(OpCodes.Ldsfld, initializedField);
        initializeIl.Emit(OpCodes.Brtrue, alreadyInitialized);
        initializeIl.Emit(OpCodes.Ldc_I4_1);
        initializeIl.Emit(OpCodes.Stsfld, initializedField);
        emitInitialization(initializeIl, initializeMethod);
        initializeIl.MarkLabel(alreadyInitialized);
        initializeIl.Emit(OpCodes.Ret);

        return initializeMethod;
    }

    /// <summary>
    /// Emits the initialization method for a module.
    /// Includes an initialization guard to ensure module is only initialized once.
    /// Script files are initialized in the main program type, not a module type.
    /// </summary>
    private void EmitModuleInit(ParsedModule module)
    {
        // Script files are initialized in the main $Program type
        if (module.IsScript)
        {
            EmitScriptInit(module);
            return;
        }

        // dotnet: interop modules have no module type and nothing to initialize.
        if (module.IsDotNetModule || module.IsDotNetExtensionModule)
        {
            return;
        }

        var moduleType = _modules.Types[module.Path];
        var exportFields = _modules.ExportFields[module.Path];

        // Create _initialized field for caching guard
        var initializedField = moduleType.DefineField(
            "_initialized",
            typeof(bool),
            FieldAttributes.Assembly | FieldAttributes.Static
        );
        _moduleInitializedFields[module.Path] = initializedField;

        // Create $Initialize method
        var initMethod = moduleType.DefineMethod(
            "$Initialize",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes
        );
        _modules.InitMethods[module.Path] = initMethod;

        var il = initMethod.GetILGenerator();

        // Guard: if (_initialized) return;
        var skipLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, initializedField);
        il.Emit(OpCodes.Brtrue, skipLabel);

        // _initialized = true;
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stsfld, initializedField);

        // Set _modules.CurrentPath before CreateModuleTopLevelContext so
        // BuildTopLevelStaticVarsForModule can scope to this module.
        var savedPath = _modules.CurrentPath;
        _modules.CurrentPath = module.Path;
        var ctx = CreateModuleTopLevelContext(il, initMethod);
        _modules.CurrentPath = savedPath;
        ctx.CurrentModulePath = module.Path;
        ctx.ModuleExportFields = _modules.ExportFields;
        ctx.ModuleTypes = _modules.Types;
        ctx.ModuleInitMethods = _modules.InitMethods;
        ctx.ModuleImportFields = _modules.ImportFields;
        ctx.ModuleResolver = _modules.Resolver;
        ctx.CommonJsExportFields = _modules.CommonJsExportFields;
        ctx.CommonJsGetExportsMethods = _modules.CommonJsGetExportsMethods;

        // Note: imports are already merged into ctx.TopLevelStaticVars via
        // BuildTopLevelStaticVarsForModule in CreateModuleTopLevelContext.

        var emitter = new ILEmitter(ctx);
        bool hasHostedTopLevelAwait = _hosted && TopLevelAwaitDetector.Contains(module.Statements);
        if (hasHostedTopLevelAwait)
        {
            // The internal async module runner executes this module through the
            // compiler's normal async state-machine implementation. Keep the
            // synchronous initializer inert so the statements cannot run twice.
            il.MarkLabel(skipLabel);
            il.Emit(OpCodes.Ret);
            return;
        }

        foreach (var stmt in module.Statements)
        {
            // Skip class, function, interface, type alias, and enum declarations
            // (they are compiled separately in earlier phases)
            if (stmt is Stmt.Class or Stmt.Function or Stmt.Interface or Stmt.TypeAlias or Stmt.Enum)
            {
                continue;
            }

            // Special handling for expression statements to wait for top-level async calls
            if (stmt is Stmt.Expression exprStmt)
            {
                if (hasHostedTopLevelAwait && exprStmt.Expr is Expr.Await)
                    continue;
                if (_hosted)
                    EmitHostedExpression(il, emitter, exprStmt);
                else
                    EmitExpressionWithAsyncWait(il, emitter, exprStmt);
            }
            else
            {
                emitter.EmitStatement(stmt);
            }
        }

        il.MarkLabel(skipLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the initialization method for a script file.
    /// Script files share global scope in $Program.
    /// </summary>
    private void EmitScriptInit(ParsedModule script)
    {
        // Create initialization method in $Program
        string methodName = $"$InitScript_{CompilationContext.SanitizeModuleName(script.ModuleName)}";
        var initMethod = _programType.DefineMethod(
            methodName,
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes
        );
        _modules.InitMethods[script.Path] = initMethod;

        // Create an initialized guard field in $Program
        var initializedField = _programType.DefineField(
            $"$script_initialized_{CompilationContext.SanitizeModuleName(script.ModuleName)}",
            typeof(bool),
            FieldAttributes.Private | FieldAttributes.Static
        );
        _moduleInitializedFields[script.Path] = initializedField;

        var il = initMethod.GetILGenerator();

        // Guard: if (_initialized) return;
        var skipLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, initializedField);
        il.Emit(OpCodes.Brtrue, skipLabel);

        // _initialized = true;
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stsfld, initializedField);

        // Scripts share global scope — pass null modulePath so BuildTopLevelStaticVarsForModule
        // returns the global dict (non-captured field is visible across script-merged files).
        var savedPath = _modules.CurrentPath;
        _modules.CurrentPath = null;
        var ctx = CreateModuleTopLevelContext(il, initMethod);
        _modules.CurrentPath = savedPath;
        ctx.CurrentModulePath = script.Path;
        ctx.ModuleExportFields = _modules.ExportFields;
        ctx.ModuleTypes = _modules.Types;
        ctx.ModuleInitMethods = _modules.InitMethods;
        ctx.ModuleResolver = _modules.Resolver;

        var emitter = new ILEmitter(ctx);

        foreach (var stmt in script.Statements)
        {
            // Skip class, function, interface, type alias, and enum declarations
            // (they are compiled separately in earlier phases)
            if (stmt is Stmt.Class or Stmt.Function or Stmt.Interface or Stmt.TypeAlias or Stmt.Enum)
            {
                continue;
            }

            // Special handling for expression statements to wait for top-level async calls
            if (stmt is Stmt.Expression exprStmt)
            {
                if (_hosted)
                    EmitHostedExpression(il, emitter, exprStmt);
                else
                    EmitExpressionWithAsyncWait(il, emitter, exprStmt);
            }
            else
            {
                emitter.EmitStatement(stmt);
            }
        }

        il.MarkLabel(skipLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the entry point that initializes all modules in dependency order.
    /// Also initializes the module registry and registers all modules for dynamic import support.
    /// Script files are initialized but not registered (they don't have exports).
    /// </summary>
    private void EmitModulesEntryPoint(List<ParsedModule> modules)
    {
        var mainMethod = _programType.DefineMethod(
            "Main",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes
        );
        _entryPoint = mainMethod;

        var il = mainMethod.GetILGenerator();

        MethodBuilder? hostedInitialize = null;
        if (_hosted)
        {
            hostedInitialize = EmitHostedModulesInitializationMethod(modules);
            EmitHostedAbi(
                hostedInitialize,
                initializerAcceptsRuntime: true,
                initializerReturnsTask: true);
        }

        // Install the event-loop SynchronizationContext before any module runs, so
        // top-level async/await continuations (e.g. fetch) resume on the event-loop
        // thread instead of escaping to the thread pool — the same durable fix the
        // single-file entry point applies (issues #319/#320/#381). Module init bodies
        // hold the first top-level awaits, so the context must be current before the
        // first $Initialize call captures an awaiter.
        EmitInstallEventLoopSyncContext(il);

        if (hostedInitialize == null)
        {
            EmitModulesInitialization(il, modules);
        }

        // Run the event loop — no-op if no handles are active
        il.Emit(OpCodes.Call, _runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, _runtime.EventLoopRun);
        // Node process lifecycle at natural drain: 'beforeExit' (re-entering
        // the loop when a listener schedules work), then 'exit' (#1080).
        il.Emit(OpCodes.Call, _runtime.ProcessRunLifecycle);

        il.Emit(OpCodes.Ret);
    }

    private MethodBuilder EmitHostedModulesInitializationMethod(List<ParsedModule> modules)
    {
        var method = _programType.DefineMethod(
            "$InitializeHostedCore",
            MethodAttributes.Assembly | MethodAttributes.Static,
            typeof(Task),
            [typeof(SharpTSHostedRuntimeBase)]);
        var il = method.GetILGenerator();

        if (_closures.EntryPointDisplayClass != null &&
            _closures.EntryPointDisplayClassCtor != null &&
            _closures.EntryPointDisplayClassStaticField != null)
        {
            il.Emit(OpCodes.Newobj, _closures.EntryPointDisplayClassCtor);
            il.Emit(OpCodes.Stsfld, _closures.EntryPointDisplayClassStaticField);
        }

        il.Emit(OpCodes.Call, _runtime.InitializeModuleRegistry);
        foreach (ParsedModule module in modules)
        {
            if (module.IsScript)
                continue;
            if (_moduleGetNamespaceMethods.TryGetValue(module.Path, out MethodBuilder? getNamespaceMethod))
            {
                string relativePath = GetRelativeModulePath(module, modules[^1]);
                EmitRegisterModule(il, relativePath, getNamespaceMethod);
                EmitRegisterModule(il, RemoveModuleExtension(relativePath), getNamespaceMethod);
                EmitRegisterModule(il, module.Path, getNamespaceMethod);
                EmitRegisterModule(il, RemoveModuleExtension(module.Path), getNamespaceMethod);
                if (!string.IsNullOrEmpty(module.ModuleName))
                    EmitRegisterModule(il, module.ModuleName, getNamespaceMethod);
            }
        }
        InitializeNamespaceFields(il);

        ParsedModule? entryModule = modules.Count > 0 ? modules[^1] : null;
        int wrapperIndex = 0;
        var moduleSteps = new Dictionary<string, MethodBuilder>(StringComparer.OrdinalIgnoreCase);
        foreach (ParsedModule module in modules)
        {
            if (_hostedModuleRunnerKeys.TryGetValue(module.Path, out string? runnerKey))
            {
                MethodBuilder asyncStep = EmitHostedAsyncModuleStep(
                    _functions.Builders[runnerKey],
                    module.Path,
                    _moduleInitializedFields.GetValueOrDefault(module.Path),
                    wrapperIndex++);
                Stmt[] importPrelude = module.Statements
                    .Where(IsHostedModulePreludeStatement)
                    .ToArray();
                Stmt[] exportPostlude = module.Statements
                    .Where(IsHostedModulePostludeStatement)
                    .ToArray();
                var sequence = new List<MethodBuilder>();
                if (importPrelude.Length > 0)
                {
                    sequence.Add(EmitHostedSynchronousStatementsStep(
                        module, importPrelude, wrapperIndex++));
                }
                sequence.Add(asyncStep);
                if (exportPostlude.Length > 0)
                {
                    sequence.Add(EmitHostedSynchronousStatementsStep(
                        module, exportPostlude, wrapperIndex++));
                }
                moduleSteps[module.Path] = sequence.Count == 1
                    ? asyncStep
                    : EmitHostedStepSequence(sequence, wrapperIndex++);
                continue;
            }
            if ((module.IsScript || module.IsCommonJs) &&
                TopLevelAwaitDetector.Contains(module.Statements))
            {
                string kind = module.IsCommonJs ? "CommonJS module" : "script";
                throw new InvalidOperationException(
                    $"Hosted compiled top-level await is not supported in {kind} '{module.Path}'.");
            }
            if (_hostedModuleSteps.TryGetValue(module.Path, out List<MethodBuilder>? legacySteps))
            {
                if (legacySteps.Count == 1)
                    moduleSteps[module.Path] = legacySteps[0];
                continue;
            }
            if (_modules.InitMethods.TryGetValue(module.Path, out MethodBuilder? initMethod))
                moduleSteps[module.Path] = EmitHostedSynchronousModuleStep(initMethod, wrapperIndex++);
        }

        var compositeInitializers = new Dictionary<string, MethodBuilder>(StringComparer.OrdinalIgnoreCase);
        var importFactories = new Dictionary<string, MethodBuilder>(StringComparer.OrdinalIgnoreCase);
        foreach (ParsedModule module in modules)
        {
            if (!moduleSteps.ContainsKey(module.Path))
                continue;
            MethodBuilder composite = EmitHostedModuleCompositeInitializer(
                module, modules, moduleSteps, wrapperIndex++);
            compositeInitializers[module.Path] = composite;

            if (!module.IsScript &&
                _moduleGetNamespaceMethods.TryGetValue(module.Path, out MethodBuilder? getNamespace))
            {
                MethodBuilder factory = EmitHostedModuleImportFactory(
                    module, getNamespace, wrapperIndex++);
                importFactories[module.Path] = factory;
                string relativePath = GetRelativeModulePath(module, modules[^1]);
                EmitRegisterHostedModule(
                    il, relativePath, module.Path, composite);
                EmitRegisterHostedModule(
                    il, RemoveModuleExtension(relativePath), module.Path, composite);
                EmitRegisterHostedModule(il, module.Path, module.Path, composite);
                EmitRegisterHostedModule(
                    il, RemoveModuleExtension(module.Path), module.Path, composite);
                if (!string.IsNullOrEmpty(module.ModuleName))
                    EmitRegisterHostedModule(il, module.ModuleName, module.Path, composite);
            }
        }

        var steps = new List<MethodBuilder>();
        foreach (ParsedModule module in modules)
        {
            if (module.IsDynamicImportOnly ||
                (module.IsCommonJs && module != entryModule))
                continue;
            if (importFactories.TryGetValue(module.Path, out MethodBuilder? factory))
                steps.Add(factory);
            else if (compositeInitializers.TryGetValue(module.Path, out MethodBuilder? composite))
                steps.Add(composite);
        }
        if (entryModule is not null &&
            FindMainFunction(entryModule.Statements) is { } main)
        {
            steps.Add(EmitHostedMainStep(main));
        }

        Type funcTask = typeof(Func<Task>);
        var stepArray = il.DeclareLocal(_types.MakeArrayType(funcTask));
        il.Emit(OpCodes.Ldc_I4, steps.Count);
        il.Emit(OpCodes.Newarr, funcTask);
        il.Emit(OpCodes.Stloc, stepArray);
        ConstructorInfo delegateCtor = funcTask.GetConstructor([typeof(object), typeof(IntPtr)])!;
        for (int index = 0; index < steps.Count; index++)
        {
            il.Emit(OpCodes.Ldloc, stepArray);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldftn, steps[index]);
            il.Emit(OpCodes.Newobj, delegateCtor);
            il.Emit(OpCodes.Stelem_Ref);
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, stepArray);
        il.Emit(OpCodes.Callvirt, typeof(SharpTSHostedRuntimeBase).GetMethod(
            nameof(SharpTSHostedRuntimeBase.RunInitializationSteps))!);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitHostedAsyncModuleStep(
        MethodBuilder runner,
        string modulePath,
        FieldBuilder? initializedField,
        int index)
    {
        var method = _programType.DefineMethod(
            $"$HostedAsyncModuleStep_{index}",
            MethodAttributes.Assembly | MethodAttributes.Static,
            typeof(Task),
            Type.EmptyTypes);
        ILGenerator il = method.GetILGenerator();
        Label run = il.DefineLabel();
        if (initializedField is not null)
        {
            il.Emit(OpCodes.Ldsfld, initializedField);
            il.Emit(OpCodes.Brfalse, run);
            il.Emit(OpCodes.Call, typeof(Task).GetProperty(nameof(Task.CompletedTask))!.GetMethod!);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(run);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stsfld, initializedField);
        }
        il.Emit(OpCodes.Call, runner);
        il.Emit(OpCodes.Ldstr, modulePath);
        il.Emit(OpCodes.Call, typeof(SharpTSHostedRuntimeBase).GetMethod(
            nameof(SharpTSHostedRuntimeBase.AttributeModuleInitialization))!);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitHostedModuleCompositeInitializer(
        ParsedModule module,
        IReadOnlyList<ParsedModule> modules,
        IReadOnlyDictionary<string, MethodBuilder> moduleSteps,
        int index)
    {
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(ParsedModule current)
        {
            if (!reachable.Add(current.Path)) return;
            foreach (ParsedModule dependency in current.Dependencies)
                Visit(dependency);
            foreach (ParsedModule dependency in current.RuntimeDependencies)
                Visit(dependency);
        }
        Visit(module);
        MethodBuilder[] steps = modules
            .Where(candidate => reachable.Contains(candidate.Path))
            .Select(candidate => moduleSteps.GetValueOrDefault(candidate.Path))
            .Where(step => step is not null)
            .Cast<MethodBuilder>()
            .ToArray();

        var method = _programType.DefineMethod(
            $"$HostedModuleInitialize_{index}",
            MethodAttributes.Assembly | MethodAttributes.Static,
            typeof(Task),
            Type.EmptyTypes);
        ILGenerator il = method.GetILGenerator();
        EmitHostedStepArray(il, steps);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitHostedModuleImportFactory(
        ParsedModule module,
        MethodBuilder getNamespace,
        int index)
    {
        var method = _programType.DefineMethod(
            $"$HostedModuleImport_{index}",
            MethodAttributes.Assembly | MethodAttributes.Static,
            typeof(Task<object?>),
            Type.EmptyTypes);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Call, _runtime.EventLoopGetHostedRuntime);
        il.Emit(OpCodes.Ldstr, module.Path);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldftn, getNamespace);
        il.Emit(OpCodes.Newobj, typeof(Func<object?>).GetConstructor(
            [typeof(object), typeof(IntPtr)])!);
        il.Emit(OpCodes.Callvirt, typeof(SharpTSHostedRuntimeBase).GetMethod(
            nameof(SharpTSHostedRuntimeBase.ImportHostedModule))!);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static void EmitRegisterHostedModule(
        ILGenerator il,
        string alias,
        string canonicalPath,
        MethodBuilder initializer)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, alias);
        il.Emit(OpCodes.Ldstr, canonicalPath);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldftn, initializer);
        il.Emit(OpCodes.Newobj, typeof(Func<Task>).GetConstructor(
            [typeof(object), typeof(IntPtr)])!);
        il.Emit(OpCodes.Callvirt, typeof(SharpTSHostedRuntimeBase).GetMethod(
            nameof(SharpTSHostedRuntimeBase.RegisterHostedModule))!);
    }

    private void EmitHostedStepArray(ILGenerator il, IReadOnlyList<MethodBuilder> steps)
    {
        Type funcTask = typeof(Func<Task>);
        LocalBuilder stepArray = il.DeclareLocal(_types.MakeArrayType(funcTask));
        il.Emit(OpCodes.Ldc_I4, steps.Count);
        il.Emit(OpCodes.Newarr, funcTask);
        il.Emit(OpCodes.Stloc, stepArray);
        ConstructorInfo delegateCtor = funcTask.GetConstructor([typeof(object), typeof(IntPtr)])!;
        for (int stepIndex = 0; stepIndex < steps.Count; stepIndex++)
        {
            il.Emit(OpCodes.Ldloc, stepArray);
            il.Emit(OpCodes.Ldc_I4, stepIndex);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldftn, steps[stepIndex]);
            il.Emit(OpCodes.Newobj, delegateCtor);
            il.Emit(OpCodes.Stelem_Ref);
        }
        il.Emit(OpCodes.Call, _runtime.EventLoopGetHostedRuntime);
        il.Emit(OpCodes.Ldloc, stepArray);
        il.Emit(OpCodes.Callvirt, typeof(SharpTSHostedRuntimeBase).GetMethod(
            nameof(SharpTSHostedRuntimeBase.RunInitializationSteps))!);
    }

    /// <summary>
    /// Defines an internal async function for a module containing top-level await.
    /// Using the regular async state-machine compiler gives hosted modules the same
    /// suspension support as async functions, including compound expressions,
    /// control flow, loops, and try/catch/finally.
    /// </summary>
    private void DefineHostedModuleRunner(ParsedModule module)
    {
        string name = "$HostedModuleRunner_" +
            CompilationContext.SanitizeModuleName(module.ModuleName);
        var nameToken = new Token(TokenType.IDENTIFIER, name, null, 0);
        string? defaultBinding = RegisterHostedExportBinding(
            module, "$default", "$HostedDefault_");
        string? exportAssignmentBinding = RegisterHostedExportBinding(
            module, "$exportAssignment", "$HostedExportAssignment_");
        var body = new List<Stmt>();
        foreach (Stmt statement in module.Statements)
        {
            if (LowerHostedTopLevelStatement(
                    statement, defaultBinding, exportAssignmentBinding) is { } lowered)
                body.Add(lowered);
        }

        var runner = new Stmt.Function(
            nameToken,
            TypeParams: null,
            ThisType: null,
            Parameters: [],
            Body: body,
            ReturnType: null,
            IsAsync: true);
        DefineFunction(runner);
        string key = GetDefinitionContext().GetQualifiedFunctionName(name);
        _hostedModuleRunnerKeys[module.Path] = key;
    }

    private string? RegisterHostedExportBinding(
        ParsedModule module,
        string exportName,
        string prefix)
    {
        if (!_modules.ExportFields.TryGetValue(module.Path, out var exports) ||
            !exports.TryGetValue(exportName, out FieldBuilder? field))
        {
            return null;
        }

        string binding = prefix + CompilationContext.SanitizeModuleName(module.ModuleName);
        if (!_moduleTopLevelStaticVars.TryGetValue(module.Path, out var variables))
        {
            variables = new Dictionary<string, FieldBuilder>(StringComparer.Ordinal);
            _moduleTopLevelStaticVars[module.Path] = variables;
        }
        variables[binding] = field;
        return binding;
    }

    private static Stmt? LowerHostedTopLevelStatement(
        Stmt statement,
        string? defaultBinding,
        string? exportAssignmentBinding) => statement switch
    {
        // Declarations are defined in the compiler's normal declaration phases.
        Stmt.Class or Stmt.Function or Stmt.Interface or Stmt.TypeAlias or Stmt.Enum or
            Stmt.Namespace or Stmt.DeclareModule => null,

        // Module imports are emitted in a synchronous prelude using the normal
        // module emitter. AsyncMoveNextEmitter intentionally handles executable
        // statements only and cannot establish module bindings itself.
        Stmt.Import or Stmt.ImportAlias or Stmt.ImportRequire => null,

        // Module-level bindings already have generated static/export fields. Assign
        // those fields from the async runner instead of introducing function locals,
        // preserving visibility to exported dependents and top-level functions.
        Stmt.Const declaration => new Stmt.Expression(
            new Expr.Assign(declaration.Name, declaration.Initializer)),
        Stmt.Var declaration when !declaration.IsDeclare => new Stmt.Expression(
            new Expr.Assign(
                declaration.Name,
                declaration.Initializer ??
                    new Expr.Literal(SharpTS.Runtime.Types.SharpTSUndefined.Instance),
                IsVarRedeclaration: declaration.IsVar)),
        Stmt.Var => null,
        Stmt.Sequence sequence => new Stmt.Sequence(
            sequence.Statements
                .Select(item => LowerHostedTopLevelStatement(
                    item, defaultBinding, exportAssignmentBinding))
                .Where(lowered => lowered is not null)
                .Cast<Stmt>()
                .ToList()),
        Stmt.Export { Declaration: Stmt.Const declaration } => new Stmt.Expression(
            new Expr.Assign(declaration.Name, declaration.Initializer)),
        Stmt.Export { Declaration: Stmt.Var declaration } when !declaration.IsDeclare =>
            new Stmt.Expression(new Expr.Assign(
                declaration.Name,
                declaration.Initializer ??
                    new Expr.Literal(SharpTS.Runtime.Types.SharpTSUndefined.Instance),
                IsVarRedeclaration: declaration.IsVar)),
        Stmt.Export { Declaration: Stmt.Var } => null,
        Stmt.Export { Declaration: Stmt.Sequence sequence } => new Stmt.Sequence(
            sequence.Statements
                .Select(item => LowerHostedTopLevelStatement(
                    item, defaultBinding, exportAssignmentBinding))
                .Where(lowered => lowered is not null)
                .Cast<Stmt>()
                .ToList()),
        Stmt.Export { DefaultExpr: not null } export when defaultBinding is not null =>
            new Stmt.Expression(new Expr.Assign(
                new Token(TokenType.IDENTIFIER, defaultBinding, null, 0),
                export.DefaultExpr)),
        Stmt.Export { ExportAssignment: not null } export when exportAssignmentBinding is not null =>
            new Stmt.Expression(new Expr.Assign(
                new Token(TokenType.IDENTIFIER, exportAssignmentBinding, null, 0),
                export.ExportAssignment)),
        Stmt.Export
        {
            Declaration: null,
            NamedExports: null or [],
            DefaultExpr: null,
            ExportAssignment: null,
            NamespaceExportName: null,
            GlobalNamespaceName: null,
        } => null,

        // Function/class declarations and re-export wiring contain no executable
        // await once their declarations have been defined. Emit them after the
        // runner with the normal module emitter so export fields are populated.
        Stmt.Export export when IsHostedModulePostludeStatement(export) => null,

        // Exported declarations still need their generated export fields populated;
        // the declarations themselves were already defined above.
        _ => statement,
    };

    private static bool IsHostedModulePreludeStatement(Stmt statement) =>
        statement is Stmt.Import or Stmt.ImportAlias or Stmt.ImportRequire;

    private static bool IsHostedModulePostludeStatement(Stmt statement)
    {
        if (statement is not Stmt.Export export || export.IsTypeOnly)
            return false;
        if (export.Declaration is Stmt.Const or Stmt.Var or Stmt.Sequence)
            return false;
        if (export.DefaultExpr is not null || export.ExportAssignment is not null)
            return false;
        return true;
    }

    private MethodBuilder EmitHostedSynchronousStatementsStep(
        ParsedModule module,
        IReadOnlyList<Stmt> statements,
        int index)
    {
        var method = _programType.DefineMethod(
            $"$HostedModulePrelude_{index}",
            MethodAttributes.Assembly | MethodAttributes.Static,
            typeof(Task),
            Type.EmptyTypes);
        ILGenerator il = method.GetILGenerator();
        CompilationContext context = CreateHostedModuleStepContext(module, il, method);
        var emitter = new ILEmitter(context);
        foreach (Stmt statement in statements)
            emitter.EmitStatement(statement);
        il.Emit(OpCodes.Call, typeof(Task).GetProperty(nameof(Task.CompletedTask))!.GetMethod!);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitHostedStepSequence(
        IReadOnlyList<MethodBuilder> steps,
        int index)
    {
        var method = _programType.DefineMethod(
            $"$HostedModuleSequence_{index}",
            MethodAttributes.Assembly | MethodAttributes.Static,
            typeof(Task),
            Type.EmptyTypes);
        ILGenerator il = method.GetILGenerator();
        EmitHostedStepArray(il, steps);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitHostedSynchronousModuleStep(MethodBuilder initialize, int index)
    {
        var method = _programType.DefineMethod(
            $"$HostedModuleStep_{index}",
            MethodAttributes.Assembly | MethodAttributes.Static,
            typeof(Task),
            Type.EmptyTypes);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Call, initialize);
        il.Emit(OpCodes.Call, typeof(Task).GetProperty(nameof(Task.CompletedTask))!.GetMethod!);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitHostedMainStep(
        (Stmt.Function Func, bool IsAsync, bool ReturnsExitCode) main)
    {
        string key = _closures.FunctionAstNodes.First(pair =>
            ReferenceEquals(pair.Value, main.Func)).Key;
        MethodBuilder target = _functions.Builders[key];
        var method = _programType.DefineMethod(
            "$HostedMainStep",
            MethodAttributes.Assembly | MethodAttributes.Static,
            typeof(Task),
            Type.EmptyTypes);
        var il = method.GetILGenerator();
        if (main.Func.Parameters.Count == 1)
        {
            il.Emit(OpCodes.Call, typeof(Environment).GetMethod(
                nameof(Environment.GetCommandLineArgs), Type.EmptyTypes)!);
        }
        il.Emit(OpCodes.Call, target);

        if (main.IsAsync)
        {
            var task = il.DeclareLocal(_types.TaskOfObject);
            il.Emit(OpCodes.Castclass, _types.TaskOfObject);
            il.Emit(OpCodes.Stloc, task);
            il.Emit(OpCodes.Call, _runtime.EventLoopGetHostedRuntime);
            il.Emit(OpCodes.Ldloc, task);
            il.Emit(main.ReturnsExitCode ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, typeof(SharpTSHostedRuntimeBase).GetMethod(
                nameof(SharpTSHostedRuntimeBase.ObserveProgramMain))!);
            il.Emit(OpCodes.Ret);
            return method;
        }

        if (main.ReturnsExitCode)
        {
            il.Emit(OpCodes.Conv_I4);
            var exitCode = il.DeclareLocal(_types.Int32);
            il.Emit(OpCodes.Stloc, exitCode);
            il.Emit(OpCodes.Call, _runtime.EventLoopGetHostedRuntime);
            il.Emit(OpCodes.Ldloc, exitCode);
            il.Emit(OpCodes.Callvirt, typeof(SharpTSHostedRuntimeBase).GetMethod(
                nameof(SharpTSHostedRuntimeBase.CompleteProgram))!);
        }
        else
        {
            il.Emit(OpCodes.Pop);
        }
        il.Emit(OpCodes.Call, typeof(Task).GetProperty(nameof(Task.CompletedTask))!.GetMethod!);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private void EmitHostedModuleSteps(ParsedModule module, TypeBuilder moduleType)
    {
        var steps = new List<MethodBuilder>();
        int index = 0;
        foreach (Stmt statement in module.Statements)
        {
            if (statement is Stmt.Class or Stmt.Function or Stmt.Interface or Stmt.TypeAlias or Stmt.Enum)
                continue;

            bool containsAwait = TopLevelAwaitDetector.Contains([statement]);
            if (TrySplitHostedAwait(statement, out Expr? awaited, out Stmt? continuation))
            {
                string captureName = $"$hostedAwaitResult_{index}";
                FieldBuilder captureField = moduleType.DefineField(
                    captureName,
                    _types.Object,
                    FieldAttributes.Private | FieldAttributes.Static);
                MethodBuilder captureMethod = EmitHostedAwaitCaptureMethod(
                    moduleType, index, captureField);
                steps.Add(EmitHostedAwaitStep(
                    module, moduleType, index++, awaited!, captureMethod));

                if (continuation is not null)
                {
                    steps.Add(EmitHostedStatementStep(
                        module, moduleType, index++, continuation, captureName, captureField));
                }
                continue;
            }
            if (containsAwait)
            {
                throw new InvalidOperationException(
                    $"Hosted compiled top-level await in module '{module.Path}' is not yet " +
                    "supported inside a compound expression or control-flow statement.");
            }

            steps.Add(EmitHostedStatementStep(
                module, moduleType, index++, statement, null, null));
        }
        _hostedModuleSteps[module.Path] = steps;
    }

    private static bool TrySplitHostedAwait(
        Stmt statement,
        out Expr? awaited,
        out Stmt? continuation)
    {
        awaited = null;
        continuation = null;
        var placeholder = new Expr.Variable(new Token(
            TokenType.IDENTIFIER, "$hostedAwaitResult", null, 0));

        switch (statement)
        {
            case Stmt.Expression { Expr: Expr.Await expression }:
                awaited = expression.Expression;
                return true;
            case Stmt.Const { Initializer: Expr.Await expression } declaration:
                awaited = expression.Expression;
                continuation = declaration with { Initializer = placeholder };
                return true;
            case Stmt.Var { Initializer: Expr.Await expression } declaration:
                awaited = expression.Expression;
                continuation = declaration with { Initializer = placeholder };
                return true;
            case Stmt.Export { Declaration: Stmt.Const { Initializer: Expr.Await expression } declaration } export:
                awaited = expression.Expression;
                continuation = export with
                {
                    Declaration = declaration with { Initializer = placeholder }
                };
                return true;
            case Stmt.Export { Declaration: Stmt.Var { Initializer: Expr.Await expression } declaration } export:
                awaited = expression.Expression;
                continuation = export with
                {
                    Declaration = declaration with { Initializer = placeholder }
                };
                return true;
            case Stmt.Export { DefaultExpr: Expr.Await expression } export:
                awaited = expression.Expression;
                continuation = export with { DefaultExpr = placeholder };
                return true;
            default:
                return false;
        }
    }

    private MethodBuilder EmitHostedAwaitCaptureMethod(
        TypeBuilder moduleType,
        int index,
        FieldBuilder captureField)
    {
        MethodBuilder method = moduleType.DefineMethod(
            $"$CaptureHostedAwait_{index}",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(void),
            [_types.Object]);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stsfld, captureField);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitHostedAwaitStep(
        ParsedModule module,
        TypeBuilder moduleType,
        int index,
        Expr awaited,
        MethodBuilder captureMethod)
    {
        MethodBuilder step = moduleType.DefineMethod(
            $"$HostedStep_{index}",
            MethodAttributes.Assembly | MethodAttributes.Static,
            typeof(Task),
            Type.EmptyTypes);
        ILGenerator il = step.GetILGenerator();
        CompilationContext ctx = CreateHostedModuleStepContext(module, il, step);
        var emitter = new ILEmitter(ctx);
        emitter.EmitExpression(awaited);
        emitter.Helpers.EnsureBoxed();
        EmitHostedAwaitableTask(il, prepare: false);
        LocalBuilder task = il.DeclareLocal(_types.TaskOfObject);
        il.Emit(OpCodes.Stloc, task);
        il.Emit(OpCodes.Call, _runtime.EventLoopGetHostedRuntime);
        il.Emit(OpCodes.Ldloc, task);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldftn, captureMethod);
        il.Emit(OpCodes.Newobj, typeof(Action<object?>).GetConstructor(
            [typeof(object), typeof(IntPtr)])!);
        il.Emit(OpCodes.Callvirt, typeof(SharpTSHostedRuntimeBase).GetMethod(
            nameof(SharpTSHostedRuntimeBase.CaptureAwait))!);
        il.Emit(OpCodes.Ret);
        return step;
    }

    private MethodBuilder EmitHostedStatementStep(
        ParsedModule module,
        TypeBuilder moduleType,
        int index,
        Stmt statement,
        string? captureName,
        FieldBuilder? captureField)
    {
        MethodBuilder step = moduleType.DefineMethod(
            $"$HostedStep_{index}",
            MethodAttributes.Assembly | MethodAttributes.Static,
            typeof(Task),
            Type.EmptyTypes);
        ILGenerator il = step.GetILGenerator();
        CompilationContext ctx = CreateHostedModuleStepContext(module, il, step);
        if (captureName is not null && captureField is not null)
        {
            ctx.TopLevelStaticVars ??= [];
            ctx.TopLevelStaticVars["$hostedAwaitResult"] = captureField;
        }
        var emitter = new ILEmitter(ctx);
        if (statement is Stmt.Expression expressionStatement)
            EmitHostedExpression(il, emitter, expressionStatement);
        else
            emitter.EmitStatement(statement);
        il.Emit(OpCodes.Call, typeof(Task).GetProperty(nameof(Task.CompletedTask))!.GetMethod!);
        il.Emit(OpCodes.Ret);
        return step;
    }

    private CompilationContext CreateHostedModuleStepContext(
        ParsedModule module,
        ILGenerator il,
        MethodBuilder method)
    {
        string? savedPath = _modules.CurrentPath;
        _modules.CurrentPath = module.Path;
        CompilationContext ctx = CreateModuleTopLevelContext(il, method);
        _modules.CurrentPath = savedPath;
        ctx.CurrentModulePath = module.Path;
        ctx.ModuleExportFields = _modules.ExportFields;
        ctx.ModuleTypes = _modules.Types;
        ctx.ModuleInitMethods = _modules.InitMethods;
        ctx.ModuleImportFields = _modules.ImportFields;
        ctx.ModuleResolver = _modules.Resolver;
        ctx.CommonJsExportFields = _modules.CommonJsExportFields;
        ctx.CommonJsGetExportsMethods = _modules.CommonJsGetExportsMethods;
        return ctx;
    }

    private void EmitHostedAwaitableTask(ILGenerator il, bool prepare = true)
    {
        var value = il.DeclareLocal(_types.Object);
        var task = il.DeclareLocal(_types.TaskOfObject);
        var isPromise = il.DefineLabel();
        var isTask = il.DefineLabel();
        var haveTask = il.DefineLabel();
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Isinst, _runtime.TSPromiseType);
        il.Emit(OpCodes.Brtrue, isPromise);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Brtrue, isTask);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Call, _runtime.CoerceAwaitableToTaskMethod);
        il.Emit(OpCodes.Stloc, task);
        il.Emit(OpCodes.Br, haveTask);
        il.MarkLabel(isPromise);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Castclass, _runtime.TSPromiseType);
        il.Emit(OpCodes.Callvirt, _runtime.TSPromiseTaskGetter);
        il.Emit(OpCodes.Stloc, task);
        il.Emit(OpCodes.Br, haveTask);
        il.MarkLabel(isTask);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Castclass, _types.TaskOfObject);
        il.Emit(OpCodes.Stloc, task);
        il.MarkLabel(haveTask);
        il.Emit(OpCodes.Ldloc, task);
        if (prepare)
            il.Emit(OpCodes.Call, _runtime.EventLoopPrepareHostedAwait);
    }

    /// <summary>
    /// Emits synchronous module registration and initialization. The caller
    /// decides whether this body belongs to the console <c>Main</c> or the
    /// experimental host-owned initializer.
    /// </summary>
    private void EmitModulesInitialization(ILGenerator il, List<ParsedModule> modules)
    {

        // Create entry-point display class instance if there are captured top-level variables
        if (_closures.EntryPointDisplayClass != null &&
            _closures.EntryPointDisplayClassCtor != null &&
            _closures.EntryPointDisplayClassStaticField != null)
        {
            il.Emit(OpCodes.Newobj, _closures.EntryPointDisplayClassCtor);
            il.Emit(OpCodes.Stsfld, _closures.EntryPointDisplayClassStaticField);
        }

        // Initialize module registry
        il.Emit(OpCodes.Call, _runtime.InitializeModuleRegistry);

        // Register each module in the registry for dynamic import support
        // Skip script files - they don't have exports and can't be dynamically imported
        foreach (var module in modules)
        {
            if (module.IsScript)
            {
                continue;  // Scripts don't have GetNamespace methods
            }

            if (_moduleGetNamespaceMethods.TryGetValue(module.Path, out var getNamespaceMethod))
            {
                // Register under relative path (e.g., "./utils.ts")
                string relativePath = GetRelativeModulePath(module, modules[^1]);
                EmitRegisterModule(il, relativePath, getNamespaceMethod);
                EmitRegisterModule(il, RemoveModuleExtension(relativePath), getNamespaceMethod);

                // Also register under absolute path for direct matches
                EmitRegisterModule(il, module.Path, getNamespaceMethod);
                EmitRegisterModule(il, RemoveModuleExtension(module.Path), getNamespaceMethod);

                // Register under module name without extension (e.g., "utils")
                string moduleName = module.ModuleName;
                if (!string.IsNullOrEmpty(moduleName))
                {
                    EmitRegisterModule(il, moduleName, getNamespaceMethod);
                }
            }
        }

        // Initialize namespace static fields before any module/script $Initialize body runs.
        // Namespace objects live in flat static fields ($ns_*) on $Program shared across every
        // module, but a module's $Initialize only POPULATES them (EmitNamespace does
        // $ns_X.Set(...)); it never creates them. The single-file entry point calls this at the
        // top of Main; the multi-module entry point must too, or the first Set() dereferences a
        // null namespace field (#1245).
        InitializeNamespaceFields(il);

        // Call each module/script's $Initialize method in dependency order.
        // CommonJS modules are initialized lazily — only the entry CJS module is run eagerly,
        // and require() triggers the rest. This matches Node semantics for the visible execution
        // order of circular-require scenarios.
        ParsedModule? entryModule = modules.Count > 0 ? modules[^1] : null;
        foreach (var module in modules)
        {
            if (module.IsCommonJs && module != entryModule)
            {
                continue; // wait for require() to trigger
            }

            if (_modules.InitMethods.TryGetValue(module.Path, out var initMethod))
            {
                il.Emit(OpCodes.Call, initMethod);
            }
        }
    }

    /// <summary>
    /// Emits code to register a module with the registry.
    /// </summary>
    private void EmitRegisterModule(ILGenerator il, string path, MethodBuilder getNamespaceMethod)
    {
        // TSRuntime.RegisterModule(path, () => $Module_xxx.$GetNamespace())
        il.Emit(OpCodes.Ldstr, path);
        il.Emit(OpCodes.Ldnull); // target for static method delegate
        il.Emit(OpCodes.Ldftn, getNamespaceMethod);
        il.Emit(OpCodes.Newobj, typeof(Func<object?>).GetConstructor([typeof(object), typeof(IntPtr)])!);
        il.Emit(OpCodes.Call, _runtime.RegisterModule);
    }

    /// <summary>
    /// Gets the relative path from entry module to target module.
    /// </summary>
    private static string GetRelativeModulePath(ParsedModule targetModule, ParsedModule entryModule)
    {
        // Get directory of entry module
        string entryDir = Path.GetDirectoryName(entryModule.Path) ?? "";
        string targetPath = targetModule.Path;

        // Try to make it relative
        if (targetPath.StartsWith(entryDir, StringComparison.OrdinalIgnoreCase))
        {
            string relative = targetPath[entryDir.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // Normalize to forward slashes and add ./ prefix
            relative = "./" + relative.Replace(Path.DirectorySeparatorChar, '/');
            return relative;
        }

        // Fall back to filename
        return "./" + Path.GetFileName(targetPath);
    }

    private static string RemoveModuleExtension(string path)
    {
        string extension = Path.GetExtension(path);
        return extension is ".ts" or ".tsx" or ".js" or ".jsx" or ".mts" or ".cts"
            ? path[..^extension.Length]
            : path;
    }

    /// <summary>
    /// Gets the qualified class name for a class in a specific module.
    /// Used during early module definition before ClassToModule is populated.
    /// </summary>
    private string GetQualifiedClassName(string simpleClassName, string modulePath)
    {
        string sanitizedModule = CompilationContext.SanitizeModuleName(Path.GetFileNameWithoutExtension(modulePath));
        string baseName = $"$M_{sanitizedModule}_{simpleClassName}";

        // Apply .NET namespace if set
        string? dotNetNamespace = _modules.Namespaces.GetValueOrDefault(modulePath);
        if (!string.IsNullOrEmpty(dotNetNamespace))
        {
            return $"{dotNetNamespace}.{baseName}";
        }

        return baseName;
    }

    /// <summary>
    /// Tracks which exports are classes to enable direct constructor calls in importing modules.
    /// Populates ExportedClasses and DefaultExportClasses dictionaries.
    /// </summary>
    private void TrackClassExports(ParsedModule module)
    {
        // Build a set of class names defined in this module
        var classNames = new HashSet<string>();
        foreach (var stmt in module.Statements)
        {
            if (stmt is Stmt.Class classStmt)
            {
                classNames.Add(classStmt.Name.Lexeme);
            }
        }

        // Initialize the export tracking for this module
        var exportedClasses = new Dictionary<string, string>();
        _modules.ExportedClasses[module.Path] = exportedClasses;

        // Scan exports to track class exports
        foreach (var stmt in module.Statements)
        {
            if (stmt is not Stmt.Export export)
                continue;
            if (export.IsTypeOnly)
                continue;

            // Default export of a class declaration
            if (export.IsDefaultExport && export.Declaration is Stmt.Class defaultClass)
            {
                string qualifiedClassName = GetQualifiedClassName(defaultClass.Name.Lexeme, module.Path);
                _modules.DefaultExportClasses[module.Path] = qualifiedClassName;
                // Also add to exportedClasses under $default for consistency
                exportedClasses["$default"] = qualifiedClassName;
            }
            // Named export of a class declaration (export class Foo { ... })
            else if (!export.IsDefaultExport && export.Declaration is Stmt.Class namedClass)
            {
                string className = namedClass.Name.Lexeme;
                string qualifiedClassName = GetQualifiedClassName(className, module.Path);
                exportedClasses[className] = qualifiedClassName;
            }
            // Named exports from list (export { Foo, Bar as Baz })
            else if (export.NamedExports != null && export.FromModulePath == null)
            {
                foreach (var spec in export.NamedExports)
                {
                    if (spec.IsTypeOnly)
                        continue;
                    string localName = spec.LocalName.Lexeme;
                    string exportedName = spec.ExportedName?.Lexeme ?? localName;

                    // Check if this is exporting a class defined in this module
                    if (classNames.Contains(localName))
                    {
                        string qualifiedClassName = GetQualifiedClassName(localName, module.Path);
                        exportedClasses[exportedName] = qualifiedClassName;
                    }
                }
            }
            // Re-exports (export { Foo } from './other' or export * from './other')
            else if (export.FromModulePath != null && _modules.Resolver != null)
            {
                string sourcePath = _modules.Resolver.ResolveRuntimeModulePath(
                    export.FromModulePath, module.Path);

                // Get the source module's exported classes
                if (_modules.ExportedClasses.TryGetValue(sourcePath, out var sourceExportedClasses))
                {
                    if (export.NamedExports != null)
                    {
                        // Re-export specific names
                        foreach (var spec in export.NamedExports)
                        {
                            if (spec.IsTypeOnly)
                                continue;
                            string importedName = spec.LocalName.Lexeme;
                            string exportedName = spec.ExportedName?.Lexeme ?? importedName;

                            if (sourceExportedClasses.TryGetValue(importedName, out var qualifiedClassName))
                            {
                                exportedClasses[exportedName] = qualifiedClassName;
                            }
                        }
                    }
                    else
                    {
                        // Re-export all (export * from './module')
                        foreach (var (name, qualifiedClassName) in sourceExportedClasses)
                        {
                            if (name == "$default") continue; // * doesn't include default
                            if (!exportedClasses.ContainsKey(name))
                            {
                                exportedClasses[name] = qualifiedClassName;
                            }
                        }
                    }
                }
            }
        }
    }
}
