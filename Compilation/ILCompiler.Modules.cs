using System.Reflection;
using System.Reflection.Emit;
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
                    string sourcePath = _modules.Resolver.ResolveModulePath(export.FromModulePath, module.Path);

                    if (export.NamedExports != null)
                    {
                        // export { x, y as z } from './module'
                        foreach (var spec in export.NamedExports)
                        {
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
    /// Emits an expression statement with special handling to wait for async return values.
    /// If the expression returns a Task or Promise, waits for it to complete.
    /// This provides "top-level await" behavior for compiled code.
    /// </summary>
    private void EmitExpressionWithAsyncWait(ILGenerator il, ILEmitter emitter, Stmt.Expression exprStmt)
    {
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

        // Wait for the task
        il.MarkLabel(waitForTaskLabel);
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

        var moduleType = _modules.Types[module.Path];
        var exportFields = _modules.ExportFields[module.Path];

        // Create _initialized field for caching guard
        var initializedField = moduleType.DefineField(
            "_initialized",
            typeof(bool),
            FieldAttributes.Private | FieldAttributes.Static
        );

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

        var ctx = CreateCompilationContext(il);
        ctx.CurrentModulePath = module.Path;
        ctx.ModuleExportFields = _modules.ExportFields;
        ctx.ModuleTypes = _modules.Types;
        ctx.ModuleResolver = _modules.Resolver;

        var emitter = new ILEmitter(ctx);

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

        var il = initMethod.GetILGenerator();

        // Guard: if (_initialized) return;
        var skipLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, initializedField);
        il.Emit(OpCodes.Brtrue, skipLabel);

        // _initialized = true;
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stsfld, initializedField);

        var ctx = CreateCompilationContext(il);
        ctx.CurrentModulePath = script.Path;
        ctx.ModuleExportFields = _modules.ExportFields;
        ctx.ModuleTypes = _modules.Types;
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

                // Also register under absolute path for direct matches
                EmitRegisterModule(il, module.Path, getNamespaceMethod);

                // Register under module name without extension (e.g., "utils")
                string moduleName = module.ModuleName;
                if (!string.IsNullOrEmpty(moduleName))
                {
                    EmitRegisterModule(il, moduleName, getNamespaceMethod);
                }
            }
        }

        // Call each module/script's $Initialize method in dependency order
        foreach (var module in modules)
        {
            if (_modules.InitMethods.TryGetValue(module.Path, out var initMethod))
            {
                il.Emit(OpCodes.Call, initMethod);
            }
        }

        il.Emit(OpCodes.Ret);
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

    /// <summary>
    /// Creates a CompilationContext with common settings.
    /// </summary>
    private CompilationContext CreateCompilationContext(ILGenerator il)
    {
        return new CompilationContext(il, _typeMapper, _functions.Builders, _classes.Builders, _types)
        {
            ClosureAnalyzer = _closures.Analyzer,
            ArrowMethods = _closures.ArrowMethods,
            DisplayClasses = _closures.DisplayClasses,
            DisplayClassFields = _closures.DisplayClassFields,
            DisplayClassConstructors = _closures.DisplayClassConstructors,
            FunctionRestParams = _functions.RestParams,
            EnumMembers = _enums.Members,
            EnumReverse = _enums.Reverse,
            EnumKinds = _enums.Kinds,
            NamespaceFields = _namespaceFields,
            TopLevelStaticVars = _topLevelStaticVars,
            Runtime = _runtime,
            FunctionGenericParams = _functions.GenericParams,
            IsGenericFunction = _functions.IsGeneric,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            AsyncMethods = null,
            ClassToModule = _modules.ClassToModule,
            FunctionToModule = _modules.FunctionToModule,
            EnumToModule = _modules.EnumToModule,
            ExportAssignmentClasses = _modules.ExportAssignmentClasses,
            ExportedClasses = _modules.ExportedClasses,
            DefaultExportClasses = _modules.DefaultExportClasses,
            DotNetNamespace = _modules.CurrentDotNetNamespace,
            TypeEmitterRegistry = _typeEmitterRegistry,
            BuiltInModuleEmitterRegistry = _builtInModuleEmitterRegistry,
            BuiltInModuleNamespaces = _builtInModuleNamespaces,
            BuiltInModuleMethodBindings = _builtInModuleMethodBindings,
            ClassExprBuilders = _classExprs.Builders,
            IsStrictMode = _isStrictMode,
            // Registry services
            ClassRegistry = GetClassRegistry(),
            // Entry-point display class for captured top-level variables
            EntryPointDisplayClassFields = _closures.EntryPointDisplayClassFields.Count > 0 ? _closures.EntryPointDisplayClassFields : null,
            CapturedTopLevelVars = _closures.CapturedTopLevelVars.Count > 0 ? _closures.CapturedTopLevelVars : null,
            ArrowEntryPointDCFields = _closures.ArrowEntryPointDCFields.Count > 0 ? _closures.ArrowEntryPointDCFields : null,
            EntryPointDisplayClassStaticField = _closures.EntryPointDisplayClassStaticField
        };
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
                string sourcePath = _modules.Resolver.ResolveModulePath(export.FromModulePath, module.Path);

                // Get the source module's exported classes
                if (_modules.ExportedClasses.TryGetValue(sourcePath, out var sourceExportedClasses))
                {
                    if (export.NamedExports != null)
                    {
                        // Re-export specific names
                        foreach (var spec in export.NamedExports)
                        {
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
