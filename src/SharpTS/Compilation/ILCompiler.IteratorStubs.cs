using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private void EmitIteratorExpressionStub(GeneratorExpressionCompilation info)
    {
        var source = info.Source;
        var method = info.Kickoff;
        var iterator = (IIteratorStateMachineBuilder)info.StateMachine;
        var il = method.GetILGenerator();
        int receiverSkip = source.HasOwnThis ? 1 : 0;
        int argumentOffset = 1 + receiverSkip; // CLR target, then optional guest this.
        var sourceTypes = method.GetParameters().Skip(receiverSkip)
            .Select(parameter => parameter.ParameterType).ToArray();
        var context = CreateModuleMemberContext(il, method);
        context.IsInstanceMethod = true;
        context.IsStrictMode = _strictArrows.Contains(source) || _isStrictMode
            || BodyDeclaresUseStrict(source.BlockBody);
        context.CapturedFields = _closures.DisplayClassFields[source];
        context.CellCapturedFieldNames = _closures.Analyzer.GetClosureCellFields(source);
        context.LoopBodyCellCaptures = _closures.Analyzer.GetLoopBodyCellCaptures(source);
        context.EmittingTypeBuilder = info.TargetType;
        context.CurrentClassName = _async.ArrowEnclosingClassNames.GetValueOrDefault(source);
        if (_closures.ArrowEntryPointDCFields.TryGetValue(source, out var entryPoint))
            context.CurrentArrowEntryPointDCField = entryPoint;
        if (_closures.ArrowFunctionDCFields.TryGetValue(source, out var functionDC))
        {
            context.CurrentArrowFunctionDCField = functionDC;
            if (_closures.ArrowFunctionDCSource.TryGetValue(source, out var owner)
                && _closures.FunctionDisplayClassFields.TryGetValue(owner, out var fields))
            {
                context.FunctionDisplayClassFields = fields;
                context.CapturedFunctionLocals = [.. fields.Keys];
            }
        }
        if (_closures.ArrowScopeDCFields.TryGetValue(source, out var arrowDC)
            && _closures.ArrowScopeDCSource.TryGetValue(source, out var arrowOwner)
            && _closures.ArrowScopeDisplayClassFields.TryGetValue(arrowOwner, out var parentFields))
        {
            context.CurrentArrowScopeDCField = arrowDC;
            context.ParentArrowScopeDisplayClassFields = parentFields;
            context.ParentArrowCapturedLocals = [.. parentFields.Keys];
        }
        if (_arrowScopeDCExtraFields.TryGetValue(source, out var extraReferences))
        {
            context.CurrentArrowScopeDCExtraFields = extraReferences;
            context.ExtraArrowScopeBindings = BuildExtraScopeBindings(
                source, _closures.Analyzer.GetCaptures(source), extraReferences);
        }
        foreach (var (name, capture) in info.Captures)
        {
            if (capture.IsCell || capture.TargetPath.Count != 2) continue;
            var reference = capture.TargetPath[0];
            if (context.CurrentArrowFunctionDCField != reference)
                context.CapturedFunctionLocals?.Remove(name);
            if (context.CurrentArrowScopeDCField != reference)
                context.ParentArrowCapturedLocals?.Remove(name);
            (context.ExtraArrowScopeBindings ??= [])[name] = new(reference, capture.TargetPath[1]);
        }
        if (source.HasOwnThis) context.DefineParameter("__this", 1, _types.Object);
        for (int i = 0; i < source.Parameters.Count; i++)
            context.DefineParameter(source.Parameters[i].Name.Lexeme, i + argumentOffset, sourceTypes[i]);
        ApplyCapturedTopLevelVariableAccess(context);
        ApplyCommonJsModuleAccess(context);

        // This scope belongs to this invocation, whereas the expression target
        // stores its enclosing lexical environment and is shared by its iterators.
        LocalBuilder? ownScope = null;
        if (_closures.ArrowScopeDisplayClasses.TryGetValue(source, out var scopeType)
            && _closures.ArrowScopeDisplayClassCtors.TryGetValue(source, out var scopeCtor))
        {
            ownScope = il.DeclareLocal(scopeType);
            il.Emit(OpCodes.Newobj, scopeCtor);
            il.Emit(OpCodes.Stloc, ownScope);
            context.ArrowScopeDisplayClassLocal = ownScope;
            context.ArrowScopeDisplayClassFields = _closures.ArrowScopeDisplayClassFields[source];
            context.CapturedArrowLocals = [.. context.ArrowScopeDisplayClassFields.Keys];
        }

        LocalBuilder? arguments = null;
        if (ReferencesArgumentsIdentifier(info.FunctionShape.Body, source.Parameters))
            arguments = EmitArgumentsLocalPrologueCore(il, context, source.Parameters,
                sourceTypes, argumentOffset, publishedArgsLeadingSkip: receiverSkip);
        new ILEmitter(context).EmitDefaultParameters(source.Parameters, argumentOffset, sourceTypes, source.BlockBody);

        il.Emit(OpCodes.Newobj, iterator.Constructor);
        EmitIteratorArgumentsStore(il, iterator, arguments);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stfld, info.EnvironmentField);
        if (iterator.ThisField != null)
        {
            il.Emit(OpCodes.Dup);
            if (source.HasOwnThis) il.Emit(OpCodes.Ldarg_1);
            else il.Emit(OpCodes.Ldsfld, _runtime.FunctionValues.CurrentThisField);
            if (!context.IsStrictMode && _runtime.GlobalObject.HasSingletonField)
            {
                var useGlobal = il.DefineLabel();
                var bound = il.DefineLabel();
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Brfalse, useGlobal);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Isinst, _runtime.Sentinels.UndefinedType);
                il.Emit(OpCodes.Brfalse, bound);
                il.MarkLabel(useGlobal);
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ldsfld, _runtime.GlobalObject.SingletonField);
                il.MarkLabel(bound);
            }
            il.Emit(OpCodes.Stfld, iterator.ThisField);
        }
        for (int i = 0; i < source.Parameters.Count; i++)
        {
            if (iterator.GetVariableField(source.Parameters[i].Name.Lexeme) is not { } field) continue;
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldarg, i + argumentOffset);
            il.Emit(OpCodes.Stfld, field);
        }
        if (ownScope != null && iterator.FunctionDCField != null)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldloc, ownScope);
            il.Emit(OpCodes.Stfld, iterator.FunctionDCField);
            var scopeFields = _closures.ArrowScopeDisplayClassFields[source];
            for (int i = 0; i < source.Parameters.Count; i++)
            {
                if (!scopeFields.TryGetValue(source.Parameters[i].Name.Lexeme, out var field)) continue;
                il.Emit(OpCodes.Ldloc, ownScope);
                il.Emit(OpCodes.Ldarg, i + argumentOffset);
                il.Emit(OpCodes.Stfld, field);
            }
            if (source.Name is { } self && scopeFields.TryGetValue(self.Lexeme, out var selfField))
            {
                il.Emit(OpCodes.Ldloc, ownScope);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, info.FunctionValueField);
                il.Emit(OpCodes.Stfld, selfField);
            }
        }
        EmitIteratorPatternBindingStore(il, iterator, source.BlockBody, context);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, info.FunctionValueField);
        il.Emit(OpCodes.Call, _runtime.GeneratorPrototypes.AssociateFunction);
        il.Emit(OpCodes.Ret);
    }

    // The reference-type iterator state-machine creation stubs, shared between the sync generator and
    // async generator families (#1126). Both build byte-identical stubs — the only prior difference was
    // the static `smBuilder` type, now abstracted behind IIteratorStateMachineBuilder — so a single free
    // -function stub and a single method (instance/static) stub replace the six near-identical copies.
    // The async *function* stub stays separate: its state machine is a value-type struct initialised via
    // ldloca/initobj with an AsyncTaskMethodBuilder, a fundamentally different shape.

    /// <summary>
    /// Emits the creation stub for a top-level (free-function) iterator: news up the reference-type state
    /// machine, snapshots the active dynamic <c>this</c> from the thread-local receiver into
    /// <c>&lt;&gt;4__this</c> (#775 — the stub runs eagerly when the iterator is created, but MoveNext
    /// runs lazily, by which time the thread-local receiver is gone), copies the parameters into their
    /// object-typed hoisted fields (no boxing — free-function stub slots are already <c>object</c>), seeds
    /// the function display class, and returns the state machine.
    /// </summary>
    private void EmitIteratorFreeFunctionStub(
        MethodBuilder methodBuilder,
        IIteratorStateMachineBuilder smBuilder,
        Stmt.Function funcStmt,
        string qualifiedName)
    {
        var il = methodBuilder.GetILGenerator();

        // Parameter initialization belongs to the generator call, before the
        // iterator is returned, rather than to its first MoveNext invocation.
        var environment = EmitIteratorStubEnvironment(
            methodBuilder, funcStmt, isInstanceMethod: false, funcDCKey: qualifiedName);

        il.Emit(OpCodes.Newobj, smBuilder.Constructor);
        EmitIteratorArgumentsStore(il, smBuilder, environment.Arguments);

        // #775: capture the active dynamic `this` into <>4__this. The receiver is the thread-local
        // `$TSFunction._currentFunctionThis` (set by InvokeWithThis for an `o.gen()` / `.call(recv)`
        // value-call), coerced from null/undefined → globalThis for a sloppy generator to match
        // OrdinaryCallBindThis and LocalVariableResolver.LoadThis. Strict generators preserve undefined.
        if (smBuilder.ThisField != null && _runtime?.FunctionValues.CurrentThisField != null)
        {
            il.Emit(OpCodes.Dup);       // Keep state machine reference on stack
            il.Emit(OpCodes.Ldsfld, _runtime.FunctionValues.CurrentThisField);
            if (_runtime.GlobalObject.HasSingletonField)
            {
                var keepThis = il.DefineLabel();
                var useGlobalThis = il.DefineLabel();
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Brfalse, useGlobalThis);
                if (!(_isStrictMode || BodyDeclaresUseStrict(funcStmt.Body)))
                {
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Isinst, _runtime.Sentinels.UndefinedType);
                    il.Emit(OpCodes.Brfalse, keepThis);
                }
                else
                {
                    il.Emit(OpCodes.Br, keepThis);
                }
                il.MarkLabel(useGlobalThis);
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ldsfld, _runtime.GlobalObject.SingletonField);
                il.MarkLabel(keepThis);
            }
            il.Emit(OpCodes.Stfld, smBuilder.ThisField);
        }

        // Copy parameters to state machine fields. Free-function stub params are object-typed
        // (BuildStateMachineStubParamTypes), so no boxing is needed.
        for (int i = 0; i < funcStmt.Parameters.Count; i++)
        {
            var field = smBuilder.GetVariableField(funcStmt.Parameters[i].Name.Lexeme);
            if (field != null)
            {
                il.Emit(OpCodes.Dup);  // Keep state machine reference on stack
                il.Emit(OpCodes.Ldarg, i);
                if (field.FieldType == _types.Double)
                    il.Emit(OpCodes.Call, _runtime!.NumericCoercion.ConvertToNumber);
                il.Emit(OpCodes.Stfld, field);
            }
        }

        // Instantiate the function display class (#674/#725) and seed every captured parameter it owns
        // so nested closures share the iterator's live storage. Captured outer-scope variables
        // are NOT copied here — snapshotting them at creation hid later mutations (#541); MoveNext reads
        // them live from their enclosing storage instead.
        EmitGeneratorFunctionDCInit(
            il, smBuilder.FunctionDCField, funcStmt, qualifiedName, paramOffset: 0,
            precreatedFunctionDC: environment.FunctionDisplayClass);
        EmitIteratorPatternBindingStore(il, smBuilder, funcStmt.Body, environment.Context);

        EmitIteratorPrototypeAssociation(il, methodBuilder);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the creation stub for an iterator *method* (instance or static). Like the free-function
    /// stub but takes <c>this</c> from arg 0 for instance methods (params then start at arg 1) and boxes
    /// value-type parameters into the object-typed fields, deciding from the method's ACTUAL IL signature
    /// (<see cref="MethodBuilder.GetParameters"/>): a private method's parameters are all <c>object</c>
    /// slots, so boxing the AST-resolved value type would mismatch the loaded <c>object</c>
    /// (StackUnexpected). The function DC is seeded only when <paramref name="funcDCKey"/> is non-null;
    /// static iterator methods use the same path with parameter argument offset zero.
    /// </summary>
    private void EmitIteratorMethodStub(
        MethodBuilder methodBuilder,
        IIteratorStateMachineBuilder smBuilder,
        Stmt.Function method,
        bool isInstanceMethod,
        string? funcDCKey,
        FieldInfo? fieldsField,
        string? currentClassName)
    {
        var parameters = method.Parameters;
        var il = methodBuilder.GetILGenerator();

        var environment = EmitIteratorStubEnvironment(
            methodBuilder,
            method,
            isInstanceMethod,
            fieldsField,
            currentClassName,
            funcDCKey);

        il.Emit(OpCodes.Newobj, smBuilder.Constructor);
        EmitIteratorArgumentsStore(il, smBuilder, environment.Arguments);

        if (smBuilder.ThisField != null)
        {
            il.Emit(OpCodes.Dup);      // Keep state machine reference on stack
            if (isInstanceMethod) il.Emit(OpCodes.Ldarg_0);
            else il.Emit(OpCodes.Ldsfld, _runtime.FunctionValues.CurrentThisField);
            il.Emit(OpCodes.Stfld, smBuilder.ThisField);
        }

        var paramTypes = methodBuilder.GetParameters();
        int paramOffset = isInstanceMethod ? 1 : 0;

        for (int i = 0; i < parameters.Count; i++)
        {
            var field = smBuilder.GetVariableField(parameters[i].Name.Lexeme);
            if (field != null)
            {
                il.Emit(OpCodes.Dup);  // Keep state machine reference on stack
                il.Emit(OpCodes.Ldarg, i + paramOffset);
                Type? sourceType = i < paramTypes.Length
                    ? paramTypes[i].ParameterType
                    : null;
                if (field.FieldType == _types.Double && sourceType != _types.Double)
                    il.Emit(OpCodes.Call, _runtime!.NumericCoercion.ConvertToNumber);
                else if (field.FieldType == _types.Object && sourceType?.IsValueType == true)
                    il.Emit(OpCodes.Box, paramTypes[i].ParameterType);
                il.Emit(OpCodes.Stfld, field);
            }
        }

        // Seed captured parameters into the function DC (typed stub params → value types are boxed
        // before the store). No-op when the method has no function DC.
        if (funcDCKey != null)
            EmitGeneratorFunctionDCInit(
                il, smBuilder.FunctionDCField, method, funcDCKey, paramOffset, paramTypes,
                environment.FunctionDisplayClass);
        EmitIteratorPatternBindingStore(il, smBuilder, method.Body, environment.Context);

        EmitIteratorPrototypeAssociation(il, methodBuilder);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Associates the new iterator with its actual kickoff's current guest prototype.</summary>
    private void EmitIteratorPrototypeAssociation(ILGenerator il, MethodBuilder kickoff)
    {
        MethodInfo method = kickoff;
        if (kickoff.DeclaringType is { IsGenericTypeDefinition: true } owner)
        {
            // Resolve within the declaring class's own generic context, rather than
            // the dynamic receiver's type, so inherited methods keep their identity.
            var self = EmitGenerics.MakeGenericType(owner, owner.GetGenericArguments());
            method = EmitterTypeHelpers.ResolveMethod(self, kickoff);
        }

        il.Emit(OpCodes.Dup);
        _types.EmitLoadMethodInfo(il, method);
        il.Emit(OpCodes.Call, _runtime.GeneratorPrototypes.Associate);
    }

    /// <summary>
    /// Emits generator and async-generator parameter initialization into their eager
    /// creation stub. Defaults update the argument slots before those values are copied
    /// into the state machine and any function display class.
    /// </summary>
    private readonly record struct IteratorStubEnvironment(LocalBuilder? FunctionDisplayClass, LocalBuilder? Arguments,
        CompilationContext? Context);

    private void EmitIteratorPatternBindingStore(ILGenerator il, IIteratorStateMachineBuilder builder,
        List<Stmt>? body, CompilationContext? context)
    {
        if (context is null) return;
        var resolver = new LocalVariableResolver(il, context, _types);
        foreach (var (declaration, name) in ParameterPatternBindings.Declarations(body))
        {
            string storageName = builder.BlockScopeRenames.GetValueOrDefault(declaration) ?? name.Lexeme;
            if (builder.GetVariableField(storageName) is not { } field) continue;
            il.Emit(OpCodes.Dup);
            var stackType = resolver.TryLoadVariable(name.Lexeme)
                ?? throw new InvalidOperationException($"Initialized parameter binding '{name.Lexeme}' is unavailable.");
            if (stackType == StackType.Double) il.Emit(OpCodes.Box, _types.Double);
            else if (stackType == StackType.Boolean) il.Emit(OpCodes.Box, _types.Boolean);
            if (field.FieldType == _types.Double)
                il.Emit(OpCodes.Call, _runtime.NumericCoercion.ConvertToNumber);
            il.Emit(OpCodes.Stfld, field);
        }
    }

    private void EmitIteratorArgumentsStore(ILGenerator il, IIteratorStateMachineBuilder builder, LocalBuilder? arguments)
    {
        if (arguments == null) return;
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldloc, arguments);
        il.Emit(OpCodes.Stfld, builder.GetVariableField("arguments")
            ?? throw new InvalidOperationException("Iterator arguments storage has not been declared."));
    }

    private IteratorStubEnvironment EmitIteratorStubEnvironment(
        MethodBuilder methodBuilder,
        Stmt.Function function,
        bool isInstanceMethod,
        FieldInfo? fieldsField = null,
        string? currentClassName = null,
        string? funcDCKey = null)
    {
        if (!function.Parameters.Any(p => p.DefaultValue != null)
            && !ReferencesArgumentsIdentifier(function.Body, function.Parameters)
            && Runtime.ParameterBinder.ParameterInitializers(function.Body) is null)
            return default;

        var ctx = CreateModuleMemberContext(methodBuilder.GetILGenerator(), methodBuilder);
        ctx.FieldsField = fieldsField;
        ctx.IsInstanceMethod = isInstanceMethod;
        ctx.IsStrictMode = _isStrictMode || BodyDeclaresUseStrict(function.Body);
        ctx.CurrentClassName = currentClassName;
        ctx.CurrentClassBuilder = methodBuilder.DeclaringType as TypeBuilder;
        ctx.EmittingTypeBuilder = methodBuilder.DeclaringType as TypeBuilder;
        ApplyCapturedTopLevelVariableAccess(ctx);
        ApplyCommonJsModuleAccess(ctx);

        LocalBuilder? functionDCLocal = null;
        if (funcDCKey != null
            && _closures.FunctionDisplayClasses.TryGetValue(funcDCKey, out var functionDCType)
            && _closures.FunctionDisplayClassCtors.TryGetValue(funcDCKey, out var functionDCCtor)
            && _closures.FunctionDisplayClassFields.TryGetValue(funcDCKey, out var functionDCFields))
        {
            functionDCLocal = methodBuilder.GetILGenerator().DeclareLocal(functionDCType);
            methodBuilder.GetILGenerator().Emit(OpCodes.Newobj, functionDCCtor);
            methodBuilder.GetILGenerator().Emit(OpCodes.Stloc, functionDCLocal);
            ctx.FunctionDisplayClassLocal = functionDCLocal;
            ctx.FunctionDisplayClassFields = functionDCFields;
            ctx.CapturedFunctionLocals = [.. functionDCFields.Keys];
            ctx.ArrowFunctionDCFields = _closures.ArrowFunctionDCFields.Count > 0
                ? _closures.ArrowFunctionDCFields
                : null;
        }

        var methodParams = methodBuilder.GetParameters();
        int paramOffset = isInstanceMethod ? 1 : 0;
        for (int i = 0; i < function.Parameters.Count; i++)
        {
            Type? paramType = i < methodParams.Length ? methodParams[i].ParameterType : null;
            ctx.DefineParameter(function.Parameters[i].Name.Lexeme, i + paramOffset, paramType);
        }

        // Snapshot caller values before eager defaults rewrite any argument slots.
        var arguments = EmitSuspendedArgumentsPrologue(methodBuilder, function, isInstanceMethod, ctx);

        new ILEmitter(ctx).EmitDefaultParameters(function.Parameters, paramOffset,
            methodParams.Select(p => p.ParameterType).ToArray(), function.Body);

        return new(functionDCLocal, arguments, ctx);
    }

    private LocalBuilder? EmitSuspendedArgumentsPrologue(MethodBuilder method, Stmt.Function function,
        bool isInstanceMethod, CompilationContext? context = null)
    {
        if (!ReferencesArgumentsIdentifier(function.Body, function.Parameters)) return null;
        // Method value helpers have a compiler-owned guest receiver parameter.
        // A real source parameter named __this does not affect this registry test.
        int receiverSkip = _privateMethodValueOwners.ContainsKey(method)
            || _declarationMethodValueOwners.ContainsKey(method) ? 1 : 0;
        var parameters = function.Parameters.Skip(receiverSkip).ToList();
        var types = method.GetParameters().Skip(receiverSkip).Select(parameter => parameter.ParameterType).ToArray();
        var il = method.GetILGenerator();
        context ??= CreateModuleMemberContext(il, method);
        return EmitArgumentsLocalPrologueCore(il, context, parameters, types,
            (isInstanceMethod ? 1 : 0) + receiverSkip, publishedArgsLeadingSkip: receiverSkip);
    }
}
