using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Layered CompilationContext factories. Every production emission context is created here;
/// direct <c>new CompilationContext(...)</c> construction outside this file is prohibited
/// (enforced by CompilationContextFactoryTests). Call sites take a factory context and apply
/// short, visible overlays for their scope-specific values.
/// </summary>
/// <remarks>
/// Layering:
/// <list type="bullet">
/// <item><see cref="CreateBaseCompilationContext"/> — compilation-wide invariants only: values
/// assigned identically by every emission context (closure registries, enum tables, runtime,
/// type maps, emitter registries, class registry, ambient strict mode).</item>
/// <item><see cref="CreateModuleMemberContext"/> — base plus the current-module scope
/// (module maps, CurrentModulePath/CurrentNamespacePath) shared by every body emitted inside a
/// module: functions, methods, accessors, constructors, generators, async bodies, arrows.</item>
/// <item><see cref="CreateModuleTopLevelContext"/> — the module/CommonJS init context; the ONLY
/// factory besides <see cref="CreateEntryPointTopLevelContext"/> that sets
/// <c>IsModuleTopLevel = true</c> (#562). Module-top-level state must never leak into function
/// or state-machine contexts.</item>
/// <item><see cref="CreateEntryPointTopLevelContext"/> — the single-file entry-point context
/// (top-level statements; also <c>IsModuleTopLevel = true</c>).</item>
/// <item><c>Apply*</c> helpers — recurring overlay groups (captured top-level variable access,
/// CommonJS resolution, @lock decorator fields, inner-function metadata).</item>
/// </list>
/// </remarks>
public partial class ILCompiler
{
    /// <summary>
    /// Creates a CompilationContext carrying only compilation-wide invariants. Scope-sensitive
    /// values (current module/namespace/class, strict-mode overrides, captured-variable maps,
    /// state-machine wiring) are applied by the specialized factories and call-site overlays.
    /// </summary>
    private CompilationContext CreateBaseCompilationContext(ILGenerator il, MethodBase? method = null)
    {
        return new CompilationContext(il, _typeMapper, _functions.Builders, _classes.Builders, _namespaceFields, _namespaceVarFields, _types)
        {
            // Closure analysis registries
            ClosureAnalyzer = _closures.Analyzer,
            ArrowMethods = _closures.ArrowMethods,
            ConstArrowBindings = _closures.ConstArrowBindings,
            DirectCallArrowBindings = _closures.DirectCallArrowBindings,
            ObjectShapes = _closures.ObjectShapes,
            DisplayClasses = _closures.DisplayClasses,
            DisplayClassFields = _closures.DisplayClassFields,
            DisplayClassConstructors = _closures.DisplayClassConstructors,
            // Function metadata
            FunctionRestParams = _functions.RestParams,
            FunctionsCapturingArguments = _functions.CapturingArguments,
            MethodsCapturingArguments = _functions.MethodsCapturingArguments,
            FunctionGenericParams = _functions.GenericParams,
            IsGenericFunction = _functions.IsGeneric,
            // Enum tables
            EnumMembers = _enums.Members,
            EnumReverse = _enums.Reverse,
            EnumKinds = _enums.Kinds,
            // Compilation-wide services
            Runtime = _runtime,
            RuntimeFeatures = _features,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            TypeEmitterRegistry = _typeEmitterRegistry,
            BuiltInModuleEmitterRegistry = _builtInModuleEmitterRegistry,
            BuiltInModuleNamespaces = _builtInModuleNamespaces,
            BuiltInModuleMethodBindings = GetCurrentBuiltInMethodBindings(),
            ImportedNames = _importedNames,
            ClassExprBuilders = _classExprs.Builders,
            ClassExprStaticMethods = _classExprs.StaticMethods,
            ClassExprCaptureFields = _classExprs.CaptureFields,
            DeferredComputedClassKeys = _classes.DeferredComputedClassKeys,
            DeferredComputedClassExprKeys = _classExprs.DeferredComputedKeys,
            BlockScopedClassBuilders = _classes.BlockScopedBuilders,
            ClassRegistry = GetClassRegistry(),
            DotNetNamespace = _modules.CurrentDotNetNamespace,
            // Ambient strict mode; bodies with their own "use strict" prologue override this.
            IsStrictMode = _isStrictMode,
            // Symbol emission: null unless this build asked for debug symbols, which is what keeps
            // ordinary builds from paying for any of it.
            CurrentMethod = method,
            DebugScope = CurrentDebugScope,
        };
    }

    /// <summary>
    /// Points the context's <see cref="LocalsManager"/> at the record collecting this method's
    /// named locals and scopes. A no-op unless the build is emitting symbols.
    /// </summary>
    private void AttachLocalSymbols(CompilationContext ctx)
    {
        if (ctx.DebugScope is null || ctx.CurrentMethod is null || ctx.IL is null) return;

        ctx.Locals.SymbolSink = _debugInfo.BeginMethodLocals(ctx.CurrentMethod, ctx.IL);
        MarkNonUserCodeIfLibrary(ctx);
    }

    /// <summary>
    /// Marks a body compiled from the bundled stdlib as non-user code, so Just My Code steps over
    /// it and lands on the next line the user actually wrote.
    /// </summary>
    /// <remarks>
    /// Emitted runtime helpers need no such marking: they carry no debug information at all, which
    /// already makes them non-user code. Stdlib modules are different — they are real TypeScript
    /// compiled alongside the program, so without this they would be as steppable as the user's own
    /// files. The line information is still emitted, so a stack trace through the stdlib remains
    /// readable and stepping in is still possible with Just My Code turned off.
    /// </remarks>
    private void MarkNonUserCodeIfLibrary(CompilationContext ctx)
    {
        if (ctx.DebugScope is not { IsLibrary: true }) return;
        if (!_nonUserCodeMethods.Add(ctx.CurrentMethod!)) return;

        var nonUserCodeCtor =
            typeof(System.Diagnostics.DebuggerNonUserCodeAttribute).GetConstructor(Type.EmptyTypes)!;

        switch (ctx.CurrentMethod)
        {
            case MethodBuilder method:
                method.SetCustomAttribute(nonUserCodeCtor, CustomAttributeEncoder.EmptyBlob);
                break;
            case ConstructorBuilder constructor:
                constructor.SetCustomAttribute(nonUserCodeCtor, CustomAttributeEncoder.EmptyBlob);
                break;
        }
    }

    /// <summary>
    /// Creates the context for a body emitted inside the current module (function, class member,
    /// generator, async body, arrow): base invariants plus module maps and the current
    /// module/namespace scope. Does NOT set IsModuleTopLevel — declarations in these bodies are
    /// locals, never module-level bindings.
    /// </summary>
    private CompilationContext CreateModuleMemberContext(ILGenerator il, MethodBase? method = null)
    {
        var ctx = CreateBaseCompilationContext(il, method);
        ApplyModuleMaps(ctx);
        ctx.CurrentModulePath = _modules.CurrentPath;
        ctx.CurrentNamespacePath = _currentNamespacePath;
        ApplyCapturedTopLevelVariableAccess(ctx, memberBodyExports: true);
        AttachLocalSymbols(ctx);
        return ctx;
    }

    /// <summary>
    /// Creates the context that emits a module's (ESM or CommonJS) top-level statements.
    /// </summary>
    private CompilationContext CreateModuleTopLevelContext(ILGenerator il, MethodBase? method = null)
    {
        var ctx = CreateBaseCompilationContext(il, method);
        ApplyModuleMaps(ctx);
        ApplyCapturedTopLevelVariableAccess(ctx, memberBodyExports: false);
        ctx.LiftedBlockScopedTopLevelVars = BuildLiftedBlockScopedTopLevelVarsForModule(_modules.CurrentPath);
        ctx.ArrowEntryPointDCFields = _closures.ArrowEntryPointDCFields.Count > 0 ? _closures.ArrowEntryPointDCFields : null;
        ctx.AsyncArrowBuilders = _async.ArrowBuilders.Count > 0 ? _async.ArrowBuilders : null;
        ctx.AsyncArrowOuterBuilders = _async.ArrowOuterBuilders;
        ctx.AsyncArrowParentBuilders = _async.ArrowParentBuilders;
        ctx.ExportAssignmentClasses = _modules.ExportAssignmentClasses;
        ctx.ExportedClasses = _modules.ExportedClasses;
        ctx.DefaultExportClasses = _modules.DefaultExportClasses;
        // This context emits the module/script top-level statements, so var/let/const
        // declarations here are genuine module-level bindings (#562).
        ctx.IsModuleTopLevel = true;
        AttachLocalSymbols(ctx);
        return ctx;
    }

    /// <summary>
    /// Creates the context that emits the single-file entry point's top-level statements
    /// (and its CommonJS variant). Like <see cref="CreateModuleTopLevelContext"/> this sets
    /// IsModuleTopLevel (#562), but carries entry-point display-class construction state and
    /// class-expression lowering maps instead of per-module export tables.
    /// </summary>
    private CompilationContext CreateEntryPointTopLevelContext(ILGenerator il, MethodBase? method = null)
    {
        var ctx = CreateBaseCompilationContext(il, method);
        ApplyCapturedTopLevelVariableAccess(ctx, memberBodyExports: false);
        ctx.LiftedBlockScopedTopLevelVars = BuildLiftedBlockScopedTopLevelVarsForModule(_modules.CurrentPath);
        ctx.ArrowEntryPointDCFields = _closures.ArrowEntryPointDCFields.Count > 0 ? _closures.ArrowEntryPointDCFields : null;
        ctx.FunctionOverloads = _functions.Overloads;
        ctx.AsyncArrowBuilders = _async.ArrowBuilders.Count > 0 ? _async.ArrowBuilders : null;
        // Class expression support
        ctx.VarToClassExpr = _classExprs.VarToClassExpr;
        ctx.ClassExprStaticFields = _classExprs.StaticFields;
        ctx.ClassExprStaticMethods = _classExprs.StaticMethods;
        ctx.ClassExprConstructors = _classExprs.Constructors;
        ctx.ClassExprGenericParams = _classExprs.GenericParams;
        ctx.ClassExprSuperclass = _classExprs.Superclass;
        ctx.UnionGenerator = _unionGenerator;
        // Entry-point display class for captured top-level variables
        ctx.EntryPointDisplayClass = _closures.EntryPointDisplayClass;
        ctx.EntryPointDisplayClassCtor = _closures.EntryPointDisplayClassCtor;
        // Top-level statements run here: var/let/const are module-level bindings (#562).
        ctx.IsModuleTopLevel = true;
        // The single-file entry point has classic-script global semantics.
        ctx.IsScriptTopLevel = true;
        AttachLocalSymbols(ctx);
        return ctx;
    }

    /// <summary>
    /// Creates the context for a nested async arrow's MoveNext, inheriting scope from the parent
    /// state machine's context rather than from compiler state (the parent may itself be a
    /// state-machine context whose maps were already scoped).
    /// </summary>
    private CompilationContext CreateNestedAsyncArrowContext(ILGenerator il, CompilationContext parentCtx, MethodBase? method = null)
    {
        return new CompilationContext(il, parentCtx.TypeMapper, parentCtx.Functions, parentCtx.Classes, parentCtx.NamespaceFields, parentCtx.NamespaceVarFields, parentCtx.Types)
        {
            Runtime = parentCtx.Runtime,
            RuntimeFeatures = parentCtx.RuntimeFeatures,
            ClosureAnalyzer = parentCtx.ClosureAnalyzer,
            ArrowMethods = parentCtx.ArrowMethods,
            ConstArrowBindings = parentCtx.ConstArrowBindings,
            DirectCallArrowBindings = parentCtx.DirectCallArrowBindings,
            DisplayClasses = parentCtx.DisplayClasses,
            DisplayClassFields = parentCtx.DisplayClassFields,
            DisplayClassConstructors = parentCtx.DisplayClassConstructors,
            EnumMembers = parentCtx.EnumMembers,
            EnumReverse = parentCtx.EnumReverse,
            EnumKinds = parentCtx.EnumKinds,
            TopLevelStaticVars = parentCtx.TopLevelStaticVars,
            FunctionRestParams = parentCtx.FunctionRestParams,
            FunctionGenericParams = parentCtx.FunctionGenericParams,
            IsGenericFunction = parentCtx.IsGenericFunction,
            TypeMap = parentCtx.TypeMap,
            DeadCode = parentCtx.DeadCode,
            AsyncMethods = null,
            AsyncArrowBuilders = _async.ArrowBuilders,
            AsyncArrowOuterBuilders = _async.ArrowOuterBuilders,
            AsyncArrowParentBuilders = _async.ArrowParentBuilders,
            // Inherit module support from parent context
            CurrentModulePath = parentCtx.CurrentModulePath,
            ClassToModule = parentCtx.ClassToModule,
            FunctionToModule = parentCtx.FunctionToModule,
            EnumToModule = parentCtx.EnumToModule,
            TypeEmitterRegistry = parentCtx.TypeEmitterRegistry,
            ClassExprBuilders = parentCtx.ClassExprBuilders,
            ClassExprCaptureFields = parentCtx.ClassExprCaptureFields,
            BlockScopedClassBuilders = parentCtx.BlockScopedClassBuilders,
            IsStrictMode = parentCtx.IsStrictMode,
            // ES2022 Private Class Elements support - inherit from parent context
            CurrentClassName = parentCtx.CurrentClassName,
            CurrentClassBuilder = parentCtx.CurrentClassBuilder,
            // Registry services
            ClassRegistry = parentCtx.ClassRegistry,
            // Entry-point display class for captured top-level variables
            EntryPointDisplayClassFields = parentCtx.EntryPointDisplayClassFields,
            CapturedTopLevelVars = parentCtx.CapturedTopLevelVars,
            EntryPointDisplayClassStaticField = parentCtx.EntryPointDisplayClassStaticField,
            // Captured locals promoted into the enclosing function's display class (#625): the
            // arrow reads/writes them through `outer.functionDC.field` rather than mutating the
            // boxed value-type state machine in place (unverifiable). Only fields the function
            // actually placed in its DC are listed here, so a name present means "route via DC".
            FunctionDisplayClassFields = parentCtx.FunctionDisplayClassFields,
            OuterFunctionDCField = parentCtx.OuterFunctionDCField,
            // Follow-up to #838: lets this nested async arrow's MoveNext populate a nested sync arrow's
            // $functionDC from this arrow's OWN DC (EmitCapturingArrowInAsyncArrow).
            ArrowFunctionDCFields = _closures.ArrowFunctionDCFields.Count > 0 ? _closures.ArrowFunctionDCFields : null,
            CurrentMethod = method,
            DebugScope = parentCtx.DebugScope,
        };
    }

    /// <summary>
    /// Creates the deliberately minimal context used to emit an overload's forwarding body —
    /// just enough to evaluate default-value expressions (#698). Not a general emission context.
    /// </summary>
    private CompilationContext CreateOverloadDefaultsContext(ILGenerator il, bool isStrict)
    {
        return new CompilationContext(il, _typeMapper, _functions.Builders, _classes.Builders, _namespaceFields, _namespaceVarFields, _types)
        {
            ClassRegistry = GetClassRegistry(),
            Runtime = _runtime,
            RuntimeFeatures = _features,
            TypeMap = _typeMap,
            IsStrictMode = isStrict
        };
    }

    /// <summary>
    /// Creates the shared definition-phase context (module name resolution). NOT an emission
    /// context: it has no ILGenerator, and ClassRegistry is intentionally not set — the
    /// definition phase uses the raw dictionaries.
    /// </summary>
    private CompilationContext CreateDefinitionPhaseContext()
    {
        return new CompilationContext(null!, _typeMapper, _functions.Builders, _classes.Builders, _namespaceFields, _namespaceVarFields, _types)
        {
            ClassToModule = _modules.ClassToModule,
            FunctionToModule = _modules.FunctionToModule,
            EnumToModule = _modules.EnumToModule,
            IsStrictMode = _isStrictMode
        };
    }

    /// <summary>Applies the compilation's definition→module maps.</summary>
    private void ApplyModuleMaps(CompilationContext ctx)
    {
        ctx.ClassToModule = _modules.ClassToModule;
        ctx.FunctionToModule = _modules.FunctionToModule;
        ctx.EnumToModule = _modules.EnumToModule;
    }

    /// <summary>
    /// Applies module-level variable access: the current module's top-level static var map, the
    /// captured-top-level-var set, and the entry-point display-class field map/static field.
    /// <paramref name="memberBodyExports"/> selects the member-body variant of the static-var
    /// map, which augments it with this module's ESM export fields so bare identifiers inside
    /// functions, arrows, state machines, and class bodies resolve to live module exports.
    /// </summary>
    private void ApplyCapturedTopLevelVariableAccess(CompilationContext ctx, bool memberBodyExports = true)
    {
        ctx.TopLevelStaticVars = memberBodyExports
            ? BuildModuleMemberTopLevelStaticVarsForModule(_modules.CurrentPath)
            : BuildTopLevelStaticVarsForModule(_modules.CurrentPath);
        ctx.CapturedTopLevelVars = BuildCapturedTopLevelVarsForModule(_modules.CurrentPath);
        ctx.EntryPointDisplayClassFields = BuildEntryPointDisplayClassFieldsForModule(_modules.CurrentPath);
        ctx.EntryPointDisplayClassStaticField = _closures.EntryPointDisplayClassStaticField;
    }

    /// <summary>
    /// Applies CJS/ESM resolution state so `exports`, `module.exports`, and `require(...)` work
    /// inside bodies nested in a CJS module.
    /// </summary>
    private void ApplyCommonJsModuleAccess(CompilationContext ctx)
    {
        ctx.ModuleResolver = _modules.Resolver;
        ctx.ModuleExportFields = _modules.ExportFields;
        ctx.ModuleInitMethods = _modules.InitMethods;
        ctx.ModuleImportFields = _modules.ImportFields;
        ctx.ModuleTypes = _modules.Types;
        ctx.CommonJsExportFields = _modules.CommonJsExportFields;
        ctx.CommonJsGetExportsMethods = _modules.CommonJsGetExportsMethods;
        ctx.CurrentCjsExportsField = _modules.CurrentPath != null
            && _modules.CommonJsExportFields.TryGetValue(_modules.CurrentPath, out var cjsExports)
            ? cjsExports
            : null;
    }

    /// <summary>Applies @lock decorator lock/reentrancy field maps.</summary>
    private void ApplyLockDecoratorFields(CompilationContext ctx)
    {
        ctx.SyncLockFields = _locks.SyncLockFields;
        ctx.AsyncLockFields = _locks.AsyncLockFields;
        ctx.LockReentrancyFields = _locks.ReentrancyFields;
        ctx.StaticSyncLockFields = _locks.StaticSyncLockFields;
        ctx.StaticAsyncLockFields = _locks.StaticAsyncLockFields;
        ctx.StaticLockReentrancyFields = _locks.StaticReentrancyFields;
    }

    /// <summary>
    /// Applies inner-function metadata — required for any `function X() {}` declared inside the
    /// body being emitted to be reachable from sibling statements.
    /// </summary>
    private void ApplyInnerFunctionSupport(CompilationContext ctx)
    {
        ctx.InnerFunctionMethods = _innerFunctionMethods;
        ctx.InnerFunctionDisplayClasses = _innerFunctionDisplayClasses;
        ctx.InnerFunctionDCFields = _innerFunctionDCFields;
        ctx.InnerFunctionDCCtors = _innerFunctionDCCtors;
        ctx.InnerFunctionEntryPointDCFields = _innerFunctionEntryPointDCFields;
        ctx.InnerFunctionFunctionDCFields = _innerFunctionFunctionDCFields;
    }
}
