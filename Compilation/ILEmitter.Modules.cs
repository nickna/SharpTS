using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

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

        if (_ctx.CurrentModulePath == null || _ctx.ModuleResolver == null ||
            _ctx.ModuleExportFields == null || _ctx.ModuleTypes == null)
        {
            // Not in module context - imports are no-ops for single-file compilation
            return;
        }

        string importedPath = _ctx.ModuleResolver.ResolveModulePath(import.ModulePath, _ctx.CurrentModulePath);

        if (!_ctx.ModuleExportFields.TryGetValue(importedPath, out var exportFields) ||
            !_ctx.ModuleTypes.TryGetValue(importedPath, out var moduleType))
        {
            // Module not found - skip (type checker should have caught this)
            return;
        }

        // Default import: bind local variable to $default field
        if (import.DefaultImport != null)
        {
            string localName = import.DefaultImport.Lexeme;
            if (exportFields.TryGetValue("$default", out var defaultField))
            {
                var local = _ctx.Locals.GetLocal(localName) ?? _ctx.Locals.DeclareLocal(localName, _ctx.Types.Object);
                IL.Emit(OpCodes.Ldsfld, defaultField);
                IL.Emit(OpCodes.Stloc, local);
            }
        }

        // Named imports: bind local variables to named export fields
        // Skip individual type-only specifiers
        if (import.NamedImports != null)
        {
            foreach (var spec in import.NamedImports.Where(s => !s.IsTypeOnly))
            {
                string importedName = spec.Imported.Lexeme;
                string localName = spec.LocalName?.Lexeme ?? importedName;

                if (exportFields.TryGetValue(importedName, out var field))
                {
                    var local = _ctx.Locals.GetLocal(localName) ?? _ctx.Locals.DeclareLocal(localName, _ctx.Types.Object);
                    IL.Emit(OpCodes.Ldsfld, field);
                    IL.Emit(OpCodes.Stloc, local);
                }
            }
        }

        // Namespace import: create a SharpTSObject with all exports
        if (import.NamespaceImport != null)
        {
            string localName = import.NamespaceImport.Lexeme;
            var local = _ctx.Locals.GetLocal(localName) ?? _ctx.Locals.DeclareLocal(localName, _ctx.Types.Object);

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
            IL.Emit(OpCodes.Stloc, local);
        }
    }

    /// <summary>
    /// Emits code for an export statement.
    /// Exports store values into module export fields.
    /// </summary>
    private void EmitExport(Stmt.Export export)
    {
        if (_ctx.CurrentModulePath == null || _ctx.ModuleExportFields == null)
        {
            // Not in module context
            return;
        }

        if (!_ctx.ModuleExportFields.TryGetValue(_ctx.CurrentModulePath, out var exportFields))
        {
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
                    else if (_ctx.Functions.TryGetValue(_ctx.GetQualifiedFunctionName(name), out var funcBuilder))
                    {
                        // Create TSFunction for function
                        EmitFunctionReference(_ctx.GetQualifiedFunctionName(name), funcBuilder);
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
                else if (_ctx.Functions.TryGetValue(_ctx.GetQualifiedFunctionName(name), out var funcBuilder))
                {
                    EmitFunctionReference(_ctx.GetQualifiedFunctionName(name), funcBuilder);
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
                string localName = spec.LocalName.Lexeme;
                string exportedName = spec.ExportedName?.Lexeme ?? localName;

                if (exportFields.TryGetValue(exportedName, out var field))
                {
                    var local = _ctx.Locals.GetLocal(localName);
                    if (local != null)
                    {
                        EmitStoreLocalToExportField(local, field);
                    }
                    else if (_ctx.Functions.TryGetValue(_ctx.GetQualifiedFunctionName(localName), out var funcBuilder))
                    {
                        EmitFunctionReference(_ctx.GetQualifiedFunctionName(localName), funcBuilder);
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
            string sourcePath = _ctx.ModuleResolver.ResolveModulePath(export.FromModulePath, _ctx.CurrentModulePath);

            if (_ctx.ModuleExportFields.TryGetValue(sourcePath, out var sourceFields))
            {
                if (export.NamedExports != null)
                {
                    // Re-export specific names
                    foreach (var spec in export.NamedExports)
                    {
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
    /// Gets the name declared by a statement.
    /// </summary>
    private static string? GetDeclarationName(Stmt decl) => decl switch
    {
        Stmt.Function f => f.Name.Lexeme,
        Stmt.Class c => c.Name.Lexeme,
        Stmt.Var v => v.Name.Lexeme,
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
        var runtimeMethodHandle = _ctx.Types.Resolve("System.RuntimeMethodHandle");
        var methodBase = _ctx.Types.Resolve("System.Reflection.MethodBase");
        IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(methodBase, "GetMethodFromHandle", runtimeMethodHandle));
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
    }

    /// <summary>
    /// Emits an import.meta expression.
    /// Returns an object with 'url' property containing the current module path.
    /// </summary>
    protected override void EmitImportMeta(Expr.ImportMeta im)
    {
        // Get current module path and convert to file:// URL
        string url = _ctx.CurrentModulePath ?? "";
        if (!string.IsNullOrEmpty(url) && !url.StartsWith("file://"))
        {
            url = "file:///" + url.Replace("\\", "/");
        }

        // Create Dictionary<string, object> and add "url" property
        IL.Emit(OpCodes.Newobj, _ctx.Types.GetDefaultConstructor(_ctx.Types.DictionaryStringObject));
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Ldstr, "url");
        IL.Emit(OpCodes.Ldstr, url);
        IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(_ctx.Types.DictionaryStringObject, "set_Item", _ctx.Types.String, _ctx.Types.Object));

        // Wrap in SharpTSObject
        EmitCallUnknown(_ctx.Runtime!.CreateObject);
    }

    #endregion
}
