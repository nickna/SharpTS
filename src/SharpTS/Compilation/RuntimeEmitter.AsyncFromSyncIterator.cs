using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the async-from-sync adapter used by compiled <c>for await...of</c>
/// loops when a value has no <c>Symbol.asyncIterator</c>.
/// </summary>
public partial class RuntimeEmitter
{
    private MethodBuilder _asyncFromSyncCreateResult = null!;
    private MethodBuilder _asyncFromSyncAwaitResult = null!;
    private MethodBuilder _asyncFromSyncAwaitContinuation = null!;

    /// <summary>
    /// Emits a BCL-only adapter into the generated assembly, preserving
    /// standalone output with no dependency on SharpTS.dll.
    /// </summary>
    private void EmitAsyncFromSyncIteratorSupport(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var runtimeType = _runtimeTypeBuilder!;

        EmitAsyncFromSyncCreateResult(runtimeType);
        EmitAsyncFromSyncAwaitContinuation(runtimeType, runtime);
        EmitAsyncFromSyncAwaitResult(runtimeType, runtime);
        EmitAsyncFromSyncIteratorType(moduleBuilder, runtime);
        EmitAdaptSyncIterableToAsyncGenerator(runtimeType, runtime);
    }

    /// <summary>
    /// static object AsyncFromSyncCreateResult(object value, bool done)
    /// </summary>
    private void EmitAsyncFromSyncCreateResult(TypeBuilder runtimeType)
    {
        var method = runtimeType.DefineMethod(
            "AsyncFromSyncCreateResult",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Boolean]);
        _asyncFromSyncCreateResult = method;

        var il = method.GetILGenerator();
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);

        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, dictLocal);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "done");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// static object AsyncFromSyncAwaitContinuation(Task&lt;object&gt; task, object step)
    /// — replaces the sync result's value with the awaited value while preserving done.
    /// </summary>
    private void EmitAsyncFromSyncAwaitContinuation(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "AsyncFromSyncAwaitContinuation",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.TaskOfObject, _types.Object]);
        _asyncFromSyncAwaitContinuation = method;

        var il = method.GetILGenerator();
        var awaiterLocal = il.DeclareLocal(_types.TaskAwaiterOfObject);
        var valueLocal = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.TaskOfObjectGetAwaiter);
        il.Emit(OpCodes.Stloc, awaiterLocal);
        il.Emit(OpCodes.Ldloca, awaiterLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.TaskAwaiterOfObject, "GetResult"));
        il.Emit(OpCodes.Stloc, valueLocal);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetIteratorDone);
        il.Emit(OpCodes.Call, _asyncFromSyncCreateResult);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// static Task&lt;object&gt; AsyncFromSyncAwaitResult(object step)
    /// — awaits Promise/Task/thenable iterator values and rebuilds the result.
    /// </summary>
    private void EmitAsyncFromSyncAwaitResult(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "AsyncFromSyncAwaitResult",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.TaskOfObject,
            [_types.Object]);
        _asyncFromSyncAwaitResult = method;

        var il = method.GetILGenerator();
        var valueLocal = il.DeclareLocal(_types.Object);
        var taskLocal = il.DeclareLocal(_types.TaskOfObject);
        var notPromise = il.DefineLabel();
        var notTask = il.DefineLabel();
        var haveTask = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetIteratorValue);
        il.Emit(OpCodes.Stloc, valueLocal);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brfalse, notPromise);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, runtime.TSPromiseType);
        il.Emit(OpCodes.Callvirt, runtime.TSPromiseTaskGetter);
        il.Emit(OpCodes.Stloc, taskLocal);
        il.Emit(OpCodes.Br, haveTask);

        il.MarkLabel(notPromise);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Brfalse, notTask);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, _types.TaskOfObject);
        il.Emit(OpCodes.Stloc, taskLocal);
        il.Emit(OpCodes.Br, haveTask);

        // Ordinary values and thenables follow the same PromiseResolve-style
        // adoption path used by compiled await expressions.
        il.MarkLabel(notTask);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, runtime.CoerceAwaitableToTaskMethod);
        il.Emit(OpCodes.Stloc, taskLocal);

        il.MarkLabel(haveTask);
        il.Emit(OpCodes.Ldloc, taskLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldftn, _asyncFromSyncAwaitContinuation);
        var continuationType = _types.MakeGenericType(
            typeof(Func<,,>), _types.TaskOfObject, _types.Object, _types.Object);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(continuationType, [_types.Object, typeof(IntPtr)])!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)TaskContinuationOptions.ExecuteSynchronously);
        il.Emit(OpCodes.Callvirt, ResolveAsyncFromSyncContinueWith());
        il.Emit(OpCodes.Ret);
    }

    private MethodInfo ResolveAsyncFromSyncContinueWith() => EmitGenerics.MakeGenericMethod(
        _types.GetMethods(_types.TaskOfObject, BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == "ContinueWith" && m.IsGenericMethodDefinition
                && m.GetParameters() is { Length: 3 } p
                && p[0].ParameterType.IsGenericType
                && p[0].ParameterType.GetGenericTypeDefinition() == typeof(Func<,,>)
                && p[1].ParameterType == typeof(object)
                && p[2].ParameterType == typeof(TaskContinuationOptions)), _types.Object);

    private void EmitAsyncFromSyncIteratorType(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$AsyncFromSyncIterator",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object,
            [runtime.AsyncGeneratorInterfaceType]);

        var iteratorField = typeBuilder.DefineField("_iterator", _types.Object, FieldAttributes.Private);
        var nextField = typeBuilder.DefineField("_next", _types.Object, FieldAttributes.Private);
        var isProtocolField = typeBuilder.DefineField("_isProtocol", _types.Boolean, FieldAttributes.Private);

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.Boolean]);
        runtime.AsyncFromSyncIteratorCtor = ctor;
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, iteratorField);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_2);
        ctorIl.Emit(OpCodes.Stfld, isProtocolField);
        var initialized = ctorIl.DefineLabel();
        ctorIl.Emit(OpCodes.Ldarg_2);
        ctorIl.Emit(OpCodes.Brfalse, initialized);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Call, runtime.GetIteratorNextMethod);
        ctorIl.Emit(OpCodes.Stfld, nextField);
        ctorIl.MarkLabel(initialized);
        ctorIl.Emit(OpCodes.Ret);

        EmitAsyncFromSyncNext(typeBuilder, runtime, iteratorField, isProtocolField, nextField);
        EmitAsyncFromSyncReturn(typeBuilder, runtime, iteratorField, isProtocolField);
        EmitAsyncFromSyncThrow(typeBuilder, runtime, iteratorField, isProtocolField);
        EmitAsyncFromSyncInheritedMembers(typeBuilder, runtime, iteratorField);

        typeBuilder.CreateType();
    }

    private void EmitAsyncFromSyncNext(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        FieldBuilder iteratorField,
        FieldBuilder isProtocolField,
        FieldBuilder nextField)
    {
        var method = typeBuilder.DefineMethod(
            "next",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.TaskOfObject,
            [_types.Object]);
        typeBuilder.DefineMethodOverride(method, runtime.AsyncGeneratorNextMethod);

        var il = method.GetILGenerator();
        var resultTask = il.DeclareLocal(_types.TaskOfObject);
        var stepLocal = il.DeclareLocal(_types.Object);
        var enumLocal = il.DeclareLocal(_types.IEnumeratorOfObject);
        var exLocal = il.DeclareLocal(_types.Exception);
        var clrIterator = il.DefineLabel();
        var exhausted = il.DefineLabel();
        var haveStep = il.DefineLabel();
        var done = il.DefineLabel();

        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, isProtocolField);
        il.Emit(OpCodes.Brfalse, clrIterator);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, iteratorField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, nextField);
        il.Emit(OpCodes.Call, runtime.InvokeCapturedIteratorNext);
        il.Emit(OpCodes.Stloc, stepLocal);
        il.Emit(OpCodes.Br, haveStep);

        il.MarkLabel(clrIterator);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, iteratorField);
        il.Emit(OpCodes.Castclass, _types.IEnumeratorOfObject);
        il.Emit(OpCodes.Stloc, enumLocal);
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, exhausted);
        il.Emit(OpCodes.Ldloc, enumLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumeratorOfObject, "Current"));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _asyncFromSyncCreateResult);
        il.Emit(OpCodes.Stloc, stepLocal);
        il.Emit(OpCodes.Br, haveStep);

        il.MarkLabel(exhausted);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, _asyncFromSyncCreateResult);
        il.Emit(OpCodes.Stloc, stepLocal);

        il.MarkLabel(haveStep);
        il.Emit(OpCodes.Ldloc, stepLocal);
        il.Emit(OpCodes.Call, _asyncFromSyncAwaitResult);
        il.Emit(OpCodes.Stloc, resultTask);
        il.Emit(OpCodes.Leave, done);

        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Stloc, exLocal);
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Call, TaskFromExceptionObject());
        il.Emit(OpCodes.Stloc, resultTask);
        il.Emit(OpCodes.Leave, done);
        il.EndExceptionBlock();

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, resultTask);
        il.Emit(OpCodes.Ret);
    }

    private void EmitAsyncFromSyncReturn(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        FieldBuilder iteratorField,
        FieldBuilder isProtocolField)
    {
        var method = typeBuilder.DefineMethod(
            "return",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.TaskOfObject,
            [_types.Object]);
        typeBuilder.DefineMethodOverride(method, runtime.AsyncGeneratorReturnMethod);

        var il = method.GetILGenerator();
        var resultTask = il.DeclareLocal(_types.TaskOfObject);
        var stepLocal = il.DeclareLocal(_types.Object);
        var fnLocal = il.DeclareLocal(_types.Object);
        var exLocal = il.DeclareLocal(_types.Exception);
        var clrIterator = il.DefineLabel();
        var noProtocolReturn = il.DefineLabel();
        var notGenerator = il.DefineLabel();
        var noDispose = il.DefineLabel();
        var afterDispose = il.DefineLabel();
        var haveStep = il.DefineLabel();
        var done = il.DefineLabel();

        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, isProtocolField);
        il.Emit(OpCodes.Brfalse, clrIterator);

        // Custom sync iterator: call its optional return(value).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, iteratorField);
        il.Emit(OpCodes.Ldstr, "return");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, fnLocal);
        il.Emit(OpCodes.Ldloc, fnLocal);
        il.Emit(OpCodes.Brfalse, noProtocolReturn);
        il.Emit(OpCodes.Ldloc, fnLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, noProtocolReturn);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, iteratorField);
        il.Emit(OpCodes.Ldloc, fnLocal);
        EmitSingleObjectArgumentArray(il, argumentIndex: 1);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Stloc, stepLocal);
        il.Emit(OpCodes.Br, haveStep);

        il.MarkLabel(noProtocolReturn);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, _asyncFromSyncCreateResult);
        il.Emit(OpCodes.Stloc, stepLocal);
        il.Emit(OpCodes.Br, haveStep);

        // Emitted sync generators expose return(value); use it so finally blocks run.
        il.MarkLabel(clrIterator);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, iteratorField);
        il.Emit(OpCodes.Isinst, runtime.GeneratorInterfaceType);
        il.Emit(OpCodes.Brfalse, notGenerator);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, iteratorField);
        il.Emit(OpCodes.Castclass, runtime.GeneratorInterfaceType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.GeneratorReturnMethod);
        il.Emit(OpCodes.Stloc, stepLocal);
        il.Emit(OpCodes.Br, haveStep);

        // Other CLR enumerators are closed through IDisposable.
        il.MarkLabel(notGenerator);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, iteratorField);
        il.Emit(OpCodes.Isinst, _types.IDisposable);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, noDispose);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IDisposable, "Dispose"));
        il.Emit(OpCodes.Br, afterDispose);
        il.MarkLabel(noDispose);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(afterDispose);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, _asyncFromSyncCreateResult);
        il.Emit(OpCodes.Stloc, stepLocal);

        il.MarkLabel(haveStep);
        il.Emit(OpCodes.Ldloc, stepLocal);
        il.Emit(OpCodes.Call, _asyncFromSyncAwaitResult);
        il.Emit(OpCodes.Stloc, resultTask);
        il.Emit(OpCodes.Leave, done);

        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Stloc, exLocal);
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Call, TaskFromExceptionObject());
        il.Emit(OpCodes.Stloc, resultTask);
        il.Emit(OpCodes.Leave, done);
        il.EndExceptionBlock();

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, resultTask);
        il.Emit(OpCodes.Ret);
    }

    private void EmitAsyncFromSyncThrow(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        FieldBuilder iteratorField,
        FieldBuilder isProtocolField)
    {
        var method = typeBuilder.DefineMethod(
            "throw",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.TaskOfObject,
            [_types.Object]);
        typeBuilder.DefineMethodOverride(method, runtime.AsyncGeneratorThrowMethod);

        var il = method.GetILGenerator();
        var fnLocal = il.DeclareLocal(_types.Object);
        var stepLocal = il.DeclareLocal(_types.Object);
        var noThrow = il.DefineLabel();
        var clrIterator = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, isProtocolField);
        il.Emit(OpCodes.Brfalse, clrIterator);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, iteratorField);
        il.Emit(OpCodes.Ldstr, "throw");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, fnLocal);
        il.Emit(OpCodes.Ldloc, fnLocal);
        il.Emit(OpCodes.Brfalse, noThrow);
        il.Emit(OpCodes.Ldloc, fnLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, noThrow);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, iteratorField);
        il.Emit(OpCodes.Ldloc, fnLocal);
        EmitSingleObjectArgumentArray(il, argumentIndex: 1);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Stloc, stepLocal);
        il.Emit(OpCodes.Ldloc, stepLocal);
        il.Emit(OpCodes.Call, _asyncFromSyncAwaitResult);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(clrIterator);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, iteratorField);
        il.Emit(OpCodes.Isinst, runtime.GeneratorInterfaceType);
        il.Emit(OpCodes.Brfalse, noThrow);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, iteratorField);
        il.Emit(OpCodes.Castclass, runtime.GeneratorInterfaceType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.GeneratorThrowMethod);
        il.Emit(OpCodes.Call, _asyncFromSyncAwaitResult);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noThrow);
        il.Emit(OpCodes.Ldstr, "Synchronous iterator does not provide a throw() method.");
        il.Emit(OpCodes.Newobj, _types.ExceptionCtorString);
        il.Emit(OpCodes.Call, TaskFromExceptionObject());
        il.Emit(OpCodes.Ret);
    }

    private void EmitAsyncFromSyncInheritedMembers(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        FieldBuilder iteratorField)
    {
        var current = typeBuilder.DefineMethod(
            "get_Current",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.SpecialName | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.Object,
            Type.EmptyTypes);
        var currentIl = current.GetILGenerator();
        currentIl.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        currentIl.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(
            current, _types.GetPropertyGetter(_types.IAsyncEnumeratorOfObject, "Current"));

        var moveNext = typeBuilder.DefineMethod(
            "MoveNextAsync",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.ValueTaskOfBool,
            Type.EmptyTypes);
        var moveNextIl = moveNext.GetILGenerator();
        var valueTaskBool = moveNextIl.DeclareLocal(_types.ValueTaskOfBool);
        moveNextIl.Emit(OpCodes.Ldloca, valueTaskBool);
        moveNextIl.Emit(OpCodes.Initobj, _types.ValueTaskOfBool);
        moveNextIl.Emit(OpCodes.Ldloc, valueTaskBool);
        moveNextIl.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(
            moveNext, _types.GetMethodNoParams(_types.IAsyncEnumeratorOfObject, "MoveNextAsync"));

        var dispose = typeBuilder.DefineMethod(
            "DisposeAsync",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.ValueTask,
            Type.EmptyTypes);
        var disposeIl = dispose.GetILGenerator();
        var noDispose = disposeIl.DefineLabel();
        var afterDispose = disposeIl.DefineLabel();
        var valueTask = disposeIl.DeclareLocal(_types.ValueTask);
        disposeIl.Emit(OpCodes.Ldarg_0);
        disposeIl.Emit(OpCodes.Ldfld, iteratorField);
        disposeIl.Emit(OpCodes.Isinst, _types.IDisposable);
        disposeIl.Emit(OpCodes.Dup);
        disposeIl.Emit(OpCodes.Brfalse, noDispose);
        disposeIl.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IDisposable, "Dispose"));
        disposeIl.Emit(OpCodes.Br, afterDispose);
        disposeIl.MarkLabel(noDispose);
        disposeIl.Emit(OpCodes.Pop);
        disposeIl.MarkLabel(afterDispose);
        disposeIl.Emit(OpCodes.Ldloca, valueTask);
        disposeIl.Emit(OpCodes.Initobj, _types.ValueTask);
        disposeIl.Emit(OpCodes.Ldloc, valueTask);
        disposeIl.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(
            dispose, _types.GetMethodNoParams(_types.IAsyncDisposable, "DisposeAsync"));

        var getAsyncEnumerator = typeBuilder.DefineMethod(
            "GetAsyncEnumerator",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            _types.IAsyncEnumeratorOfObject,
            [_types.CancellationToken]);
        var getAsyncEnumeratorIl = getAsyncEnumerator.GetILGenerator();
        getAsyncEnumeratorIl.Emit(OpCodes.Ldarg_0);
        getAsyncEnumeratorIl.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(
            getAsyncEnumerator,
            _types.GetMethod(_types.IAsyncEnumerableOfObject, "GetAsyncEnumerator", _types.CancellationToken));
    }

    /// <summary>
    /// static object AdaptSyncIterableToAsyncGenerator(object source)
    /// </summary>
    private void EmitAdaptSyncIterableToAsyncGenerator(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "AdaptSyncIterableToAsyncGenerator",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        runtime.AdaptSyncIterableToAsyncGenerator = method;

        var il = method.GetILGenerator();
        var iteratorFnLocal = il.DeclareLocal(_types.Object);
        var iteratorLocal = il.DeclareLocal(_types.Object);
        var trySyncProtocol = il.DefineLabel();
        var tryClrIterator = il.DefineLabel();
        var tryEnumerable = il.DefineLabel();
        var materialize = il.DefineLabel();
        var constructClr = il.DefineLabel();

        // A genuine async generator already implements the common interface.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.AsyncGeneratorInterfaceType);
        il.Emit(OpCodes.Brfalse, trySyncProtocol);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        // A custom Symbol.iterator must be called exactly once and retains its
        // return/throw protocol for early-close behavior.
        il.MarkLabel(trySyncProtocol);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolIterator);
        il.Emit(OpCodes.Call, runtime.GetIteratorFunction);
        il.Emit(OpCodes.Stloc, iteratorFnLocal);
        il.Emit(OpCodes.Ldloc, iteratorFnLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, tryClrIterator);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iteratorFnLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newobj, runtime.AsyncFromSyncIteratorCtor);
        il.Emit(OpCodes.Ret);

        // Preserve an existing object enumerator or obtain one once from an
        // IEnumerable<object>. This covers arrays, Sets, and sync generators.
        il.MarkLabel(tryClrIterator);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.IEnumeratorOfObject);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, tryEnumerable);
        il.Emit(OpCodes.Stloc, iteratorLocal);
        il.Emit(OpCodes.Br, constructClr);
        il.MarkLabel(tryEnumerable);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.IEnumerableOfObject);
        il.Emit(OpCodes.Brfalse, materialize);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.NormalizeToEnumerator);
        il.Emit(OpCodes.Stloc, iteratorLocal);
        il.Emit(OpCodes.Br, constructClr);

        // Strings, Maps, typed arrays, Buffers, and non-generic CLR iterables
        // use IterateToList's existing JS-aware normalization, then iterate the
        // resulting dense object list.
        il.MarkLabel(materialize);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolIterator);
        il.Emit(OpCodes.Ldtoken, runtime.RuntimeType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle"));
        il.Emit(OpCodes.Call, runtime.IterateToList);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerableOfObject, "GetEnumerator"));
        il.Emit(OpCodes.Stloc, iteratorLocal);

        il.MarkLabel(constructClr);
        il.Emit(OpCodes.Ldloc, iteratorLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, runtime.AsyncFromSyncIteratorCtor);
        il.Emit(OpCodes.Ret);
    }

    private MethodInfo TaskFromExceptionObject() => EmitGenerics.MakeGenericMethod(
        typeof(Task).GetMethod("FromException", 1, [typeof(Exception)])!, _types.Object);

    private void EmitSingleObjectArgumentArray(ILGenerator il, int argumentIndex)
    {
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg, argumentIndex);
        il.Emit(OpCodes.Stelem_Ref);
    }
}
