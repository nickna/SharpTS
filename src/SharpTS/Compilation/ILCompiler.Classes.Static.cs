using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Static method and static constructor emission for class compilation.
/// </summary>
public partial class ILCompiler
{
    private void DefineStaticMethod(TypeBuilder typeBuilder, string className, Stmt.Function method)
    {
        // Skip if already pre-defined in DefineClassMethodsOnly
        if (_classes.StaticMethods.TryGetValue(className, out var existingMethods) &&
            existingMethods.ContainsKey(method.Name.Lexeme))
        {
            return;
        }

        // Resolve typed parameters from TypeMap
        var paramTypes = ParameterTypeResolver.ResolveMethodParameters(
            className, method.Name.Lexeme, method.Parameters, _typeMapper, _typeMap);
        // Keep return type as object (async methods return Task<object>)
        var returnType = method.IsAsync ? _types.TaskOfObject : typeof(object);
        var methodBuilder = typeBuilder.DefineMethod(
            method.Name.Lexeme,
            MethodAttributes.Public | MethodAttributes.Static,
            returnType,
            paramTypes
        );

        // Initialize dictionary if needed
        if (!_classes.StaticMethods.ContainsKey(className))
        {
            _classes.StaticMethods[className] = [];
        }
        _classes.StaticMethods[className][method.Name.Lexeme] = methodBuilder;
    }

    private void EmitStaticConstructor(TypeBuilder typeBuilder, Stmt.Class classStmt, string qualifiedClassName)
    {
        // Check if we need a static constructor
        // Note: Declare fields are excluded - they have no initialization
        var staticFieldsWithInit = classStmt.Fields.Where(f => f.IsStatic && !f.IsPrivate && !f.IsDeclare && f.Initializer != null).ToList();
        var staticPrivateFieldsWithInit = classStmt.Fields.Where(f => f.IsStatic && f.IsPrivate && f.Initializer != null).ToList();
        var staticAutoAccessorsWithInit = classStmt.AutoAccessors?.Where(a => a.IsStatic && a.Initializer != null).ToList() ?? [];
        bool hasStaticLockFields = _locks.StaticSyncLockFields.ContainsKey(qualifiedClassName);
        bool hasPrivateFieldStorage = _classes.PrivateFieldStorage.ContainsKey(qualifiedClassName);
        bool hasStaticPrivateFields = _classes.StaticPrivateFields.TryGetValue(qualifiedClassName, out var staticPrivateFields) && staticPrivateFields.Count > 0;
        bool hasStaticInitializers = classStmt.StaticInitializers != null && classStmt.StaticInitializers.Count > 0;
        // Symbol-keyed computed accessors (#266) and methods (#647) register in the .cctor.
        bool hasSymbolAccessors = _classes.SymbolAccessors.ContainsKey(typeBuilder.Name);
        bool hasSymbolMethods = _classes.SymbolMethods.ContainsKey(typeBuilder.Name);

        var cctor = typeBuilder.DefineConstructor(
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );

        var il = cctor.GetILGenerator();
        var ctx = CreateModuleMemberContext(il, cctor);
        ctx.CurrentClassBuilder = typeBuilder;
        ctx.EmittingTypeBuilder = typeBuilder;
        ctx.CurrentClassName = qualifiedClassName; // Required for static member access via 'this'
        ctx.IsStaticConstructorContext = true;
        ApplyCapturedTopLevelVariableAccess(ctx);

        // Add class generic type parameters to context (required for static blocks in generic classes)
        if (_classes.GenericParams.TryGetValue(qualifiedClassName, out var classGenericParams))
        {
            foreach (var gp in classGenericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        var emitter = new ILEmitter(ctx);

        // ECMAScript creates Constructor.prototype as part of class evaluation.
        // Register it before user static fields/blocks so they can observe it.
        EmitClassPrototypeRegistration(
            il, typeBuilder, GetClassConstructorLength(classStmt.Methods));

        // Initialize static @lock decorator fields
        if (_locks.StaticSyncLockFields.TryGetValue(qualifiedClassName, out var staticSyncLockField))
        {
            // _staticSyncLock = new object();
            il.Emit(OpCodes.Newobj, _types.ObjectDefaultCtor);
            il.Emit(OpCodes.Stsfld, staticSyncLockField);
        }

        if (_locks.StaticAsyncLockFields.TryGetValue(qualifiedClassName, out var staticAsyncLockField))
        {
            // _staticAsyncLock = new SemaphoreSlim(1, 1);
            il.Emit(OpCodes.Ldc_I4_1);  // initialCount = 1
            il.Emit(OpCodes.Ldc_I4_1);  // maxCount = 1
            il.Emit(OpCodes.Newobj, _types.SemaphoreSlimCtor);
            il.Emit(OpCodes.Stsfld, staticAsyncLockField);
        }

        if (_locks.StaticReentrancyFields.TryGetValue(qualifiedClassName, out var staticReentrancyField))
        {
            // _staticLockReentrancy = new AsyncLocal<int>();
            il.Emit(OpCodes.Newobj, typeof(AsyncLocal<int>).GetConstructor([])!);
            il.Emit(OpCodes.Stsfld, staticReentrancyField);
        }

        // Initialize ES2022 private field storage (ConditionalWeakTable)
        if (_classes.PrivateFieldStorage.TryGetValue(qualifiedClassName, out var privateFieldStorage))
        {
            // __privateFields = new ConditionalWeakTable<object, Dictionary<string, object?>>()
            var cwtType = EmitGenerics.MakeGenericType(typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>), typeof(object), typeof(Dictionary<string, object?>));
            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(cwtType));
            il.Emit(OpCodes.Stsfld, privateFieldStorage);
        }

        // Use StaticInitializers for proper declaration order if available
        if (hasStaticInitializers)
        {
            // Get static field builders
            _classes.StaticFields.TryGetValue(qualifiedClassName, out var classStaticFields);
            _classes.StaticPrivateFields.TryGetValue(qualifiedClassName, out var staticPrivateFieldBuilders);

            // Emit static initializers in declaration order
            foreach (var initializer in classStmt.StaticInitializers!)
            {
                switch (initializer)
                {
                    case Stmt.Field field when field.IsStatic:
                        if (field.Initializer != null)
                        {
                            emitter.EmitExpression(field.Initializer);
                            emitter.EmitBoxIfNeeded(field.Initializer);

                            if (field.IsPrivate)
                            {
                                string fieldName = field.Name.Lexeme;
                                if (fieldName.StartsWith('#'))
                                    fieldName = fieldName[1..];
                                if (staticPrivateFieldBuilders != null && staticPrivateFieldBuilders.TryGetValue(fieldName, out var staticPrivateField))
                                {
                                    il.Emit(OpCodes.Stsfld, staticPrivateField);
                                }
                            }
                            else if (classStaticFields != null)
                            {
                                var staticField = classStaticFields[field.Name.Lexeme];
                                il.Emit(OpCodes.Stsfld, staticField);
                            }
                        }
                        break;

                    case Stmt.StaticBlock block:
                        // Emit block body statements
                        foreach (var stmt in block.Body)
                        {
                            emitter.EmitStatement(stmt);
                        }
                        break;
                }
            }
        }
        else
        {
            // Old behavior: initialize static private fields with their initializers
            if (_classes.StaticPrivateFields.TryGetValue(qualifiedClassName, out var staticPrivateFieldBuilders))
            {
                foreach (var field in classStmt.Fields.Where(f => f.IsStatic && f.IsPrivate && f.Initializer != null))
                {
                    string fieldName = field.Name.Lexeme;
                    if (fieldName.StartsWith('#'))
                        fieldName = fieldName[1..];

                    if (staticPrivateFieldBuilders.TryGetValue(fieldName, out var staticPrivateField))
                    {
                        emitter.EmitExpression(field.Initializer!);
                        emitter.EmitBoxIfNeeded(field.Initializer!);
                        il.Emit(OpCodes.Stsfld, staticPrivateField);
                    }
                }
            }

            // Initialize static field initializers
            if (staticFieldsWithInit.Count > 0 && _classes.StaticFields.TryGetValue(qualifiedClassName, out var classStaticFields))
            {
                foreach (var field in staticFieldsWithInit)
                {
                    // Emit the initializer expression
                    emitter.EmitExpression(field.Initializer!);
                    emitter.EmitBoxIfNeeded(field.Initializer!);

                    // Store in static field using the stored FieldBuilder
                    var staticField = classStaticFields[field.Name.Lexeme];
                    il.Emit(OpCodes.Stsfld, staticField);
                }
            }
        }

        // Initialize static auto-accessor backing fields (TypeScript 4.9+)
        foreach (var autoAccessor in staticAutoAccessorsWithInit)
        {
            EmitAutoAccessorInitializer(emitter, autoAccessor, qualifiedClassName, isStatic: true);
        }

        // Register symbol-keyed computed accessors (#266) in the runtime registry,
        // keyed by this class's Type so dynamic bracket get/set can dispatch them.
        EmitSymbolAccessorRegistrations(emitter, il, typeBuilder);
        EmitSymbolMethodRegistrations(emitter, il, typeBuilder);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>$Runtime.RegisterSymbolAccessor(typeof(ThisClass), symbol, getter, setter, isStatic)</c>
    /// for each symbol-keyed accessor recorded for this class (#266).
    /// </summary>
    private void EmitSymbolAccessorRegistrations(ILEmitter emitter, ILGenerator il, TypeBuilder typeBuilder)
    {
        if (!_classes.SymbolAccessors.TryGetValue(typeBuilder.Name, out var list))
            return;

        var getTypeFromHandle = _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle);

        foreach (var (accessor, method) in list)
        {
            bool isGetter = accessor.Kind.Type == TokenType.GET;

            // owner: typeof(ThisClass)
            il.Emit(OpCodes.Ldtoken, typeBuilder);
            il.Emit(OpCodes.Call, getTypeFromHandle);

            // property key: preserve Symbols; coerce every other value exactly as bracket
            // access does (notably null -> "null") before using it as a registry key.
            EmitComputedPropertyKey(emitter, il, accessor.ComputedKey!);

            // getter MethodInfo (or null)
            if (isGetter)
                EmitMethodInfoLiteral(il, method, typeBuilder);
            else
                il.Emit(OpCodes.Ldnull);

            // setter MethodInfo (or null)
            if (isGetter)
                il.Emit(OpCodes.Ldnull);
            else
                EmitMethodInfoLiteral(il, method, typeBuilder);

            // isStatic
            il.Emit(accessor.IsStatic ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);

            il.Emit(OpCodes.Call, _runtime.RegisterSymbolAccessor);
        }
    }

    /// <summary>
    /// Emits <c>$Runtime.RegisterSymbolMethod(typeof(ThisClass), symbol, methodInfo, isStatic)</c>
    /// for each computed symbol-keyed method recorded for this class (#647), mirroring
    /// <see cref="EmitSymbolAccessorRegistrations"/>.
    /// </summary>
    private void EmitSymbolMethodRegistrations(ILEmitter emitter, ILGenerator il, TypeBuilder typeBuilder)
    {
        if (!_classes.SymbolMethods.TryGetValue(typeBuilder.Name, out var list))
            return;

        var getTypeFromHandle = _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle);

        foreach (var (method, key, builder) in list)
        {
            if (ExpressionContainsYield(key))
                continue;
            // owner: typeof(ThisClass)
            il.Emit(OpCodes.Ldtoken, typeBuilder);
            il.Emit(OpCodes.Call, getTypeFromHandle);

            // property key: preserve Symbols; coerce every other value exactly as bracket
            // access does (notably null -> "null") before using it as a registry key.
            EmitComputedPropertyKey(emitter, il, key);

            // method MethodInfo
            EmitMethodInfoLiteral(il, builder, typeBuilder);

            // isStatic
            il.Emit(method.IsStatic ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);

            il.Emit(OpCodes.Call, _runtime.RegisterSymbolMethod);
        }
    }

    private (MethodBuilder Method, IReadOnlyList<Expr> Keys)? DefineDeferredComputedMethodKeyRegistrar(TypeBuilder typeBuilder)
    {
        if (!_classes.SymbolMethods.TryGetValue(typeBuilder.Name, out var methods))
            return null;

        var deferred = methods.Where(entry => ExpressionContainsYield(entry.Key)).ToList();
        if (deferred.Count == 0)
            return null;

        var registrar = typeBuilder.DefineMethod(
            "$registerDeferredComputedKeys",
            MethodAttributes.Assembly | MethodAttributes.Static,
            _types.Void,
            [_types.ObjectArray]);
        var il = registrar.GetILGenerator();
        var getTypeFromHandle = _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle);

        for (int i = 0; i < deferred.Count; i++)
        {
            var (method, _key, builder) = deferred[i];
            il.Emit(OpCodes.Ldtoken, typeBuilder);
            il.Emit(OpCodes.Call, getTypeFromHandle);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ldelem_Ref);
            EmitMethodInfoLiteral(il, builder, typeBuilder);
            il.Emit(method.IsStatic ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, _runtime.RegisterSymbolMethod);
        }

        il.Emit(OpCodes.Ret);
        return (registrar, deferred.Select(entry => entry.Key).ToArray());
    }

    private static bool ExpressionContainsYield(Expr expression)
    {
        var visitor = new YieldPresenceVisitor();
        visitor.Visit(expression);
        return visitor.Found;
    }

    private sealed class YieldPresenceVisitor : Parsing.Visitors.AstVisitorBase
    {
        public bool Found { get; private set; }

        protected override void VisitYield(Expr.Yield expr)
        {
            Found = true;
            ShouldContinue = false;
        }
    }

    private void EmitComputedPropertyKey(ILEmitter emitter, ILGenerator il, Expr key)
    {
        var keyLocal = il.DeclareLocal(_types.Object);
        var isSymbol = il.DefineLabel();

        emitter.EmitExpression(key);
        emitter.EmitBoxIfNeeded(key);
        il.Emit(OpCodes.Stloc, keyLocal);

        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Isinst, _runtime.TSSymbolType);
        il.Emit(OpCodes.Brtrue, isSymbol);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, _runtime.ToJsString);
        il.Emit(OpCodes.Stloc, keyLocal);

        il.MarkLabel(isSymbol);
        il.Emit(OpCodes.Ldloc, keyLocal);
    }

    private void EmitMethodInfoLiteral(ILGenerator il, MethodBuilder method, TypeBuilder declaringType)
    {
        il.Emit(OpCodes.Ldtoken, method);
        il.Emit(OpCodes.Ldtoken, declaringType);
        il.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandleWithType);
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
    }

    private void EmitStaticMethodBody(string className, Stmt.Function method)
    {
        // #703: a static method invoked as a value pads omitted optional args with the
        // `undefined` sentinel on the value-call path. Mark before the branches below so it
        // covers sync, async, and generator (#692) static methods (same builder).
        MarkPadsUndefined(_classes.StaticMethods[className][method.Name.Lexeme]);
        MarkFunctionLength(_classes.StaticMethods[className][method.Name.Lexeme], method.Parameters);

        // Static async generator methods (#778) use the async generator state machine, set up like a
        // free function (no `this`). Checked FIRST since a `static async *m()` has both IsAsync and
        // IsGenerator set, so it must not fall into the sync-generator or plain-async branch below.
        if (method.IsGenerator && method.IsAsync)
        {
            var agMethodBuilder = _classes.StaticMethods[className][method.Name.Lexeme];
            EmitAsyncGeneratorMethodBody(agMethodBuilder, method, fieldsField: null, isInstanceMethod: false);
            return;
        }

        // Static generator methods (#692) use the generator state machine, set up like a free
        // function (no `this`).
        if (method.IsGenerator && !method.IsAsync)
        {
            var genMethodBuilder = _classes.StaticMethods[className][method.Name.Lexeme];
            EmitGeneratorMethodBody(genMethodBuilder, method, fieldsField: null, isInstanceMethod: false);
            return;
        }

        // Async static methods use state machine generation
        if (method.IsAsync)
        {
            EmitStaticAsyncMethodBody(className, method);
            return;
        }

        var typeBuilder = _classes.Builders[className];
        var methodBuilder = _classes.StaticMethods[className][method.Name.Lexeme];

        // Check if method has @lock decorator
        bool hasLock = HasLockDecorator(method);

        var il = methodBuilder.GetILGenerator();
        var ctx = CreateModuleMemberContext(il, methodBuilder);
        ctx.IsStrictMode = true;
        ctx.CurrentClassBuilder = typeBuilder;
        ctx.EmittingTypeBuilder = typeBuilder;
        ApplyLockDecoratorFields(ctx);
        // ES2022 Private Class Elements support
        ctx.CurrentClassName = className;
        ApplyCapturedTopLevelVariableAccess(ctx);
        SetupSyncMethodFunctionDisplayClass(ctx, il, method);

        // Define parameters with types (starting at index 0, not 1 since no 'this')
        var methodParams = methodBuilder.GetParameters();
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            Type paramType = i < methodParams.Length ? methodParams[i].ParameterType : typeof(object);
            ctx.DefineParameter(method.Parameters[i].Name.Lexeme, i, paramType);
        }

        var emitter = new ILEmitter(ctx);

        // Apply parameter defaults. Static methods get no OverloadGenerator forwarding (only
        // free functions do), so without this a defaulted argument that is omitted or explicit
        // `undefined` would never fire its default (omit → null/0, undefined → NaN/cast error).
        // Value-type-defaulted params are widened to an object slot by ParameterTypeResolver so
        // the prologue can observe the `$Undefined` sentinel. (#705)
        var staticDefaultParamTypes = methodBuilder.GetParameters().Select(p => p.ParameterType).ToArray();
        EmitFunctionEnvironmentPrologue(
            il,
            ctx,
            emitter,
            method.Parameters,
            method.Body,
            staticDefaultParamTypes,
            argumentOffset: 0);
        InitializeSyncMethodCapturedParameters(
            ctx, il, method, methodBuilder, argumentOffset: 0);

        // Variables for @lock decorator support
        LocalBuilder? prevReentrancyLocal = null;
        LocalBuilder? lockTakenLocal = null;
        FieldBuilder? staticSyncLockField = null;
        FieldBuilder? staticReentrancyField = null;

        // Set up @lock decorator - reentrancy-aware Monitor pattern for static methods
        if (hasLock && _locks.StaticSyncLockFields.TryGetValue(className, out staticSyncLockField) &&
            _locks.StaticReentrancyFields.TryGetValue(className, out staticReentrancyField))
        {
            prevReentrancyLocal = il.DeclareLocal(typeof(int));     // int __prevReentrancy
            lockTakenLocal = il.DeclareLocal(typeof(bool));         // bool __lockTaken

            // Set up deferred return handling for the lock's exception block
            // Use the builder to define the label so it's tracked for validation
            ctx.ReturnValueLocal = il.DeclareLocal(typeof(object));
            ctx.ReturnLabel = ctx.ILBuilder.DefineLabel("static_lock_deferred_return");
            ctx.ExceptionBlockDepth++;

            // int __prevReentrancy = _staticLockReentrancy.Value;
            il.Emit(OpCodes.Ldsfld, staticReentrancyField);         // _staticLockReentrancy
            il.Emit(OpCodes.Callvirt, typeof(AsyncLocal<int>).GetProperty("Value")!.GetMethod!);
            il.Emit(OpCodes.Stloc, prevReentrancyLocal);

            // _staticLockReentrancy.Value = __prevReentrancy + 1;
            il.Emit(OpCodes.Ldsfld, staticReentrancyField);         // _staticLockReentrancy
            il.Emit(OpCodes.Ldloc, prevReentrancyLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Callvirt, typeof(AsyncLocal<int>).GetProperty("Value")!.SetMethod!);

            // bool __lockTaken = false;
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, lockTakenLocal);

            // if (__prevReentrancy == 0) { Monitor.Enter(_staticSyncLock, ref __lockTaken); }
            var skipEnterLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, prevReentrancyLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Bne_Un, skipEnterLabel);

            // Monitor.Enter(_staticSyncLock, ref __lockTaken);
            il.Emit(OpCodes.Ldsfld, staticSyncLockField);           // _staticSyncLock
            il.Emit(OpCodes.Ldloca, lockTakenLocal);                // ref __lockTaken
            il.Emit(OpCodes.Call, _types.MonitorEnter);

            il.MarkLabel(skipEnterLabel);

            // Begin try block - use builder to keep exception depth in sync
            ctx.ILBuilder.BeginExceptionBlock();
        }

        // Abstract methods have no body to emit
        if (method.Body != null)
        {
            // #1237: materialize inner function declarations in place, matching the instance-method
            // path so an inner `function` declared inside a static method becomes a binding.
            WireInPlaceInnerFunctions(ctx);

            foreach (var stmt in method.Body)
            {
                emitter.EmitStatement(stmt);
            }
        }

        // Close @lock decorator - finally block for static methods
        if (hasLock && prevReentrancyLocal != null && lockTakenLocal != null &&
            staticSyncLockField != null && staticReentrancyField != null)
        {
            // Store the implicit completion value if no explicit return was emitted.
            // ReturnValueLocal is guaranteed non-null here (set up earlier in hasLock
            // block) and is always typed `object`, so the default is the `$Undefined`
            // sentinel — a method falling off the end completes with `undefined`. (#588)
            EmitDefaultReturnValue(il, ctx.ReturnValueLocal!.LocalType);
            il.Emit(OpCodes.Stloc, ctx.ReturnValueLocal!);
            ctx.ILBuilder.Emit_Leave(ctx.ReturnLabel);

            // Begin finally block - use builder for exception block tracking
            ctx.ILBuilder.BeginFinallyBlock();

            // _staticLockReentrancy.Value = __prevReentrancy;
            il.Emit(OpCodes.Ldsfld, staticReentrancyField);         // _staticLockReentrancy
            il.Emit(OpCodes.Ldloc, prevReentrancyLocal);
            il.Emit(OpCodes.Callvirt, typeof(AsyncLocal<int>).GetProperty("Value")!.SetMethod!);

            // if (__lockTaken) { Monitor.Exit(_staticSyncLock); }
            var skipExitLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, lockTakenLocal);
            il.Emit(OpCodes.Brfalse, skipExitLabel);

            // Monitor.Exit(_staticSyncLock);
            il.Emit(OpCodes.Ldsfld, staticSyncLockField);           // _staticSyncLock
            il.Emit(OpCodes.Call, _types.MonitorExit);

            il.MarkLabel(skipExitLabel);

            // End try/finally block - use builder for exception block tracking
            ctx.ILBuilder.EndExceptionBlock();

            ctx.ExceptionBlockDepth--;

            // Mark return label and emit actual return - use builder since label was defined with builder
            ctx.ILBuilder.MarkLabel(ctx.ReturnLabel);
            il.Emit(OpCodes.Ldloc, ctx.ReturnValueLocal!);  // Non-null in hasLock path
            il.Emit(OpCodes.Ret);
        }
        // Finalize any deferred returns from exception blocks (non-@lock path)
        else if (emitter.HasDeferredReturns)
        {
            emitter.FinalizeReturns();
        }
        else
        {
            // Falling off the end completes with `undefined` (ECMA-262). Route through
            // EmitDefaultReturnValue so an `object` slot materializes the `$Undefined`
            // sentinel instead of CLR null. (#588)
            EmitDefaultReturnValue(il, methodBuilder.ReturnType);
            il.Emit(OpCodes.Ret);
        }
    }

    private void EmitStaticAsyncMethodBody(string className, Stmt.Function method)
    {
        var typeBuilder = _classes.Builders[className];
        var methodBuilder = _classes.StaticMethods[className][method.Name.Lexeme];

        // Analyze async function to determine await points and hoisted variables
        var analysis = _async.Analyzer.Analyze(method);

        // Check if method has @lock decorator
        bool hasLock = HasLockDecorator(method);

        // Build state machine type
        var smBuilder = new AsyncStateMachineBuilder(_moduleBuilder, _types, _async.StateMachineCounter++);
        var hasAsyncArrows = analysis.AsyncArrows.Count > 0;
        smBuilder.DefineStateMachine(
            $"{className}_{method.Name.Lexeme}",
            analysis,
            _types.Object,
            isInstanceMethod: false,  // Static method!
            hasAsyncArrows: hasAsyncArrows,
            hasLock: hasLock
        );

        // #682/#follow-up: attach the static method's function display class (registered in Phase 4) to
        // the state machine — shares verifiable reference storage for both async-arrow and nested
        // sync-arrow captured writes. Null when nothing is shared.
        string? methodDCKey = SetupAsyncMethodFunctionDC(smBuilder, method);

        // Build state machines for any async arrows found in this method
        DefineAsyncArrowStateMachines(analysis.AsyncArrows, smBuilder);

        // Get static lock fields if @lock decorator is present
        FieldBuilder? staticAsyncLockField = null;
        FieldBuilder? staticLockReentrancyField = null;
        if (hasLock)
        {
            _locks.StaticAsyncLockFields.TryGetValue(className, out staticAsyncLockField);
            _locks.StaticReentrancyFields.TryGetValue(className, out staticLockReentrancyField);
        }

        // Emit stub method body (creates state machine and starts it)
        // Pass isInstanceMethod: false and static lock fields
        EmitAsyncStubMethod(
            methodBuilder,
            smBuilder,
            method.Parameters,
            isInstanceMethod: false,  // Static method!
            staticAsyncLockField,
            staticLockReentrancyField,
            functionDCKey: methodDCKey);

        // Create context for MoveNext emission
        var il = smBuilder.MoveNextMethod.GetILGenerator();
        var ctx = CreateModuleMemberContext(il, smBuilder.MoveNextMethod);
        ctx.IsStrictMode = true;
        // Static method: IsInstanceMethod stays false (the default).
        ctx.CurrentClassBuilder = typeBuilder;
        ctx.EmittingTypeBuilder = typeBuilder;
        ctx.AsyncArrowBuilders = _async.ArrowBuilders;
        ctx.AsyncArrowOuterBuilders = _async.ArrowOuterBuilders;
        ctx.AsyncArrowParentBuilders = _async.ArrowParentBuilders;
        ApplyLockDecoratorFields(ctx);
        // ES2022 Private Class Elements support
        ctx.CurrentClassName = className;

        // #682: route promoted captures through the static method's function display class.
        WireAsyncMethodFunctionDC(ctx, smBuilder, methodDCKey);

        // Emit MoveNext body
        var moveNextEmitter = new AsyncMoveNextEmitter(smBuilder, analysis, _types);
        moveNextEmitter.EmitMoveNext(method.Body, ctx, _types.Object, method.Parameters);

        // Emit MoveNext bodies for async arrows. Delegate to the shared EmitAsyncArrowMoveNext (which
        // builds a fresh per-arrow ctx with the arrow's own IL) rather than reusing this method's ctx —
        // the latter routed strategy emissions via `ctx.IL` into the method's IL stream, producing
        // invalid IL for any suspending arrow in an async method (see EmitAsyncMethodBody for details).
        foreach (var arrowInfo in analysis.AsyncArrows)
        {
            if (_async.ArrowBuilders.TryGetValue(arrowInfo.Arrow, out var arrowBuilder))
            {
                EmitAsyncArrowMoveNext(arrowBuilder, arrowInfo.Arrow, ctx);
            }
        }

        // Finalize async arrow state machine types
        foreach (var arrowInfo in analysis.AsyncArrows)
        {
            if (_async.ArrowBuilders.TryGetValue(arrowInfo.Arrow, out var arrowBuilder))
            {
                arrowBuilder.CreateType();
            }
        }

        // Finalize state machine type
        smBuilder.CreateType();
    }
}
