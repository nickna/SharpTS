using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Modules;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// CommonJS module compilation methods for the IL compiler.
/// </summary>
/// <remarks>
/// CJS modules are emitted as `$Module_Name` static classes with a single `$exports` static field
/// instead of per-export fields. The body of the CJS file is compiled into a `$Initialize()`
/// method that creates an empty exports dictionary, runs the file body, and is guarded by
/// `_initStarted` for circular-require correctness.
///
/// `$GetExports()` is the public entry that callers (require/ESM imports) use; it calls
/// `$Initialize()` first, then returns the current `$exports` value.
///
/// At the moment of $Initialize call, `_initStarted` is set BEFORE running the body, so a
/// re-entrant require sees the partially-populated exports object — matching Node semantics.
/// </remarks>
public partial class ILCompiler
{
    /// <summary>
    /// Per-CJS-module init guard fields (kept directly because <see cref="EmitterTypeHelpers.ResolveField(string, BindingFlags)"/>
    /// doesn't work before <c>CreateType()</c>).
    /// </summary>
    private readonly Dictionary<string, (FieldBuilder InitStarted, FieldBuilder Initialized)> _cjsInitGuardFields = [];
    private readonly Dictionary<string, FieldBuilder> _cjsModuleObjectFields = [];

    /// <summary>
    /// Defines the .NET type for a CommonJS module: a static class with $exports field,
    /// $GetExports() and $Initialize() methods, plus circular-init guards.
    /// </summary>
    private void DefineCommonJsModuleType(ParsedModule module)
    {
        string moduleTypeName = $"$Module_{CompilationContext.SanitizeModuleName(module.ModuleName)}";
        var moduleType = _moduleBuilder.DefineType(
            moduleTypeName,
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.Abstract
        );

        _modules.Types[module.Path] = moduleType;

        // The single $exports field — holds the live module.exports value.
        var exportsField = moduleType.DefineField(
            "$exports",
            typeof(object),
            FieldAttributes.Public | FieldAttributes.Static
        );
        _modules.CommonJsExportFields[module.Path] = exportsField;

        // Spec-compliant `module` object — a $CJSModule instance that proxies exports
        // back to the static $exports field and exposes id/filename/loaded/paths/etc.
        // Stored in a static field so inner arrows and nested functions can reach it
        // via the existing TopLevelStaticVars resolution path without requiring
        // capture-display-class machinery for module-scope magic names.
        var moduleObjField = moduleType.DefineField(
            "$module",
            typeof(object),
            FieldAttributes.Public | FieldAttributes.Static
        );
        _cjsModuleObjectFields[module.Path] = moduleObjField;

        // Register `module` so BuildTopLevelStaticVarsForModule picks it up for any
        // other compilation context scoped to this module (class methods, arrows,
        // inner functions) — they all rebuild their TopLevelStaticVars map from here.
        if (!_moduleTopLevelStaticVars.TryGetValue(module.Path, out var moduleStatics))
        {
            moduleStatics = new Dictionary<string, FieldBuilder>();
            _moduleTopLevelStaticVars[module.Path] = moduleStatics;
        }
        moduleStatics["module"] = moduleObjField;

        // Provide a degenerate ExportFields entry so the rest of the module-emission machinery
        // (which always indexes ExportFields[path]) doesn't throw KeyNotFoundException.
        _modules.ExportFields[module.Path] = new Dictionary<string, FieldBuilder>
        {
            ["$exports"] = exportsField
        };

        // Init guard fields. _initStarted is set BEFORE the body runs (so circular requires see
        // partial state). _initialized is set AFTER the body completes (currently informational).
        var initStartedField = moduleType.DefineField(
            "_initStarted",
            typeof(bool),
            FieldAttributes.Private | FieldAttributes.Static
        );
        var initializedField = moduleType.DefineField(
            "_initialized",
            typeof(bool),
            FieldAttributes.Private | FieldAttributes.Static
        );
        _cjsInitGuardFields[module.Path] = (initStartedField, initializedField);

        // $Initialize: idempotent body executor with init-started guard.
        var initMethod = moduleType.DefineMethod(
            "$Initialize",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes
        );
        _modules.InitMethods[module.Path] = initMethod;

        // $GetExports: calls $Initialize, returns $exports.
        var getExportsMethod = moduleType.DefineMethod(
            "$GetExports",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            Type.EmptyTypes
        );
        _modules.CommonJsGetExportsMethods[module.Path] = getExportsMethod;

        var getExportsIl = getExportsMethod.GetILGenerator();
        getExportsIl.Emit(OpCodes.Call, initMethod);
        getExportsIl.Emit(OpCodes.Ldsfld, exportsField);
        getExportsIl.Emit(OpCodes.Ret);

        // $GetNamespace: same as $GetExports for CJS modules. Provided so the dynamic-import
        // module registry treats CJS files like ESM files (returns the module's exports object).
        var getNamespaceMethod = moduleType.DefineMethod(
            "$GetNamespace",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            Type.EmptyTypes
        );
        _moduleGetNamespaceMethods[module.Path] = getNamespaceMethod;

        var getNamespaceIl = getNamespaceMethod.GetILGenerator();
        getNamespaceIl.Emit(OpCodes.Call, initMethod);
        getNamespaceIl.Emit(OpCodes.Ldsfld, exportsField);
        getNamespaceIl.Emit(OpCodes.Ret);

        // Note: the $Initialize body is emitted later in EmitCommonJsModuleInit during Phase 9.
        // We only define the field/method shells here so other modules can reference them.
    }

    /// <summary>
    /// Emits the body of a CommonJS module's $Initialize method.
    /// </summary>
    private void EmitCommonJsModuleInit(ParsedModule module)
    {
        var moduleType = _modules.Types[module.Path];
        var initMethod = _modules.InitMethods[module.Path];
        var exportsField = _modules.CommonJsExportFields[module.Path];
        var (initStartedField, initializedField) = _cjsInitGuardFields[module.Path];

        var il = initMethod.GetILGenerator();

        // Guard: if (_initStarted) return;
        var skipLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, initStartedField);
        il.Emit(OpCodes.Brtrue, skipLabel);

        // _initStarted = true;
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stsfld, initStartedField);

        // $exports = new Dictionary<string, object?>();
        var dictType = typeof(Dictionary<string, object?>);
        var dictCtor = dictType.GetConstructor(Type.EmptyTypes)!;
        il.Emit(OpCodes.Newobj, dictCtor);
        il.Emit(OpCodes.Stsfld, exportsField);

        // Build the compilation context for this module's body.
        // Set _modules.CurrentPath first so BuildTopLevelStaticVarsForModule /
        // BuildEntryPointDisplayClassFieldsForModule scope to this module —
        // otherwise they'd fall through to the SingleFile bucket and miss
        // this module's captured-top-level-var fields. (EmitModuleInit for
        // ESM does the same dance.)
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

        // CRITICAL: this is what tells the ILEmitter to lower module.exports/exports/require
        // patterns for this body.
        ctx.CurrentCjsExportsField = exportsField;

        // Initialize the static `$module` field — a $CJSModule instance pointing at the
        // module's own $exports field. Accessible to all code in the module's scope
        // (including nested arrow/inner-function bodies) via the TopLevelStaticVars
        // resolution path (registered at module-type definition time).
        var moduleObjField = _cjsModuleObjectFields[module.Path];
        il.Emit(OpCodes.Ldtoken, moduleType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ldstr, exportsField.Name);
        il.Emit(OpCodes.Ldc_I4, (int)(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static));
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetField", [typeof(string), typeof(System.Reflection.BindingFlags)])!);
        il.Emit(OpCodes.Ldstr, module.Path);
        il.Emit(OpCodes.Ldstr, module.Path);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Newobj, _runtime.CjsModuleCtor);
        il.Emit(OpCodes.Stsfld, moduleObjField);

        // The per-module ctx was built BEFORE we registered `module` in _moduleTopLevelStaticVars
        // on some code paths — ensure this ctx sees it too. Cheap and idempotent.
        ctx.TopLevelStaticVars ??= new Dictionary<string, FieldBuilder>();
        ctx.TopLevelStaticVars["module"] = moduleObjField;

        var emitter = new ILEmitter(ctx);

        foreach (var stmt in module.Statements)
        {
            // Skip class/function/interface/type-alias/enum declarations — compiled separately.
            if (stmt is Stmt.Class or Stmt.Function or Stmt.Interface or Stmt.TypeAlias or Stmt.Enum)
            {
                continue;
            }

            if (stmt is Stmt.Expression exprStmt)
            {
                EmitExpressionWithAsyncWait(il, emitter, exprStmt);
            }
            else
            {
                emitter.EmitStatement(stmt);
            }
        }

        // _initialized = true;
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stsfld, initializedField);

        il.MarkLabel(skipLabel);
        il.Emit(OpCodes.Ret);

        ILLabelValidator.Validate(il, $"CJS module init {module.Path}");
    }
}
