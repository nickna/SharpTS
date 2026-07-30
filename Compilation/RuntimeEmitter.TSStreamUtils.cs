using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $StreamUtils class with static helper methods for stream utilities:
/// Finished, Pipeline, ReadableFrom, PromisePipeline, PromiseFinished.
/// These are emitted into the output assembly for standalone DLL support.
/// </summary>
public partial class RuntimeEmitter
{
    // Cleanup class fields for finished() return value
    private FieldBuilder _cleanupStreamField = null!;
    private FieldBuilder _cleanupCallbackField = null!;
    private ConstructorBuilder _cleanupCtor = null!;
    private MethodBuilder _cleanupInvokeMethod = null!;

    private void EmitTSStreamUtilsClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Emit cleanup closure class first
        EmitStreamFinishedCleanupClass(moduleBuilder, runtime);

        var typeBuilder = moduleBuilder.DefineType(
            "$StreamUtils",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit
        );

        // compose bridge closure (#1028) — needs $Duplex/$Writable Write/End + $Readable Push.
        EmitStreamComposeBridgeClass(moduleBuilder, runtime);

        EmitStreamFinished(typeBuilder, runtime);
        EmitStreamPipeline(typeBuilder, runtime);
        EmitStreamReadableFrom(typeBuilder, runtime);
        EmitStreamDuplexFrom(typeBuilder, runtime);     // #1028
        EmitStreamComposeMethod(typeBuilder, runtime);  // #1028
        EmitStreamDefaultHwm(typeBuilder, runtime);     // #1030
        EmitStreamPromisePipeline(typeBuilder, runtime);
        EmitStreamPromiseFinished(typeBuilder, runtime);
        EmitStreamConstructorFactories(typeBuilder, runtime);

        typeBuilder.CreateType();

        // Register utility functions as module methods for TSFunction wrapping
        runtime.RegisterBuiltInModuleMethod("stream", "finished", runtime.StreamFinished);
        runtime.RegisterBuiltInModuleMethod("stream", "pipeline", runtime.StreamPipeline);
    }

    /// <summary>
    /// Emits $StreamFinishedCleanup class with stream/callback fields and Invoke method
    /// that calls stream.Off() for all 4 events.
    /// </summary>
    private void EmitStreamFinishedCleanupClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$StreamFinishedCleanup",
            TypeAttributes.Public | TypeAttributes.BeforeFieldInit
        );

        _cleanupStreamField = typeBuilder.DefineField("_stream", runtime.TSEventEmitterType, FieldAttributes.Public);
        _cleanupCallbackField = typeBuilder.DefineField("_callback", _types.Object, FieldAttributes.Public);

        // Constructor(stream, callback)
        _cleanupCtor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [runtime.TSEventEmitterType, _types.Object]);
        {
            var il = _cleanupCtor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, _cleanupStreamField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, _cleanupCallbackField);
            il.Emit(OpCodes.Ret);
        }

        // Invoke(object[] args) → null  (removes listeners)
        _cleanupInvokeMethod = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [_types.ObjectArray]);
        {
            var il = _cleanupInvokeMethod.GetILGenerator();

            // stream.Off("end", callback)
            EmitOffCall(il, runtime, "end");
            EmitOffCall(il, runtime, "finish");
            EmitOffCall(il, runtime, "error");
            EmitOffCall(il, runtime, "close");

            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        typeBuilder.CreateType();

        void EmitOffCall(ILGenerator il, EmittedRuntime rt, string eventName)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _cleanupStreamField);
            il.Emit(OpCodes.Ldstr, eventName);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _cleanupCallbackField);
            il.Emit(OpCodes.Callvirt, rt.TSEventEmitterOff);
            il.Emit(OpCodes.Pop); // Off returns the emitter
        }
    }

    /// <summary>
    /// Emits: public static object Finished(object[] args)
    /// Attaches end/finish/error/close listeners, calls callback on first trigger.
    /// </summary>
    private void EmitStreamFinished(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Finished",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.MakeArrayType(_types.Object)]
        );
        runtime.StreamFinished = method;

        var il = method.GetILGenerator();

        // Extract stream from args[0]
        var streamLocal = il.DeclareLocal(runtime.TSEventEmitterType); // local 0: stream
        var callbackLocal = il.DeclareLocal(_types.Object); // local 1: callback
        var optionsLocal = il.DeclareLocal(_types.Object); // local 2: options (null if 2-arg)
        var readableOptLocal = il.DeclareLocal(_types.Boolean); // local 3: readable option (default true)
        var writableOptLocal = il.DeclareLocal(_types.Boolean); // local 4: writable option (default true)

        // Defaults
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, readableOptLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, writableOptLocal);

        // Cast args[0] to $EventEmitter
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, runtime.TSEventEmitterType);
        il.Emit(OpCodes.Stloc, streamLocal);

        // Determine callback position
        var threeArgs = il.DefineLabel();
        var afterCallback = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Bge, threeArgs);

        // 2-arg case: callback = args[1], options = null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, callbackLocal);
        il.Emit(OpCodes.Br, afterCallback);

        // 3-arg case: options = args[1], callback = args[2]
        il.MarkLabel(threeArgs);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, optionsLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, callbackLocal);

        // Extract readable/writable options if present
        EmitExtractBooleanOption(il, runtime, optionsLocal, "readable", readableOptLocal);
        EmitExtractBooleanOption(il, runtime, optionsLocal, "writable", writableOptLocal);

        il.MarkLabel(afterCallback);

        // Attach once listeners based on options
        // if (readable) stream.Once("end", callback)
        var skipEnd = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, readableOptLocal);
        il.Emit(OpCodes.Brfalse, skipEnd);
        EmitOnceCall(il, runtime, streamLocal, callbackLocal, "end");
        il.MarkLabel(skipEnd);

        // if (writable) stream.Once("finish", callback)
        var skipFinish = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, writableOptLocal);
        il.Emit(OpCodes.Brfalse, skipFinish);
        EmitOnceCall(il, runtime, streamLocal, callbackLocal, "finish");
        il.MarkLabel(skipFinish);

        // Always listen for error and close
        EmitOnceCall(il, runtime, streamLocal, callbackLocal, "error");
        EmitOnceCall(il, runtime, streamLocal, callbackLocal, "close");

        // Create cleanup closure: new $StreamFinishedCleanup(stream, callback)
        il.Emit(OpCodes.Ldloc, streamLocal);
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Newobj, _cleanupCtor);
        var cleanupInstanceLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, cleanupInstanceLocal);

        // Return new $TSFunction(cleanupInstance, cleanupInvokeMethod)
        il.Emit(OpCodes.Ldloc, cleanupInstanceLocal);
        il.Emit(OpCodes.Ldtoken, _cleanupInvokeMethod);
        il.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandle);
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// If options has a boolean property with the given name set to false, store false in targetLocal.
    /// Uses $IHasFields.GetProperty interface which is available early in emission order.
    /// </summary>
    private void EmitExtractBooleanOption(ILGenerator il, EmittedRuntime runtime,
        LocalBuilder optionsLocal, string propName, LocalBuilder targetLocal)
    {
        var skipLabel = il.DefineLabel();
        var valueLocal = il.DeclareLocal(_types.Object);

        // if (options == null) skip
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Brfalse, skipLabel);

        // Try $IHasFields interface first, then raw Dictionary<string,object?>
        var notHasFieldsLabel = il.DefineLabel();
        var tryDictLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brfalse, tryDictLabel);

        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Ldstr, propName);
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsGetProperty);
        il.Emit(OpCodes.Stloc, valueLocal);
        var afterGetLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, afterGetLabel);

        // Fallback: raw Dictionary<string, object?>
        il.MarkLabel(tryDictLabel);
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Isinst, typeof(Dictionary<string, object?>));
        il.Emit(OpCodes.Brfalse, notHasFieldsLabel);
        {
            var dictLocal = il.DeclareLocal(typeof(Dictionary<string, object?>));
            il.Emit(OpCodes.Ldloc, optionsLocal);
            il.Emit(OpCodes.Castclass, typeof(Dictionary<string, object?>));
            il.Emit(OpCodes.Stloc, dictLocal);

            // dict.TryGetValue(propName, out value)
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Ldstr, propName);
            il.Emit(OpCodes.Ldloca, valueLocal);
            il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object?>).GetMethod("TryGetValue")!);
            il.Emit(OpCodes.Brfalse, notHasFieldsLabel); // key not found
        }

        il.MarkLabel(afterGetLabel);

        // Check if value is bool
        var checkIntLabel = il.DefineLabel();
        var doneExtractLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, typeof(bool));
        il.Emit(OpCodes.Brfalse, checkIntLabel);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, typeof(bool));
        il.Emit(OpCodes.Stloc, targetLocal);
        il.Emit(OpCodes.Br, doneExtractLabel);

        // Also check if value is int (compiled booleans may be boxed as int32)
        il.MarkLabel(checkIntLabel);
        var checkDoubleLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, typeof(int));
        il.Emit(OpCodes.Brfalse, checkDoubleLabel);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Cgt_Un);
        il.Emit(OpCodes.Stloc, targetLocal);
        il.Emit(OpCodes.Br, doneExtractLabel);

        // Also check double (JS false may be compiled as 0.0)
        il.MarkLabel(checkDoubleLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, typeof(double));
        il.Emit(OpCodes.Brfalse, doneExtractLabel);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, typeof(double));
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq); // true if value != 0.0
        il.Emit(OpCodes.Stloc, targetLocal);

        il.MarkLabel(doneExtractLabel);
        il.MarkLabel(notHasFieldsLabel);
        il.MarkLabel(skipLabel);
    }

    private static void EmitOnceCall(ILGenerator il, EmittedRuntime runtime, LocalBuilder stream, LocalBuilder callback, string eventName)
    {
        // stream.Once(eventName, callback) returns the emitter - pop it
        il.Emit(OpCodes.Ldloc, stream);
        il.Emit(OpCodes.Ldstr, eventName);
        il.Emit(OpCodes.Ldloc, callback);
        il.Emit(OpCodes.Callvirt, runtime.TSEventEmitterOnce);
        il.Emit(OpCodes.Pop); // Once returns the emitter, discard it
    }

    /// <summary>
    /// Emits: public static object Pipeline(object[] args)
    /// Pipes streams together. The last arg may be a callback function.
    /// Returns the last stream (not the callback).
    /// </summary>
    private void EmitStreamPipeline(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Pipeline",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.MakeArrayType(_types.Object)]
        );
        runtime.StreamPipeline = method;

        var il = method.GetILGenerator();

        var argsLocal = il.DeclareLocal(_types.MakeArrayType(_types.Object)); // local 0
        var streamCountLocal = il.DeclareLocal(_types.Int32); // local 1: number of streams (excluding callback)
        var indexLocal = il.DeclareLocal(_types.Int32); // local 2
        var callbackLocal = il.DeclareLocal(_types.Object); // local 3: callback (if present)

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, argsLocal);

        // Determine if last arg is a callback (TSFunction) or a stream ($EventEmitter).
        // If the last arg is NOT a stream, treat it as the callback.
        var lastArgLocal = il.DeclareLocal(_types.Object); // local 4
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, lastArgLocal);

        var noCallbackLabel = il.DefineLabel();
        var afterCallbackCheck = il.DefineLabel();

        // if (lastArg is $EventEmitter) → no callback, all args are streams
        il.Emit(OpCodes.Ldloc, lastArgLocal);
        il.Emit(OpCodes.Isinst, runtime.TSEventEmitterType);
        il.Emit(OpCodes.Brtrue, noCallbackLabel);

        // Last arg is the callback: streamCount = args.Length - 1
        il.Emit(OpCodes.Ldloc, lastArgLocal);
        il.Emit(OpCodes.Stloc, callbackLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, streamCountLocal);
        il.Emit(OpCodes.Br, afterCallbackCheck);

        // No callback: streamCount = args.Length
        il.MarkLabel(noCallbackLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, callbackLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, streamCountLocal);

        il.MarkLabel(afterCallbackCheck);

        // Loop: for (i = 0; i < streamCount - 1; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, streamCountLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Bge, loopEnd);

        // Cast args[i] to $Readable and call Pipe(args[i+1], null)
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, runtime.TSReadableType);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldnull); // options = null
        il.Emit(OpCodes.Callvirt, runtime.TSReadablePipe);
        il.Emit(OpCodes.Pop); // Pipe returns destination

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // If callback present, attach it via Finished() on the last stream
        // so it fires when the pipeline completes (not immediately)
        var skipCallbackInvoke = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Brfalse, skipCallbackInvoke);

        // Call Finished(lastStream, callback) to attach completion listeners
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        // lastStream = args[streamCount - 1]
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldloc, streamCountLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.StreamFinished);
        il.Emit(OpCodes.Pop); // Finished returns cleanup function, discard

        il.MarkLabel(skipCallbackInvoke);

        // Return last stream: args[streamCount - 1]
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldloc, streamCountLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object ReadableFrom(object[] args)
    /// Creates a Readable from args[0] (array/list), pushes items, pushes null.
    /// </summary>
    private void EmitStreamReadableFrom(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReadableFrom",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.MakeArrayType(_types.Object)]
        );
        runtime.StreamReadableFrom = method;

        var il = method.GetILGenerator();

        // Create new $Readable instance
        var streamLocal = il.DeclareLocal(runtime.TSReadableType); // local 0
        il.Emit(OpCodes.Newobj, runtime.TSReadableCtor);
        il.Emit(OpCodes.Stloc, streamLocal);

        // Set object mode
        il.Emit(OpCodes.Ldloc, streamLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, runtime.TSReadableSetObjectMode);

        // Get iterable from args[0]
        var iterableLocal = il.DeclareLocal(_types.Object); // local 1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, iterableLocal);

        // If iterable is List<object?>, iterate and push
        var notListLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, iterableLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, notListLabel);

        // Cast to List<object?> and iterate
        var listLocal = il.DeclareLocal(_types.ListOfObject); // local 2
        il.Emit(OpCodes.Ldloc, iterableLocal);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);

        var countLocal = il.DeclareLocal(_types.Int32); // local 3
        var idxLocal = il.DeclareLocal(_types.Int32); // local 4
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, countLocal);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, idxLocal);

        var pushLoop = il.DefineLabel();
        var pushEnd = il.DefineLabel();

        il.MarkLabel(pushLoop);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, pushEnd);

        // Push item: stream.Push(list[idx])
        il.Emit(OpCodes.Ldloc, streamLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item")!);
        il.Emit(OpCodes.Callvirt, runtime.TSReadablePush);
        il.Emit(OpCodes.Pop); // Push returns bool

        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, idxLocal);
        il.Emit(OpCodes.Br, pushLoop);

        il.MarkLabel(pushEnd);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(notListLabel);
        // For non-list types, skip

        il.MarkLabel(doneLabel);

        // Push null for EOF
        il.Emit(OpCodes.Ldloc, streamLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, runtime.TSReadablePush);
        il.Emit(OpCodes.Pop);

        // Return the stream
        il.Emit(OpCodes.Ldloc, streamLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object PromisePipeline(object[] args)
    /// </summary>
    private void EmitStreamPromisePipeline(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "PromisePipeline",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.MakeArrayType(_types.Object)]
        );
        runtime.StreamPromisePipeline = method;

        var il = method.GetILGenerator();

        // Call Pipeline and wrap result
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.StreamPipeline);
        // Pipeline returns an object (the last stream), wrap in Task
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(typeof(Task).GetMethod("FromResult")!, _types.Object));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object PromiseFinished(object[] args)
    /// </summary>
    private void EmitStreamPromiseFinished(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "PromiseFinished",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.MakeArrayType(_types.Object)]
        );
        runtime.StreamPromiseFinished = method;

        var il = method.GetILGenerator();

        // Call Finished to set up listeners
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.StreamFinished);
        il.Emit(OpCodes.Pop); // Finished returns null, discard

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(typeof(Task).GetMethod("FromResult")!, _types.Object));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits factory methods for each stream constructor so they can be wrapped as TSFunction
    /// and used as first-class values (e.g., typeof Readable === "function").
    /// Each factory takes (object[] args) and returns a new stream instance with options applied.
    /// </summary>
    private void EmitStreamConstructorFactories(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Simple factory methods that just create instances.
        // These are wrapped as TSFunction for first-class constructor references
        // (e.g., typeof Readable === "function").
        // Direct `new Readable(options)` calls still use the optimized constructor
        // emission in ExpressionEmitterBase.Constructors.cs which handles options inline.
        EmitSimpleFactory(typeBuilder, runtime, "Readable", runtime.TSReadableCtor);
        EmitSimpleFactory(typeBuilder, runtime, "Writable", runtime.TSWritableCtor);
        EmitSimpleFactory(typeBuilder, runtime, "Duplex", runtime.TSDuplexCtor);
        EmitSimpleFactory(typeBuilder, runtime, "Transform", runtime.TSTransformCtor);
        EmitSimpleFactory(typeBuilder, runtime, "PassThrough", runtime.TSPassThroughCtor);

        // Also register utility functions for stream/promises
        runtime.RegisterBuiltInModuleMethod("stream/promises", "pipeline", runtime.StreamPromisePipeline);
        runtime.RegisterBuiltInModuleMethod("stream/promises", "finished", runtime.StreamPromiseFinished);
    }

    private void EmitSimpleFactory(TypeBuilder typeBuilder, EmittedRuntime runtime,
        string name, ConstructorBuilder ctor)
    {
        var method = typeBuilder.DefineMethod($"Create{name}",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.ObjectArray]);
        runtime.RegisterBuiltInModuleMethod("stream", name, method);

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Ret);
    }
}
