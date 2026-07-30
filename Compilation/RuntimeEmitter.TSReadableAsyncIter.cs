using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the <c>[Symbol.asyncIterator]</c> surface for the standalone <c>$Readable</c> class
/// (#1024), so <c>for await (const chunk of readable)</c> works in compiled output.
/// Mirrors <see cref="SharpTS.Runtime.Types.SharpTSReadable"/>'s interpreter iterator:
/// <list type="bullet">
///   <item><c>GetAsyncIterator()</c> returns a <c>{ next, return }</c> iterator object.</item>
///   <item><c>IterNext()</c> yields one buffered chunk, settles done on end, rejects on error,
///     or parks a <see cref="TaskCompletionSource{TResult}"/> for a slow producer.</item>
///   <item><c>IterReturn()</c> destroys the stream for an early <c>break</c>/return/throw.</item>
/// </list>
/// All BCL-only — no SharpTS.dll dependency, so standalone output is preserved.
/// </summary>
public partial class RuntimeEmitter
{
    private MethodBuilder _tsReadableMakeIterResult = null!;
    private MethodBuilder _tsReadableIterNext = null!;
    private MethodBuilder _tsReadableIterReturn = null!;

    /// <summary>
    /// Phase 2b: emit the async-iterator methods on <c>$Readable</c>. Must run before
    /// <c>CreateType()</c>. Depends only on BCL types plus the already-emitted
    /// <c>$TSFunction</c> / <c>$PromiseRejectedException</c> ctors — never on a
    /// $Runtime method (those are emitted later, after the stream classes).
    /// </summary>
    private void EmitTSReadableAsyncIteratorMethods(TypeBuilder typeBuilder, EmittedRuntime runtime, Type queueType)
    {
        EmitTSReadableMakeIterResult(typeBuilder, runtime);
        EmitTSReadableIterNext(typeBuilder, runtime, queueType);
        EmitTSReadableIterReturn(typeBuilder, runtime, queueType);
        EmitTSReadableGetAsyncIterator(typeBuilder, runtime);
    }

    private static MethodInfo TaskFromResultObject(TypeProvider types)
        => EmitGenerics.MakeGenericMethod(types.Task.GetMethod("FromResult")!, types.Object);

    /// <summary>
    /// private Dictionary&lt;string,object?&gt; MakeIterResult(object? value, bool done)
    /// — builds the <c>{ value, done }</c> iterator-result record.
    /// </summary>
    private void EmitTSReadableMakeIterResult(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MakeIterResult",
            MethodAttributes.Private,
            _types.Object,
            [_types.Object, _types.Boolean]);
        _tsReadableMakeIterResult = method;

        var il = method.GetILGenerator();
        var dictSetItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);

        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, dictLocal);

        // dict["value"] = value;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, dictSetItem);

        // dict["done"] = (object)done;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "done");
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, dictSetItem);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// public object IterNext() — returns a Task&lt;object?&gt; resolving to { value, done }.
    /// </summary>
    private void EmitTSReadableIterNext(TypeBuilder typeBuilder, EmittedRuntime runtime, Type queueType)
    {
        var method = typeBuilder.DefineMethod(
            "IterNext",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes);
        _tsReadableIterNext = method;

        var il = method.GetILGenerator();
        var countGetter = queueType.GetProperty("Count")!.GetGetMethod()!;
        var dequeue = queueType.GetMethod("Dequeue")!;
        var fromResult = TaskFromResultObject(_types);
        var tcsCtor = _types.GetConstructor(_types.TaskCompletionSourceOfObject, [typeof(TaskCreationOptions)])!;
        var tcsTaskGetter = _types.GetProperty(_types.TaskCompletionSourceOfObject, "Task")!.GetGetMethod()!;
        var trySetException = _types.GetMethod(_types.TaskCompletionSourceOfObject, "TrySetException", [_types.Exception])!;

        var notBuffered = il.DefineLabel();
        var notErrored = il.DefineLabel();
        var notEnded = il.DefineLabel();
        var endedReturnLabel = il.DefineLabel();

        // if (_readBuffer.Count > 0) return Task.FromResult(MakeIterResult(_readBuffer.Dequeue(), false));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, notBuffered);

        il.Emit(OpCodes.Ldarg_0); // this (for MakeIterResult)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Callvirt, dequeue);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _tsReadableMakeIterResult);
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notBuffered);
        // if (_errored) { var tcs = new TCS(); tcs.TrySetException(new $PromiseRejectedException(_error)); return tcs.Task; }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableErroredField);
        il.Emit(OpCodes.Brfalse, notErrored);

        var tcsErrLocal = il.DeclareLocal(_types.TaskCompletionSourceOfObject);
        il.Emit(OpCodes.Ldc_I4_0); // TaskCreationOptions.None
        il.Emit(OpCodes.Newobj, tcsCtor);
        il.Emit(OpCodes.Stloc, tcsErrLocal);
        il.Emit(OpCodes.Ldloc, tcsErrLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableErrorField);
        il.Emit(OpCodes.Newobj, runtime.TSPromiseRejectedExceptionCtor);
        il.Emit(OpCodes.Callvirt, trySetException);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, tcsErrLocal);
        il.Emit(OpCodes.Callvirt, tcsTaskGetter);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notErrored);
        // if (_ended || _destroyed) return Task.FromResult(MakeIterResult(null, true));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableEndedField);
        il.Emit(OpCodes.Brtrue, endedReturnLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableDestroyedField);
        il.Emit(OpCodes.Brfalse, notEnded);

        il.MarkLabel(endedReturnLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, _tsReadableMakeIterResult);
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEnded);
        // _iterWaiter = new TCS(RunContinuationsAsynchronously); return _iterWaiter.Task;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)TaskCreationOptions.RunContinuationsAsynchronously);
        il.Emit(OpCodes.Newobj, tcsCtor);
        il.Emit(OpCodes.Stfld, _tsReadableIterWaiterField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableIterWaiterField);
        il.Emit(OpCodes.Callvirt, tcsTaskGetter);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// public object IterReturn() — destroys the stream, settles any parked pull, returns a
    /// resolved Task with { value: undefined, done: true }.
    /// </summary>
    private void EmitTSReadableIterReturn(TypeBuilder typeBuilder, EmittedRuntime runtime, Type queueType)
    {
        var method = typeBuilder.DefineMethod(
            "IterReturn",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes);
        _tsReadableIterReturn = method;

        var il = method.GetILGenerator();
        var clear = queueType.GetMethod("Clear")!;
        var listClear = _types.GetMethod(_types.ListOfObject, "Clear");
        var fromResult = TaskFromResultObject(_types);

        // if (!_destroyed) { _destroyed = true; _readable = false; _readBuffer.Clear(); _pipeDestinations.Clear(); }
        var alreadyDestroyed = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableDestroyedField);
        il.Emit(OpCodes.Brtrue, alreadyDestroyed);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsReadableDestroyedField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsReadableReadableField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableBufferField);
        il.Emit(OpCodes.Callvirt, clear);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadablePipeDestinationsField);
        il.Emit(OpCodes.Callvirt, listClear);
        il.MarkLabel(alreadyDestroyed);

        // Settle a parked pull as done (best-effort).
        EmitSettleIterWaiterDone(il, runtime);

        // return Task.FromResult(MakeIterResult(null, true));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, _tsReadableMakeIterResult);
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// public object GetAsyncIterator() — returns a { next, return } iterator object whose
    /// methods bind back to this $Readable's IterNext / IterReturn.
    /// </summary>
    private void EmitTSReadableGetAsyncIterator(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetAsyncIterator",
            MethodAttributes.Public,
            _types.Object,
            Type.EmptyTypes);
        runtime.TSReadableGetAsyncIterator = method;

        var il = method.GetILGenerator();
        var dictSetItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);

        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, dictLocal);

        // dict["next"] = new $TSFunction(this, IterNext);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "next");
        il.Emit(OpCodes.Ldarg_0);
        EmitInstanceMethodInfoLiteral(il, _tsReadableIterNext, typeBuilder);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Callvirt, dictSetItem);

        // dict["return"] = new $TSFunction(this, IterReturn);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "return");
        il.Emit(OpCodes.Ldarg_0);
        EmitInstanceMethodInfoLiteral(il, _tsReadableIterReturn, typeBuilder);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Callvirt, dictSetItem);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the load of a MethodInfo literal for an intra-assembly MethodBuilder
    /// (resolves at runtime via the 2-arg GetMethodFromHandle).
    /// </summary>
    private void EmitInstanceMethodInfoLiteral(ILGenerator il, MethodBuilder method, Type declaringType)
    {
        il.Emit(OpCodes.Ldtoken, method);
        il.Emit(OpCodes.Ldtoken, declaringType);
        il.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandleWithType);
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
    }

    /// <summary>
    /// Emits: if (_iterWaiter != null) { var w = _iterWaiter; _iterWaiter = null; w.TrySetResult(MakeIterResult(null, true)); }
    /// Leaves the evaluation stack unchanged. Used by Push (EOF) / Destroy / IterReturn.
    /// </summary>
    private void EmitSettleIterWaiterDone(ILGenerator il, EmittedRuntime runtime)
    {
        var trySetResult = _types.GetMethod(_types.TaskCompletionSourceOfObject, "TrySetResult", [_types.Object])!;
        var noWaiter = il.DefineLabel();
        var wLocal = il.DeclareLocal(_types.TaskCompletionSourceOfObject);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableIterWaiterField);
        il.Emit(OpCodes.Stloc, wLocal);
        il.Emit(OpCodes.Ldloc, wLocal);
        il.Emit(OpCodes.Brfalse, noWaiter);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, _tsReadableIterWaiterField);

        il.Emit(OpCodes.Ldloc, wLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, _tsReadableMakeIterResult);
        il.Emit(OpCodes.Callvirt, trySetResult);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noWaiter);
    }

    /// <summary>
    /// Emits: if (_iterWaiter != null) { var w = _iterWaiter; _iterWaiter = null; w.TrySetException(new $PromiseRejectedException(arg1)); }
    /// Leaves the stack unchanged. Loads the error from arg1 (Destroy's error parameter).
    /// </summary>
    private void EmitFaultIterWaiter(ILGenerator il, EmittedRuntime runtime)
    {
        var trySetException = _types.GetMethod(_types.TaskCompletionSourceOfObject, "TrySetException", [_types.Exception])!;
        var noWaiter = il.DefineLabel();
        var wLocal = il.DeclareLocal(_types.TaskCompletionSourceOfObject);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableIterWaiterField);
        il.Emit(OpCodes.Stloc, wLocal);
        il.Emit(OpCodes.Ldloc, wLocal);
        il.Emit(OpCodes.Brfalse, noWaiter);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, _tsReadableIterWaiterField);

        il.Emit(OpCodes.Ldloc, wLocal);
        il.Emit(OpCodes.Ldarg_1); // error
        il.Emit(OpCodes.Newobj, runtime.TSPromiseRejectedExceptionCtor);
        il.Emit(OpCodes.Callvirt, trySetException);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noWaiter);
    }

    /// <summary>
    /// Emits: if (_iterWaiter != null) { var w = _iterWaiter; _iterWaiter = null; w.TrySetResult(MakeIterResult(chunk, false)); return true; }
    /// The chunk is loaded from arg1 (Push's chunk parameter). Used by Push's data path; on
    /// delivery it returns <c>true</c> from Push so the chunk is not also buffered.
    /// </summary>
    private void EmitDeliverChunkToIterWaiterAndReturn(ILGenerator il, EmittedRuntime runtime)
    {
        var trySetResult = _types.GetMethod(_types.TaskCompletionSourceOfObject, "TrySetResult", [_types.Object])!;
        var noWaiter = il.DefineLabel();
        var wLocal = il.DeclareLocal(_types.TaskCompletionSourceOfObject);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsReadableIterWaiterField);
        il.Emit(OpCodes.Stloc, wLocal);
        il.Emit(OpCodes.Ldloc, wLocal);
        il.Emit(OpCodes.Brfalse, noWaiter);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, _tsReadableIterWaiterField);

        il.Emit(OpCodes.Ldloc, wLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1); // chunk
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _tsReadableMakeIterResult);
        il.Emit(OpCodes.Callvirt, trySetResult);
        il.Emit(OpCodes.Pop);

        // return true; (chunk consumed by the iterator)
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noWaiter);
    }
}
