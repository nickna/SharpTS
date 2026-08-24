using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void DefinePromisePrototypePopulateShell(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.PromisePrototypePopulateMethod = typeBuilder.DefineMethod(
            "_PromisePrototypePopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
    }

    /// <summary>
    /// Emits <c>PromiseThenHelper(object __this, params object[] args)</c>,
    /// <c>PromiseCatchHelper</c>, <c>PromiseFinallyHelper</c>. Each unwraps the
    /// receiver to <c>Task&lt;object&gt;</c> (handles raw Task and $Promise wrapper),
    /// then dispatches to the corresponding state-machine helper. Used as
    /// MethodInfo backing for the <c>$TSFunction</c> wrappers installed on
    /// <see cref="EmittedRuntime.PromisePrototypeField"/>.
    /// </summary>
    private void EmitPromisePrototypeHelpers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var finallyFunctions = EmitPromiseFinallyFunctionTypes(
            (ModuleBuilder)typeBuilder.Module, runtime);

        // PromiseThenHelper(object __this, object onFulfilled, object onRejected) -> Task<object>
        {
            var m = typeBuilder.DefineMethod(
                "PromiseThenHelper",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.Object, _types.Object, _types.Object]);
            m.DefineParameter(1, ParameterAttributes.None, "__this");
            m.DefineParameter(2, ParameterAttributes.None, "onFulfilled");
            m.DefineParameter(3, ParameterAttributes.None, "onRejected");
            runtime.PromiseThenHelperMethod = m;

            var il = m.GetILGenerator();
            // ECMA-262 §27.2.5.4 step 2: If IsPromise(promise) is false, throw
            // TypeError. then is more restrictive than catch/finally — must be
            // a real Promise (not just any thenable).
            EmitThrowIfNullOrUndefined(il, runtime, "Promise.prototype.then called on null or undefined");
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.NormalizeManagedAwaitable);
            il.Emit(OpCodes.Starg, 0);
            var isPromiseOkLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
            il.Emit(OpCodes.Brtrue, isPromiseOkLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, _types.TaskOfObject);
            il.Emit(OpCodes.Brtrue, isPromiseOkLabel);
            GuestErrorEmitter.ThrowTypeError(il, runtime, "Promise.prototype.then called on non-Promise");
            il.MarkLabel(isPromiseOkLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.ObservePromiseConstructorMethod);
            var taskLocal = il.DeclareLocal(_types.TaskOfObject);
            EmitUnwrapToTask(il, runtime, taskLocal);
            il.Emit(OpCodes.Ldloc, taskLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.PromiseThen);
            // Promise-subclass receivers get subclass-typed results (#242).
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.WrapDerivedPromiseResultMethod);
            il.Emit(OpCodes.Ret);
        }

        // PromiseCatchHelper(object __this, object onRejected) -> object
        {
            var m = typeBuilder.DefineMethod(
                "PromiseCatchHelper",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.Object, _types.Object]);
            m.DefineParameter(1, ParameterAttributes.None, "__this");
            m.DefineParameter(2, ParameterAttributes.None, "onRejected");
            runtime.PromiseCatchHelperMethod = m;

            var il = m.GetILGenerator();
            // ECMA-262 §27.2.5.1: catch(onRejected) is `this.then(undefined, onRejected)`.
            // The `this.then` lookup begins with ToObject(this), which throws TypeError
            // on null/undefined. Per the spec invariant, surface that synchronously
            // before any Task-conversion or PromiseThen dispatch.
            EmitThrowIfNullOrUndefined(il, runtime, "Promise.prototype.catch called on null or undefined");
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.NormalizeManagedAwaitable);
            il.Emit(OpCodes.Starg, 0);
            // Fast path: real $TSPromise / Task<object> receiver → direct PromiseCatch.
            EmitFastPathOrUserThenInvoke(il, runtime, isCatch: true, methodNameForError: "Promise.prototype.catch");
            il.Emit(OpCodes.Ret);
        }

        // PromiseFinallyHelper(object __this, object onFinally) -> object
        {
            var m = typeBuilder.DefineMethod(
                "PromiseFinallyHelper",
                MethodAttributes.Public | MethodAttributes.Static,
                _types.Object,
                [_types.Object, _types.Object]);
            m.DefineParameter(1, ParameterAttributes.None, "__this");
            m.DefineParameter(2, ParameterAttributes.None, "onFinally");
            runtime.PromiseFinallyHelperMethod = m;

            var il = m.GetILGenerator();
            EmitThrowIfNullOrUndefined(il, runtime, "Promise.prototype.finally called on null or undefined");
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.NormalizeManagedAwaitable);
            il.Emit(OpCodes.Starg, 0);
            EmitDynamicPromiseFinallyInvoke(il, runtime, finallyFunctions);
            il.Emit(OpCodes.Ret);
        }
    }

    private sealed record PromiseFinallyFunctionBuilders(
        ConstructorBuilder ClosureCtor,
        MethodBuilder ThenMethod,
        MethodBuilder CatchMethod);

    /// <summary>
    /// Emits the closure objects used by Promise.prototype.finally. The outer
    /// pair capture onFinally and implement ThenFinally/CatchFinally; the inner
    /// thunk captures the original fulfillment value or rejection reason for
    /// the `promise.then(valueThunk/thrower)` continuation.
    /// </summary>
    private PromiseFinallyFunctionBuilders EmitPromiseFinallyFunctionTypes(
        ModuleBuilder moduleBuilder,
        EmittedRuntime runtime)
    {
        var thunkType = EmitTypeDefinitions.DefineType(
            moduleBuilder,
            "$PromiseFinallyValueThunk",
            TypeAttributes.NotPublic | TypeAttributes.Sealed,
            _types.Object);
        var thunkValueField = thunkType.DefineField(
            "Value", _types.Object, FieldAttributes.Private);
        var thunkThrowsField = thunkType.DefineField(
            "Throws", _types.Boolean, FieldAttributes.Private);
        var thunkCtor = thunkType.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.Boolean]);
        {
            var il = thunkCtor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, thunkValueField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, thunkThrowsField);
            il.Emit(OpCodes.Ret);
        }
        var thunkInvoke = thunkType.DefineMethod(
            "Invoke", MethodAttributes.Public, _types.Object, Type.EmptyTypes);
        {
            var il = thunkInvoke.GetILGenerator();
            var returnValueLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, thunkThrowsField);
            il.Emit(OpCodes.Brfalse, returnValueLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, thunkValueField);
            il.Emit(OpCodes.Call, runtime.CreateException);
            il.Emit(OpCodes.Throw);
            il.MarkLabel(returnValueLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, thunkValueField);
            il.Emit(OpCodes.Ret);
        }
        thunkType.CreateType();

        var closureType = EmitTypeDefinitions.DefineType(
            moduleBuilder,
            "$PromiseFinallyFunctions",
            TypeAttributes.NotPublic | TypeAttributes.Sealed,
            _types.Object);
        var onFinallyField = closureType.DefineField(
            "OnFinally", _types.Object, FieldAttributes.Private);
        var closureCtor = closureType.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object]);
        {
            var il = closureCtor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.GetConstructor(_types.Object, Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, onFinallyField);
            il.Emit(OpCodes.Ret);
        }

        MethodBuilder DefineContinuation(string name, bool throws)
        {
            var method = closureType.DefineMethod(
                name, MethodAttributes.Public, _types.Object, [_types.Object]);
            var il = method.GetILGenerator();
            var resultLocal = il.DeclareLocal(_types.Object);
            var promiseLocal = il.DeclareLocal(_types.TaskOfObject);
            var thunkLocal = il.DeclareLocal(_types.Object);
            var thunkFunctionLocal = il.DeclareLocal(_types.Object);
            var thenLocal = il.DeclareLocal(_types.Object);

            // result = Call(onFinally, undefined, « »)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, onFinallyField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Call, runtime.InvokeValue);
            il.Emit(OpCodes.Stloc, resultLocal);

            // promise = PromiseResolve(%Promise%, result)
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Call, runtime.CoerceAwaitableToTaskMethod);
            il.Emit(OpCodes.Stloc, promiseLocal);

            // thunk = new ValueThunk(valueOrReason, throws)
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(throws ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newobj, thunkCtor);
            il.Emit(OpCodes.Stloc, thunkLocal);

            // thunkFunction = new $TSFunction(thunk, Invoke, "", 0)
            il.Emit(OpCodes.Ldloc, thunkLocal);
            il.Emit(OpCodes.Ldtoken, thunkInvoke);
            il.Emit(OpCodes.Call, _types.GetMethod(
                _types.MethodBase, "GetMethodFromHandle", _types.RuntimeMethodHandle));
            il.Emit(OpCodes.Castclass, _types.MethodInfo);
            il.Emit(OpCodes.Ldstr, string.Empty);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newobj, runtime.TSFunctionCtorWithCache);
            il.Emit(OpCodes.Stloc, thunkFunctionLocal);

            // return Invoke(promise, "then", « thunkFunction »)
            il.Emit(OpCodes.Ldloc, promiseLocal);
            il.Emit(OpCodes.Ldstr, "then");
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Stloc, thenLocal);
            il.Emit(OpCodes.Ldloc, promiseLocal);
            il.Emit(OpCodes.Ldloc, thenLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, thunkFunctionLocal);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
            il.Emit(OpCodes.Ret);
            return method;
        }

        var thenMethod = DefineContinuation("ThenFinally", throws: false);
        var catchMethod = DefineContinuation("CatchFinally", throws: true);
        closureType.CreateType();
        return new PromiseFinallyFunctionBuilders(
            closureCtor, thenMethod, catchMethod);
    }

    /// <summary>
    /// Emits the exact §27.2.5.3 dynamic Invoke(promise, "then", …) operation.
    /// Unlike the old Task fast path, this observes own/prototype/proxy `then`
    /// replacements and supplies real ThenFinally/CatchFinally built-ins when
    /// onFinally is callable.
    /// </summary>
    private void EmitDynamicPromiseFinallyInvoke(
        ILGenerator il,
        EmittedRuntime runtime,
        PromiseFinallyFunctionBuilders functions)
    {
        var onFinallyCallableLabel = il.DefineLabel();
        var haveArgumentsLabel = il.DefineLabel();
        var thenCallableLabel = il.DefineLabel();
        var closureLocal = il.DeclareLocal(_types.Object);
        var thenFinallyLocal = il.DeclareLocal(_types.Object);
        var catchFinallyLocal = il.DeclareLocal(_types.Object);
        var thenLocal = il.DeclareLocal(_types.Object);

        // Non-callable onFinally values are forwarded unchanged in both slots.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.TypeOf);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, onFinallyCallableLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, thenFinallyLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, catchFinallyLocal);
        il.Emit(OpCodes.Br, haveArgumentsLabel);

        il.MarkLabel(onFinallyCallableLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, functions.ClosureCtor);
        il.Emit(OpCodes.Stloc, closureLocal);
        EmitPromiseFinallyFunctionWrapper(
            il, runtime, closureLocal, functions.ThenMethod, length: 1);
        il.Emit(OpCodes.Stloc, thenFinallyLocal);
        EmitPromiseFinallyFunctionWrapper(
            il, runtime, closureLocal, functions.CatchMethod, length: 1);
        il.Emit(OpCodes.Stloc, catchFinallyLocal);

        il.MarkLabel(haveArgumentsLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "then");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, thenLocal);
        il.Emit(OpCodes.Ldloc, thenLocal);
        il.Emit(OpCodes.Call, runtime.TypeOf);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, thenCallableLabel);
        GuestErrorEmitter.ThrowTypeError(
            il, runtime, "Promise.prototype.finally: this.then is not callable");
        il.MarkLabel(thenCallableLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, thenLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, thenFinallyLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, catchFinallyLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
    }

    private void EmitPromiseFinallyFunctionWrapper(
        ILGenerator il,
        EmittedRuntime runtime,
        LocalBuilder closure,
        MethodBuilder method,
        int length)
    {
        il.Emit(OpCodes.Ldloc, closure);
        il.Emit(OpCodes.Ldtoken, method);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.MethodBase, "GetMethodFromHandle", _types.RuntimeMethodHandle));
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Ldstr, string.Empty);
        il.Emit(OpCodes.Ldc_I4, length);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtorWithCache);
    }

    /// <summary>
    /// Emits IL that branches on receiver shape and leaves a result on the
    /// stack (caller emits Ret):
    /// - $TSPromise / Task&lt;object&gt; → direct call to PromiseCatch/Finally (fast path).
    /// - Anything else (user object with a custom `then`) → spec-compliant
    ///   `Invoke(this, "then", « onFulfilled, onRejected »)` via GetProperty +
    ///   IsCallable check + InvokeMethodValue.
    /// </summary>
    /// <param name="isCatch">
    /// true → emit catch dispatch: fast path calls PromiseCatch, user-then
    /// invokes <c>this.then(undefined, onRejected)</c> per §27.2.5.1.
    /// false → emit finally dispatch: fast path calls PromiseFinally, user-then
    /// invokes <c>this.then(onFinally, onFinally)</c> as the closure-free
    /// approximation of §27.2.5.3 step 7.
    /// </param>
    /// <param name="methodNameForError">
    /// User-facing method name (e.g. "Promise.prototype.catch") prepended to
    /// the "this.then is not callable" TypeError message so finally callers
    /// don't see a catch-prefixed message.
    /// </param>
    private void EmitFastPathOrUserThenInvoke(ILGenerator il, EmittedRuntime runtime, bool isCatch, string methodNameForError)
    {
        var taskLocal = il.DeclareLocal(_types.TaskOfObject);
        var notTSPromiseLabel = il.DefineLabel();
        var fastPathLabel = il.DefineLabel();
        var userThenPathLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Branch A: $TSPromise → fast path with extracted Task. EXCEPT when
        // the user installed an own `then` descriptor on the instance, in
        // which case spec requires invoking that user-installed function.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brfalse, notTSPromiseLabel);
        // Check PDS for own "then" — if present, user has installed it →
        // user-then path (preserves spec-compliant `Invoke(this, "then", ...)`).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "then");
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, userThenPathLabel);
        // No user override → extract Task and use fast path.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSPromiseType);
        il.Emit(OpCodes.Callvirt, runtime.TSPromiseTaskGetter);
        il.Emit(OpCodes.Stloc, taskLocal);
        il.Emit(OpCodes.Br, fastPathLabel);

        // Branch B: raw Task<object>. Its ordinary own descriptors live in
        // the shared descriptor store just like $Promise expandos, so an own
        // `then` must take the dynamic invocation path.
        il.MarkLabel(notTSPromiseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, taskLocal);
        il.Emit(OpCodes.Brfalse, userThenPathLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "then");
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, userThenPathLabel);
        il.Emit(OpCodes.Br, fastPathLabel);

        il.MarkLabel(fastPathLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ObservePromiseConstructorMethod);
        il.Emit(OpCodes.Ldloc, taskLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, isCatch ? runtime.PromiseCatch : runtime.PromiseFinally);
        // Promise-subclass receivers get subclass-typed results (#242).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.WrapDerivedPromiseResultMethod);
        il.Emit(OpCodes.Br, endLabel);

        // Branch C: user object — look up `then`, validate callable, invoke.
        il.MarkLabel(userThenPathLabel);
        // then = GetProperty(this, "then")
        var thenLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "then");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, thenLocal);
        // if (!IsCallable(then)) throw TypeError. Inline check — Isinst against
        // each known callable shape. Mirrors $Runtime's typeof "function" branch
        // dispatch (TSFunction + bind/call/apply wrappers + bound array methods).
        var thenCallableLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, thenLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, thenCallableLabel);
        il.Emit(OpCodes.Ldloc, thenLocal);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, thenCallableLabel);
        il.Emit(OpCodes.Ldloc, thenLocal);
        il.Emit(OpCodes.Isinst, runtime.BoundArrayMethodType);
        il.Emit(OpCodes.Brtrue, thenCallableLabel);
        // Not callable — throw TypeError. Message is parameterized so finally
        // callers don't see a "Promise.prototype.catch:" prefix.
        il.Emit(OpCodes.Ldstr, methodNameForError + ": this.then is not callable");
        GuestErrorEmitter.ThrowErrorFromStack(il, runtime, runtime.TSTypeErrorCtor);
        il.MarkLabel(thenCallableLabel);
        // args[0]: undefined (catch) or onFinally (finally — both slots fire
        //         the callback so it runs on fulfillment AND rejection).
        // args[1]: onRejected (catch) or onFinally (finally) — Ldarg_1 either way.
        il.Emit(OpCodes.Ldarg_0);  // receiver for InvokeMethodValue
        il.Emit(OpCodes.Ldloc, thenLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        if (isCatch)
            il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        else
            il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);

        il.MarkLabel(endLabel);
    }

    /// <summary>
    /// Emits IL that throws TypeError if arg0 is null or $Undefined. Mirrors
    /// ECMA-262 ToObject step 1 for Promise.prototype.{then,catch,finally}
    /// which all begin with `ToObject(this)`.
    /// </summary>
    private void EmitThrowIfNullOrUndefined(ILGenerator il, EmittedRuntime runtime, string message)
    {
        var okLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, throwLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, throwLabel);
        il.Emit(OpCodes.Br, okLabel);
        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, message);
        GuestErrorEmitter.ThrowErrorFromStack(il, runtime, runtime.TSTypeErrorCtor);
        il.MarkLabel(okLabel);
    }

    /// <summary>
    /// Emits IL that stores <c>Ldarg_0</c> (or its <c>$Promise.Task</c> projection
    /// when the arg is a $Promise wrapper) into <paramref name="taskLocal"/>.
    /// Falls through with the local set; on non-Promise/non-Task receivers
    /// leaves it null and lets the downstream helper handle ToString/await.
    /// </summary>
    private void EmitUnwrapToTask(ILGenerator il, EmittedRuntime runtime, LocalBuilder taskLocal)
    {
        var notTSPromiseLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brfalse, notTSPromiseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSPromiseType);
        il.Emit(OpCodes.Callvirt, runtime.TSPromiseTaskGetter);
        il.Emit(OpCodes.Stloc, taskLocal);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(notTSPromiseLabel);
        // Raw Task<object> or anything else — Castclass works for Task<object>;
        // null/non-Task falls to a null task local and the helper invocation
        // will throw NRE which the surrounding async/then machinery surfaces
        // as a rejection.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Stloc, taskLocal);

        il.MarkLabel(doneLabel);
    }

    /// <summary>
    /// Populates <see cref="EmittedRuntime.PromisePrototypeField"/> with
    /// <c>$TSFunction</c> wrappers for then/catch/finally + a constructor
    /// pointer to <c>typeof(Task&lt;object&gt;)</c> + non-enumerable PDS
    /// descriptors (ECMA-262 §17 spec attrs for built-in methods).
    /// </summary>
    private void EmitPromisePrototypePopulate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = runtime.PromisePrototypePopulateMethod;
        var il = method.GetILGenerator();
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);

        EmitPrototypePopulateGuard(il, runtime.PromisePrototypeField);

        var descLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);

        // Promise.prototype.constructor === Promise (= typeof(Task<object>)).
        EmitInstallConstructor(il, runtime, runtime.PromisePrototypeField, descLocal, setItem, () =>
        {
            il.Emit(OpCodes.Ldtoken, _types.TaskOfObject);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        });

        // Wire then/catch/finally with their helper MethodInfos. The helpers
        // take the receiver as __this (named at their emit site — nameThisParam:
        // false skips the rename).
        void Wire(string jsName, MethodBuilder helper, int jsLength)
            => EmitWirePrototypeMethod(il, runtime, runtime.PromisePrototypeField, descLocal,
                setItem, jsName, helper, jsLength, nameThisParam: false);

        // ECMA-262 §27.2.5: then/catch take 2 and 1 args respectively;
        // finally takes 1 (onFinally). Spec lengths matter for length.js tests.
        Wire("then",    runtime.PromiseThenHelperMethod,    2);
        Wire("catch",   runtime.PromiseCatchHelperMethod,   1);
        Wire("finally", runtime.PromiseFinallyHelperMethod, 1);

        // ECMA-262 §27.2.5.5: Promise.prototype[@@toStringTag] = "Promise".
        // Attributes per spec: writable:false, enumerable:false, configurable:true.
        // GetSymbolDict(PromisePrototype)[SymbolToStringTag] = "Promise".
        il.Emit(OpCodes.Ldsfld, runtime.PromisePrototypeField);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolToStringTag);
        il.Emit(OpCodes.Ldstr, "Promise");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "set_Item",
            _types.Object, _types.Object));

        il.Emit(OpCodes.Ret);
    }
}
