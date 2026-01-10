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
        var paramTypes = method.Parameters.Select(_ => typeof(object)).ToArray();
        var methodBuilder = typeBuilder.DefineMethod(
            method.Name.Lexeme,
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            paramTypes
        );

        _staticMethods[className][method.Name.Lexeme] = methodBuilder;
    }

    private void EmitStaticConstructor(TypeBuilder typeBuilder, Stmt.Class classStmt, string qualifiedClassName)
    {
        // Check if we need a static constructor
        var staticFieldsWithInit = classStmt.Fields.Where(f => f.IsStatic && f.Initializer != null).ToList();
        bool hasStaticLockFields = _staticSyncLockFields.ContainsKey(qualifiedClassName);

        // Only emit if there are static fields with initializers OR static lock fields
        if (staticFieldsWithInit.Count == 0 && !hasStaticLockFields) return;

        var cctor = typeBuilder.DefineConstructor(
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );

        var il = cctor.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders, _types)
        {
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            CurrentClassBuilder = typeBuilder,
            StaticFields = _staticFields,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _instanceMethods,
            InstanceGetters = _instanceGetters,
            InstanceSetters = _instanceSetters,
            ClassSuperclass = _classSuperclass,
            AsyncMethods = null,
            // Module support for multi-module compilation
            CurrentModulePath = _currentModulePath,
            ClassToModule = _classToModule,
            FunctionToModule = _functionToModule,
            EnumToModule = _enumToModule,
            DotNetNamespace = _currentDotNetNamespace
        };

        var emitter = new ILEmitter(ctx);

        // Initialize static @lock decorator fields
        if (_staticSyncLockFields.TryGetValue(qualifiedClassName, out var staticSyncLockField))
        {
            // _staticSyncLock = new object();
            il.Emit(OpCodes.Newobj, typeof(object).GetConstructor([])!);
            il.Emit(OpCodes.Stsfld, staticSyncLockField);
        }

        if (_staticAsyncLockFields.TryGetValue(qualifiedClassName, out var staticAsyncLockField))
        {
            // _staticAsyncLock = new SemaphoreSlim(1, 1);
            il.Emit(OpCodes.Ldc_I4_1);  // initialCount = 1
            il.Emit(OpCodes.Ldc_I4_1);  // maxCount = 1
            il.Emit(OpCodes.Newobj, typeof(SemaphoreSlim).GetConstructor([typeof(int), typeof(int)])!);
            il.Emit(OpCodes.Stsfld, staticAsyncLockField);
        }

        if (_staticLockReentrancyFields.TryGetValue(qualifiedClassName, out var staticReentrancyField))
        {
            // _staticLockReentrancy = new AsyncLocal<int>();
            il.Emit(OpCodes.Newobj, typeof(AsyncLocal<int>).GetConstructor([])!);
            il.Emit(OpCodes.Stsfld, staticReentrancyField);
        }

        // Initialize static field initializers
        var classStaticFields = _staticFields[qualifiedClassName];
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
        var typeBuilder = _classBuilders[className];
        var methodBuilder = _staticMethods[className][method.Name.Lexeme];

        // Check if method has @lock decorator
        bool hasLock = HasLockDecorator(method);

        var il = methodBuilder.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders, _types)
        {
            IsInstanceMethod = false,
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            CurrentClassBuilder = typeBuilder,
            StaticFields = _staticFields,
            StaticMethods = _staticMethods,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction,
            TypeMap = _typeMap,
            DeadCode = _deadCodeInfo,
            InstanceMethods = _instanceMethods,
            InstanceGetters = _instanceGetters,
            InstanceSetters = _instanceSetters,
            ClassSuperclass = _classSuperclass,
            AsyncMethods = null,
            // Module support for multi-module compilation
            CurrentModulePath = _currentModulePath,
            ClassToModule = _classToModule,
            FunctionToModule = _functionToModule,
            EnumToModule = _enumToModule,
            DotNetNamespace = _currentDotNetNamespace,
            // @lock decorator support
            SyncLockFields = _syncLockFields,
            AsyncLockFields = _asyncLockFields,
            LockReentrancyFields = _lockReentrancyFields,
            StaticSyncLockFields = _staticSyncLockFields,
            StaticAsyncLockFields = _staticAsyncLockFields,
            StaticLockReentrancyFields = _staticLockReentrancyFields
        };

        // Define parameters (starting at index 0, not 1 since no 'this')
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            ctx.DefineParameter(method.Parameters[i].Name.Lexeme, i);
        }

        var emitter = new ILEmitter(ctx);

        // Emit default parameter checks (static method)
        emitter.EmitDefaultParameters(method.Parameters, false);

        // Variables for @lock decorator support
        LocalBuilder? prevReentrancyLocal = null;
        LocalBuilder? lockTakenLocal = null;
        FieldBuilder? staticSyncLockField = null;
        FieldBuilder? staticReentrancyField = null;

        // Set up @lock decorator - reentrancy-aware Monitor pattern for static methods
        if (hasLock && _staticSyncLockFields.TryGetValue(className, out staticSyncLockField) &&
            _staticLockReentrancyFields.TryGetValue(className, out staticReentrancyField))
        {
            prevReentrancyLocal = il.DeclareLocal(typeof(int));     // int __prevReentrancy
            lockTakenLocal = il.DeclareLocal(typeof(bool));         // bool __lockTaken

            // Set up deferred return handling for the lock's exception block
            ctx.ReturnValueLocal = il.DeclareLocal(typeof(object));
            ctx.ReturnLabel = il.DefineLabel();
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

            // Begin try block
            il.BeginExceptionBlock();
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
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stloc, ctx.ReturnValueLocal);
            il.Emit(OpCodes.Leave, ctx.ReturnLabel);

            // Begin finally block
            il.BeginFinallyBlock();

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

            // End try/finally block
            il.EndExceptionBlock();

            ctx.ExceptionBlockDepth--;

            // Mark return label and emit actual return
            il.MarkLabel(ctx.ReturnLabel);
            il.Emit(OpCodes.Ldloc, ctx.ReturnValueLocal);
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
}
