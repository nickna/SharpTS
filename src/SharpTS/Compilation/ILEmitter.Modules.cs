using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns.Modules;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    #region Module Support

    /// <summary>
    /// Emits code for an import statement.
    /// Imports bind local variables to module export fields.
    /// Type-only imports are skipped (erased at compile time).
    /// </summary>
    private void EmitImport(Stmt.Import import)
    {
        // Skip type-only imports entirely - they have no runtime code
        if (import.IsTypeOnly)
            return;

        // dotnet: interop imports emit no binding code — use sites compile to direct
        // external-interop IL via the ExternalTypes registry (see RegisterDotNetImports).
        if (SharpTS.Modules.DotNetImports.IsDotNetSpecifier(import.ModulePath) ||
            SharpTS.Modules.DotNetExtensionImports.IsSpecifier(import.ModulePath))
            return;

        // Check for built-in module imports (fs, path, etc.) and stdlib-internal
        // primitive:* imports. Both route through the same emitter-registry dispatch.
        string? builtInModuleName = ResolveBuiltInOrPrimitiveKey(import.ModulePath);
        if (builtInModuleName == null && _ctx.ModuleResolver != null && _ctx.CurrentModulePath != null)
        {
            string resolvedPath = _ctx.ModuleResolver.ResolveRuntimeModulePath(
                import.ModulePath, _ctx.CurrentModulePath);
            builtInModuleName = ResolveBuiltInOrPrimitiveKey(resolvedPath);
        }

        if (builtInModuleName != null)
        {
            EmitBuiltInModuleImport(import, builtInModuleName);
            return;
        }

        if (_ctx.CurrentModulePath == null || _ctx.ModuleResolver == null ||
            _ctx.ModuleExportFields == null || _ctx.ModuleTypes == null)
        {
            // Not in module context - imports are no-ops for single-file compilation
            return;
        }

        string importedPath = _ctx.ModuleResolver.ResolveRuntimeModulePath(
            import.ModulePath, _ctx.CurrentModulePath);

        // CommonJS target: route through the CJS-specific import lowering. CJS modules don't
        // have per-export fields — they have a single $exports dictionary accessed via $GetExports.
        if (_ctx.CommonJsGetExportsMethods?.TryGetValue(importedPath, out var cjsGetExports) == true)
        {
            EmitImportFromCommonJs(import, importedPath, cjsGetExports);
            return;
        }

        if (!_ctx.ModuleExportFields.TryGetValue(importedPath, out var exportFields) ||
            !_ctx.ModuleTypes.TryGetValue(importedPath, out var moduleType))
        {
            // Module not found - skip (type checker should have caught this)
            return;
        }

        // Ensure the source module is initialized before reading its exports
        // This handles cases where modules import from each other
        if (_ctx.ModuleInitMethods?.TryGetValue(importedPath, out var sourceInitMethod) == true)
        {
            IL.Emit(OpCodes.Call, sourceInitMethod);
        }

        // Get import fields for current module (if any)
        Dictionary<string, FieldBuilder>? currentModuleImportFields = null;
        if (_ctx.CurrentModulePath != null)
        {
            _ctx.ModuleImportFields?.TryGetValue(_ctx.CurrentModulePath, out currentModuleImportFields);
        }

        // Default import: bind to static field or local variable
        if (import.DefaultImport != null)
        {
            string localName = import.DefaultImport.Lexeme;
            if (exportFields.TryGetValue("$default", out var defaultField))
            {
                IL.Emit(OpCodes.Ldsfld, defaultField);

                // Store in static field if available, otherwise use local
                if (currentModuleImportFields?.TryGetValue(localName, out var importField) == true)
                {
                    IL.Emit(OpCodes.Stsfld, importField);
                }
                else
                {
                    var local = _ctx.Locals.GetLocal(localName) ?? _ctx.Locals.DeclareLocal(localName, _ctx.Types.Object);
                    IL.Emit(OpCodes.Stloc, local);
                }

                // Track if this is a default class export for direct constructor calls
                if (_ctx.DefaultExportClasses?.TryGetValue(importedPath, out var qualifiedClassName) == true)
                {
                    _ctx.ImportedClassAliases ??= new Dictionary<string, string>();
                    _ctx.ImportedClassAliases[localName] = qualifiedClassName;
                }
            }
        }

        // Named imports: bind to static fields or local variables
        // Skip individual type-only specifiers
        if (import.NamedImports != null)
        {
            // Get the exported classes for this module (if any)
            Dictionary<string, string>? exportedClasses = null;
            _ctx.ExportedClasses?.TryGetValue(importedPath, out exportedClasses);

            foreach (var spec in import.NamedImports.Where(s => !s.IsTypeOnly))
            {
                string importedName = spec.Imported.Lexeme;
                string localName = spec.LocalName?.Lexeme ?? importedName;

                if (exportFields.TryGetValue(importedName, out var field))
                {
                    IL.Emit(OpCodes.Ldsfld, field);

                    // Store in static field if available, otherwise use local
                    if (currentModuleImportFields?.TryGetValue(localName, out var importField) == true)
                    {
                        IL.Emit(OpCodes.Stsfld, importField);
                    }
                    else
                    {
                        var local = _ctx.Locals.GetLocal(localName) ?? _ctx.Locals.DeclareLocal(localName, _ctx.Types.Object);
                        IL.Emit(OpCodes.Stloc, local);
                    }

                    // Track if this is a class export for direct constructor calls
                    if (exportedClasses?.TryGetValue(importedName, out var qualifiedClassName) == true)
                    {
                        _ctx.ImportedClassAliases ??= new Dictionary<string, string>();
                        _ctx.ImportedClassAliases[localName] = qualifiedClassName;
                    }
                }
            }
        }

        // Namespace import: create a SharpTSObject with all exports
        if (import.NamespaceImport != null)
        {
            string localName = import.NamespaceImport.Lexeme;

            // Track this namespace import for resolving namespace-qualified class construction
            _ctx.NamespaceImports ??= new Dictionary<string, string>();
            _ctx.NamespaceImports[localName] = importedPath;

            // Create new Dictionary<string, object?>
            var dictType = _ctx.Types.DictionaryStringObject;
            var dictCtor = _ctx.Types.GetDefaultConstructor(dictType);
            var addMethod = _ctx.Types.GetMethod(dictType, "Add", _ctx.Types.String, _ctx.Types.Object);

            IL.Emit(OpCodes.Newobj, dictCtor);

            // Add each export to the dictionary
            foreach (var (exportName, field) in exportFields)
            {
                if (exportName == "$default") continue; // Skip default export in namespace import

                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldstr, exportName);
                IL.Emit(OpCodes.Ldsfld, field);
                IL.Emit(OpCodes.Callvirt, addMethod);
            }

            // Call CreateObject to wrap the dictionary
            IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateObject);

            // Store in static field if available, otherwise use local
            if (currentModuleImportFields?.TryGetValue(localName, out var importField) == true)
            {
                IL.Emit(OpCodes.Stsfld, importField);
            }
            else
            {
                var local = _ctx.Locals.GetLocal(localName) ?? _ctx.Locals.DeclareLocal(localName, _ctx.Types.Object);
                IL.Emit(OpCodes.Stloc, local);
            }
        }
    }

    /// <summary>
    /// Emits code for a CommonJS-style require import: import x = require('path')
    /// </summary>
    private void EmitImportRequire(Stmt.ImportRequire importReq)
    {
        // Check for built-in module imports (fs, path, os, etc.)
        string? builtInModuleName = BuiltInModuleRegistry.GetModuleName(importReq.ModulePath);
        if (builtInModuleName == null && _ctx.ModuleResolver != null && _ctx.CurrentModulePath != null)
        {
            string resolvedPath = _ctx.ModuleResolver.ResolveRuntimeModulePath(
                importReq.ModulePath, _ctx.CurrentModulePath, ResolutionKind.Cjs);
            builtInModuleName = BuiltInModuleRegistry.GetModuleName(resolvedPath);
        }

        if (builtInModuleName != null)
        {
            // Built-in module - create namespace object
            EmitBuiltInModuleNamespace(builtInModuleName, importReq.AliasName.Lexeme);
            return;
        }

        if (_ctx.CurrentModulePath == null || _ctx.ModuleResolver == null ||
            _ctx.ModuleExportFields == null || _ctx.ModuleTypes == null)
        {
            // Not in module context - define as null
            var local = _ctx.Locals.GetLocal(importReq.AliasName.Lexeme) ?? _ctx.Locals.DeclareLocal(importReq.AliasName.Lexeme, _ctx.Types.Object);
            IL.Emit(OpCodes.Ldnull);
            IL.Emit(OpCodes.Stloc, local);
            return;
        }

        string importedPath = _ctx.ModuleResolver.ResolveRuntimeModulePath(
            importReq.ModulePath, _ctx.CurrentModulePath, ResolutionKind.Cjs);

        if (!_ctx.ModuleExportFields.TryGetValue(importedPath, out var exportFields) ||
            !_ctx.ModuleTypes.TryGetValue(importedPath, out var moduleType))
        {
            // Module not found - define as null
            var local = _ctx.Locals.GetLocal(importReq.AliasName.Lexeme) ?? _ctx.Locals.DeclareLocal(importReq.AliasName.Lexeme, _ctx.Types.Object);
            IL.Emit(OpCodes.Ldnull);
            IL.Emit(OpCodes.Stloc, local);
            return;
        }

        // Ensure the source module is initialized before reading its exports
        if (_ctx.ModuleInitMethods?.TryGetValue(importedPath, out var sourceInitMethod) == true)
        {
            IL.Emit(OpCodes.Call, sourceInitMethod);
        }

        // Get import fields for current module (if any)
        Dictionary<string, FieldBuilder>? currentModuleImportFields = null;
        if (_ctx.CurrentModulePath != null)
        {
            _ctx.ModuleImportFields?.TryGetValue(_ctx.CurrentModulePath, out currentModuleImportFields);
        }

        string aliasName = importReq.AliasName.Lexeme;
        FieldBuilder? importField = null;
        bool hasImportField = currentModuleImportFields != null &&
                              currentModuleImportFields.TryGetValue(aliasName, out importField);
        LocalBuilder? aliasLocal = null;
        if (!hasImportField)
        {
            aliasLocal = _ctx.Locals.GetLocal(aliasName) ?? _ctx.Locals.DeclareLocal(aliasName, _ctx.Types.Object);
        }

        // Check if module uses export = syntax
        if (exportFields.TryGetValue("$exportAssignment", out var exportAssignField))
        {
            // Module uses export = - load the assignment value directly
            IL.Emit(OpCodes.Ldsfld, exportAssignField);

            // Track if this import is an exported class (for cross-module static member access)
            if (_ctx.ExportAssignmentClasses?.TryGetValue(importedPath, out var qualifiedClassName) == true)
            {
                _ctx.ImportedClassAliases ??= new Dictionary<string, string>();
                _ctx.ImportedClassAliases[aliasName] = qualifiedClassName;
            }
        }
        else
        {
            // ES6-style module - create namespace object from all exports
            var dictType = _ctx.Types.DictionaryStringObject;
            var dictCtor = _ctx.Types.GetDefaultConstructor(dictType);
            var addMethod = _ctx.Types.GetMethod(dictType, "Add", _ctx.Types.String, _ctx.Types.Object);

            IL.Emit(OpCodes.Newobj, dictCtor);

            // Add each export to the dictionary
            foreach (var (exportName, field) in exportFields)
            {
                if (exportName == "$default") continue; // Skip default in namespace import

                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldstr, exportName);
                IL.Emit(OpCodes.Ldsfld, field);
                IL.Emit(OpCodes.Callvirt, addMethod);
            }

            // Wrap in SharpTSObject
            IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateObject);
        }

        // Store in static field if available, otherwise use local
        if (hasImportField)
        {
            IL.Emit(OpCodes.Stsfld, importField!);
        }
        else
        {
            IL.Emit(OpCodes.Stloc, aliasLocal!);
        }

        // If this is a re-export (export import x = require('...')), store in export field
        if (importReq.IsExported &&
            _ctx.CurrentModulePath != null &&
            _ctx.ModuleExportFields.TryGetValue(_ctx.CurrentModulePath, out var currentExportFields))
        {
            if (currentExportFields.TryGetValue(aliasName, out var exportField))
            {
                // Load from import field or local
                if (currentModuleImportFields?.TryGetValue(aliasName, out var impField) == true)
                {
                    IL.Emit(OpCodes.Ldsfld, impField);
                }
                else
                {
                    IL.Emit(OpCodes.Ldloc, aliasLocal!);
                }
                IL.Emit(OpCodes.Stsfld, exportField);
            }
        }
    }

    /// <summary>
    /// Emits a namespace object for a built-in module.
    /// </summary>
    private void EmitBuiltInModuleNamespace(string moduleName, string localName)
    {
        var emitter = _ctx.BuiltInModuleEmitterRegistry?.GetEmitter(moduleName);
        var local = _ctx.Locals.GetLocal(localName) ?? _ctx.Locals.DeclareLocal(localName, _ctx.Types.Object);

        if (emitter == null)
        {
            // Module emitter not registered - store null
            IL.Emit(OpCodes.Ldnull);
            IL.Emit(OpCodes.Stloc, local);
            return;
        }

        // Track this variable as a built-in module namespace for direct method dispatch
        _ctx.BuiltInModuleNamespaces ??= new Dictionary<string, string>();
        _ctx.BuiltInModuleNamespaces[localName] = moduleName;

        // Create a dictionary to hold all module exports
        var dictType = _ctx.Types.DictionaryStringObject;
        var dictCtor = _ctx.Types.GetDefaultConstructor(dictType);
        var addMethod = _ctx.Types.GetMethod(dictType, "Add", _ctx.Types.String, _ctx.Types.Object);

        IL.Emit(OpCodes.Newobj, dictCtor);

        // Add each exported member to the dictionary
        foreach (var memberName in emitter.GetExportedMembers())
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldstr, memberName);

            // Try to emit the property value, or create a function wrapper
            if (!emitter.TryEmitPropertyGet(this, memberName))
            {
                // For methods, create a TSFunction wrapper
                EmitBuiltInModuleMethodWrapper(moduleName, memberName);
            }

            EnsureBoxed();
            IL.Emit(OpCodes.Callvirt, addMethod);
        }

        // Wrap dictionary in SharpTSObject
        IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateObject);
        IL.Emit(OpCodes.Stloc, local);
    }

    /// <summary>
    /// Emits code for an export statement.
    /// Exports store values into module export fields.
    /// </summary>
    private void EmitExport(Stmt.Export export)
    {
        if (export.IsTypeOnly)
            return;

        if (_ctx.CurrentModulePath == null || _ctx.ModuleExportFields == null)
        {
            // Single-file (script) mode: there is no module to export into, so the
            // `export` marker itself is inert. But a wrapped var/const declaration
            // still needs its initializer to run and its binding stored — exactly
            // as a bare `const x = …` would (its static/captured field was defined
            // by DefineModuleScopedTopLevelStaticField / RegisterCapturedStmt). A
            // Stmt.Sequence covers destructuring and multi-declarator forms
            // (`export const { a } = o`, `export let a = 1, b = 2`). Function/class/
            // enum/namespace declarations bind by name via the registries and need
            // no entry-point store, so they stay no-ops here.
            if (_ctx.CurrentModulePath == null &&
                export.Declaration is Stmt.Var or Stmt.Const or Stmt.Sequence or Stmt.Class)
            {
                EmitStatement(export.Declaration);
            }
            return;
        }

        if (!_ctx.ModuleExportFields.TryGetValue(_ctx.CurrentModulePath, out var exportFields))
        {
            return;
        }

        // Handle export = assignment (CommonJS-style)
        if (export.ExportAssignment != null)
        {
            if (exportFields.TryGetValue("$exportAssignment", out var exportAssignField))
            {
                EmitExpression(export.ExportAssignment);
                EnsureBoxed();
                IL.Emit(OpCodes.Stsfld, exportAssignField);
            }
            return;
        }

        if (export.IsDefaultExport)
        {
            if (export.Declaration != null)
            {
                // export default class/function - execute declaration and store value
                EmitStatement(export.Declaration);

                string? name = GetDeclarationName(export.Declaration);
                if (name != null && exportFields.TryGetValue("$default", out var defaultField))
                {
                    // Load the declared value and store in export field
                    var local = _ctx.Locals.GetLocal(name);
                    if (local != null)
                    {
                        EmitStoreLocalToExportField(local, defaultField);
                    }
                    else if (_ctx.TopLevelStaticVars?.TryGetValue(name, out var staticField) == true)
                    {
                        // Top-level variable stored as static field
                        IL.Emit(OpCodes.Ldsfld, staticField);
                        IL.Emit(OpCodes.Stsfld, defaultField);
                    }
                    else if (TryResolveExportableFunction(name, out var funcBuilder))
                    {
                        // Create TSFunction for function (sync, async, generator, or async generator)
                        EmitFunctionReference(name, funcBuilder);
                        IL.Emit(OpCodes.Stsfld, defaultField);
                    }
                    else if (_ctx.Classes.TryGetValue(_ctx.GetQualifiedClassName(name), out var classBuilder))
                    {
                        // Store class type token
                        IL.Emit(OpCodes.Ldtoken, classBuilder);
                        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Type, "GetTypeFromHandle"));
                        IL.Emit(OpCodes.Stsfld, defaultField);
                    }
                    else if (_ctx.EnumMembers?.TryGetValue(_ctx.GetQualifiedEnumName(name), out var enumMembers) == true)
                    {
                        // Create SharpTSObject with enum members
                        EmitEnumAsObject(enumMembers);
                        IL.Emit(OpCodes.Stsfld, defaultField);
                    }
                }
            }
            else if (export.DefaultExpr != null)
            {
                // export default <expression>
                if (exportFields.TryGetValue("$default", out var defaultField))
                {
                    EmitExpression(export.DefaultExpr);
                    EnsureBoxed();
                    IL.Emit(OpCodes.Stsfld, defaultField);
                }
            }
        }
        else if (export.Declaration != null)
        {
            // export const/let/function/class - execute declaration and store in named field
            EmitStatement(export.Declaration);

            string? name = GetDeclarationName(export.Declaration);
            if (name != null && exportFields.TryGetValue(name, out var field))
            {
                var local = _ctx.Locals.GetLocal(name);
                if (local != null)
                {
                    EmitStoreLocalToExportField(local, field);
                }
                else if (_ctx.TopLevelStaticVars?.TryGetValue(name, out var staticField) == true)
                {
                    // Top-level variable stored as static field
                    IL.Emit(OpCodes.Ldsfld, staticField);
                    IL.Emit(OpCodes.Stsfld, field);
                }
                else if (TryResolveExportableFunction(name, out var funcBuilder))
                {
                    EmitFunctionReference(name, funcBuilder);
                    IL.Emit(OpCodes.Stsfld, field);
                }
                else if (_ctx.Classes.TryGetValue(_ctx.GetQualifiedClassName(name), out var classBuilder))
                {
                    IL.Emit(OpCodes.Ldtoken, classBuilder);
                    IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Type, "GetTypeFromHandle"));
                    IL.Emit(OpCodes.Stsfld, field);
                }
                else if (_ctx.EnumMembers?.TryGetValue(_ctx.GetQualifiedEnumName(name), out var enumMembers) == true)
                {
                    // Create SharpTSObject with enum members
                    EmitEnumAsObject(enumMembers);
                    IL.Emit(OpCodes.Stsfld, field);
                }
            }
        }
        else if (export.NamedExports != null && export.FromModulePath == null)
        {
            // export { x, y as z }
            foreach (var spec in export.NamedExports)
            {
                if (spec.IsTypeOnly)
                    continue;
                string localName = spec.LocalName.Lexeme;
                string exportedName = spec.ExportedName?.Lexeme ?? localName;

                if (exportFields.TryGetValue(exportedName, out var field))
                {
                    var local = _ctx.Locals.GetLocal(localName);
                    if (local != null)
                    {
                        EmitStoreLocalToExportField(local, field);
                    }
                    else if (_ctx.TopLevelStaticVars?.TryGetValue(localName, out var staticField) == true)
                    {
                        // Top-level variable stored as static field
                        IL.Emit(OpCodes.Ldsfld, staticField);
                        IL.Emit(OpCodes.Stsfld, field);
                    }
                    else if (TryResolveExportableFunction(localName, out var funcBuilder))
                    {
                        EmitFunctionReference(localName, funcBuilder);
                        IL.Emit(OpCodes.Stsfld, field);
                    }
                    else if (_ctx.Classes.TryGetValue(_ctx.GetQualifiedClassName(localName), out var classBuilder))
                    {
                        IL.Emit(OpCodes.Ldtoken, classBuilder);
                        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Type, "GetTypeFromHandle"));
                        IL.Emit(OpCodes.Stsfld, field);
                    }
                    else if (_ctx.EnumMembers?.TryGetValue(_ctx.GetQualifiedEnumName(localName), out var enumMembers) == true)
                    {
                        // Create SharpTSObject with enum members
                        EmitEnumAsObject(enumMembers);
                        IL.Emit(OpCodes.Stsfld, field);
                    }
                }
            }
        }
        else if (export.FromModulePath != null && _ctx.ModuleResolver != null)
        {
            // Re-export: export { x } from './module' or export * from './module'
            string sourcePath = _ctx.ModuleResolver.ResolveRuntimeModulePath(
                export.FromModulePath, _ctx.CurrentModulePath);

            // Ensure the source module is initialized before reading its exports
            if (_ctx.ModuleInitMethods?.TryGetValue(sourcePath, out var sourceInitMethod) == true)
            {
                IL.Emit(OpCodes.Call, sourceInitMethod);
            }

            if (_ctx.ModuleExportFields.TryGetValue(sourcePath, out var sourceFields))
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

                        if (sourceFields.TryGetValue(importedName, out var sourceField) &&
                            exportFields.TryGetValue(exportedName, out var targetField))
                        {
                            IL.Emit(OpCodes.Ldsfld, sourceField);
                            IL.Emit(OpCodes.Stsfld, targetField);
                        }
                    }
                }
                else
                {
                    // Re-export all: export * from './module'
                    foreach (var (name, sourceField) in sourceFields)
                    {
                        if (name == "$default") continue; // Don't re-export default
                        if (exportFields.TryGetValue(name, out var targetField))
                        {
                            IL.Emit(OpCodes.Ldsfld, sourceField);
                            IL.Emit(OpCodes.Stsfld, targetField);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Resolves a top-level function name to its emitted stub <see cref="MethodBuilder"/>,
    /// covering every function flavor that registers into <see cref="CompilationContext.Functions"/>.
    /// Every flavor — sync and async / generator / async-generator alike — registers its stub
    /// under the module-qualified name (since #418; <c>ILCompiler.Async</c> /
    /// <c>ILCompiler.Generators</c> / <c>ILCompiler.AsyncGenerators</c> key
    /// <c>_functions.Builders</c> by <see cref="CompilationContext.GetQualifiedFunctionName"/>),
    /// so the qualified lookup is the live path. The simple-name fallback is retained as a
    /// defensive measure for any stub registered before module qualification was wired up.
    /// Without resolving the stub here, an <c>export async function</c>, <c>export function*</c>,
    /// or <c>export async function*</c> leaves its export field null and the importing module
    /// throws "object is not a function" on call (#395).
    /// </summary>
    private bool TryResolveExportableFunction(string name, out MethodBuilder method)
    {
        // All function flavors: module-qualified key (matches single-file too, where the
        // qualified name collapses to the simple name).
        if (_ctx.Functions.TryGetValue(_ctx.GetQualifiedFunctionName(name), out method!))
            return true;
        // Defensive fallback: simple (unqualified) key.
        if (_ctx.Functions.TryGetValue(name, out method!))
            return true;
        method = null!;
        return false;
    }

    /// <summary>
    /// Gets the name declared by a statement.
    /// </summary>
    private static string? GetDeclarationName(Stmt decl) => decl switch
    {
        Stmt.Function f => f.Name.Lexeme,
        Stmt.Class c => c.Name.Lexeme,
        Stmt.Var v => v.Name.Lexeme,
        Stmt.Const ct => ct.Name.Lexeme,
        Stmt.Enum e => e.Name.Lexeme,
        _ => null
    };

    /// <summary>
    /// Stores a value from a local variable to an export field, boxing if necessary.
    /// </summary>
    private void EmitStoreLocalToExportField(LocalBuilder local, FieldBuilder field)
    {
        IL.Emit(OpCodes.Ldloc, local);
        if (local.LocalType.IsValueType)
        {
            IL.Emit(OpCodes.Box, local.LocalType);
        }
        IL.Emit(OpCodes.Stsfld, field);
    }

    /// <summary>
    /// Emits a TSFunction reference for a method (used for function exports).
    /// Creates: new TSFunction(null, methodInfo)
    /// </summary>
    private void EmitFunctionReference(string name, MethodBuilder method)
    {
        // Create TSFunction(null, methodInfo) - same pattern as arrow functions
        IL.Emit(OpCodes.Ldnull);  // target (null for static methods)
        IL.Emit(OpCodes.Ldtoken, method);

        // Use two-parameter GetMethodFromHandle with declaring type for proper token resolution
        // This is required for cross-module function references in persisted assemblies
        var declaringType = method.DeclaringType;
        if (declaringType != null)
        {
            IL.Emit(OpCodes.Ldtoken, declaringType);
            IL.Emit(OpCodes.Call, _ctx.Types.MethodBaseGetMethodFromHandleWithType);
        }
        else
        {
            IL.Emit(OpCodes.Call, _ctx.Types.MethodBaseGetMethodFromHandle);
        }

        IL.Emit(OpCodes.Castclass, _ctx.Types.MethodInfo);
        IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
    }

    /// <summary>
    /// Emits an enum as a SharpTSObject with its member values.
    /// </summary>
    private void EmitEnumAsObject(Dictionary<string, object> members)
    {
        // Create new Dictionary<string, object?>()
        var dictType = _ctx.Types.DictionaryStringObject;
        var dictCtor = _ctx.Types.GetDefaultConstructor(dictType);
        var addMethod = _ctx.Types.GetMethod(dictType, "Add", _ctx.Types.String, _ctx.Types.Object);

        IL.Emit(OpCodes.Newobj, dictCtor);

        foreach (var (memberName, value) in members)
        {
            IL.Emit(OpCodes.Dup);  // Keep dictionary on stack
            IL.Emit(OpCodes.Ldstr, memberName);
            if (value is double d)
            {
                IL.Emit(OpCodes.Ldc_R8, d);
                IL.Emit(OpCodes.Box, _ctx.Types.Double);
            }
            else if (value is string s)
            {
                IL.Emit(OpCodes.Ldstr, s);
            }
            else
            {
                IL.Emit(OpCodes.Ldnull);
            }
            IL.Emit(OpCodes.Call, addMethod);
        }

        // Wrap in SharpTSObject using the CreateObject helper
        IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateObject);
    }

    /// <summary>
    /// Emits a dynamic import expression.
    /// Dynamic import returns a Promise that resolves to the module namespace.
    /// </summary>
    protected override void EmitDynamicImport(Expr.DynamicImport di)
    {
        // Emit the path expression
        EmitExpression(di.PathExpression);
        EmitBoxIfNeeded(di.PathExpression);

        // Convert to string
        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Convert, "ToString", _ctx.Types.Object));

        // Push current module path (or empty string if not in module context)
        IL.Emit(OpCodes.Ldstr, _ctx.CurrentModulePath ?? "");

        // Call DynamicImportModule(path, currentModulePath) -> Task<object?>
        IL.Emit(OpCodes.Call, _ctx.Runtime!.DynamicImportModule);

        // Wrap Task<object?> in SharpTSPromise
        EmitCallUnknown(_ctx.Runtime!.WrapTaskAsPromise);

        // A bare import() expression returns an ordinary Promise. Do not let
        // the standalone entry point's top-level auto-await convenience turn
        // an intentionally ignored rejection into an uncaught program error.
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.MarkNonAutoAwaitPromiseMethod);
    }

    /// <summary>
    /// Emits an import.meta expression.
    /// Returns an object with 'url', 'filename', and 'dirname' properties containing the current module path.
    /// </summary>
    protected override void EmitImportMeta(Expr.ImportMeta im)
    {
        // Get current module path and convert to file:// URL
        string path = _ctx.CurrentModulePath ?? "";
        string url = path;
        if (!string.IsNullOrEmpty(url) && !url.StartsWith("file://"))
        {
            url = "file:///" + url.Replace("\\", "/");
        }
        string dirname = string.IsNullOrEmpty(path) ? "" : Path.GetDirectoryName(path) ?? "";

        // Create Dictionary<string, object> and add properties
        IL.Emit(OpCodes.Newobj, _ctx.Types.GetDefaultConstructor(_ctx.Types.DictionaryStringObject));

        // Add "url" property
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Ldstr, "url");
        IL.Emit(OpCodes.Ldstr, url);
        IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(_ctx.Types.DictionaryStringObject, "set_Item", _ctx.Types.String, _ctx.Types.Object));

        // Add "filename" property
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Ldstr, "filename");
        IL.Emit(OpCodes.Ldstr, path);
        IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(_ctx.Types.DictionaryStringObject, "set_Item", _ctx.Types.String, _ctx.Types.Object));

        // Add "dirname" property
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Ldstr, "dirname");
        IL.Emit(OpCodes.Ldstr, dirname);
        IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(_ctx.Types.DictionaryStringObject, "set_Item", _ctx.Types.String, _ctx.Types.Object));

        // Wrap in SharpTSObject
        EmitCallUnknown(_ctx.Runtime!.CreateObject);
    }

    /// <summary>
    /// Emits code for importing a built-in module (fs, path, os, etc.).
    /// Creates local variables bound to module methods/properties.
    /// </summary>
    private void EmitBuiltInModuleImport(Stmt.Import import, string moduleName)
    {
        var emitter = _ctx.BuiltInModuleEmitterRegistry?.GetEmitter(moduleName);
        if (emitter == null)
        {
            // Module emitter not registered - skip
            return;
        }

        // Namespace import: import * as mod from 'module'
        if (import.NamespaceImport != null)
        {
            string localName = import.NamespaceImport.Lexeme;
            var local = _ctx.Locals.GetLocal(localName) ?? _ctx.Locals.DeclareLocal(localName, _ctx.Types.Object);

            // Track this variable as a built-in module namespace for direct method dispatch
            _ctx.BuiltInModuleNamespaces ??= new Dictionary<string, string>();
            _ctx.BuiltInModuleNamespaces[localName] = moduleName;

            // Create a dictionary to hold all module exports
            var dictType = _ctx.Types.DictionaryStringObject;
            var dictCtor = _ctx.Types.GetDefaultConstructor(dictType);
            var addMethod = _ctx.Types.GetMethod(dictType, "Add", _ctx.Types.String, _ctx.Types.Object);

            IL.Emit(OpCodes.Newobj, dictCtor);

            // Add each exported member to the dictionary
            foreach (var memberName in emitter.GetExportedMembers())
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldstr, memberName);

                // Try to emit the property value, or create a function wrapper
                if (!emitter.TryEmitPropertyGet(this, memberName))
                {
                    // For methods, we need to create a TSFunction wrapper
                    // This will be handled by EmitBuiltInModuleMethodWrapper
                    EmitBuiltInModuleMethodWrapper(moduleName, memberName);
                }

                EnsureBoxed();
                IL.Emit(OpCodes.Call, addMethod);
            }

            // Wrap dictionary in SharpTSObject
            IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateObject);
            IL.Emit(OpCodes.Stloc, local);
        }

        // Named imports: import { readFileSync, writeFileSync } from 'fs'
        // These are stored as static fields so they're accessible from functions
        if (import.NamedImports != null)
        {
            foreach (var spec in import.NamedImports.Where(s => !s.IsTypeOnly))
            {
                string importedName = spec.Imported.Lexeme;
                string localName = spec.LocalName?.Lexeme ?? importedName;

                // Track this binding for direct dispatch (avoids TSFunction reflection issues)
                _ctx.BuiltInModuleMethodBindings ??= new Dictionary<string, (string, string)>();
                _ctx.BuiltInModuleMethodBindings[localName] = (moduleName, importedName);

                // Try property first, then method
                if (!emitter.TryEmitPropertyGet(this, importedName))
                {
                    EmitBuiltInModuleMethodWrapper(moduleName, importedName);
                }

                EnsureBoxed();

                // Store in static field (created in PreScanBuiltInModuleImports)
                if (_ctx.TopLevelStaticVars?.TryGetValue(localName, out var staticField) == true)
                {
                    IL.Emit(OpCodes.Stsfld, staticField);
                }
                else
                {
                    // Fallback to local (shouldn't happen for built-in modules)
                    var local = _ctx.Locals.GetLocal(localName) ?? _ctx.Locals.DeclareLocal(localName, _ctx.Types.Object);
                    IL.Emit(OpCodes.Stloc, local);
                }
            }
        }

        // Default import: treat same as namespace import for built-in modules
        // Node.js allows: import fs from 'fs' which works like import * as fs from 'fs'
        if (import.DefaultImport != null)
        {
            string localName = import.DefaultImport.Lexeme;
            var local = _ctx.Locals.GetLocal(localName) ?? _ctx.Locals.DeclareLocal(localName, _ctx.Types.Object);

            // Track this variable as a built-in module namespace for direct method dispatch
            _ctx.BuiltInModuleNamespaces ??= new Dictionary<string, string>();
            _ctx.BuiltInModuleNamespaces[localName] = moduleName;

            // Create a dictionary to hold all module exports
            var dictType = _ctx.Types.DictionaryStringObject;
            var dictCtor = _ctx.Types.GetDefaultConstructor(dictType);
            var addMethod = _ctx.Types.GetMethod(dictType, "Add", _ctx.Types.String, _ctx.Types.Object);

            IL.Emit(OpCodes.Newobj, dictCtor);

            // Add each exported member to the dictionary
            foreach (var memberName in emitter.GetExportedMembers())
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldstr, memberName);

                // Try to emit the property value, or create a function wrapper
                if (!emitter.TryEmitPropertyGet(this, memberName))
                {
                    EmitBuiltInModuleMethodWrapper(moduleName, memberName);
                }

                EnsureBoxed();
                IL.Emit(OpCodes.Call, addMethod);
            }

            // Wrap dictionary in SharpTSObject
            IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateObject);
            IL.Emit(OpCodes.Stloc, local);
        }
    }

    /// <summary>
    /// Resolves a module specifier or virtual path to the key used in
    /// <see cref="BuiltInModuleEmitterRegistry"/>. Returns the specifier itself
    /// for <c>primitive:*</c> entries, the name stripped from <c>builtin:</c>
    /// sentinels, or <c>null</c> when the input is neither.
    /// </summary>
    private static string? ResolveBuiltInOrPrimitiveKey(string path)
    {
        if (Modules.Stdlib.PrimitiveRegistry.IsPrimitive(path)) return path;
        return BuiltInModuleRegistry.GetModuleName(path);
    }

    /// <summary>
    /// Emits a TSFunction wrapper for a built-in module method.
    /// This allows the method to be passed around as a first-class function.
    /// </summary>
    private void EmitBuiltInModuleMethodWrapper(string moduleName, string methodName)
    {
        // Create a TSFunction that wraps the built-in method
        // The runtime helper will be generated to handle the dispatch
        var helperMethod = _ctx.Runtime?.GetBuiltInModuleMethod(moduleName, methodName);
        if (helperMethod != null)
        {
            IL.Emit(OpCodes.Ldnull); // target (null for static methods)
            _ctx.Types.EmitLoadMethodInfoViaHandle(IL, helperMethod);
            IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
        }
        else
        {
            // Fallback: emit null for unknown methods
            IL.Emit(OpCodes.Ldnull);
        }
    }

    #endregion
}
