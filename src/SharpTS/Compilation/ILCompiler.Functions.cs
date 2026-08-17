using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Top-level function definition and emission for the IL compiler.
/// </summary>
public partial class ILCompiler
{
    private void DefineFunction(Stmt.Function funcStmt)
    {
        // Check if this is an async generator function - use combined state machine
        // Must check this FIRST since it has both IsAsync and IsGenerator true
        if (funcStmt.IsAsync && funcStmt.IsGenerator)
        {
            RegisterStateMachineFunctionModule(funcStmt);
            DefineAsyncGeneratorFunction(funcStmt);
            return;
        }

        // Check if this is an async function - use native IL state machine
        if (funcStmt.IsAsync)
        {
            RegisterStateMachineFunctionModule(funcStmt);
            DefineAsyncFunction(funcStmt);
            return;
        }

        // Check if this is a generator function - use generator state machine
        if (funcStmt.IsGenerator)
        {
            RegisterStateMachineFunctionModule(funcStmt);
            DefineGeneratorFunction(funcStmt);
            return;
        }

        var ctx = GetDefinitionContext();

        // Get qualified function name (module-prefixed in multi-module compilation)
        string qualifiedFunctionName = ctx.GetQualifiedFunctionName(funcStmt.Name.Lexeme);

        // Track simple name -> module mapping for later lookups
        if (_modules.CurrentPath != null)
        {
            _modules.FunctionToModule[funcStmt.Name.Lexeme] = _modules.CurrentPath;
            _functionDefinitionModule[funcStmt.Name.Lexeme] = _modules.CurrentPath;
        }

        // Resolve typed parameters and return type from TypeMap. The TypeMap is
        // keyed by simple name (the type checker doesn't see module qualification);
        // try both so cross-module definitions recover the right param types
        // (otherwise `...parts: string[]` degrades to `object`, breaking rest dispatch).
        var funcType = _typeMap?.GetFunctionType(qualifiedFunctionName)
                    ?? _typeMap?.GetFunctionType(funcStmt.Name.Lexeme);
        var paramTypes = ParameterTypeResolver.ResolveParameters(
            funcStmt.Parameters, _typeMapper, funcType, _typeMap);

        // Resolve typed return type (optimization: avoid boxing for : number returns).
        // Widen a number/boolean slot to object if the checker flagged an undefined-reachable
        // return (e.g. `return undefined as any`) — the unboxed slot would coerce it (#344).
        bool returnMayBeUndefined = ReturnSlotAnalysis.BlockReturnsMayBeUndefined(funcStmt.Body, _typeMap);
        Type returnType = ParameterTypeResolver.ResolveReturnType(
            funcType?.ReturnType, isAsync: false, _typeMapper, returnMayBeUndefined);

        var methodBuilder = _programType.DefineMethod(
            qualifiedFunctionName,
            MethodAttributes.Public | MethodAttributes.Static,
            returnType,
            paramTypes
        );

        // Handle generic type parameters
        bool isGeneric = funcStmt.TypeParams != null && funcStmt.TypeParams.Count > 0;
        _functions.IsGeneric[qualifiedFunctionName] = isGeneric;

        if (isGeneric)
        {
            string[] typeParamNames = funcStmt.TypeParams!.Select(tp => tp.Name.Lexeme).ToArray();
            var genericParams = methodBuilder.DefineGenericParameters(typeParamNames);

            // Apply constraints
            for (int i = 0; i < funcStmt.TypeParams!.Count; i++)
            {
                var constraint = funcStmt.TypeParams[i].Constraint;
                if (constraint != null)
                {
                    Type constraintType = ResolveConstraintType(constraint);
                    if (constraintType.IsInterface)
                        genericParams[i].SetInterfaceConstraints(constraintType);
                    else
                        EmitTypeDefinitions.SetBaseTypeConstraint(genericParams[i], constraintType);
                }
            }

            _functions.GenericParams[qualifiedFunctionName] = genericParams;
        }

        _functions.Builders[qualifiedFunctionName] = methodBuilder;

        // User TS function: when invoked as a value, omitted trailing args must pad with the
        // `undefined` sentinel (JS semantics), not CLR null. (#640)
        MarkPadsUndefined(methodBuilder);

        // Flag eagerly (phase 3) so direct-call sites emitted in phase 7 can publish
        // caller args to the thread-static before OpCodes.Call. Uses the same scanner
        // the prologue consults, keeping the two sides in sync. Overload signatures
        // (no body) can't reference `arguments`; skip them.
        if (funcStmt.Body != null && ReferencesArgumentsIdentifier(funcStmt.Body))
        {
            _functions.CapturingArguments.Add(qualifiedFunctionName);
            // Mark the method so $TSFunction can detect (at runtime, via
            // IsDefined) that this callback may observe the iteration index
            // through `arguments`. Without it, the iterator-helper
            // skip-index-box optimization treats this `this`-less declaration
            // like an arrow and drops args[1], so `function(){...arguments[1]...}`
            // used as a map/forEach/every callback reads a null index (#101).
            if (_runtime?.CapturesArgumentsAttrCtor != null)
                methodBuilder.SetCustomAttribute(
                    _runtime.CapturesArgumentsAttrCtor, CustomAttributeEncoder.EmptyBlob);
        }

        // Generate overloads for functions with default parameters
        var overloadSignatures = OverloadGenerator.GetOverloadSignatures(
            funcStmt.Parameters, paramTypes);
        if (overloadSignatures.Count > 0)
        {
            _functions.Overloads[qualifiedFunctionName] = [];
            foreach (var overloadParams in overloadSignatures)
            {
                var overload = _programType.DefineMethod(
                    qualifiedFunctionName,
                    MethodAttributes.Public | MethodAttributes.Static,
                    returnType,  // Use same typed return type as main method
                    overloadParams
                );
                _functions.Overloads[qualifiedFunctionName].Add(overload);
            }
        }

        // Track rest parameter info
        var restParam = funcStmt.Parameters.FirstOrDefault(p => p.IsRest);
        if (restParam != null)
        {
            int restIndex = funcStmt.Parameters.IndexOf(restParam);
            int regularCount = funcStmt.Parameters.Count(p => !p.IsRest);
            _functions.RestParams[qualifiedFunctionName] = (restIndex, regularCount);
        }

        // Track function AST node for closure analysis lookups
        _closures.FunctionAstNodes[qualifiedFunctionName] = funcStmt;

        // Create function-level display class if this function has captured locals
        DefineFunctionDisplayClass(funcStmt, qualifiedFunctionName);
    }

    /// <summary>
    /// Builds the parameter-type array for a state-machine stub method (async,
    /// generator, or async-generator). Every parameter is typed <c>object</c>
    /// except a trailing rest parameter, which is typed <c>List&lt;object&gt;</c>
    /// — the marker that <c>$TSFunction.AdjustArgs</c> recognizes when it has to
    /// pack the trailing call arguments into the rest list. State-machine stubs
    /// invoked indirectly (e.g. a cross-module import routed through
    /// <c>$TSFunction.Invoke</c>) rely on that marker; without it the indirect
    /// path drops the first raw argument straight into the rest slot, so the
    /// body's <c>for...of</c> over the rest value casts a scalar to
    /// <c>IEnumerable</c> and crashes (#426). Same-module direct calls pack a
    /// <c>$Array</c> (a <c>List&lt;object&gt;</c> subclass) via
    /// EmitRestParameterCall, so both call paths stay type-compatible with the
    /// <c>List&lt;object&gt;</c> slot. Mirrors the sync path's
    /// <see cref="ParameterTypeResolver.ResolveParameters"/> rest handling.
    /// </summary>
    private Type[] BuildStateMachineStubParamTypes(Stmt.Function funcStmt)
    {
        var paramTypes = new Type[funcStmt.Parameters.Count];
        for (int i = 0; i < funcStmt.Parameters.Count; i++)
            paramTypes[i] = funcStmt.Parameters[i].IsRest ? _types.ListOfObject : _types.Object;
        return paramTypes;
    }

    /// <summary>
    /// Module-qualifies the registry bookkeeping for an async / generator / async-generator
    /// top-level function so two modules declaring a same-named state-machine function no
    /// longer clobber each other (#418). Mirrors what <see cref="DefineFunction"/> does for
    /// sync functions: the stub/state-machine registries are keyed by the module-qualified
    /// name (see <c>DefineAsyncFunction</c> / <c>DefineGeneratorFunction</c> /
    /// <c>DefineAsyncGeneratorFunction</c>), so this records:
    /// <list type="bullet">
    /// <item><see cref="_functionDefinitionModule"/> keyed by the <em>qualified</em> name, which
    /// the Phase-7 emission loops use to restore <c>_modules.CurrentPath</c> per function.</item>
    /// <item><c>_modules.FunctionToModule</c> keyed by the simple name, so
    /// <see cref="CompilationContext.ResolveFunctionName"/> qualifies call-site / value
    /// references to the now-qualified stub key.</item>
    /// </list>
    /// No-op in single-file compilation (<c>CurrentPath == null</c>): there the qualified name
    /// equals the simple name and the registries stay under the simple key.
    /// </summary>
    private void RegisterStateMachineFunctionModule(Stmt.Function funcStmt)
    {
        string qualifiedName = GetDefinitionContext().GetQualifiedFunctionName(funcStmt.Name.Lexeme);

        // A state-machine function declared in a namespace must resolve namespace-level
        // var/let/const from its MoveNext body, which is emitted in a dedicated later phase
        // after _currentNamespacePath is cleared. Record the namespace here (independent of
        // module path, so single-file namespaces work) so that phase can restore it (#567).
        if (_currentNamespacePath != null)
            _functionDefinitionNamespace[qualifiedName] = _currentNamespacePath;

        if (_modules.CurrentPath == null)
            return;

        _functionDefinitionModule[qualifiedName] = _modules.CurrentPath;
        _modules.FunctionToModule[funcStmt.Name.Lexeme] = _modules.CurrentPath;
    }

    /// <summary>
    /// Creates a display class for a function's captured local variables.
    /// This is needed when local variables are captured by inner arrow functions.
    /// </summary>
    /// <param name="blockScopeRenames">
    /// When non-null (state-machine contexts whose body is retokenized by <see cref="GeneratorBlockScopeRenamer"/>,
    /// i.e. async functions), makes the DC rename-aware: a write-captured nested-block shadow is registered
    /// under its renamed storage so it does not collide with the outer same-named binding (#838). Pass null
    /// for plain sync functions (their bodies are not retokenized, so the DC must stay name-keyed).
    /// </param>
    private void DefineFunctionDisplayClass(Stmt.Function funcStmt, string qualifiedFunctionName,
        BlockScopeRenameResult? blockScopeRenames = null)
    {
        // Check if this function has local variables that are captured by inner closures
        var capturedLocals = _closures.Analyzer.GetCapturedLocals(funcStmt);
        if (capturedLocals.Count == 0)
            return;

        // Per-iteration `for (let/const …)` loop bindings must NOT share the function
        // display class (a single instance per call): each iteration gets its own
        // binding (ECMA-262 13.7.4), so closures created in different iterations must
        // capture distinct values. Keeping them out of the function DC leaves them as
        // locals / state-machine fields that closures snapshot per iteration — matching
        // the already-correct top-level case (#649).
        var perIterationBindings = _closures.Analyzer.GetPerIterationLoopBindings(funcStmt);
        if (perIterationBindings.Count > 0)
        {
            capturedLocals = new HashSet<string>(capturedLocals);
            capturedLocals.ExceptWith(perIterationBindings);
            if (capturedLocals.Count == 0)
                return;
        }

        // For async functions, exclude variables that are also captured by async arrows.
        // Those use the hoisted field mechanism and would conflict with the function DC.
        if (_closures.AsyncCapturedVarsExclusion.TryGetValue(qualifiedFunctionName, out var exclusions))
        {
            capturedLocals = new HashSet<string>(capturedLocals);
            capturedLocals.ExceptWith(exclusions);
            if (capturedLocals.Count == 0)
                return;
        }

        // #838: in a retokenized state-machine body (async functions), split a write-captured nested-block
        // shadow into its own renamed DC field and record the per-arrow remap for the arrow body resolver.
        if (blockScopeRenames is { } renames && renames.WriteCaptureRenames.Count > 0)
        {
            var renameAware = new HashSet<string>(capturedLocals);
            ApplyWriteCaptureRenames(renameAware, renames);
            capturedLocals = renameAware;
        }

        RegisterFunctionDisplayClass(qualifiedFunctionName, capturedLocals);
    }

    /// <summary>
    /// Creates and registers a function-level display class holding one <c>object</c> field per named
    /// captured variable. Shared by the sync/async path (<see cref="DefineFunctionDisplayClass"/>, which
    /// lifts all captured locals), the generator path (captured-AND-mutated locals, #674), and the
    /// async-method / standalone-arrow path (only the promoted async-written captures, #682). The caller
    /// decides membership; this only builds the type.
    /// </summary>
    private void RegisterFunctionDisplayClass(string qualifiedFunctionName, IEnumerable<string> capturedLocals)
    {
        // Create display class type. The counter guarantees a unique type name; '.' and ':' in the key
        // (async-method keys are "<Class>::<method>") are sanitized to valid identifier characters.
        var displayClass = EmitTypeDefinitions.DefineType(_moduleBuilder,
            $"<>c__FuncDisplayClass_{qualifiedFunctionName.Replace(".", "_").Replace(":", "_")}_{_closures.DisplayClassCounter++}",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object);
        MarkCompilerGenerated(displayClass);

        // Define fields for each captured variable
        var fieldMap = new Dictionary<string, FieldBuilder>();
        foreach (var varName in capturedLocals)
        {
            var field = displayClass.DefineField(varName, _types.Object, FieldAttributes.Public);
            fieldMap[varName] = field;
        }

        // Define default constructor
        var ctor = displayClass.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes);
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIl.Emit(OpCodes.Ret);

        // Store the display class info
        _closures.FunctionDisplayClasses[qualifiedFunctionName] = displayClass;
        _closures.FunctionDisplayClassCtors[qualifiedFunctionName] = ctor;
        _closures.FunctionDisplayClassFields[qualifiedFunctionName] = fieldMap;
    }

    private void EmitFunctionBody(Stmt.Function funcStmt)
    {
        // Get qualified function name (must match what DefineFunction used)
        string qualifiedFunctionName = GetDefinitionContext().GetQualifiedFunctionName(funcStmt.Name.Lexeme);

        // Skip async functions - they use native state machine emission
        if (funcStmt.IsAsync || _async.StateMachines.ContainsKey(qualifiedFunctionName))
            return;

        // Skip generator functions - they use generator state machine emission
        if (funcStmt.IsGenerator || _generators.StateMachines.ContainsKey(qualifiedFunctionName))
            return;

        var methodBuilder = _functions.Builders[qualifiedFunctionName];
        var il = methodBuilder.GetILGenerator();

        // Check if this function has captured locals that need a display class.
        // Derive the captured-local set from the display class's actual field map
        // (not the raw analyzer set) so per-iteration loop bindings excluded by
        // DefineFunctionDisplayClass (#649) are also excluded here — otherwise
        // CapturedFunctionLocals would claim a loop var the DC has no field for.
        var hasFunctionDC = _closures.FunctionDisplayClasses.TryGetValue(qualifiedFunctionName, out var functionDCType);
        var capturedLocals = hasFunctionDC && _closures.FunctionDisplayClassFields.TryGetValue(qualifiedFunctionName, out var hasFuncDCFields)
            ? new HashSet<string>(hasFuncDCFields.Keys)
            : null;

        // Build module-scoped top-level vars so this function only sees its own
        // module's bindings plus global imports. When emitting a namespace member body this
        // also surfaces the enclosing namespace's var/let/const backing fields (#567) — the
        // augmentation now lives in BuildModuleMemberTopLevelStaticVarsForModule so every emission site
        // (state machines, class methods) gets it uniformly, not just this plain-function path.
        Dictionary<string, FieldBuilder>? topLevelVars = BuildModuleMemberTopLevelStaticVarsForModule(_modules.CurrentPath);

        var ctx = CreateModuleMemberContext(il, methodBuilder);
        ctx.FunctionOverloads = _functions.Overloads;
        ctx.AsyncArrowBuilders = _async.ArrowBuilders.Count > 0 ? _async.ArrowBuilders : null;
        // CJS/ESM resolution — needed so require('./literal') and module.exports/exports
        // work inside function bodies nested in a CJS module (e.g. debug's common.js
        // setup() calls require('ms') from inside the exported function).
        ApplyCommonJsModuleAccess(ctx);
        ctx.UnionGenerator = _unionGenerator;
        // Check for function-level "use strict" directive
        ctx.IsStrictMode = _isStrictMode || Parsing.DirectivePrologue.HasUseStrict(funcStmt.Body);
        // Entry-point display class for captured top-level variables. TopLevelStaticVars uses
        // the pre-computed per-function map rather than the module-wide default.
        ApplyCapturedTopLevelVariableAccess(ctx);
        ctx.TopLevelStaticVars = topLevelVars;
        ctx.ArrowEntryPointDCFields = _closures.ArrowEntryPointDCFields.Count > 0 ? _closures.ArrowEntryPointDCFields : null;
        // Function-level display class for captured function-local variables
        ctx.FunctionDisplayClassFields = hasFunctionDC ? _closures.FunctionDisplayClassFields[qualifiedFunctionName] : null;
        ctx.CapturedFunctionLocals = capturedLocals;
        ctx.ArrowFunctionDCFields = _closures.ArrowFunctionDCFields.Count > 0 ? _closures.ArrowFunctionDCFields : null;
        ctx.ArrowScopeDCFields = _closures.ArrowScopeDCFields.Count > 0 ? _closures.ArrowScopeDCFields : null;
        ctx.ArrowScopeDCExtraFieldsByArrow = _arrowScopeDCExtraFields.Count > 0 ? _arrowScopeDCExtraFields : null;
        // Inner function support
        ApplyInnerFunctionSupport(ctx);
        // Typed return type for unboxed return optimization
        ctx.CurrentMethodReturnType = methodBuilder.ReturnType;

        // Create function display class instance if needed
        LocalBuilder? displayLocal = null;
        if (hasFunctionDC && _closures.FunctionDisplayClassCtors.TryGetValue(qualifiedFunctionName, out var functionDCCtor))
        {
            displayLocal = il.DeclareLocal(functionDCType!);
            il.Emit(OpCodes.Newobj, functionDCCtor);
            il.Emit(OpCodes.Stloc, displayLocal);
            ctx.FunctionDisplayClassLocal = displayLocal;
        }

        // Add generic type parameters to context if this is a generic function
        if (_functions.GenericParams.TryGetValue(qualifiedFunctionName, out var genericParams))
        {
            foreach (var gp in genericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        // Define parameters with their types
        var methodParams = methodBuilder.GetParameters();
        for (int i = 0; i < funcStmt.Parameters.Count; i++)
        {
            Type paramType = i < methodParams.Length ? methodParams[i].ParameterType : typeof(object);
            ctx.DefineParameter(funcStmt.Parameters[i].Name.Lexeme, i, paramType);
        }

        var emitter = new ILEmitter(ctx);

        // Top-level functions should always have a body
        if (funcStmt.Body == null)
        {
            throw new CompileException($"Cannot compile function '{funcStmt.Name.Lexeme}' without a body.");
        }

        // Emit default parameter null-checks at the top of the body. OverloadGenerator
        // already emits separate lower-arity methods that forward with defaults, but the
        // $TSFunction.Invoke path (module imports, callback dispatch) always targets the
        // full-arity method with nulls padded in via AdjustArgs. Without this, callers
        // through that path see null for every missing defaulted argument.
        // paramTypes is passed so value-type params (double, bool) are skipped — the
        // null-check pattern only works for reference types.
        var resolvedParamTypes = methodBuilder.GetParameters()
            .Select(p => p.ParameterType)
            .ToArray();
        EmitFunctionEnvironmentPrologue(
            il,
            ctx,
            emitter,
            funcStmt.Parameters,
            funcStmt.Body,
            resolvedParamTypes,
            argumentOffset: 0);

        // Initialize captured parameters into the function display class. Runs
        // AFTER EmitDefaultParameters (which writes defaults back via Starg) so
        // closures see the defaulted value, not the missing-arg padding.
        if (displayLocal != null && capturedLocals != null && _closures.FunctionDisplayClassFields.TryGetValue(qualifiedFunctionName, out var funcDCFieldMap))
        {
            for (int i = 0; i < funcStmt.Parameters.Count; i++)
            {
                var paramName = funcStmt.Parameters[i].Name.Lexeme;
                if (capturedLocals.Contains(paramName) && funcDCFieldMap.TryGetValue(paramName, out var field))
                {
                    il.Emit(OpCodes.Ldloc, displayLocal);
                    il.Emit(OpCodes.Ldarg, i);
                    // Box if the parameter is a value type (numbers are double)
                    Type paramType = i < methodParams.Length ? methodParams[i].ParameterType : typeof(object);
                    if (paramType.IsValueType)
                    {
                        il.Emit(OpCodes.Box, paramType);
                    }
                    il.Emit(OpCodes.Stfld, field);
                }
            }
        }

        // Hoist inner function declarations (create TSFunction locals before other statements)
        EmitInnerFunctionHoisting(il, ctx, funcStmt.Body);

        // Use EmitStatements to handle 'using' declarations with proper try/finally disposal
        emitter.EmitStatements(funcStmt.Body);

        // Finalize any deferred returns from exception blocks
        if (emitter.HasDeferredReturns)
        {
            emitter.FinalizeReturns();
        }
        else
        {
            // Emit appropriate default return value based on return type
            EmitDefaultReturnValue(il, methodBuilder.ReturnType);
            il.Emit(OpCodes.Ret);
        }

    }

    /// <summary>
    /// Returns true if the given statements reference the identifier
    /// <c>arguments</c> anywhere in the AST, descending through nested arrows
    /// (which inherit it) and stopping at nested non-arrow functions (which bind
    /// their own).
    /// </summary>
    /// <remarks>
    /// Used by <see cref="EmitFunctionBody"/> to skip the prologue when the
    /// body never uses <c>arguments</c>. False positives only cost a few IL
    /// instructions per function; false negatives would be a correctness bug,
    /// so we delegate traversal to <see cref="Parsing.Visitors.AstVisitorBase"/>
    /// rather than hand-rolling an incomplete walk.
    /// </remarks>
    private static bool ReferencesArgumentsIdentifier(List<Stmt> stmts)
    {
        // Direct eval can mention `arguments` in a string literal that is absent
        // from the surrounding AST. Stay conservative and create the binding for
        // any function containing eval; static direct-eval lowering then resolves
        // it through the same lexical local as an ordinary source reference.
        var scanner = new Parsing.Visitors.ArgumentsRefScanner(treatEvalReferenceAsUse: true);
        foreach (var s in stmts)
        {
            scanner.Visit(s);
            if (scanner.Found) return true;
        }
        return false;
    }

    private void RegisterArgumentsCapturingMethod(MethodBase method, List<Stmt>? body)
    {
        if (body != null && ReferencesArgumentsIdentifier(body))
            _functions.MethodsCapturingArguments.Add(method);
    }

    /// <summary>
    /// Emits the shared entry environment for an ECMAScript function-like body.
    /// The caller argument snapshot is created before parameter defaults mutate CLR
    /// argument slots; defaults then run in declaration order with later/current
    /// parameters in TDZ. All synchronous function, class, constructor, and named
    /// function-expression paths route through this method.
    /// </summary>
    private void EmitFunctionEnvironmentPrologue(
        ILGenerator il,
        CompilationContext ctx,
        ILEmitter emitter,
        List<Stmt.Parameter> parameters,
        List<Stmt>? body,
        Type[] paramTypes,
        int argumentOffset,
        bool createsArgumentsBinding = true,
        int publishedArgsLeadingSkip = 0)
    {
        if (createsArgumentsBinding && body != null && ReferencesArgumentsIdentifier(body))
        {
            EmitArgumentsLocalPrologueCore(
                il,
                ctx,
                parameters,
                paramTypes,
                argumentOffset,
                publishedArgsLeadingSkip);
        }

        emitter.EmitDefaultParameters(parameters, argumentOffset, paramTypes);
    }

    /// <summary>
    /// Emits a function prologue that binds <c>arguments</c> to a fresh
    /// <c>List&lt;object&gt;</c> holding the boxed declared-parameter values.
    /// The local is registered under the name <c>arguments</c>, so normal
    /// variable resolution picks it up. This is the compiled-mode counterpart
    /// of the <c>SharpTSFunction.Call</c> <c>environment.Define("arguments", ...)</c>
    /// binding in interpreter mode.
    /// </summary>
    /// <param name="publishedArgsLeadingSkip">
    /// Number of leading elements of <c>$TSFunction._currentArguments</c> to skip when
    /// that thread-static is non-null. Non-zero for the function-expression / arrow-
    /// with-<c>__this</c> case where <c>$TSFunction.InvokeWithThis</c> prepends
    /// <c>thisArg</c> to <c>effectiveArgs</c> before calling <c>Invoke</c> — the
    /// prepended slot is a synthetic receiver, not a user-supplied argument, so
    /// <c>arguments</c> must not include it (JS spec).
    /// </param>
    private void EmitArgumentsLocalPrologueCore(
        ILGenerator il,
        CompilationContext ctx,
        List<Stmt.Parameter> parameters,
        Type[] paramTypes,
        int argBase,
        int publishedArgsLeadingSkip = 0)
    {
        var listType = ctx.Types.ListOfObject;
        var argsLocal = il.DeclareLocal(listType);
        var addMethod = ctx.Types.GetMethod(listType, "Add", ctx.Types.Object);

        // Stage 6h: bind `arguments` as a $Arguments : List<object> marker
        // subclass instance so the brand-tagger can return "[object Arguments]"
        // and Array.isArray returns false per ECMA-262 sloppy-arguments spec.
        // The runtime helpers (Castclass List<object>, Isinst List<object>)
        // continue working transparently via inheritance — only the construction
        // sites switch to the marker ctors. The local stays typed as
        // List<object> because that's the lowest-common-denominator type for
        // every code path that reads `arguments`.
        var argsCtorEmpty = ctx.Runtime?.ArgumentsDefaultCtor
            ?? ctx.Types.GetDefaultConstructor(listType);
        var argsCtorEnum = ctx.Runtime?.ArgumentsEnumerableCtor
            ?? ctx.Types.GetConstructor(listType, ctx.Types.IEnumerableOfObject);

        // Fast-path: if $TSFunction._currentArguments is set (we were invoked via
        // $TSFunction.Invoke, which publishes the full caller args before AdjustArgs
        // truncates), rebuild `arguments` from that array so extras past the declared
        // arity are visible — lodash overRest pattern from #64. Otherwise, fall through
        // to the declared-parameter materialization below (covers the direct-call path
        // where arity matches by construction).
        var currentArgsField = ctx.Runtime?.CurrentArgumentsField;
        var useDeclaredParamsLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        if (currentArgsField != null)
        {
            var currentArgsLocal = il.DeclareLocal(ctx.Types.ObjectArray);
            il.Emit(OpCodes.Ldsfld, currentArgsField);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Stloc, currentArgsLocal);
            il.Emit(OpCodes.Brfalse, useDeclaredParamsLabel);

            if (publishedArgsLeadingSkip > 0)
            {
                // Skip the leading synthetic thisArg slot that $TSFunction.InvokeWithThis
                // prepends when the method declares __this as a parameter. Use a manual
                // copy loop rather than Skip/LINQ to keep emitted IL light: allocate
                // the result sized to max(len - skip, 0) and element-copy from `skip`.
                var lenLocal = il.DeclareLocal(ctx.Types.Int32);
                var idxLocal = il.DeclareLocal(ctx.Types.Int32);
                var loopStart = il.DefineLabel();
                var loopEnd = il.DefineLabel();
                var addMethodLocal = ctx.Types.GetMethod(listType, "Add", ctx.Types.Object);

                il.Emit(OpCodes.Ldloc, currentArgsLocal);
                il.Emit(OpCodes.Ldlen);
                il.Emit(OpCodes.Conv_I4);
                il.Emit(OpCodes.Stloc, lenLocal);

                il.Emit(OpCodes.Newobj, argsCtorEmpty);
                il.Emit(OpCodes.Stloc, argsLocal);

                il.Emit(OpCodes.Ldc_I4, publishedArgsLeadingSkip);
                il.Emit(OpCodes.Stloc, idxLocal);

                il.MarkLabel(loopStart);
                il.Emit(OpCodes.Ldloc, idxLocal);
                il.Emit(OpCodes.Ldloc, lenLocal);
                il.Emit(OpCodes.Bge, loopEnd);

                il.Emit(OpCodes.Ldloc, argsLocal);
                il.Emit(OpCodes.Ldloc, currentArgsLocal);
                il.Emit(OpCodes.Ldloc, idxLocal);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Callvirt, addMethodLocal);

                il.Emit(OpCodes.Ldloc, idxLocal);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Add);
                il.Emit(OpCodes.Stloc, idxLocal);
                il.Emit(OpCodes.Br, loopStart);

                il.MarkLabel(loopEnd);
            }
            else
            {
                // arguments = new $Arguments(_currentArguments) — copies the
                // caller's arg array into the marker subclass.
                il.Emit(OpCodes.Ldloc, currentArgsLocal);
                il.Emit(OpCodes.Newobj, argsCtorEnum);
                il.Emit(OpCodes.Stloc, argsLocal);
            }

            // Clear the slot so nested direct calls to other flagged functions don't
            // see stale data — each new Invoke re-sets it, direct calls read null and
            // fall back to their declared params.
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stsfld, currentArgsField);
            il.Emit(OpCodes.Br, doneLabel);
        }

        il.MarkLabel(useDeclaredParamsLabel);
        il.Emit(OpCodes.Newobj, argsCtorEmpty);
        il.Emit(OpCodes.Stloc, argsLocal);

        for (int i = 0; i < parameters.Count; i++)
        {
            var param = parameters[i];
            Type paramType = i < paramTypes.Length ? paramTypes[i] : typeof(object);
            int argIndex = argBase + i;

            if (param.IsRest)
            {
                // Rest params are already collected into a List<T> (or similar
                // IEnumerable) at this arg slot. Spread them into `arguments`
                // via AddRange so each caller-supplied value occupies its own
                // index, matching `arguments` semantics.
                EmitRestParamSpread(il, ctx, listType, argsLocal, argIndex, paramType);
            }
            else
            {
                il.Emit(OpCodes.Ldloc, argsLocal);
                il.Emit(OpCodes.Ldarg, argIndex);
                if (paramType.IsValueType)
                {
                    il.Emit(OpCodes.Box, paramType);
                }
                il.Emit(OpCodes.Callvirt, addMethod);
            }
        }

        il.MarkLabel(doneLabel);

        // Snapshot the JS-visible length on $Arguments after population. The
        // enumerable ctor already does this (sets _length = base.Count), but
        // the declared-params and slow-path branches above call the empty
        // ctor and then push elements via Add — we need to update _length to
        // match the post-population Count. Set it now so subsequent
        // arguments[N] = v writes (which DO extend list.Count) don't move the
        // JS-visible length per ECMA-262 sloppy-arguments spec.
        var argsLengthField = ctx.Runtime?.ArgumentsLengthField;
        if (argsLengthField != null)
        {
            // Only $Arguments has _length — use Isinst to skip the field set
            // when argsLocal happens to be plain List<object> (e.g., during
            // tests where ArgumentsType isn't wired). Defensive; in production
            // the local is always $Arguments-typed.
            var skipLengthSetLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Isinst, ctx.Runtime!.ArgumentsType);
            il.Emit(OpCodes.Brfalse, skipLengthSetLabel);
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Castclass, ctx.Runtime!.ArgumentsType);
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Callvirt, ctx.Types.GetPropertyGetter(ctx.Types.ListOfObject, "Count"));
            il.Emit(OpCodes.Stfld, argsLengthField);
            il.MarkLabel(skipLengthSetLabel);
        }

        ctx.Locals.RegisterLocal("arguments", argsLocal);

        // If a nested arrow captures `arguments`, the closure analyzer declared it
        // as a captured local and a display-class field was allocated for it. Mirror
        // the "initialize captured parameters into the function DC" step (see
        // DefineFunctionBody) so the arrow's display-class read finds the populated
        // List, not the default null. Without this, arrow bodies referencing
        // `arguments` see null.length / null[i] at runtime.
        if (ctx.CapturedFunctionLocals?.Contains("arguments") == true
            && ctx.FunctionDisplayClassLocal != null
            && ctx.FunctionDisplayClassFields?.TryGetValue("arguments", out var argsDCField) == true)
        {
            il.Emit(OpCodes.Ldloc, ctx.FunctionDisplayClassLocal);
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Stfld, argsDCField);
        }
    }

    /// <summary>
    /// Spreads a rest-parameter collection into the <c>arguments</c> list so
    /// its elements occupy distinct indices. Supports the two shapes SharpTS
    /// materializes for rest today: <c>List&lt;object&gt;</c>
    /// (AddRange-compatible with the target list) and typed lists like
    /// <c>List&lt;double&gt;</c> / <c>List&lt;bool&gt;</c> (must iterate and
    /// box each element).
    /// </summary>
    private void EmitRestParamSpread(
        ILGenerator il,
        CompilationContext ctx,
        Type targetListType,
        LocalBuilder argsLocal,
        int argIndex,
        Type paramType)
    {
        // Fast path: parameter is already List<object?> (the common case).
        if (paramType == targetListType)
        {
            var addRange = ctx.Types.GetMethod(
                targetListType,
                "AddRange",
                ctx.Types.IEnumerableOfObject);
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Ldarg, argIndex);
            il.Emit(OpCodes.Callvirt, addRange);
            return;
        }

        // Slow path: typed List<T> (e.g. List<double>). Iterate via the generic
        // enumerator and box each element.
        // Find the element type from the parameter's generic argument.
        if (!paramType.IsGenericType)
        {
            // Unknown shape — skip silently; `arguments` will miss the rest element.
            return;
        }
        var elemType = paramType.GetGenericArguments()[0];
        var addMethod = ctx.Types.GetMethod(targetListType, "Add", ctx.Types.Object);
        var enumerableType = _types.MakeGenericType(typeof(System.Collections.Generic.IEnumerable<>), elemType);
        var enumeratorType = _types.MakeGenericType(typeof(System.Collections.Generic.IEnumerator<>), elemType);
        var getEnumerator = ctx.Types.GetMethodNoParams(enumerableType, "GetEnumerator");
        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        var getCurrent = ctx.Types.GetPropertyGetter(enumeratorType, "Current");

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var enumeratorLocal = il.DeclareLocal(enumeratorType);

        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Callvirt, getEnumerator);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, enumeratorLocal);
        il.Emit(OpCodes.Callvirt, moveNext);
        il.Emit(OpCodes.Brfalse, loopEnd);

        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldloc, enumeratorLocal);
        il.Emit(OpCodes.Callvirt, getCurrent);
        if (elemType.IsValueType)
        {
            il.Emit(OpCodes.Box, elemType);
        }
        il.Emit(OpCodes.Callvirt, addMethod);

        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);
    }

    /// <summary>
    /// Emits the default return value for a given return type.
    /// For reference types: null
    /// For double: 0.0
    /// For bool: false
    /// For void: nothing
    /// </summary>
    private void EmitDefaultReturnValue(ILGenerator il, Type returnType)
    {
        if (returnType == typeof(void))
        {
            // Void functions don't return a value
            return;
        }
        else if (returnType == typeof(double))
        {
            il.Emit(OpCodes.Ldc_R8, 0.0);
        }
        else if (returnType == typeof(bool))
        {
            il.Emit(OpCodes.Ldc_I4_0);
        }
        else if (returnType.IsValueType)
        {
            // For other value types, use default(T)
            var local = il.DeclareLocal(returnType);
            il.Emit(OpCodes.Ldloca, local);
            il.Emit(OpCodes.Initobj, returnType);
            il.Emit(OpCodes.Ldloc, local);
        }
        else if (returnType == typeof(object))
        {
            // ECMA-262: function with no explicit return returns undefined.
            // Emit $Undefined.Instance only for untyped object returns; typed
            // reference returns (specific class types) keep their null default
            // since interpreter treats explicit `T | null` returns as null too.
            il.Emit(OpCodes.Ldsfld, _runtime.UndefinedInstance);
        }
        else
        {
            // Reference types default to null
            il.Emit(OpCodes.Ldnull);
        }
    }

    /// <summary>
    /// Finds a user-defined main() function with the expected signature.
    /// Returns the function, whether it's async, and whether it returns an exit code, or null if no valid main exists.
    /// </summary>
    /// <remarks>
    /// Expected signatures:
    /// - function main(args: string[]): void
    /// - function main(args: string[]): number
    /// - async function main(args: string[]): Promise&lt;void&gt;
    /// - async function main(args: string[]): Promise&lt;number&gt;
    /// </remarks>
    private (Stmt.Function Func, bool IsAsync, bool ReturnsExitCode)? FindMainFunction(List<Stmt> statements)
    {
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Function func && func.Name.Lexeme == "main" && func.Body != null)
            {
                // Hosted/console entry functions may take no parameters or args: string[].
                if (func.Parameters.Count > 1)
                    continue;
                if (func.Parameters.Count == 1 && func.Parameters[0].Type != "string[]")
                    continue;

                // Determine return type:
                // Sync: void, null (implicit void), or number (exit code)
                // Async: Promise<void>, null (implicit Promise<void>), or Promise<number> (exit code)
                if (func.IsAsync)
                {
                    if (func.ReturnType == null || func.ReturnType == "Promise<void>")
                        return (func, true, false);
                    if (func.ReturnType == "Promise<number>")
                        return (func, true, true);
                    continue; // Invalid async return type
                }
                else
                {
                    if (func.ReturnType == null || func.ReturnType == "void")
                        return (func, false, false);
                    if (func.ReturnType == "number")
                        return (func, false, true);
                    continue; // Invalid sync return type
                }
            }
        }
        return null;
    }

    private void EmitEntryPoint(List<Stmt> statements)
    {
        // For EXE target, check if user defined a main() function
        if (_outputTarget == OutputTarget.Exe)
        {
            var mainFunc = FindMainFunction(statements);
            if (mainFunc != null)
            {
                EmitExeEntryPointWithUserMain(statements, mainFunc.Value.Func, mainFunc.Value.IsAsync, mainFunc.Value.ReturnsExitCode);
                return;
            }
        }

        // Default behavior: synthetic Main with top-level statements
        EmitDefaultEntryPoint(statements);
    }

    /// <summary>
    /// Emits, at the top of an entry-point Main, the install of the event-loop
    /// SynchronizationContext so async/await continuations resume on the
    /// event-loop thread instead of escaping to the thread pool (Node
    /// semantics). Must run before the first top-level await — the first awaiter
    /// captures whatever context is current. Standalone-safe: the ctor is in the
    /// emitted assembly and <see cref="System.Threading.SynchronizationContext"/>
    /// is BCL. In-process hosts (the test harness, CompilationService) save and
    /// restore the ambient context around the Main invoke; a real EXE simply
    /// exits, so no restore is emitted here.
    /// </summary>
    private void EmitInstallEventLoopSyncContext(ILGenerator il)
    {
        il.Emit(OpCodes.Newobj, _runtime.EventLoopSyncContextCtor);
        il.Emit(OpCodes.Call, typeof(System.Threading.SynchronizationContext).GetMethod(
            "SetSynchronizationContext",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!);
    }

    /// <summary>
    /// Emits the default entry point where top-level statements run as the program.
    /// Used for DLL target or EXE without user-defined main().
    /// </summary>
    private void EmitDefaultEntryPoint(List<Stmt> statements)
    {
        var mainMethod = _programType.DefineMethod(
            "Main",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes
        );

        _entryPoint = mainMethod;

        var il = mainMethod.GetILGenerator();
        EmitInstallEventLoopSyncContext(il);

        if (_hosted)
        {
            var hostedInitialize = EmitHostedInitializationMethod(
                (hostedIl, hostedMethod) =>
                    EmitSingleFileInitialization(hostedIl, hostedMethod, statements, waitForPromises: false));
            EmitHostedAbi(hostedInitialize);
            il.Emit(OpCodes.Call, hostedInitialize);
        }
        else
        {
            EmitSingleFileInitialization(il, mainMethod, statements, waitForPromises: true);
        }

        // Run the event loop — no-op if no handles are active
        il.Emit(OpCodes.Call, _runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, _runtime.EventLoopRun);
        // Node process lifecycle at natural drain: 'beforeExit' (re-entering
        // the loop when a listener schedules work), then 'exit' (#1080).
        il.Emit(OpCodes.Call, _runtime.ProcessRunLifecycle);

        il.Emit(OpCodes.Ret);
    }

    private void EmitSingleFileInitialization(
        ILGenerator il,
        MethodBuilder owningMethod,
        List<Stmt> statements,
        bool waitForPromises)
    {
        var ctx = CreateEntryPointTopLevelContext(il, owningMethod);
        ctx.PropertyTypes = _typedInterop.PropertyTypes;
        // Program type for GetMethodFromHandle resolution
        ctx.ProgramType = _programType;

        // Create entry-point display class instance if there are captured top-level variables
        if (_closures.EntryPointDisplayClass != null && _closures.EntryPointDisplayClassCtor != null)
        {
            // Create instance and store in both local variable and static field
            var displayLocal = il.DeclareLocal(_closures.EntryPointDisplayClass);
            il.Emit(OpCodes.Newobj, _closures.EntryPointDisplayClassCtor);
            il.Emit(OpCodes.Dup); // Keep copy for static field
            il.Emit(OpCodes.Stloc, displayLocal);
            if (_closures.EntryPointDisplayClassStaticField != null)
            {
                il.Emit(OpCodes.Stsfld, _closures.EntryPointDisplayClassStaticField);
            }
            else
            {
                il.Emit(OpCodes.Pop);
            }
            ctx.EntryPointDisplayClassLocal = displayLocal;
        }

        EmitInitializeHoistedVars(il, ctx, statements);

        // Initialize namespace static fields before any code that might reference them
        InitializeNamespaceFields(il);

        var emitter = new ILEmitter(ctx);

        foreach (var stmt in statements)
        {
            // Skip class, function, interface, and enum declarations (already handled)
            // Note: Namespace statements are NOT skipped - they need to emit member storage
            if (stmt is Stmt.Class classDecl)
            {
                emitter.EmitStatement(classDecl);
                // Emit runtime decorator execution if decorators are present
                if (_decoratorMode != DecoratorMode.None && HasAnyRuntimeDecorators(classDecl))
                {
                    EmitRuntimeDecorators(classDecl, emitter, il);
                }
                continue;
            }
            if (stmt is Stmt.Function or Stmt.Interface or Stmt.Enum)
            {
                continue;
            }

            // An exported class (`@dec export class`) is wrapped in Stmt.Export, so the
            // bare-Stmt.Class branch above never sees it and its runtime decorators would
            // be dropped in compiled mode (issue #1192). Emit the export's own logic
            // (EmitStatement — module-export storage in module mode, a no-op in script
            // mode, exactly as before) and then the wrapped class's runtime decorators,
            // mirroring the bare-class branch.
            if (stmt is Stmt.Export { Declaration: Stmt.Class exportedClass })
            {
                emitter.EmitStatement(stmt);
                if (_decoratorMode != DecoratorMode.None && HasAnyRuntimeDecorators(exportedClass))
                {
                    EmitRuntimeDecorators(exportedClass, emitter, il);
                }
                continue;
            }

            // Special handling for expression statements to wait for top-level async
            // calls — "top-level await" behavior. Shared with the module/script init
            // bodies so every entry point pumps the loop the same way (see the remarks
            // on EmitExpressionWithAsyncWait for why this must pump, not block).
            if (stmt is Stmt.Expression exprStmt)
            {
                if (waitForPromises)
                    EmitExpressionWithAsyncWait(il, emitter, exprStmt);
                else
                    EmitHostedExpression(il, emitter, exprStmt);
            }
            else
            {
                emitter.EmitStatement(stmt);
            }
        }

    }

    /// <summary>
    /// Initializes script-level <c>var</c> bindings to the JavaScript undefined
    /// sentinel before executing any statement. CLR fields otherwise start as
    /// null, which is observably different for reads before the declaration.
    /// </summary>
    private void EmitInitializeHoistedVars(
        ILGenerator il,
        CompilationContext ctx,
        IEnumerable<Stmt> statements)
    {
        static IEnumerable<Stmt.Var> HoistedVars(Stmt statement)
        {
            switch (statement)
            {
                case Stmt.Var { IsVar: true } variable:
                    yield return variable;
                    break;
                case Stmt.Sequence sequence:
                    foreach (var nested in sequence.Statements)
                        foreach (var variable in HoistedVars(nested))
                            yield return variable;
                    break;
                case Stmt.Export { Declaration: { } declaration }:
                    foreach (var variable in HoistedVars(declaration))
                        yield return variable;
                    break;
            }
        }

        foreach (var variable in statements.SelectMany(HoistedVars).DistinctBy(v => v.Name.Lexeme))
        {
            if (ctx.EntryPointDisplayClassLocal != null &&
                ctx.EntryPointDisplayClassFields?.TryGetValue(variable.Name.Lexeme, out var displayField) == true)
            {
                il.Emit(OpCodes.Ldloc, ctx.EntryPointDisplayClassLocal);
                il.Emit(OpCodes.Ldsfld, _runtime.UndefinedInstance);
                il.Emit(OpCodes.Stfld, displayField);
            }
            else if (ctx.TopLevelStaticVars?.TryGetValue(variable.Name.Lexeme, out var staticField) == true)
            {
                il.Emit(OpCodes.Ldsfld, _runtime.UndefinedInstance);
                il.Emit(OpCodes.Stsfld, staticField);
            }
        }
    }

    /// <summary>
    /// Emits an entry point that calls the user's main(args) function.
    /// Used for EXE target when a valid main() function is defined.
    /// </summary>
    private void EmitExeEntryPointWithUserMain(List<Stmt> statements, Stmt.Function mainFunc, bool isAsync, bool returnsExitCode)
    {
        // PE entry point must return void (or int for exit code)
        // For async main, we create a void Main that awaits the async main
        var mainMethod = _programType.DefineMethod(
            "Main",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.StringArray]  // Accept string[] args from .NET runtime
        );

        _entryPoint = mainMethod;

        var il = mainMethod.GetILGenerator();
        EmitInstallEventLoopSyncContext(il);
        var ctx = CreateEntryPointTopLevelContext(il, mainMethod);

        // Create entry-point display class instance if there are captured top-level variables
        if (_closures.EntryPointDisplayClass != null && _closures.EntryPointDisplayClassCtor != null)
        {
            // Create instance and store in both local variable and static field
            var displayLocal = il.DeclareLocal(_closures.EntryPointDisplayClass);
            il.Emit(OpCodes.Newobj, _closures.EntryPointDisplayClassCtor);
            il.Emit(OpCodes.Dup); // Keep copy for static field
            il.Emit(OpCodes.Stloc, displayLocal);
            if (_closures.EntryPointDisplayClassStaticField != null)
            {
                il.Emit(OpCodes.Stsfld, _closures.EntryPointDisplayClassStaticField);
            }
            else
            {
                il.Emit(OpCodes.Pop);
            }
            ctx.EntryPointDisplayClassLocal = displayLocal;
        }

        EmitInitializeHoistedVars(il, ctx, statements);

        // Initialize namespace static fields before any code
        InitializeNamespaceFields(il);

        var emitter = new ILEmitter(ctx);

        // Execute top-level statements (module initialization), excluding the main function
        foreach (var stmt in statements)
        {
            // Class declarations still have runtime definition work (static
            // elements and computed keys) at this exact source position.
            if (stmt is Stmt.Class classDecl)
            {
                emitter.EmitStatement(classDecl);
                continue;
            }

            // Skip the remaining declarations (handled in earlier phases), including main().
            if (stmt is Stmt.Function or Stmt.Interface or Stmt.Enum)
            {
                continue;
            }

            // Run top-level code (imports, variable initialization, etc.)
            if (stmt is Stmt.Expression exprStmt)
            {
                emitter.EmitExpression(exprStmt.Expr);

                // Check for async calls and wait for them
                // Box value types first (e.g., delete returns boolean)
                emitter.Helpers.EnsureBoxed();
                var exprResult = il.DeclareLocal(_types.Object);
                il.Emit(OpCodes.Stloc, exprResult);

                var notTaskLabel = il.DefineLabel();
                var doneLabel = il.DefineLabel();

                il.Emit(OpCodes.Ldloc, exprResult);
                il.Emit(OpCodes.Isinst, _types.TaskOfObject);
                il.Emit(OpCodes.Brfalse, notTaskLabel);

                // Wait via $EventLoop.WaitForTask (see single-file entry point):
                // blocks while timers/handles/callbacks could settle the task,
                // checks cancellation, and bails out (false) on a never-settling
                // task instead of hanging the process.
                il.Emit(OpCodes.Ldloc, exprResult);
                il.Emit(OpCodes.Castclass, _types.TaskOfObject);
                var taskLocal2 = il.DeclareLocal(_types.TaskOfObject);
                il.Emit(OpCodes.Stloc, taskLocal2);

                il.Emit(OpCodes.Call, _runtime.EventLoopGetInstance);
                il.Emit(OpCodes.Ldloc, taskLocal2);
                il.Emit(OpCodes.Callvirt, _runtime.EventLoopWaitForTask);
                il.Emit(OpCodes.Brfalse, notTaskLabel);

                // Task is complete — GetResult() to rethrow if faulted
                il.Emit(OpCodes.Ldloc, taskLocal2);
                var getAwaiter = _types.GetMethodNoParams(_types.TaskOfObject, "GetAwaiter");
                il.Emit(OpCodes.Call, getAwaiter);
                var awaiterLocal = il.DeclareLocal(_types.TaskAwaiterOfObject);
                il.Emit(OpCodes.Stloc, awaiterLocal);
                il.Emit(OpCodes.Ldloca, awaiterLocal);
                var getResult = _types.GetMethodNoParams(_types.TaskAwaiterOfObject, "GetResult");
                il.Emit(OpCodes.Call, getResult);
                il.Emit(OpCodes.Pop);

                il.MarkLabel(notTaskLabel);
                // No pop needed - value is in local

                il.MarkLabel(doneLabel);
            }
            else
            {
                emitter.EmitStatement(stmt);
            }
        }

        // Now call the user's main(args) function
        // Load args only when the guest main declares it.
        if (mainFunc.Parameters.Count == 1)
            il.Emit(OpCodes.Ldarg_0);

        // Call the user's main function
        var userMainMethod = _functions.Builders[mainFunc.Name.Lexeme];
        il.Emit(OpCodes.Call, userMainMethod);

        if (isAsync)
        {
            // Async main returns Task<object> — wait via $EventLoop.WaitForTask
            // (fires timers, checks cancellation, escapes if main's task can
            // never settle because the process is quiescent — a deadlocked main
            // shouldn't hang the program forever).
            il.Emit(OpCodes.Castclass, _types.TaskOfObject);
            var asyncMainTask = il.DeclareLocal(_types.TaskOfObject);
            il.Emit(OpCodes.Stloc, asyncMainTask);

            var skipMainResult = il.DefineLabel();
            il.Emit(OpCodes.Call, _runtime.EventLoopGetInstance);
            il.Emit(OpCodes.Ldloc, asyncMainTask);
            il.Emit(OpCodes.Callvirt, _runtime.EventLoopWaitForTask);
            il.Emit(OpCodes.Brfalse, skipMainResult);

            il.Emit(OpCodes.Ldloc, asyncMainTask);
            var getAwaiter = _types.GetMethodNoParams(_types.TaskOfObject, "GetAwaiter");
            il.Emit(OpCodes.Call, getAwaiter);
            var awaiterLocal = il.DeclareLocal(_types.TaskAwaiterOfObject);
            il.Emit(OpCodes.Stloc, awaiterLocal);
            il.Emit(OpCodes.Ldloca, awaiterLocal);
            var getResult = _types.GetMethodNoParams(_types.TaskAwaiterOfObject, "GetResult");
            il.Emit(OpCodes.Call, getResult);

            if (returnsExitCode)
            {
                // Unbox double, convert to int, call Environment.Exit
                il.Emit(OpCodes.Unbox_Any, _types.Double);
                il.Emit(OpCodes.Conv_I4);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.Environment, "Exit", _types.Int32));
            }
            else
            {
                il.Emit(OpCodes.Pop);  // Discard the result
            }
            il.MarkLabel(skipMainResult);
            // Run the event loop — no-op if no handles are active
            il.Emit(OpCodes.Call, _runtime.EventLoopGetInstance);
            il.Emit(OpCodes.Call, _runtime.EventLoopRun);
            // Node process lifecycle at natural drain: 'beforeExit' (re-entering
            // the loop when a listener schedules work), then 'exit' (#1080).
            il.Emit(OpCodes.Call, _runtime.ProcessRunLifecycle);
            il.Emit(OpCodes.Ret);
        }
        else
        {
            if (returnsExitCode)
            {
                // Unbox double, convert to int, call Environment.Exit
                il.Emit(OpCodes.Unbox_Any, _types.Double);
                il.Emit(OpCodes.Conv_I4);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.Environment, "Exit", _types.Int32));
            }
            else
            {
                // Sync main returns object, but we expect void behavior - just pop
                il.Emit(OpCodes.Pop);
            }
            // Run the event loop — no-op if no handles are active
            il.Emit(OpCodes.Call, _runtime.EventLoopGetInstance);
            il.Emit(OpCodes.Call, _runtime.EventLoopRun);
            // Node process lifecycle at natural drain: 'beforeExit' (re-entering
            // the loop when a listener schedules work), then 'exit' (#1080).
            il.Emit(OpCodes.Call, _runtime.ProcessRunLifecycle);
            il.Emit(OpCodes.Ret);
        }

    }

    /// <summary>
    /// Resolves a constraint type name to a .NET Type.
    /// </summary>
    private Type ResolveConstraintType(string constraint)
    {
        // Check class builders first
        if (_classes.Builders.TryGetValue(constraint, out var tb))
            return tb;

        // Delegate primitive resolution to centralized mappings
        return PrimitiveTypeMappings.StringToClrType.GetValueOrDefault(constraint, typeof(object));
    }

    /// <summary>
    /// Emits forwarding bodies for function overloads.
    /// Must be called after EmitFunctionBody so the full method is available.
    /// </summary>
    private void EmitFunctionOverloads(Stmt.Function funcStmt)
    {
        string qualifiedFunctionName = GetDefinitionContext().GetQualifiedFunctionName(funcStmt.Name.Lexeme);

        // Skip if no overloads were generated
        if (!_functions.Overloads.TryGetValue(qualifiedFunctionName, out var overloads) || overloads.Count == 0)
            return;

        var fullMethod = _functions.Builders[qualifiedFunctionName];

        // For each overload, emit a forwarding body that calls the full method
        int overloadIndex = 0;
        for (int arity = funcStmt.Parameters.Count - 1; arity >= GetFirstDefaultIndex(funcStmt.Parameters); arity--)
        {
            var overload = overloads[overloadIndex++];
            var il = overload.GetILGenerator();

            // Create a minimal context just for emitting default value expressions
            var ctx = CreateOverloadDefaultsContext(il, _isStrictMode || Parsing.DirectivePrologue.HasUseStrict(funcStmt.Body));
            var emitter = new ILEmitter(ctx);

            // Make the provided parameters resolvable so a default value that references an
            // earlier parameter (e.g. `function f(a, b = a) {}`) emits `ldarg` instead of
            // throwing "Undefined variable" at runtime. Static functions have no implicit
            // `this`, so parameter i lives at arg index i. (#698)
            var fullParams = fullMethod.GetParameters();
            for (int i = 0; i < arity; i++)
            {
                Type paramType = i < fullParams.Length ? fullParams[i].ParameterType : _types.Object;
                ctx.DefineParameter(funcStmt.Parameters[i].Name.Lexeme, i, paramType);
            }

            // Cascade: forward to the overload one arity higher (it fills the next default), or to
            // the full implementation when this overload is one below full arity. Higher-arity
            // overloads come earlier in the list, so the next arity up is overloads[overloadIndex-1].
            // This lets a later default reference an earlier *defaulted* parameter — that parameter
            // is a real argument of the target method rather than a transient stack value. (#698)
            MethodInfo targetMethod = overloadIndex == 1 ? fullMethod : overloads[overloadIndex - 2];

            OverloadGenerator.EmitOverloadBody(
                il,
                targetMethod,
                funcStmt.Parameters,
                arity,
                isStatic: true,
                emitter
            );
        }
    }

    /// <summary>
    /// Marks a user TypeScript function method with the <c>$PadUndefined</c> attribute so that
    /// <c>$TSFunction.AdjustArgs</c> pads omitted trailing arguments with the <c>undefined</c>
    /// sentinel (JS semantics) when the function is invoked as a value (cross-module imports,
    /// callbacks, <c>$TSFunction.Invoke</c>) — matching the direct-call path. Runtime built-ins
    /// stay unmarked and keep CLR-null padding. No-op when the runtime attribute is unavailable. (#640)
    /// </summary>
    internal void MarkPadsUndefined(MethodBuilder method)
    {
        if (_runtime?.PadUndefinedAttrCtor != null)
            method.SetCustomAttribute(
                _runtime.PadUndefinedAttrCtor, CustomAttributeEncoder.EmptyBlob);
    }

    /// <summary>
    /// Records the ECMAScript <c>Function.length</c> for a user method. Arity counts formal
    /// parameters only up to the first default initializer and excludes the rest parameter.
    /// TypeScript optional markers are erased syntax, so those parameters still count.
    /// </summary>
    internal void MarkFunctionLength(MethodBuilder method, IReadOnlyList<Stmt.Parameter> parameters)
    {
        if (_runtime?.FunctionLengthAttrCtor == null)
            return;

        int length = 0;
        foreach (var parameter in parameters)
        {
            if (parameter.DefaultValue != null || parameter.IsRest)
                break;
            length++;
        }

        method.SetCustomAttribute(
            _runtime.FunctionLengthAttrCtor,
            CustomAttributeEncoder.Encode(_runtime.FunctionLengthAttrCtor, length));
    }

    /// <summary>
    /// Marks a method whose first emitted parameter is the synthetic <c>__this</c> receiver slot
    /// (a user function expression or <c>this</c>-bearing arrow) with the <c>$ExpectsThis</c>
    /// attribute, so <c>$TSFunction</c> can detect that slot via <c>IsDefined</c> instead of the
    /// parameter name. That keeps the check independent of the <c>Param</c> table: a
    /// <c>--ref-asm</c> rewrite that mis-resolved a method's parameter list would otherwise shift
    /// value-call arguments by one. No-op when the runtime attribute is unavailable. (#738)
    /// </summary>
    internal void MarkExpectsThis(MethodBuilder method)
    {
        if (_runtime?.ExpectsThisAttrCtor != null)
            method.SetCustomAttribute(
                _runtime.ExpectsThisAttrCtor, CustomAttributeEncoder.EmptyBlob);
    }

    /// <summary>
    /// Gets the index of the first parameter with a default value.
    /// Returns -1 if no default parameters exist.
    /// </summary>
    private static int GetFirstDefaultIndex(List<Stmt.Parameter> parameters)
    {
        for (int i = 0; i < parameters.Count; i++)
        {
            if (parameters[i].DefaultValue != null)
                return i;
        }
        return -1;
    }
}
