using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits timers/promises support methods into the $Runtime class.
/// Uses Task.Delay for non-blocking promise-based timers with event loop ref counting.
/// </summary>
public partial class RuntimeEmitter
{
    private TypeBuilder _timerPromiseClosureType = null!;
    private FieldBuilder _timerPromiseClosureValue = null!;
    private FieldBuilder _timerPromiseClosureToken = null!;
    private ConstructorBuilder _timerPromiseClosureCtor = null!;
    private MethodBuilder _timerPromiseClosureOnComplete = null!;

    // Async interval closure fields
    private TypeBuilder _asyncIntervalClosureType = null!;
    private FieldBuilder _asyncIntervalClosureDelayMs = null!;
    private FieldBuilder _asyncIntervalClosureValue = null!;
    private FieldBuilder _asyncIntervalClosureDone = null!;
    private FieldBuilder _asyncIntervalClosureSelf = null!;
    private FieldBuilder _asyncIntervalClosureToken = null!;
    private ConstructorBuilder _asyncIntervalClosureCtor = null!;
    private MethodBuilder _asyncIntervalClosureNext = null!;
    private MethodBuilder _asyncIntervalClosureReturn = null!;
    private MethodBuilder _asyncIntervalClosureGetSelf = null!;

    private void EmitTimerPromisesMethods(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var moduleBuilder = runtimeType.Module as ModuleBuilder ?? throw new Exception("need ModuleBuilder");
        EmitTimerPromiseClosure(moduleBuilder, runtime);
        EmitExtractTimerOptionsToken(runtimeType, runtime);
        EmitSetTimeoutPromise(runtimeType, runtime);
        EmitSetTimeoutPromiseWithSignal(runtimeType, runtime);
        EmitSetImmediatePromise(runtimeType, runtime);
        EmitSetImmediatePromiseWithSignal(runtimeType, runtime);
        EmitAsyncIntervalClosure(moduleBuilder, runtime);
        EmitSetIntervalAsyncIterable(runtimeType, runtime);
        EmitSetIntervalAsyncIterableWithSignal(runtimeType, runtime);
    }

    /// <summary>
    /// Emits: private static CancellationToken ExtractTimerOptionsToken(object options)
    /// Extracts the CancellationToken from an options dict's "signal" key.
    /// Returns CancellationToken.None if options is null, not a dict, has no signal,
    /// or signal is not a $AbortSignal dict.
    /// </summary>
    private void EmitExtractTimerOptionsToken(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "ExtractTimerOptionsToken",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.CancellationToken,
            [_types.Object]);
        runtime.ExtractTimerOptionsToken = method;

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryStringObject;
        var ctType = _types.CancellationToken;

        var noneLabel = il.DefineLabel();
        var dictLocal = il.DeclareLocal(dictType);
        var signalLocal = il.DeclareLocal(_types.Object);
        var resultLocal = il.DeclareLocal(ctType);

        // if (options == null) return CancellationToken.None
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, noneLabel);

        // var dict = options as Dictionary<string, object?>
        // if (dict == null) return CancellationToken.None
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, noneLabel);

        // if (!dict.TryGetValue("signal", out signal)) return None
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "signal");
        il.Emit(OpCodes.Ldloca, signalLocal);
        var tryGetValue = dictType.GetMethod("TryGetValue", [_types.String, _types.Object.MakeByRefType()])!;
        il.Emit(OpCodes.Callvirt, tryGetValue);
        il.Emit(OpCodes.Brfalse, noneLabel);

        // if (signal is not Dictionary<string, object?> signalDict) return None
        il.Emit(OpCodes.Ldloc, signalLocal);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, noneLabel);

        // if (!signalDict.TryGetValue("_token", out tokenObj)) return None
        var tokenObjLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "_token");
        il.Emit(OpCodes.Ldloca, tokenObjLocal);
        il.Emit(OpCodes.Callvirt, tryGetValue);
        il.Emit(OpCodes.Brfalse, noneLabel);

        // if (tokenObj is not CancellationToken) return None
        il.Emit(OpCodes.Ldloc, tokenObjLocal);
        il.Emit(OpCodes.Isinst, ctType);
        il.Emit(OpCodes.Brfalse, noneLabel);

        // return (CancellationToken)tokenObj
        il.Emit(OpCodes.Ldloc, tokenObjLocal);
        il.Emit(OpCodes.Unbox_Any, ctType);
        il.Emit(OpCodes.Ret);

        // return CancellationToken.None
        il.MarkLabel(noneLabel);
        il.Emit(OpCodes.Ldloca, resultLocal);
        il.Emit(OpCodes.Initobj, ctType);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits $TimerPromiseClosure display class.
    /// Captures the resolved value and handles EventLoop.Unref on completion.
    /// Also tracks an optional CancellationToken for AbortSignal support.
    /// </summary>
    private void EmitTimerPromiseClosure(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        _timerPromiseClosureType = moduleBuilder.DefineType(
            "$TimerPromiseClosure",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);

        _timerPromiseClosureValue = _timerPromiseClosureType.DefineField(
            "Value", _types.Object, FieldAttributes.Public);

        _timerPromiseClosureToken = _timerPromiseClosureType.DefineField(
            "Token", _types.CancellationToken, FieldAttributes.Public);

        _timerPromiseClosureCtor = _timerPromiseClosureType.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        {
            var il = _timerPromiseClosureCtor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ret);
        }

        // OnComplete(Task t) → EventLoop.Unref(); if Token cancelled throw AbortError; return Value;
        _timerPromiseClosureOnComplete = _timerPromiseClosureType.DefineMethod(
            "OnComplete",
            MethodAttributes.Public,
            _types.Object,
            [_types.Task]);
        {
            var il = _timerPromiseClosureOnComplete.GetILGenerator();

            // EventLoop.GetInstance().Unref();
            il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
            il.Emit(OpCodes.Call, runtime.EventLoopUnref);

            // if (this.Token.IsCancellationRequested) throw new Exception("AbortError: ...")
            var notCancelledLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldflda, _timerPromiseClosureToken);
            il.Emit(OpCodes.Call, _types.GetProperty(_types.CancellationToken, "IsCancellationRequested").GetGetMethod()!);
            il.Emit(OpCodes.Brfalse, notCancelledLabel);
            il.Emit(OpCodes.Ldstr, "AbortError: The operation was aborted");
            il.Emit(OpCodes.Newobj, _types.ExceptionCtorString);
            il.Emit(OpCodes.Throw);

            il.MarkLabel(notCancelledLabel);

            // return this.Value;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _timerPromiseClosureValue);
            il.Emit(OpCodes.Ret);
        }

        _timerPromiseClosureType.CreateType();
    }

    /// <summary>
    /// Emits: public static object SetTimeoutPromise(double delay, object? value)
    /// Creates a promise that resolves with value after delay ms.
    /// </summary>
    private void EmitSetTimeoutPromise(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "SetTimeoutPromise",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Double, _types.Object]);
        runtime.SetTimeoutPromise = method;

        var il = method.GetILGenerator();

        // int delayMs = Math.Max(0, (int)delay);
        var delayMsLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0); // delay
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, delayMsLocal);

        // EventLoop.Ref() — keep event loop alive during delay
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopRef);

        // var closure = new $TimerPromiseClosure();
        var closureLocal = il.DeclareLocal(_timerPromiseClosureType);
        il.Emit(OpCodes.Newobj, _timerPromiseClosureCtor);
        il.Emit(OpCodes.Stloc, closureLocal);

        // closure.Value = value;
        il.Emit(OpCodes.Ldloc, closureLocal);
        il.Emit(OpCodes.Ldarg_1); // value
        il.Emit(OpCodes.Stfld, _timerPromiseClosureValue);

        // Task.Delay(delayMs)
        il.Emit(OpCodes.Ldloc, delayMsLocal);
        il.Emit(OpCodes.Call, typeof(Task).GetMethod("Delay", [typeof(int)])!);

        // .ContinueWith<object?>(closure.OnComplete)
        il.Emit(OpCodes.Ldloc, closureLocal);
        il.Emit(OpCodes.Ldftn, _timerPromiseClosureOnComplete);
        il.Emit(OpCodes.Newobj, typeof(Func<Task, object?>).GetConstructors()[0]);
        il.Emit(OpCodes.Call,
            typeof(Task).GetMethod("ContinueWith", 1,
                [_types.MakeGenericType(typeof(Func<,>), typeof(Task), Type.MakeGenericMethodParameter(0))])!
            .MakeGenericMethod(typeof(object)));

        // WrapTaskAsPromise(task)
        il.Emit(OpCodes.Call, runtime.WrapTaskAsPromise);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object SetTimeoutPromiseWithSignal(double delay, object? value, object? options)
    /// Creates a promise that resolves with value after delay ms, supporting AbortSignal cancellation.
    /// If options.signal is already aborted, returns a rejected promise (faulted task) immediately.
    /// </summary>
    private void EmitSetTimeoutPromiseWithSignal(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "SetTimeoutPromiseWithSignal",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Double, _types.Object, _types.Object]);
        runtime.SetTimeoutPromiseWithSignal = method;

        var il = method.GetILGenerator();

        // var token = ExtractTimerOptionsToken(options)
        var tokenLocal = il.DeclareLocal(_types.CancellationToken);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.ExtractTimerOptionsToken);
        il.Emit(OpCodes.Stloc, tokenLocal);

        // if (token.IsCancellationRequested) return $TSPromise.Reject("AbortError: ...")
        var notAbortedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloca, tokenLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.CancellationToken, "IsCancellationRequested").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, notAbortedLabel);
        {
            // Create faulted task with AbortError exception, then wrap as promise
            il.Emit(OpCodes.Ldstr, "AbortError: The operation was aborted");
            il.Emit(OpCodes.Newobj, _types.ExceptionCtorString);
            il.Emit(OpCodes.Call, typeof(Task).GetMethod("FromException", 1, [typeof(Exception)])!.MakeGenericMethod(typeof(object)));
            il.Emit(OpCodes.Call, runtime.WrapTaskAsPromise);
            il.Emit(OpCodes.Ret);
        }
        il.MarkLabel(notAbortedLabel);

        // int delayMs = Math.Max(0, (int)delay);
        var delayMsLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, delayMsLocal);

        // EventLoop.Ref()
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopRef);

        // var closure = new $TimerPromiseClosure();
        var closureLocal = il.DeclareLocal(_timerPromiseClosureType);
        il.Emit(OpCodes.Newobj, _timerPromiseClosureCtor);
        il.Emit(OpCodes.Stloc, closureLocal);

        // closure.Value = value;
        il.Emit(OpCodes.Ldloc, closureLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _timerPromiseClosureValue);

        // closure.Token = token;
        il.Emit(OpCodes.Ldloc, closureLocal);
        il.Emit(OpCodes.Ldloc, tokenLocal);
        il.Emit(OpCodes.Stfld, _timerPromiseClosureToken);

        // Task.Delay(delayMs, token)
        il.Emit(OpCodes.Ldloc, delayMsLocal);
        il.Emit(OpCodes.Ldloc, tokenLocal);
        il.Emit(OpCodes.Call, typeof(Task).GetMethod("Delay", [typeof(int), typeof(CancellationToken)])!);

        // .ContinueWith<object?>(closure.OnComplete)
        il.Emit(OpCodes.Ldloc, closureLocal);
        il.Emit(OpCodes.Ldftn, _timerPromiseClosureOnComplete);
        il.Emit(OpCodes.Newobj, typeof(Func<Task, object?>).GetConstructors()[0]);
        il.Emit(OpCodes.Call,
            typeof(Task).GetMethod("ContinueWith", 1,
                [_types.MakeGenericType(typeof(Func<,>), typeof(Task), Type.MakeGenericMethodParameter(0))])!
            .MakeGenericMethod(typeof(object)));

        // WrapTaskAsPromise
        il.Emit(OpCodes.Call, runtime.WrapTaskAsPromise);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object SetImmediatePromise(object? value)
    /// Creates a promise that resolves with value on the next tick.
    /// </summary>
    private void EmitSetImmediatePromise(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "SetImmediatePromise",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        runtime.SetImmediatePromise = method;

        var il = method.GetILGenerator();

        // SetTimeoutPromise(0.0, value)
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ldarg_0); // value
        il.Emit(OpCodes.Call, runtime.SetTimeoutPromise);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object SetImmediatePromiseWithSignal(object? value, object? options)
    /// Delegates to SetTimeoutPromiseWithSignal(0, value, options).
    /// </summary>
    private void EmitSetImmediatePromiseWithSignal(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "SetImmediatePromiseWithSignal",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        runtime.SetImmediatePromiseWithSignal = method;

        var il = method.GetILGenerator();

        // SetTimeoutPromiseWithSignal(0.0, value, options)
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ldarg_0); // value
        il.Emit(OpCodes.Ldarg_1); // options
        il.Emit(OpCodes.Call, runtime.SetTimeoutPromiseWithSignal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits $AsyncIntervalClosure: a display class that holds the mutable state for
    /// setInterval's async iterable. Has Next(), Return(), GetSelf() instance methods.
    /// </summary>
    private void EmitAsyncIntervalClosure(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        _asyncIntervalClosureType = moduleBuilder.DefineType(
            "$AsyncIntervalClosure",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);

        _asyncIntervalClosureDelayMs = _asyncIntervalClosureType.DefineField(
            "DelayMs", _types.Int32, FieldAttributes.Public);
        _asyncIntervalClosureValue = _asyncIntervalClosureType.DefineField(
            "Value", _types.Object, FieldAttributes.Public);
        _asyncIntervalClosureDone = _asyncIntervalClosureType.DefineField(
            "Done", _types.Boolean, FieldAttributes.Public);
        _asyncIntervalClosureSelf = _asyncIntervalClosureType.DefineField(
            "Self", _types.DictionaryStringObject, FieldAttributes.Public);

        _asyncIntervalClosureToken = _asyncIntervalClosureType.DefineField(
            "Token", _types.CancellationToken, FieldAttributes.Public);

        // Constructor
        _asyncIntervalClosureCtor = _asyncIntervalClosureType.DefineConstructor(
            MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        {
            var il = _asyncIntervalClosureCtor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
            il.Emit(OpCodes.Ret);
        }

        // Next(object[] args) → object (returns Task<object> containing {value, done} dict)
        _asyncIntervalClosureNext = _asyncIntervalClosureType.DefineMethod(
            "Next",
            MethodAttributes.Public,
            _types.Object,
            [_types.ObjectArray]);
        EmitAsyncIntervalNext(runtime);

        // Return(object[] args) → object (returns Task<object> containing {value: arg, done: true})
        _asyncIntervalClosureReturn = _asyncIntervalClosureType.DefineMethod(
            "Return",
            MethodAttributes.Public,
            _types.Object,
            [_types.ObjectArray]);
        EmitAsyncIntervalReturn(runtime);

        // GetSelf(object[] args) → object (returns the self dict)
        _asyncIntervalClosureGetSelf = _asyncIntervalClosureType.DefineMethod(
            "GetSelf",
            MethodAttributes.Public,
            _types.Object,
            [_types.ObjectArray]);
        {
            var il = _asyncIntervalClosureGetSelf.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _asyncIntervalClosureSelf);
            il.Emit(OpCodes.Ret);
        }

        _asyncIntervalClosureType.CreateType();
    }

    /// <summary>
    /// Emits the body of $AsyncIntervalClosure.Next(args):
    /// if (Done || Token.IsCancellationRequested) return Promise.resolve({done: true});
    /// return Promise wrapping Task.Delay(DelayMs, Token).ContinueWith(...).
    /// Returns $TSPromise — for await...of unwraps it via the TSPromise check.
    /// </summary>
    private void EmitAsyncIntervalNext(EmittedRuntime runtime)
    {
        var il = _asyncIntervalClosureNext.GetILGenerator();

        var notDoneLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // if (this.Done) goto doneLabel
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _asyncIntervalClosureDone);
        il.Emit(OpCodes.Brtrue, doneLabel);

        // if (this.Token.IsCancellationRequested) goto doneLabel
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _asyncIntervalClosureToken);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.CancellationToken, "IsCancellationRequested").GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, doneLabel);

        il.Emit(OpCodes.Br, notDoneLabel);

        il.MarkLabel(doneLabel);
        {
            // Create {value: null, done: true} dict
            EmitIteratorResultDict(il, null, true);
            // Task.FromResult<object>(dict)
            il.Emit(OpCodes.Call, typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(typeof(object)));
            // WrapTaskAsPromise → $TSPromise
            il.Emit(OpCodes.Call, runtime.WrapTaskAsPromise);
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(notDoneLabel);

        // EventLoop.Ref()
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopRef);

        // Task.Delay(this.DelayMs, this.Token)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _asyncIntervalClosureDelayMs);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _asyncIntervalClosureToken);
        il.Emit(OpCodes.Call, typeof(Task).GetMethod("Delay", [typeof(int), typeof(CancellationToken)])!);

        // .ContinueWith<object>(this.OnNextComplete)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldftn, EmitOnNextComplete(runtime));
        il.Emit(OpCodes.Newobj, typeof(Func<Task, object?>).GetConstructors()[0]);
        il.Emit(OpCodes.Call,
            typeof(Task).GetMethod("ContinueWith", 1,
                [_types.MakeGenericType(typeof(Func<,>), typeof(Task), Type.MakeGenericMethodParameter(0))])!
            .MakeGenericMethod(typeof(object)));

        // WrapTaskAsPromise → $TSPromise
        il.Emit(OpCodes.Call, runtime.WrapTaskAsPromise);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits OnNextComplete(Task t) → EventLoop.Unref();
    /// if Done OR Token cancelled OR Task cancelled return {done:true};
    /// otherwise return {value, done:false}.
    /// </summary>
    private MethodBuilder EmitOnNextComplete(EmittedRuntime runtime)
    {
        var method = _asyncIntervalClosureType.DefineMethod(
            "OnNextComplete",
            MethodAttributes.Public,
            _types.Object,
            [_types.Task]);

        var il = method.GetILGenerator();
        var notDoneLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // EventLoop.Unref()
        il.Emit(OpCodes.Call, runtime.EventLoopGetInstance);
        il.Emit(OpCodes.Call, runtime.EventLoopUnref);

        // if (this.Done) goto doneLabel
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _asyncIntervalClosureDone);
        il.Emit(OpCodes.Brtrue, doneLabel);

        // if (this.Token.IsCancellationRequested) goto doneLabel
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, _asyncIntervalClosureToken);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.CancellationToken, "IsCancellationRequested").GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, doneLabel);

        // if (task.IsCanceled) goto doneLabel — Task.Delay was cancelled
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Task, "IsCanceled").GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, doneLabel);

        il.Emit(OpCodes.Br, notDoneLabel);

        il.MarkLabel(doneLabel);
        {
            EmitIteratorResultDict(il, null, true);
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(notDoneLabel);

        // Create {value: this.Value, done: false}
        var dictCtor = _types.GetDefaultConstructor(_types.DictionaryStringObject);
        var dictSetItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object);

        il.Emit(OpCodes.Newobj, dictCtor);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _asyncIntervalClosureValue);
        il.Emit(OpCodes.Callvirt, dictSetItem);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "done");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, dictSetItem);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits the body of $AsyncIntervalClosure.Return(args):
    /// this.Done = true; return Task.FromResult({value: args[0] ?? null, done: true});
    /// </summary>
    private void EmitAsyncIntervalReturn(EmittedRuntime runtime)
    {
        var il = _asyncIntervalClosureReturn.GetILGenerator();

        // this.Done = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _asyncIntervalClosureDone);

        // value = args.Length > 0 ? args[0] : null
        var hasValue = il.DefineLabel();
        var afterValue = il.DefineLabel();
        var valueLocal = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt_S, hasValue);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br_S, afterValue);

        il.MarkLabel(hasValue);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, valueLocal);

        il.MarkLabel(afterValue);

        // Create {value: value, done: true}
        var dictCtor = _types.GetDefaultConstructor(_types.DictionaryStringObject);
        var dictSetItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object);

        il.Emit(OpCodes.Newobj, dictCtor);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, dictSetItem);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "done");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, dictSetItem);

        // Task.FromResult<object>(dict) → WrapTaskAsPromise → $TSPromise
        il.Emit(OpCodes.Call, typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(typeof(object)));
        il.Emit(OpCodes.Call, runtime.WrapTaskAsPromise);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Helper: emits a {value: null, done: true/false} dictionary.
    /// If value is null, uses ldnull; otherwise loads the field. done is a boolean.
    /// </summary>
    private void EmitIteratorResultDict(ILGenerator il, FieldBuilder? valueField, bool done)
    {
        var dictCtor = _types.GetDefaultConstructor(_types.DictionaryStringObject);
        var dictSetItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object);

        il.Emit(OpCodes.Newobj, dictCtor);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "value");
        if (valueField != null)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, valueField);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
        il.Emit(OpCodes.Callvirt, dictSetItem);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "done");
        il.Emit(OpCodes.Ldc_I4, done ? 1 : 0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, dictSetItem);
    }

    /// <summary>
    /// Emits: public static object SetIntervalAsyncIterable(double delay, object? value)
    /// Creates a dictionary with next/return TSFunctions and Symbol.asyncIterator,
    /// forming an async iterable for for await...of consumption.
    /// </summary>
    private void EmitSetIntervalAsyncIterable(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "SetIntervalAsyncIterable",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Double, _types.Object]);
        runtime.SetIntervalAsyncIterable = method;

        var il = method.GetILGenerator();

        // int delayMs = Math.Max(0, (int)delay);
        var delayMsLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0); // delay
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, delayMsLocal);

        // var closure = new $AsyncIntervalClosure();
        var closureLocal = il.DeclareLocal(_asyncIntervalClosureType);
        il.Emit(OpCodes.Newobj, _asyncIntervalClosureCtor);
        il.Emit(OpCodes.Stloc, closureLocal);

        // closure.DelayMs = delayMs;
        il.Emit(OpCodes.Ldloc, closureLocal);
        il.Emit(OpCodes.Ldloc, delayMsLocal);
        il.Emit(OpCodes.Stfld, _asyncIntervalClosureDelayMs);

        // closure.Value = value;
        il.Emit(OpCodes.Ldloc, closureLocal);
        il.Emit(OpCodes.Ldarg_1); // value
        il.Emit(OpCodes.Stfld, _asyncIntervalClosureValue);

        // var dict = new Dictionary<string, object?>();
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, dictLocal);

        // closure.Self = dict;
        il.Emit(OpCodes.Ldloc, closureLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Stfld, _asyncIntervalClosureSelf);

        var dictSetItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object);

        // dict["next"] = new $TSFunction(closure, closure.Next, "next", 0);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "next");
        il.Emit(OpCodes.Ldloc, closureLocal);         // target
        _types.EmitLoadMethodInfoViaHandle(il, _asyncIntervalClosureNext);
        il.Emit(OpCodes.Ldstr, "next");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtorWithCache);
        il.Emit(OpCodes.Callvirt, dictSetItem);

        // dict["return"] = new $TSFunction(closure, closure.Return, "return", 1);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "return");
        il.Emit(OpCodes.Ldloc, closureLocal);         // target
        _types.EmitLoadMethodInfoViaHandle(il, _asyncIntervalClosureReturn);
        il.Emit(OpCodes.Ldstr, "return");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtorWithCache);
        il.Emit(OpCodes.Callvirt, dictSetItem);

        // Set Symbol.asyncIterator on dict → TSFunction that returns dict itself
        // GetSymbolDict(dict)[Symbol.asyncIterator] = new $TSFunction(closure, closure.GetSelf, "[Symbol.asyncIterator]", 0);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolAsyncIterator);
        il.Emit(OpCodes.Ldloc, closureLocal);         // target
        _types.EmitLoadMethodInfoViaHandle(il, _asyncIntervalClosureGetSelf);
        il.Emit(OpCodes.Ldstr, "[Symbol.asyncIterator]");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtorWithCache);
        var symbolDictSetItem = _types.DictionaryObjectObject.GetMethod("set_Item", [_types.Object, _types.Object])!;
        il.Emit(OpCodes.Callvirt, symbolDictSetItem);

        // return dict;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object SetIntervalAsyncIterableWithSignal(double delay, object value, object options)
    /// Pre-checks signal abort (throws AbortError synchronously to match Node.js behavior),
    /// then builds an async iterable closure with the cancellation token wired in.
    /// When the signal aborts later, the iterator transitions to done=true on the next iteration.
    /// </summary>
    private void EmitSetIntervalAsyncIterableWithSignal(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        var method = runtimeType.DefineMethod(
            "SetIntervalAsyncIterableWithSignal",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Double, _types.Object, _types.Object]);
        runtime.SetIntervalAsyncIterableWithSignal = method;

        var il = method.GetILGenerator();

        // var token = ExtractTimerOptionsToken(options)
        var tokenLocal = il.DeclareLocal(_types.CancellationToken);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.ExtractTimerOptionsToken);
        il.Emit(OpCodes.Stloc, tokenLocal);

        // if (token.IsCancellationRequested) throw AbortError
        // (Node.js semantics: setInterval with pre-aborted signal throws synchronously)
        var notAbortedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloca, tokenLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.CancellationToken, "IsCancellationRequested").GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, notAbortedLabel);
        il.Emit(OpCodes.Ldstr, "AbortError: The operation was aborted");
        il.Emit(OpCodes.Newobj, _types.ExceptionCtorString);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(notAbortedLabel);

        // Call SetIntervalAsyncIterable to build the dict, then set closure.Token via the dict's "next" function target.
        // Simpler: inline the closure construction here so we can set Token directly.
        // Mirrors EmitSetIntervalAsyncIterable but with token wiring.

        // int delayMs = Math.Max(0, (int)delay);
        var delayMsLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, delayMsLocal);

        // var closure = new $AsyncIntervalClosure();
        var closureLocal = il.DeclareLocal(_asyncIntervalClosureType);
        il.Emit(OpCodes.Newobj, _asyncIntervalClosureCtor);
        il.Emit(OpCodes.Stloc, closureLocal);

        // closure.DelayMs = delayMs;
        il.Emit(OpCodes.Ldloc, closureLocal);
        il.Emit(OpCodes.Ldloc, delayMsLocal);
        il.Emit(OpCodes.Stfld, _asyncIntervalClosureDelayMs);

        // closure.Value = value;
        il.Emit(OpCodes.Ldloc, closureLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _asyncIntervalClosureValue);

        // closure.Token = token;
        il.Emit(OpCodes.Ldloc, closureLocal);
        il.Emit(OpCodes.Ldloc, tokenLocal);
        il.Emit(OpCodes.Stfld, _asyncIntervalClosureToken);

        // var dict = new Dictionary<string, object?>();
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, dictLocal);

        // closure.Self = dict;
        il.Emit(OpCodes.Ldloc, closureLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Stfld, _asyncIntervalClosureSelf);

        var dictSetItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object);

        // dict["next"] = new $TSFunction(closure, closure.Next, "next", 0);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "next");
        il.Emit(OpCodes.Ldloc, closureLocal);
        _types.EmitLoadMethodInfoViaHandle(il, _asyncIntervalClosureNext);
        il.Emit(OpCodes.Ldstr, "next");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtorWithCache);
        il.Emit(OpCodes.Callvirt, dictSetItem);

        // dict["return"] = new $TSFunction(closure, closure.Return, "return", 1);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "return");
        il.Emit(OpCodes.Ldloc, closureLocal);
        _types.EmitLoadMethodInfoViaHandle(il, _asyncIntervalClosureReturn);
        il.Emit(OpCodes.Ldstr, "return");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtorWithCache);
        il.Emit(OpCodes.Callvirt, dictSetItem);

        // GetSymbolDict(dict)[Symbol.asyncIterator] = closure.GetSelf
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolAsyncIterator);
        il.Emit(OpCodes.Ldloc, closureLocal);
        _types.EmitLoadMethodInfoViaHandle(il, _asyncIntervalClosureGetSelf);
        il.Emit(OpCodes.Ldstr, "[Symbol.asyncIterator]");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtorWithCache);
        var symbolDictSetItem = _types.DictionaryObjectObject.GetMethod("set_Item", [_types.Object, _types.Object])!;
        il.Emit(OpCodes.Callvirt, symbolDictSetItem);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits module method wrappers for timers/promises (used for named/namespace imports).
    /// Each wrapper takes object[] args and dispatches to the actual runtime method.
    /// </summary>
    private void EmitTimerPromisesModuleWrappers(TypeBuilder runtimeType, EmittedRuntime runtime)
    {
        // setTimeout wrapper: object SetTimeoutPromiseWrapper(object[] args)
        {
            var method = runtimeType.DefineMethod(
                "SetTimeoutPromiseWrapper",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.ObjectArray]);

            var il = method.GetILGenerator();

            // delay = args.Length > 0 ? (double)args[0] : 0.0
            var hasDelay = il.DefineLabel();
            var afterDelay = il.DefineLabel();
            var delayLocal = il.DeclareLocal(_types.Double);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Bgt_S, hasDelay);
            il.Emit(OpCodes.Ldc_R8, 0.0);
            il.Emit(OpCodes.Stloc, delayLocal);
            il.Emit(OpCodes.Br_S, afterDelay);

            il.MarkLabel(hasDelay);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Unbox_Any, _types.Double);
            il.Emit(OpCodes.Stloc, delayLocal);

            il.MarkLabel(afterDelay);

            // value = args.Length > 1 ? args[1] : null
            var hasValue = il.DefineLabel();
            var afterValue = il.DefineLabel();
            var valueLocal = il.DeclareLocal(_types.Object);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Bgt_S, hasValue);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stloc, valueLocal);
            il.Emit(OpCodes.Br_S, afterValue);

            il.MarkLabel(hasValue);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Stloc, valueLocal);

            il.MarkLabel(afterValue);

            // If args.Length > 2, pass options to SetTimeoutPromiseWithSignal
            var noOptionsLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Ble_S, noOptionsLabel);

            // Call SetTimeoutPromiseWithSignal(delay, value, args[2])
            il.Emit(OpCodes.Ldloc, delayLocal);
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Call, runtime.SetTimeoutPromiseWithSignal);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(noOptionsLabel);

            // Call SetTimeoutPromise(delay, value)
            il.Emit(OpCodes.Ldloc, delayLocal);
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Call, runtime.SetTimeoutPromise);
            il.Emit(OpCodes.Ret);

            runtime.RegisterBuiltInModuleMethod("timers/promises", "setTimeout", method);
        }

        // setImmediate wrapper: object SetImmediatePromiseWrapper(object[] args)
        {
            var method = runtimeType.DefineMethod(
                "SetImmediatePromiseWrapper",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.ObjectArray]);

            var il = method.GetILGenerator();

            // value = args.Length > 0 ? args[0] : null
            var hasValue = il.DefineLabel();
            var afterValue = il.DefineLabel();
            var valueLocal = il.DeclareLocal(_types.Object);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Bgt_S, hasValue);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stloc, valueLocal);
            il.Emit(OpCodes.Br_S, afterValue);

            il.MarkLabel(hasValue);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Stloc, valueLocal);

            il.MarkLabel(afterValue);

            // If args.Length > 1, route to SetImmediatePromiseWithSignal
            var noOptionsLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ble_S, noOptionsLabel);

            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Call, runtime.SetImmediatePromiseWithSignal);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(noOptionsLabel);

            // Call SetImmediatePromise(value)
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Call, runtime.SetImmediatePromise);
            il.Emit(OpCodes.Ret);

            runtime.RegisterBuiltInModuleMethod("timers/promises", "setImmediate", method);
        }

        // setInterval wrapper: object SetIntervalAsyncIterableWrapper(object[] args)
        {
            var method = runtimeType.DefineMethod(
                "SetIntervalAsyncIterableWrapper",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.ObjectArray]);

            var il = method.GetILGenerator();

            // delay = args.Length > 0 ? (double)args[0] : 0.0
            var hasDelay = il.DefineLabel();
            var afterDelay = il.DefineLabel();
            var delayLocal = il.DeclareLocal(_types.Double);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Bgt_S, hasDelay);
            il.Emit(OpCodes.Ldc_R8, 0.0);
            il.Emit(OpCodes.Stloc, delayLocal);
            il.Emit(OpCodes.Br_S, afterDelay);

            il.MarkLabel(hasDelay);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Unbox_Any, _types.Double);
            il.Emit(OpCodes.Stloc, delayLocal);

            il.MarkLabel(afterDelay);

            // value = args.Length > 1 ? args[1] : null
            var hasValue = il.DefineLabel();
            var afterValue = il.DefineLabel();
            var valueLocal = il.DeclareLocal(_types.Object);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Bgt_S, hasValue);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stloc, valueLocal);
            il.Emit(OpCodes.Br_S, afterValue);

            il.MarkLabel(hasValue);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Stloc, valueLocal);

            il.MarkLabel(afterValue);

            // If args.Length > 2, route to SetIntervalAsyncIterableWithSignal
            var noOptionsLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Ble_S, noOptionsLabel);

            il.Emit(OpCodes.Ldloc, delayLocal);
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Call, runtime.SetIntervalAsyncIterableWithSignal);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(noOptionsLabel);

            // Call SetIntervalAsyncIterable(delay, value)
            il.Emit(OpCodes.Ldloc, delayLocal);
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Call, runtime.SetIntervalAsyncIterable);
            il.Emit(OpCodes.Ret);

            runtime.RegisterBuiltInModuleMethod("timers/promises", "setInterval", method);
        }
    }
}
