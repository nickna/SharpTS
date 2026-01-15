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
        var staticFieldsWithInit = classStmt.Fields.Where(f => f.IsStatic && f.Initializer != null).ToList();
        bool hasStaticLockFields = _locks.StaticSyncLockFields.ContainsKey(qualifiedClassName);

        // Only emit if there are static fields with initializers OR static lock fields
        if (staticFieldsWithInit.Count == 0 && !hasStaticLockFields) return;

        var cctor = typeBuilder.DefineConstructor(
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );

        var il = cctor.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functions.Builders, _classes.Builders, _types)
        {
            ClosureAnalyzer = _closures.Analyzer,
            ArrowMethods = _closures.ArrowMethods,
            DisplayClasses = _closures.DisplayClasses,
            DisplayClassFields = _closures.DisplayClassFields,
            DisplayClassConstructors = _closures.DisplayClassConstructors,
            CurrentClassBuilder = typeBuilder,
            StaticFields = _classes.StaticFields,
            ClassConstructors = _classes.Constructors,
            FunctionRestParams = _functions.RestParams,
            EnumMembers = _enums.Members,
            EnumReverse = _enums.Reverse,
            EnumKinds = _enums.Kinds,
            Runtime = _runtime,
            ClassGenericParams = _classes.GenericParams,
            FunctionGenericParams = _functions.GenericParams,
            IsGenericFunction = _functions.IsGeneric,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _classes.InstanceMethods,
            InstanceGetters = _classes.InstanceGetters,
            InstanceSetters = _classes.InstanceSetters,
            ClassSuperclass = _classes.Superclass,
            AsyncMethods = null,
            // Module support for multi-module compilation
            CurrentModulePath = _modules.CurrentPath,
            ClassToModule = _modules.ClassToModule,
            FunctionToModule = _modules.FunctionToModule,
            EnumToModule = _modules.EnumToModule,
            DotNetNamespace = _modules.CurrentDotNetNamespace,
            TypeEmitterRegistry = _typeEmitterRegistry,
            BuiltInModuleEmitterRegistry = _builtInModuleEmitterRegistry,
            BuiltInModuleNamespaces = _builtInModuleNamespaces,
            ClassExprBuilders = _classExprs.Builders
        };

        var emitter = new ILEmitter(ctx);

        // Initialize static @lock decorator fields
        if (_locks.StaticSyncLockFields.TryGetValue(qualifiedClassName, out var staticSyncLockField))
        {
            // _staticSyncLock = new object();
            il.Emit(OpCodes.Newobj, typeof(object).GetConstructor([])!);
            il.Emit(OpCodes.Stsfld, staticSyncLockField);
        }

        if (_locks.StaticAsyncLockFields.TryGetValue(qualifiedClassName, out var staticAsyncLockField))
        {
            // _staticAsyncLock = new SemaphoreSlim(1, 1);
            il.Emit(OpCodes.Ldc_I4_1);  // initialCount = 1
            il.Emit(OpCodes.Ldc_I4_1);  // maxCount = 1
            il.Emit(OpCodes.Newobj, typeof(SemaphoreSlim).GetConstructor([typeof(int), typeof(int)])!);
            il.Emit(OpCodes.Stsfld, staticAsyncLockField);
        }

        if (_locks.StaticReentrancyFields.TryGetValue(qualifiedClassName, out var staticReentrancyField))
        {
            // _staticLockReentrancy = new AsyncLocal<int>();
            il.Emit(OpCodes.Newobj, typeof(AsyncLocal<int>).GetConstructor([])!);
            il.Emit(OpCodes.Stsfld, staticReentrancyField);
        }

        // Initialize static field initializers
        var classStaticFields = _classes.StaticFields[qualifiedClassName];
        foreach (var field in staticFieldsWithInit)
        {
            // Emit the initializer expression
            emitter.EmitExpression(field.Initializer!);
            emitter.EmitBoxIfNeeded(field.Initializer!);

            // Store in static field using the stored FieldBuilder
            var staticField = classStaticFields[field.Name.Lexeme];
            il.Emit(OpCodes.Stsfld, staticField);
        }

        il.Emit(OpCodes.Ret);
    }

    private void EmitStaticMethodBody(string className, Stmt.Function method)
    {
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
        var ctx = new CompilationContext(il, _typeMapper, _functions.Builders, _classes.Builders, _types)
        {
            IsInstanceMethod = false,
            ClosureAnalyzer = _closures.Analyzer,
            ArrowMethods = _closures.ArrowMethods,
            DisplayClasses = _closures.DisplayClasses,
            DisplayClassFields = _closures.DisplayClassFields,
            DisplayClassConstructors = _closures.DisplayClassConstructors,
            CurrentClassBuilder = typeBuilder,
            StaticFields = _classes.StaticFields,
            StaticMethods = _classes.StaticMethods,
            ClassConstructors = _classes.Constructors,
            FunctionRestParams = _functions.RestParams,
            EnumMembers = _enums.Members,
            EnumReverse = _enums.Reverse,
            EnumKinds = _enums.Kinds,
            Runtime = _runtime,
            ClassGenericParams = _classes.GenericParams,
            FunctionGenericParams = _functions.GenericParams,
            IsGenericFunction = _functions.IsGeneric,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _classes.InstanceMethods,
            InstanceGetters = _classes.InstanceGetters,
            InstanceSetters = _classes.InstanceSetters,
            ClassSuperclass = _classes.Superclass,
            AsyncMethods = null,
            // Module support for multi-module compilation
            CurrentModulePath = _modules.CurrentPath,
            ClassToModule = _modules.ClassToModule,
            FunctionToModule = _modules.FunctionToModule,
            EnumToModule = _modules.EnumToModule,
            DotNetNamespace = _modules.CurrentDotNetNamespace,
            // @lock decorator support
            SyncLockFields = _locks.SyncLockFields,
            AsyncLockFields = _locks.AsyncLockFields,
            LockReentrancyFields = _locks.ReentrancyFields,
            StaticSyncLockFields = _locks.StaticSyncLockFields,
            StaticAsyncLockFields = _locks.StaticAsyncLockFields,
            StaticLockReentrancyFields = _locks.StaticReentrancyFields,
            TypeEmitterRegistry = _typeEmitterRegistry,
            BuiltInModuleEmitterRegistry = _builtInModuleEmitterRegistry,
            BuiltInModuleNamespaces = _builtInModuleNamespaces,
            ClassExprBuilders = _classExprs.Builders
        };

        // Define parameters with types (starting at index 0, not 1 since no 'this')
        var methodParams = methodBuilder.GetParameters();
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            Type paramType = i < methodParams.Length ? methodParams[i].ParameterType : typeof(object);
            ctx.DefineParameter(method.Parameters[i].Name.Lexeme, i, paramType);
        }

        var emitter = new ILEmitter(ctx);

        // No runtime default parameter checks needed - overloads handle this

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
            il.Emit(OpCodes.Call, typeof(Monitor).GetMethod("Enter", [typeof(object), typeof(bool).MakeByRefType()])!);

            il.MarkLabel(skipEnterLabel);

            // Begin try block - use builder to keep exception depth in sync
            ctx.ILBuilder.BeginExceptionBlock();
        }

        // Abstract methods have no body to emit
        if (method.Body != null)
        {
            foreach (var stmt in method.Body)
            {
                emitter.EmitStatement(stmt);
            }
        }

        // Close @lock decorator - finally block for static methods
        if (hasLock && prevReentrancyLocal != null && lockTakenLocal != null &&
            staticSyncLockField != null && staticReentrancyField != null)
        {
            // Store default return value if no explicit return was emitted
            // ReturnValueLocal is guaranteed non-null here (set up earlier in hasLock block)
            il.Emit(OpCodes.Ldnull);
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
            il.Emit(OpCodes.Call, typeof(Monitor).GetMethod("Exit", [typeof(object)])!);

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
            // Default return null
            il.Emit(OpCodes.Ldnull);
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
            staticLockReentrancyField);

        // Create context for MoveNext emission
        var il = smBuilder.MoveNextMethod.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functions.Builders, _classes.Builders, _types)
        {
            IsInstanceMethod = false,  // Static method!
            ClosureAnalyzer = _closures.Analyzer,
            ArrowMethods = _closures.ArrowMethods,
            DisplayClasses = _closures.DisplayClasses,
            DisplayClassFields = _closures.DisplayClassFields,
            DisplayClassConstructors = _closures.DisplayClassConstructors,
            CurrentClassBuilder = typeBuilder,
            StaticFields = _classes.StaticFields,
            StaticMethods = _classes.StaticMethods,
            ClassConstructors = _classes.Constructors,
            FunctionRestParams = _functions.RestParams,
            EnumMembers = _enums.Members,
            EnumReverse = _enums.Reverse,
            EnumKinds = _enums.Kinds,
            Runtime = _runtime,
            ClassGenericParams = _classes.GenericParams,
            FunctionGenericParams = _functions.GenericParams,
            IsGenericFunction = _functions.IsGeneric,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _classes.InstanceMethods,
            InstanceGetters = _classes.InstanceGetters,
            InstanceSetters = _classes.InstanceSetters,
            ClassSuperclass = _classes.Superclass,
            AsyncMethods = null,
            AsyncArrowBuilders = _async.ArrowBuilders,
            AsyncArrowOuterBuilders = _async.ArrowOuterBuilders,
            AsyncArrowParentBuilders = _async.ArrowParentBuilders,
            // Module support for multi-module compilation
            CurrentModulePath = _modules.CurrentPath,
            ClassToModule = _modules.ClassToModule,
            FunctionToModule = _modules.FunctionToModule,
            EnumToModule = _modules.EnumToModule,
            DotNetNamespace = _modules.CurrentDotNetNamespace,
            // @lock decorator support
            SyncLockFields = _locks.SyncLockFields,
            AsyncLockFields = _locks.AsyncLockFields,
            LockReentrancyFields = _locks.ReentrancyFields,
            StaticSyncLockFields = _locks.StaticSyncLockFields,
            StaticAsyncLockFields = _locks.StaticAsyncLockFields,
            StaticLockReentrancyFields = _locks.StaticReentrancyFields,
            TypeEmitterRegistry = _typeEmitterRegistry,
            BuiltInModuleEmitterRegistry = _builtInModuleEmitterRegistry,
            BuiltInModuleNamespaces = _builtInModuleNamespaces,
            ClassExprBuilders = _classExprs.Builders
        };

        // Emit MoveNext body
        var moveNextEmitter = new AsyncMoveNextEmitter(smBuilder, analysis, _types);
        moveNextEmitter.EmitMoveNext(method.Body, ctx, _types.Object);

        // Emit MoveNext bodies for async arrows
        foreach (var arrowInfo in analysis.AsyncArrows)
        {
            if (_async.ArrowBuilders.TryGetValue(arrowInfo.Arrow, out var arrowBuilder))
            {
                var arrowAnalysis = AnalyzeAsyncArrow(arrowInfo.Arrow);
                var arrow = arrowInfo.Arrow;

                List<Stmt> bodyStatements;
                if (arrow.BlockBody != null)
                {
                    bodyStatements = arrow.BlockBody;
                }
                else if (arrow.ExpressionBody != null)
                {
                    var returnToken = new Token(TokenType.RETURN, "return", null, 0);
                    bodyStatements = [new Stmt.Return(returnToken, arrow.ExpressionBody)];
                }
                else
                {
                    bodyStatements = [];
                }

                var arrowEmitter = new AsyncArrowMoveNextEmitter(arrowBuilder,
                    new AsyncStateAnalyzer.AsyncFunctionAnalysis(
                        arrowAnalysis.AwaitCount,
                        [],  // AwaitPoints not needed for emission
                        arrowAnalysis.HoistedLocals,
                        [],  // HoistedParameters - arrow params are in ParameterFields
                        false, // HasTryCatch
                        false, // UsesThis
                        []     // AsyncArrows - handled separately via _async.ArrowBuilders
                    ), _types);
                arrowEmitter.EmitMoveNext(bodyStatements, ctx, _types.Object);
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
