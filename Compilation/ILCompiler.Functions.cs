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
            // Record mapping for Phase-7 state-machine emission (see _functionDefinitionModule).
            if (_modules.CurrentPath != null)
                _functionDefinitionModule[funcStmt.Name.Lexeme] = _modules.CurrentPath;
            DefineAsyncGeneratorFunction(funcStmt);
            return;
        }

        // Check if this is an async function - use native IL state machine
        if (funcStmt.IsAsync)
        {
            if (_modules.CurrentPath != null)
                _functionDefinitionModule[funcStmt.Name.Lexeme] = _modules.CurrentPath;
            DefineAsyncFunction(funcStmt);
            return;
        }

        // Check if this is a generator function - use generator state machine
        if (funcStmt.IsGenerator)
        {
            if (_modules.CurrentPath != null)
                _functionDefinitionModule[funcStmt.Name.Lexeme] = _modules.CurrentPath;
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
            funcStmt.Parameters, _typeMapper, funcType);

        // Resolve typed return type (optimization: avoid boxing for : number returns)
        Type returnType = ParameterTypeResolver.ResolveReturnType(
            funcType?.ReturnType, isAsync: false, _typeMapper);

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
                        genericParams[i].SetBaseTypeConstraint(constraintType);
                }
            }

            _functions.GenericParams[qualifiedFunctionName] = genericParams;
        }

        _functions.Builders[qualifiedFunctionName] = methodBuilder;

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
                    new System.Reflection.Emit.CustomAttributeBuilder(_runtime.CapturesArgumentsAttrCtor, []));
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
    /// Creates a display class for a function's captured local variables.
    /// This is needed when local variables are captured by inner arrow functions.
    /// </summary>
    private void DefineFunctionDisplayClass(Stmt.Function funcStmt, string qualifiedFunctionName)
    {
        // Check if this function has local variables that are captured by inner closures
        var capturedLocals = _closures.Analyzer.GetCapturedLocals(funcStmt);
        if (capturedLocals.Count == 0)
            return;

        // For async functions, exclude variables that are also captured by async arrows.
        // Those use the hoisted field mechanism and would conflict with the function DC.
        if (_closures.AsyncCapturedVarsExclusion.TryGetValue(qualifiedFunctionName, out var exclusions))
        {
            capturedLocals = new HashSet<string>(capturedLocals);
            capturedLocals.ExceptWith(exclusions);
            if (capturedLocals.Count == 0)
                return;
        }

        // Create display class type
        var displayClass = _moduleBuilder.DefineType(
            $"<>c__FuncDisplayClass_{qualifiedFunctionName.Replace(".", "_")}_{_closures.DisplayClassCounter++}",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object);

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

        // Check if this function has captured locals that need a display class
        var hasFunctionDC = _closures.FunctionDisplayClasses.TryGetValue(qualifiedFunctionName, out var functionDCType);
        var capturedLocals = hasFunctionDC ? _closures.Analyzer.GetCapturedLocals(funcStmt) : null;

        // Build module-scoped top-level vars so this function only sees its own
        // module's bindings plus global imports.
        Dictionary<string, FieldBuilder>? topLevelVars = BuildTopLevelStaticVarsForModule(_modules.CurrentPath);

        var ctx = new CompilationContext(il, _typeMapper, _functions.Builders, _classes.Builders, _types)
        {
            ClosureAnalyzer = _closures.Analyzer,
            ArrowMethods = _closures.ArrowMethods,
            ConstArrowBindings = _closures.ConstArrowBindings,
            DisplayClasses = _closures.DisplayClasses,
            DisplayClassFields = _closures.DisplayClassFields,
            DisplayClassConstructors = _closures.DisplayClassConstructors,
            FunctionRestParams = _functions.RestParams,
            FunctionOverloads = _functions.Overloads,
            FunctionsCapturingArguments = _functions.CapturingArguments,
            EnumMembers = _enums.Members,
            EnumReverse = _enums.Reverse,
            EnumKinds = _enums.Kinds,
            Runtime = _runtime,
            FunctionGenericParams = _functions.GenericParams,
            IsGenericFunction = _functions.IsGeneric,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            AsyncMethods = null,
            AsyncArrowBuilders = _async.ArrowBuilders.Count > 0 ? _async.ArrowBuilders : null,
            TopLevelStaticVars = topLevelVars,
            // Module support for multi-module compilation
            CurrentModulePath = _modules.CurrentPath,
            ClassToModule = _modules.ClassToModule,
            FunctionToModule = _modules.FunctionToModule,
            EnumToModule = _modules.EnumToModule,
            DotNetNamespace = _modules.CurrentDotNetNamespace,
            // CJS/ESM resolution — needed so require('./literal') and module.exports/exports
            // work inside function bodies nested in a CJS module (e.g. debug's common.js
            // setup() calls require('ms') from inside the exported function).
            ModuleResolver = _modules.Resolver,
            ModuleExportFields = _modules.ExportFields,
            ModuleInitMethods = _modules.InitMethods,
            ModuleImportFields = _modules.ImportFields,
            ModuleTypes = _modules.Types,
            CommonJsExportFields = _modules.CommonJsExportFields,
            CommonJsGetExportsMethods = _modules.CommonJsGetExportsMethods,
            CurrentCjsExportsField = _modules.CurrentPath != null
                && _modules.CommonJsExportFields.TryGetValue(_modules.CurrentPath, out var cjsExports)
                ? cjsExports
                : null,
            TypeEmitterRegistry = _typeEmitterRegistry,
            BuiltInModuleEmitterRegistry = _builtInModuleEmitterRegistry,
            BuiltInModuleNamespaces = _builtInModuleNamespaces,
            BuiltInModuleMethodBindings = GetCurrentBuiltInMethodBindings(),
            ImportedNames = _importedNames,
            ClassExprBuilders = _classExprs.Builders,
            UnionGenerator = _unionGenerator,
            // Check for function-level "use strict" directive
            IsStrictMode = _isStrictMode || CheckForUseStrict(funcStmt.Body),
            // Registry services
            ClassRegistry = GetClassRegistry(),
            // Entry-point display class for captured top-level variables
            EntryPointDisplayClassFields = BuildEntryPointDisplayClassFieldsForModule(_modules.CurrentPath),
            CapturedTopLevelVars = BuildCapturedTopLevelVarsForModule(_modules.CurrentPath),
            EntryPointDisplayClassStaticField = _closures.EntryPointDisplayClassStaticField,
            ArrowEntryPointDCFields = _closures.ArrowEntryPointDCFields.Count > 0 ? _closures.ArrowEntryPointDCFields : null,
            // Function-level display class for captured function-local variables
            FunctionDisplayClassFields = hasFunctionDC ? _closures.FunctionDisplayClassFields[qualifiedFunctionName] : null,
            CapturedFunctionLocals = capturedLocals,
            ArrowFunctionDCFields = _closures.ArrowFunctionDCFields.Count > 0 ? _closures.ArrowFunctionDCFields : null,
            ArrowScopeDCFields = _closures.ArrowScopeDCFields.Count > 0 ? _closures.ArrowScopeDCFields : null,
            // Inner function support
            InnerFunctionMethods = _innerFunctionMethods,
            InnerFunctionDisplayClasses = _innerFunctionDisplayClasses,
            InnerFunctionDCFields = _innerFunctionDCFields,
            InnerFunctionDCCtors = _innerFunctionDCCtors,
            InnerFunctionEntryPointDCFields = _innerFunctionEntryPointDCFields,
            InnerFunctionFunctionDCFields = _innerFunctionFunctionDCFields,
            // Typed return type for unboxed return optimization
            CurrentMethodReturnType = methodBuilder.ReturnType
        };

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

        // Initialize captured parameters into the function display class
        // This must happen after parameter definitions so we have correct indices
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
        emitter.EmitDefaultParameters(
            funcStmt.Parameters,
            isInstanceMethod: false,
            hasOwnThis: false,
            paramTypes: resolvedParamTypes);

        // If the body references `arguments`, materialize the JS-spec array-like
        // from the declared parameters and bind it as a local. Arrow functions
        // do NOT bind `arguments` (they inherit lexically), so we only do this
        // for real function declarations — which is exactly what this method
        // emits (see Phase6_EmitArrowAndStateMachineBodies for arrows).
        if (ReferencesArgumentsIdentifier(funcStmt.Body))
        {
            EmitArgumentsLocalPrologue(il, ctx, funcStmt.Parameters, resolvedParamTypes);
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

        ILLabelValidator.Validate(il, $"function {qualifiedFunctionName}");
    }

    /// <summary>
    /// Returns true if the given statements reference the identifier
    /// <c>arguments</c> anywhere in the AST (including nested arrow/function
    /// bodies — since a nested arrow's <c>arguments</c> refers to the
    /// enclosing non-arrow function's bindings per JS spec).
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
        var scanner = new ArgumentsReferenceScanner();
        foreach (var s in stmts)
        {
            scanner.Visit(s);
            if (scanner.Found) return true;
        }
        return false;
    }

    private sealed class ArgumentsReferenceScanner : Parsing.Visitors.AstVisitorBase
    {
        public bool Found { get; private set; }

        protected override void VisitVariable(Expr.Variable expr)
        {
            if (expr.Name.Lexeme == "arguments")
            {
                Found = true;
                ShouldContinue = false;
            }
        }
    }

    /// <summary>
    /// Emits a function prologue that binds <c>arguments</c> to a fresh
    /// <c>List&lt;object&gt;</c> holding the boxed declared-parameter values.
    /// The local is registered under the name <c>arguments</c>, so normal
    /// variable resolution picks it up. This is the compiled-mode counterpart
    /// of the <c>SharpTSFunction.Call</c> <c>environment.Define("arguments", ...)</c>
    /// binding in interpreter mode.
    /// </summary>
    /// <remarks>
    /// The list is populated from declared parameters only — callers that pass
    /// more arguments than the function declares see those extras silently
    /// dropped today (a pre-existing calling-convention limitation). Rest
    /// parameters are inserted as a single List element per the current signature.
    /// Real codebases already prefer <c>...rest</c> over <c>arguments</c>; this
    /// covers the legacy-syntax compat case without adding a second call path.
    /// </remarks>
    private void EmitArgumentsLocalPrologue(
        ILGenerator il,
        CompilationContext ctx,
        List<Stmt.Parameter> parameters,
        Type[] paramTypes)
        => EmitArgumentsLocalPrologueCore(il, ctx, parameters, paramTypes, argBase: 0, publishedArgsLeadingSkip: 0);

    /// <summary>
    /// Instance-method variant: `this` occupies arg slot 0, so parameters start
    /// at slot 1. Same semantics as <see cref="EmitArgumentsLocalPrologue"/>.
    /// </summary>
    private void EmitArgumentsLocalPrologueForInstanceMethod(
        ILGenerator il,
        CompilationContext ctx,
        List<Stmt.Parameter> parameters,
        Type[] paramTypes)
        // Instance methods receive `this` as the MethodInfo.Invoke target, not in the args
        // array, so _currentArguments published by $TSFunction.Invoke has no leading skip.
        => EmitArgumentsLocalPrologueCore(il, ctx, parameters, paramTypes, argBase: 1, publishedArgsLeadingSkip: 0);

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
            ?? (System.Reflection.ConstructorInfo)listType.GetConstructor(Type.EmptyTypes)!;
        var argsCtorEnum = ctx.Runtime?.ArgumentsEnumerableCtor
            ?? (System.Reflection.ConstructorInfo)listType.GetConstructor([ctx.Types.IEnumerableOfObject])!;

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
            var addRange = targetListType.GetMethod("AddRange", [ctx.Types.IEnumerableOfObject])!;
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
        var enumerableType = typeof(System.Collections.Generic.IEnumerable<>).MakeGenericType(elemType);
        var enumeratorType = typeof(System.Collections.Generic.IEnumerator<>).MakeGenericType(elemType);
        var getEnumerator = enumerableType.GetMethod("GetEnumerator")!;
        var moveNext = typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!;
        var getCurrent = enumeratorType.GetProperty("Current")!.GetGetMethod()!;

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
                // Validate signature: exactly one parameter (args: string[])
                if (func.Parameters.Count != 1)
                    continue;

                var param = func.Parameters[0];
                // Parameter should be named 'args' with type 'string[]'
                if (param.Type != "string[]")
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
        var ctx = new CompilationContext(il, _typeMapper, _functions.Builders, _classes.Builders, _types)
        {
            ClosureAnalyzer = _closures.Analyzer,
            ArrowMethods = _closures.ArrowMethods,
            ConstArrowBindings = _closures.ConstArrowBindings,
            DisplayClasses = _closures.DisplayClasses,
            DisplayClassFields = _closures.DisplayClassFields,
            DisplayClassConstructors = _closures.DisplayClassConstructors,
            ClassExprBuilders = _classExprs.Builders,
            FunctionRestParams = _functions.RestParams,
            FunctionOverloads = _functions.Overloads,
            FunctionsCapturingArguments = _functions.CapturingArguments,
            EnumMembers = _enums.Members,
            EnumReverse = _enums.Reverse,
            EnumKinds = _enums.Kinds,
            NamespaceFields = _namespaceFields,
            TopLevelStaticVars = BuildTopLevelStaticVarsForModule(_modules.CurrentPath),
            Runtime = _runtime,
            FunctionGenericParams = _functions.GenericParams,
            IsGenericFunction = _functions.IsGeneric,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            AsyncMethods = null,
            AsyncArrowBuilders = _async.ArrowBuilders.Count > 0 ? _async.ArrowBuilders : null,
            DotNetNamespace = _modules.CurrentDotNetNamespace,
            TypeEmitterRegistry = _typeEmitterRegistry,
            BuiltInModuleEmitterRegistry = _builtInModuleEmitterRegistry,
            BuiltInModuleNamespaces = _builtInModuleNamespaces,
            BuiltInModuleMethodBindings = GetCurrentBuiltInMethodBindings(),
            ImportedNames = _importedNames,
            // Class expression support
            VarToClassExpr = _classExprs.VarToClassExpr,
            ClassExprStaticFields = _classExprs.StaticFields,
            ClassExprStaticMethods = _classExprs.StaticMethods,
            ClassExprConstructors = _classExprs.Constructors,
            ClassExprSuperclass = _classExprs.Superclass,
            UnionGenerator = _unionGenerator,
            PropertyTypes = _typedInterop.PropertyTypes,
            IsStrictMode = _isStrictMode,
            // Registry services
            ClassRegistry = GetClassRegistry(),
            // Entry-point display class for captured top-level variables
            EntryPointDisplayClass = _closures.EntryPointDisplayClass,
            EntryPointDisplayClassCtor = _closures.EntryPointDisplayClassCtor,
            EntryPointDisplayClassFields = BuildEntryPointDisplayClassFieldsForModule(_modules.CurrentPath),
            CapturedTopLevelVars = BuildCapturedTopLevelVarsForModule(_modules.CurrentPath),
            ArrowEntryPointDCFields = _closures.ArrowEntryPointDCFields.Count > 0 ? _closures.ArrowEntryPointDCFields : null,
            EntryPointDisplayClassStaticField = _closures.EntryPointDisplayClassStaticField,
            // Program type for GetMethodFromHandle resolution
            ProgramType = _programType
        };

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

        // Initialize namespace static fields before any code that might reference them
        InitializeNamespaceFields(il);

        // Trigger static constructors for classes with static blocks
        // In JavaScript/TypeScript, static blocks run when the class is defined.
        // In .NET, static constructors are lazy, so we force them to run here.
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Class classStmt && classStmt.StaticInitializers?.Count > 0)
            {
                string className = _modules.CurrentDotNetNamespace != null
                    ? $"{_modules.CurrentDotNetNamespace}.{classStmt.Name.Lexeme}"
                    : classStmt.Name.Lexeme;
                if (_classes.Builders.TryGetValue(className, out var classBuilder))
                {
                    // Emit: RuntimeHelpers.RunClassConstructor(typeof(ClassName).TypeHandle)
                    il.Emit(OpCodes.Ldtoken, classBuilder);
                    il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
                    il.Emit(OpCodes.Callvirt, typeof(Type).GetProperty("TypeHandle")!.GetGetMethod()!);
                    il.Emit(OpCodes.Call, _types.RuntimeHelpersRunClassConstructor);
                }
            }
        }

        var emitter = new ILEmitter(ctx);

        foreach (var stmt in statements)
        {
            // Skip class, function, interface, and enum declarations (already handled)
            // Note: Namespace statements are NOT skipped - they need to emit member storage
            if (stmt is Stmt.Class classDecl)
            {
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

            // Special handling for expression statements to wait for top-level async calls
            if (stmt is Stmt.Expression exprStmt)
            {
                emitter.EmitExpression(exprStmt.Expr);

                // Check if the result is a Task<object> or $Promise and wait for it
                // This provides "top-level await" behavior for compiled code
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

                // Wait for the task via $EventLoop.WaitForTask: blocks while the
                // event loop has work that could settle it (timers fire as part
                // of the wait), checks cancellation each iteration, and returns
                // false once the process is provably quiescent — a never-settling
                // promise (`new Promise(() => {})`) must not block exit (matches
                // Node). On false we skip GetResult and move on.
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
            else
            {
                emitter.EmitStatement(stmt);
            }
        }

        // Run the event loop — no-op if no handles are active
        il.Emit(OpCodes.Call, _runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, _runtime.EventLoopRun);

        il.Emit(OpCodes.Ret);

        ILLabelValidator.Validate(il, "entry point (single-file)");
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
        var ctx = new CompilationContext(il, _typeMapper, _functions.Builders, _classes.Builders, _types)
        {
            ClosureAnalyzer = _closures.Analyzer,
            ArrowMethods = _closures.ArrowMethods,
            ConstArrowBindings = _closures.ConstArrowBindings,
            DisplayClasses = _closures.DisplayClasses,
            DisplayClassFields = _closures.DisplayClassFields,
            DisplayClassConstructors = _closures.DisplayClassConstructors,
            ClassExprBuilders = _classExprs.Builders,
            FunctionRestParams = _functions.RestParams,
            FunctionOverloads = _functions.Overloads,
            FunctionsCapturingArguments = _functions.CapturingArguments,
            EnumMembers = _enums.Members,
            EnumReverse = _enums.Reverse,
            EnumKinds = _enums.Kinds,
            NamespaceFields = _namespaceFields,
            TopLevelStaticVars = BuildTopLevelStaticVarsForModule(_modules.CurrentPath),
            Runtime = _runtime,
            FunctionGenericParams = _functions.GenericParams,
            IsGenericFunction = _functions.IsGeneric,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            AsyncMethods = null,
            AsyncArrowBuilders = _async.ArrowBuilders.Count > 0 ? _async.ArrowBuilders : null,
            DotNetNamespace = _modules.CurrentDotNetNamespace,
            TypeEmitterRegistry = _typeEmitterRegistry,
            BuiltInModuleEmitterRegistry = _builtInModuleEmitterRegistry,
            BuiltInModuleNamespaces = _builtInModuleNamespaces,
            BuiltInModuleMethodBindings = GetCurrentBuiltInMethodBindings(),
            ImportedNames = _importedNames,
            VarToClassExpr = _classExprs.VarToClassExpr,
            ClassExprStaticFields = _classExprs.StaticFields,
            ClassExprStaticMethods = _classExprs.StaticMethods,
            ClassExprConstructors = _classExprs.Constructors,
            ClassExprSuperclass = _classExprs.Superclass,
            UnionGenerator = _unionGenerator,
            IsStrictMode = _isStrictMode,
            // Registry services
            ClassRegistry = GetClassRegistry(),
            // Entry-point display class for captured top-level variables
            EntryPointDisplayClass = _closures.EntryPointDisplayClass,
            EntryPointDisplayClassCtor = _closures.EntryPointDisplayClassCtor,
            EntryPointDisplayClassFields = BuildEntryPointDisplayClassFieldsForModule(_modules.CurrentPath),
            CapturedTopLevelVars = BuildCapturedTopLevelVarsForModule(_modules.CurrentPath),
            ArrowEntryPointDCFields = _closures.ArrowEntryPointDCFields.Count > 0 ? _closures.ArrowEntryPointDCFields : null,
            EntryPointDisplayClassStaticField = _closures.EntryPointDisplayClassStaticField
        };

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

        // Initialize namespace static fields before any code
        InitializeNamespaceFields(il);

        // Trigger static constructors for classes with static blocks
        // In JavaScript/TypeScript, static blocks run when the class is defined.
        // In .NET, static constructors are lazy, so we force them to run here.
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Class classStmt && classStmt.StaticInitializers?.Count > 0)
            {
                string className = _modules.CurrentDotNetNamespace != null
                    ? $"{_modules.CurrentDotNetNamespace}.{classStmt.Name.Lexeme}"
                    : classStmt.Name.Lexeme;
                if (_classes.Builders.TryGetValue(className, out var classBuilder))
                {
                    il.Emit(OpCodes.Ldtoken, classBuilder);
                    il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
                    il.Emit(OpCodes.Callvirt, typeof(Type).GetProperty("TypeHandle")!.GetGetMethod()!);
                    il.Emit(OpCodes.Call, _types.RuntimeHelpersRunClassConstructor);
                }
            }
        }

        var emitter = new ILEmitter(ctx);

        // Execute top-level statements (module initialization), excluding the main function
        foreach (var stmt in statements)
        {
            // Skip declarations (handled in earlier phases), including main()
            if (stmt is Stmt.Class or Stmt.Function or Stmt.Interface or Stmt.Enum)
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
        // Load the args parameter (arg 0) - string[] is a reference type, no boxing needed
        il.Emit(OpCodes.Ldarg_0);  // Load string[] args (reference types implicitly convert to object)

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
            il.Emit(OpCodes.Ret);
        }

        ILLabelValidator.Validate(il, "entry point (multi-module)");
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
            var ctx = new CompilationContext(il, _typeMapper, _functions.Builders, _classes.Builders, _types)
            {
                ClassRegistry = GetClassRegistry(),
                Runtime = _runtime,
                TypeMap = _typeMap,
                // Check for function-level "use strict" directive
                IsStrictMode = _isStrictMode || CheckForUseStrict(funcStmt.Body)
            };
            var emitter = new ILEmitter(ctx);

            OverloadGenerator.EmitOverloadBody(
                il,
                fullMethod,
                funcStmt.Parameters,
                arity,
                isStatic: true,
                emitter
            );
        }
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
