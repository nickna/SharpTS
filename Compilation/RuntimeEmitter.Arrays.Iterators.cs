using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the hoisted lazy-mode preamble at the top of an iterator helper.
    /// Reads <c>_currentArrayLikeReceiver</c> once into a local and computes
    /// a <c>bool isLazyLocal</c> indicating whether per-iteration descriptor
    /// reads are needed (receiver is Dict, $Object, or an array/list). Per-element loads
    /// then branch on <c>isLazyLocal</c> — a stack-resident bool the JIT can
    /// hoist with branch prediction — rather than re-reading the
    /// thread-static slot and re-dispatching on type N times.
    /// </summary>
    /// <remarks>
    /// The receiver doesn't change during an iterator helper invocation
    /// (the dispatch site saves+restores the field around the call), so a
    /// single read at entry is sound. <c>isLazyLocal</c> is stable for the
    /// duration of the loop.
    /// </remarks>
    private void EmitHoistedLazyCheck(ILGenerator il, EmittedRuntime runtime,
        out LocalBuilder isLazyLocal, out LocalBuilder rcvrLocal)
    {
        rcvrLocal = il.DeclareLocal(_types.Object);
        isLazyLocal = il.DeclareLocal(_types.Boolean);

        il.Emit(OpCodes.Ldsfld, runtime.LazyArrayLikeReceiverField);
        il.Emit(OpCodes.Stloc, rcvrLocal);

        // Direct `array.map/reduce/...` calls do not pass through the generic
        // Array.prototype.call dispatcher that populates the thread-static
        // receiver slot. The helper's first argument is the original array in
        // that case, so use it as the observable receiver.
        var haveReceiver = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, rcvrLocal);
        il.Emit(OpCodes.Brtrue, haveReceiver);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, rcvrLocal);
        il.MarkLabel(haveReceiver);

        var isLazyTrue = il.DefineLabel();
        var isLazyFalse = il.DefineLabel();
        var doneLabel = il.DefineLabel();
        // $Arguments inherits List<object>, but its observable `length` is an
        // independent internal slot. The eager materializer already honors
        // that slot; treating it as a raw List would visit appended indices.
        il.Emit(OpCodes.Ldloc, rcvrLocal);
        il.Emit(OpCodes.Isinst, runtime.ArgumentsType);
        il.Emit(OpCodes.Brtrue, isLazyFalse);
        il.Emit(OpCodes.Ldloc, rcvrLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, isLazyTrue);
        il.Emit(OpCodes.Ldloc, rcvrLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, isLazyTrue);
        il.Emit(OpCodes.Ldloc, rcvrLocal);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brtrue, isLazyTrue);
        il.Emit(OpCodes.Ldloc, rcvrLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, isLazyTrue);
        il.MarkLabel(isLazyFalse);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, isLazyLocal);
        il.Emit(OpCodes.Br, doneLabel);
        il.MarkLabel(isLazyTrue);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, isLazyLocal);
        il.MarkLabel(doneLabel);
    }

    /// <summary>
    /// Emits IL that loads <c>list[idx]</c> on the eager path or
    /// <c>$Runtime.LoadArrayLikeElement(list, idx)</c> on the lazy path,
    /// branching on <c>isLazyLocal</c>. Leaves the element value on the stack.
    /// </summary>
    private void EmitElementLoad(ILGenerator il, LocalBuilder indexLocal, EmittedRuntime runtime, LocalBuilder isLazyLocal)
    {
        var lazyPathLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, isLazyLocal);
        il.Emit(OpCodes.Brtrue, lazyPathLabel);

        // Eager path: direct list[idx] — same IL the helpers used pre-#90.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(lazyPathLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Call, runtime.LoadArrayLikeElement);

        il.MarkLabel(doneLabel);
    }

    /// <summary>
    /// Emits: <c>if (callback == null || callback is $Undefined) throw new TypeError(...);</c>.
    /// ECMA-262 23.1.3.* require iterator-helper callbacks to be callable.
    /// Without this, our ArrayMap/ArrayEvery/etc. silently invoke null and
    /// throw NullReferenceException instead of TypeError. Caller passes the
    /// argument-index of the callback (most are arg1).
    /// </summary>
    private void EmitThrowIfCallbackNotCallable(
        ILGenerator il,
        EmittedRuntime runtime,
        int argIndex,
        string methodName,
        bool allowUndefined = false)
    {
        var okLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();
        OpCode loadOp = argIndex switch
        {
            0 => OpCodes.Ldarg_0,
            1 => OpCodes.Ldarg_1,
            2 => OpCodes.Ldarg_2,
            3 => OpCodes.Ldarg_3,
            _ => throw new InvalidOperationException("EmitThrowIfCallbackNotCallable: unsupported argIndex")
        };
        il.Emit(loadOp);
        // Missing optional callback slots are normalized to $Undefined by
        // both direct and bound-method dispatch. CLR null is JavaScript null,
        // which is never callable and must not be treated as an omission.
        il.Emit(OpCodes.Brfalse, throwLabel);
        il.Emit(loadOp);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, allowUndefined ? okLabel : throwLabel);

        // Positive callable check: $TSFunction / $BoundTSFunction / Function*
        // wrappers pass; anything else (bool, double, string, dict, list, …)
        // throws TypeError. ECMA-262 IsCallable returns true only for
        // function-like values; tests like `arr.every(true)` rely on the throw.
        il.Emit(loadOp);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, okLabel);
        il.Emit(loadOp);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, okLabel);
        il.Emit(loadOp);
        il.Emit(OpCodes.Isinst, runtime.FunctionBindWrapperType);
        il.Emit(OpCodes.Brtrue, okLabel);
        il.Emit(loadOp);
        il.Emit(OpCodes.Isinst, runtime.FunctionCallWrapperType);
        il.Emit(OpCodes.Brtrue, okLabel);
        il.Emit(loadOp);
        il.Emit(OpCodes.Isinst, runtime.FunctionApplyWrapperType);
        il.Emit(OpCodes.Brtrue, okLabel);
        // Built-in constructors are represented by System.Type and InvokeValue
        // supports their call form (String, Number, Object, Array, ...).
        il.Emit(loadOp);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brtrue, okLabel);
        // Default: not a recognized function-like → throw.
        il.Emit(OpCodes.Br, throwLabel);

        il.MarkLabel(throwLabel);
        // Build a real $TypeError instance + wrap in .NET Exception via CreateException
        // (matches the pattern used elsewhere — Object.defineProperty etc.). This is
        // important because test262 harness's `assert.throws(TypeError, fn)` checks
        // `e instanceof TypeError`, and only $TypeError instances satisfy that.
        il.Emit(OpCodes.Ldstr, methodName + " callback is not callable");
        GuestErrorEmitter.ThrowErrorFromStack(il, runtime, runtime.TSTypeErrorCtor);

        il.MarkLabel(okLabel);
    }

    /// <summary>
    /// Emits: <c>if (Element(list, indexLocal) is $ArrayHole) goto skipLabel;</c>
    /// where Element is a per-element load that branches on the hoisted
    /// <c>isLazyLocal</c>. Used to skip holes in forEach/filter/reduce/every/
    /// some/flat/flatMap per ECMA-262. Expects <c>arg0 = list</c>.
    /// </summary>
    /// <remarks>
    /// On the eager path (lazy field is null at helper entry), this reduces
    /// to one extra <c>Brtrue</c> over the pre-#90 IL — predictable, branch-
    /// predictor friendly, and within JIT noise. On the lazy path the work
    /// goes through <see cref="EmitLoadArrayLikeElement"/> so dynamically-
    /// added accessors are observed at iteration time (Test262 -b-X tests).
    /// </remarks>
    private void EmitSkipIfHole(ILGenerator il, LocalBuilder indexLocal, Label skipLabel, EmittedRuntime runtime, LocalBuilder isLazyLocal)
    {
        EmitElementLoad(il, indexLocal, runtime, isLazyLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brtrue, skipLabel);
    }

    /// <summary>
    /// Emits: load element (eager or lazy via <see cref="EmitElementLoad"/>),
    /// and if it's the <c>$ArrayHole</c> sentinel substitute
    /// <c>$Undefined.Instance</c>. Leaves the boundary-adjusted value on the
    /// stack. Used by find/findIndex/findLast/findLastIndex where the
    /// callback IS invoked on holes but with <c>undefined</c> as the element.
    /// </summary>
    private void EmitLoadElementUnholed(ILGenerator il, LocalBuilder indexLocal, EmittedRuntime runtime, LocalBuilder isLazyLocal)
    {
        var notHoleLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();
        EmitElementLoad(il, indexLocal, runtime, isLazyLocal);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brfalse, notHoleLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Br, doneLabel);
        il.MarkLabel(notHoleLabel);
        il.MarkLabel(doneLabel);
    }

    /// <summary>
    /// Emits IL to create callback args array [list[i], (double)i, list],
    /// invoke the callback via InvokeMethodValue with undefined thisArg,
    /// and leave the result on the stack.
    /// Expects: arg0 = list (List&lt;object&gt;), arg1 = callback (object).
    /// </summary>
    /// <remarks>
    /// Must dispatch through <see cref="EmittedRuntime.InvokeMethodValue"/> (not
    /// <see cref="EmittedRuntime.InvokeValue"/>) so that callbacks compiled from
    /// <c>function(…){…}</c> expressions (which carry a synthetic <c>__this</c>
    /// first parameter) have the <c>__this</c> slot filled by the runtime rather
    /// than absorbing the first real argument (list[i]) into it. ES spec: when
    /// no <c>thisArg</c> is supplied to forEach/map/filter/etc., the callback's
    /// <c>this</c> is undefined in strict mode, which we represent as null.
    ///
    /// Stage E.2 M4: args[0] is unholed so callbacks never see the $ArrayHole
    /// sentinel. Callers that want to skip holes entirely should call
    /// <see cref="EmitSkipIfHole"/> BEFORE this helper — the unhole here is
    /// for find/etc. which must still invoke the callback on holes (with
    /// undefined as the element).
    /// </remarks>
    /// <summary>
    /// Allocates the per-helper-invocation callback args buffer ONCE, pre-fills
    /// the receiver slot (args[2] — constant for the duration of the helper),
    /// and returns the local. Per-iteration work then only writes args[0] (the
    /// element) and args[1] (the boxed index) before invoking — no per-iter
    /// <c>newarr</c>. For N=1,000,000, this saves N-1 array allocations
    /// (~32 bytes × 1M = ~32 MB of GC pressure on Map alone).
    /// </summary>
    /// <remarks>
    /// Safety: <c>InvokeMethodValue</c> ultimately dispatches to
    /// <c>MethodInfo.Invoke(target, args)</c>, which reads values out of the
    /// array onto the call stack and does NOT retain a reference to the args
    /// array. Compiled function bodies build their own <c>arguments</c> object
    /// from the call frame, not by aliasing our args[]. So reusing the same
    /// args[] across iterations is sound.
    /// </remarks>
    private void EmitInitCallbackArgs(ILGenerator il, EmittedRuntime runtime, out LocalBuilder argsLocal)
    {
        argsLocal = il.DeclareLocal(_types.ObjectArray);

        // args = new object[3]
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);

        // args[2] = $Runtime._currentArrayLikeReceiver ?? list (constant for
        // the helper invocation — receiver doesn't change per iteration)
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldsfld, runtime.CurrentArrayLikeReceiverField);
        var useOriginalLabel = il.DefineLabel();
        var afterReceiverLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, useOriginalLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Br, afterReceiverLabel);
        il.MarkLabel(useOriginalLabel);
        il.MarkLabel(afterReceiverLabel);
        il.Emit(OpCodes.Stelem_Ref);
    }

    /// <summary>
    /// Allocates the 4-slot args buffer used by reduce/reduceRight ONCE per
    /// helper invocation, with args[3] pre-filled to the receiver. The loop
    /// body then writes args[0]=acc, args[1]=element, args[2]=index per
    /// iteration without re-allocating.
    /// </summary>
    private void EmitInitReduceArgs(ILGenerator il, EmittedRuntime runtime, out LocalBuilder argsLocal)
    {
        argsLocal = il.DeclareLocal(_types.ObjectArray);

        // args = new object[4]
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);

        // args[3] = $Runtime._currentArrayLikeReceiver ?? list
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Ldsfld, runtime.CurrentArrayLikeReceiverField);
        var useOriginalLabel = il.DefineLabel();
        var afterReceiverLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, useOriginalLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Br, afterReceiverLabel);
        il.MarkLabel(useOriginalLabel);
        il.MarkLabel(afterReceiverLabel);
        il.Emit(OpCodes.Stelem_Ref);
    }

    /// <summary>
    /// Detects at iterator entry whether the callback is an arrow function
    /// (compiled $TSFunction without a __this synthetic param) AND has an
    /// arity of at most 1. When both hold, the index argument boxed for
    /// args[1] is statically unreachable: arrows can't access JS
    /// <c>arguments</c>, and the formal index param doesn't exist either.
    /// Skipping the per-iter <c>(double)i → Box → Stelem_Ref</c> trio saves
    /// ~24 bytes per iteration. Out-bool defaults false (i.e., always box)
    /// for any callback that isn't a plain $TSFunction or that DOES have a
    /// <c>__this</c> param (compiled regular functions can access
    /// <c>arguments</c>) or has arity >= 2.
    /// </summary>
    private void EmitDetectSkipIndexBox(ILGenerator il, EmittedRuntime runtime, out LocalBuilder skipIndexBoxLocal)
    {
        skipIndexBoxLocal = il.DeclareLocal(_types.Boolean);
        // skipIndexBox = false
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, skipIndexBoxLocal);

        var notTSFunctionLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        // var tsFn = callback as $TSFunction;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        var tsFnLocal = il.DeclareLocal(runtime.TSFunctionType);
        il.Emit(OpCodes.Stloc, tsFnLocal);
        il.Emit(OpCodes.Ldloc, tsFnLocal);
        il.Emit(OpCodes.Brfalse, notTSFunctionLabel);

        // if (tsFn._expectsThis) goto doneLabel — regular function (compiled
        // bodies that read JS `arguments`); leave skipIndexBox=false to keep
        // boxing the index for arguments[1] correctness.
        il.Emit(OpCodes.Ldloc, tsFnLocal);
        il.Emit(OpCodes.Ldfld, runtime.TSFunctionExpectsThisField);
        il.Emit(OpCodes.Brtrue, doneLabel);

        // if (tsFn._capturesArguments) goto doneLabel — `this`-less function
        // DECLARATIONS have _expectsThis=false (no __this param) yet can still
        // observe the index via `arguments`. Without this guard the index box
        // was skipped and `function(){...arguments[1]...}` callbacks read a null
        // index (#101). Arrows never set this flag (they can't bind their own
        // `arguments`), so the unary-arrow fast path is preserved.
        il.Emit(OpCodes.Ldloc, tsFnLocal);
        il.Emit(OpCodes.Ldfld, runtime.TSFunctionCapturesArgumentsField);
        il.Emit(OpCodes.Brtrue, doneLabel);

        // if (tsFn._paramCount > 1) goto doneLabel
        il.Emit(OpCodes.Ldloc, tsFnLocal);
        il.Emit(OpCodes.Ldfld, runtime.TSFunctionParamCountField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, doneLabel);

        // skipIndexBox = true
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, skipIndexBoxLocal);

        il.MarkLabel(notTSFunctionLabel);
        il.MarkLabel(doneLabel);
    }

    private void EmitCallbackArgsAndInvoke(ILGenerator il, LocalBuilder indexLocal, EmittedRuntime runtime, LocalBuilder isLazyLocal, LocalBuilder argsLocal, LocalBuilder skipIndexBoxLocal, Action<ILGenerator>? loadElementOverride = null)
    {
        // args[0] = element value. Default: unholed list[i]. When the caller
        // already knows list[i] would IOOB (e.g. find/findIndex after callback
        // truncated the array), it passes a loader that emits undefined —
        // ECMA-262 §23.1.3.10/§23.1.3.11 still invoke the callback with
        // `kValue = ? Get(O, ToString(k))` which is undefined for OOB.
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        if (loadElementOverride is not null)
            loadElementOverride(il);
        else
            EmitLoadElementUnholed(il, indexLocal, runtime, isLazyLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // args[1] = (double)i — but skip the box+store entirely when the
        // callback is a unary arrow (skipIndexBoxLocal=true). The args[1]
        // slot stays at whatever it was last (or null on first iter), but
        // the callback can't see it: arity≤1 means no formal `index` param,
        // and arrows can't access JS `arguments`. AdjustArgs trims args to
        // paramCount=1 before reflection-Invoke anyway, so the slot's
        // contents are dropped before reaching the callback's stack frame.
        var skipBoxLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, skipIndexBoxLocal);
        il.Emit(OpCodes.Brtrue, skipBoxLabel);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);
        il.MarkLabel(skipBoxLabel);

        // InvokeMethodValue(thisArg, callback, args). thisArg comes from the
        // _currentCallbackThisArg thread-static (set by ArrayEmitter when the
        // user passes a 2nd arg to forEach/map/etc, e.g. `arr.forEach(cb, ctx)`).
        // args[2] (receiver) was pre-filled by EmitInitCallbackArgs.
        il.Emit(OpCodes.Ldsfld, runtime.CurrentCallbackThisArgField);
        il.Emit(OpCodes.Ldarg_1); // callback
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
    }

    private void EmitArrayMap(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayMap",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArrayMap = method;

        var il = method.GetILGenerator();
        EmitThrowIfCallbackNotCallable(il, runtime, 1, "Array.prototype.map");

        // Hoist the lazy check once at entry so per-element loads branch on
        // a stack-resident bool instead of re-reading the thread-static.
        EmitHoistedLazyCheck(il, runtime, out var isLazyLocal, out _);
        EmitInitCallbackArgs(il, runtime, out var argsLocal);
        EmitDetectSkipIndexBox(il, runtime, out var skipIndexBoxLocal);

        // var result = new List<object>(list.Count). Map's output length is
        // exactly list.Count (1:1 transform, holes preserved as $ArrayHole),
        // so pre-size the backing array to skip log(N) doublings.
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.Int32));
        il.Emit(OpCodes.Stloc, resultLocal);

        // var i = 0
        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // ECMA-262 §23.1.3.19 step 2: cache len ONCE before the loop. Callback
        // mutations to list.Count (e.g. arr[1000]=v) must not affect iteration.
        var lenLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var holeLabel = il.DefineLabel();
        var addedLabel = il.DefineLabel();

        // Loop: for (int i = 0; i < cached_len; i++)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // ECMA-262 23.1.3.19: map SKIPS the callback on holes but PRESERVES
        // the hole in the output at the same position. So a `map` over
        // `[1, hole, 3]` yields `[fn(1), hole, fn(3)]`, not `[fn(1), fn(3)]`.
        EmitSkipIfHole(il, indexLocal, holeLabel, runtime, isLazyLocal);

        EmitCallbackArgsAndInvoke(il, indexLocal, runtime, isLazyLocal, argsLocal, skipIndexBoxLocal);

        // Store the call result
        var callResultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, callResultLocal);

        // result.Add(callResult)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, callResultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Br, addedLabel);

        il.MarkLabel(holeLabel);
        // Hole-preserving output: push $ArrayHole.Instance to the result list.
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldsfld, runtime.ArrayHoleInstance);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.MarkLabel(addedLabel);
        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Fast-path map helper for non-capturing literal-arrow callbacks. Takes a
    /// <c>Func&lt;object, object&gt;</c> directly (built at the call site via
    /// ldftn + Func ctor) instead of the generic <c>object</c> callback that
    /// ArrayMap dispatches through <c>$TSFunction.InvokeWithThis</c> →
    /// MethodInvoker. Per-iter cost drops to a single delegate invocation.
    ///
    /// Preconditions enforced statically at the call site:
    /// - Callback is a non-capturing arrow with arity ≤ 1, no <c>arguments</c>,
    ///   no <c>__this</c> param, not async/generator.
    /// - Receiver is a real <c>List&lt;object&gt;</c> (no lazy/Dict path).
    /// - No <c>thisArg</c> argument (arrows ignore it anyway).
    ///
    /// Holes preserved as <c>$ArrayHole.Instance</c> per ECMA-262 23.1.3.19.
    /// </summary>
    private void EmitArrayMapDirect(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var funcObjectObject = typeof(Func<object, object>);
        var method = typeBuilder.DefineMethod(
            "ArrayMapDirect",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, funcObjectObject]
        );
        runtime.ArrayMapDirect = method;

        var il = method.GetILGenerator();

        var listCountGetter = _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!;
        var listIndexerGetter = _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!;
        var listAdd = _types.GetMethod(_types.ListOfObject, "Add", _types.Object);
        var funcInvoke = funcObjectObject.GetMethod("Invoke")!;

        // var result = new List<object>(list.Count)
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, listCountGetter);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.Int32));
        il.Emit(OpCodes.Stloc, resultLocal);

        // var i = 0
        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var holeLabel = il.DefineLabel();
        var addedLabel = il.DefineLabel();

        // for (int i = 0; i < list.Count; i++)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, listCountGetter);
        il.Emit(OpCodes.Bge, loopEnd);

        // var element = list[i]; if (element is $ArrayHole) goto holeLabel;
        var elementLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, listIndexerGetter);
        il.Emit(OpCodes.Stloc, elementLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brtrue, holeLabel);

        // result.Add(cb(element))
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Callvirt, funcInvoke);
        il.Emit(OpCodes.Callvirt, listAdd);
        il.Emit(OpCodes.Br, addedLabel);

        // result.Add($ArrayHole.Instance)
        il.MarkLabel(holeLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldsfld, runtime.ArrayHoleInstance);
        il.Emit(OpCodes.Callvirt, listAdd);

        il.MarkLabel(addedLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Fast-path filter for non-capturing literal-arrow predicates. Same
    /// structure as <see cref="EmitArrayMapDirect"/> but Add is gated on
    /// <c>IsTruthy(predicate(element))</c>. Holes skipped per ECMA-262
    /// 23.1.3.8 (callback not invoked, element not copied — filter densifies).
    /// </summary>
    private void EmitArrayFilterDirect(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var funcObjectObject = typeof(Func<object, object>);
        var method = typeBuilder.DefineMethod(
            "ArrayFilterDirect",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, funcObjectObject]
        );
        runtime.ArrayFilterDirect = method;

        var il = method.GetILGenerator();
        var listCountGetter = _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!;
        var listIndexerGetter = _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!;
        var listAdd = _types.GetMethod(_types.ListOfObject, "Add", _types.Object);
        var funcInvoke = funcObjectObject.GetMethod("Invoke")!;

        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, listCountGetter);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.Int32));
        il.Emit(OpCodes.Stloc, resultLocal);

        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var skipAdd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, listCountGetter);
        il.Emit(OpCodes.Bge, loopEnd);

        // Load element; skip if hole.
        var elementLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, listIndexerGetter);
        il.Emit(OpCodes.Stloc, elementLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brtrue, skipAdd);

        // if (!IsTruthy(cb(element))) goto skipAdd
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Callvirt, funcInvoke);
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Brfalse, skipAdd);

        // result.Add(element)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Callvirt, listAdd);

        il.MarkLabel(skipAdd);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Bool-typed-predicate variant of <see cref="EmitArrayFilterDirect"/>.
    /// Predicate signature is <c>Func&lt;object, bool&gt;</c>; we skip the
    /// <c>IsTruthy</c> indirection and the predicate's result-box. Engages
    /// when the type checker infers a typed boolean return (e.g.
    /// <c>v =&gt; v &gt; 10</c>).
    /// </summary>
    private void EmitArrayFilterDirectBool(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var funcObjectBool = typeof(Func<object, bool>);
        var method = typeBuilder.DefineMethod(
            "ArrayFilterDirectBool",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, funcObjectBool]
        );
        runtime.ArrayFilterDirectBool = method;

        var il = method.GetILGenerator();
        var listCountGetter = _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!;
        var listIndexerGetter = _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!;
        var listAdd = _types.GetMethod(_types.ListOfObject, "Add", _types.Object);
        var funcInvoke = funcObjectBool.GetMethod("Invoke")!;

        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, listCountGetter);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.Int32));
        il.Emit(OpCodes.Stloc, resultLocal);

        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var skipAdd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, listCountGetter);
        il.Emit(OpCodes.Bge, loopEnd);

        var elementLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, listIndexerGetter);
        il.Emit(OpCodes.Stloc, elementLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brtrue, skipAdd);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Callvirt, funcInvoke);
        il.Emit(OpCodes.Brfalse, skipAdd);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Callvirt, listAdd);

        il.MarkLabel(skipAdd);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Fast-path forEach. No result list; callback's return value discarded.
    /// Holes skipped per ECMA-262 23.1.3.10. Helper returns void; the call
    /// site is responsible for pushing Ldnull to balance the expression
    /// stack (matches the existing slow path's <c>Ldnull</c>).
    /// </summary>
    private void EmitArrayForEachDirect(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var funcObjectObject = typeof(Func<object, object>);
        var method = typeBuilder.DefineMethod(
            "ArrayForEachDirect",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.ListOfObject, funcObjectObject]
        );
        runtime.ArrayForEachDirect = method;

        var il = method.GetILGenerator();
        var listCountGetter = _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!;
        var listIndexerGetter = _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!;
        var funcInvoke = funcObjectObject.GetMethod("Invoke")!;

        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var skipCall = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, listCountGetter);
        il.Emit(OpCodes.Bge, loopEnd);

        // Load element; skip if hole.
        var elementLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, listIndexerGetter);
        il.Emit(OpCodes.Stloc, elementLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brtrue, skipCall);

        // cb(element); discard return.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Callvirt, funcInvoke);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skipCall);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Fast-path find. ECMA-262 23.1.3.10: callback IS invoked on holes (with
    /// undefined). Returns first element where predicate is truthy, else
    /// <c>$Undefined.Instance</c>.
    /// </summary>
    private void EmitArrayFindDirect(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var funcObjectObject = typeof(Func<object, object>);
        var method = typeBuilder.DefineMethod(
            "ArrayFindDirect",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject, funcObjectObject]
        );
        runtime.ArrayFindDirect = method;

        var il = method.GetILGenerator();
        var listCountGetter = _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!;
        var listIndexerGetter = _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!;
        var funcInvoke = funcObjectObject.GetMethod("Invoke")!;

        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var notFound = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, listCountGetter);
        il.Emit(OpCodes.Bge, loopEnd);

        // Load element; if hole, substitute $Undefined (find still invokes).
        var elementLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, listIndexerGetter);
        il.Emit(OpCodes.Stloc, elementLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        var notHole = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notHole);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Stloc, elementLocal);
        il.MarkLabel(notHole);

        // if (IsTruthy(cb(element))) return element-unholed
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Callvirt, funcInvoke);
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Brfalse, notFound);
        // Return the unholed element (already substituted above)
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFound);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Bool-typed-predicate variant of <see cref="EmitArrayFindDirect"/>.
    /// </summary>
    private void EmitArrayFindDirectBool(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var funcObjectBool = typeof(Func<object, bool>);
        var method = typeBuilder.DefineMethod(
            "ArrayFindDirectBool",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject, funcObjectBool]
        );
        runtime.ArrayFindDirectBool = method;

        var il = method.GetILGenerator();
        var listCountGetter = _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!;
        var listIndexerGetter = _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!;
        var funcInvoke = funcObjectBool.GetMethod("Invoke")!;

        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var notFound = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, listCountGetter);
        il.Emit(OpCodes.Bge, loopEnd);

        var elementLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, listIndexerGetter);
        il.Emit(OpCodes.Stloc, elementLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        var notHole = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notHole);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Stloc, elementLocal);
        il.MarkLabel(notHole);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Callvirt, funcInvoke);
        il.Emit(OpCodes.Brfalse, notFound);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFound);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Fast-path findIndex. Same shape as findDirect, but returns
    /// <c>(double)i</c> on match, else <c>-1.0</c>.
    /// </summary>
    private void EmitArrayFindIndexDirect(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var funcObjectObject = typeof(Func<object, object>);
        var method = typeBuilder.DefineMethod(
            "ArrayFindIndexDirect",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.ListOfObject, funcObjectObject]
        );
        runtime.ArrayFindIndexDirect = method;

        var il = method.GetILGenerator();
        var listCountGetter = _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!;
        var listIndexerGetter = _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!;
        var funcInvoke = funcObjectObject.GetMethod("Invoke")!;

        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var notFound = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, listCountGetter);
        il.Emit(OpCodes.Bge, loopEnd);

        var elementLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, listIndexerGetter);
        il.Emit(OpCodes.Stloc, elementLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        var notHole = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notHole);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Stloc, elementLocal);
        il.MarkLabel(notHole);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Callvirt, funcInvoke);
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Brfalse, notFound);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFound);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Bool-typed-predicate variant of <see cref="EmitArrayFindIndexDirect"/>.
    /// </summary>
    private void EmitArrayFindIndexDirectBool(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var funcObjectBool = typeof(Func<object, bool>);
        var method = typeBuilder.DefineMethod(
            "ArrayFindIndexDirectBool",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.ListOfObject, funcObjectBool]
        );
        runtime.ArrayFindIndexDirectBool = method;

        var il = method.GetILGenerator();
        var listCountGetter = _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!;
        var listIndexerGetter = _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!;
        var funcInvoke = funcObjectBool.GetMethod("Invoke")!;

        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var notFound = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, listCountGetter);
        il.Emit(OpCodes.Bge, loopEnd);

        var elementLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, listIndexerGetter);
        il.Emit(OpCodes.Stloc, elementLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        var notHole = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notHole);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Stloc, elementLocal);
        il.MarkLabel(notHole);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Callvirt, funcInvoke);
        il.Emit(OpCodes.Brfalse, notFound);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFound);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Fast-path some. ECMA-262 23.1.3.29: skips holes. Returns boxed-true on
    /// first truthy result, else boxed-false. Matches slow path's
    /// <c>_types.Object</c> return convention (caller already expects a boxed
    /// bool there).
    /// </summary>
    private void EmitArraySomeDirect(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitPredicateAllOrAnyDirect(typeBuilder, runtime, name: "ArraySomeDirect",
            shortCircuitOnTruthy: true, defaultResult: false,
            assignTo: m => runtime.ArraySomeDirect = m);
    }

    /// <summary>
    /// Fast-path every. Same shape as some but inverted: short-circuits on
    /// first falsy result with boxed-false; returns boxed-true if all match.
    /// </summary>
    private void EmitArrayEveryDirect(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitPredicateAllOrAnyDirect(typeBuilder, runtime, name: "ArrayEveryDirect",
            shortCircuitOnTruthy: false, defaultResult: true,
            assignTo: m => runtime.ArrayEveryDirect = m);
    }

    /// <summary>
    /// Bool-typed-predicate some/every variants. Fast path skips
    /// <c>IsTruthy</c> and the box around the predicate's bool result.
    /// </summary>
    private void EmitArraySomeDirectBool(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitPredicateAllOrAnyDirectBool(typeBuilder, runtime, name: "ArraySomeDirectBool",
            shortCircuitOnTruthy: true, defaultResult: false,
            assignTo: m => runtime.ArraySomeDirectBool = m);
    }

    private void EmitArrayEveryDirectBool(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitPredicateAllOrAnyDirectBool(typeBuilder, runtime, name: "ArrayEveryDirectBool",
            shortCircuitOnTruthy: false, defaultResult: true,
            assignTo: m => runtime.ArrayEveryDirectBool = m);
    }

    private void EmitPredicateAllOrAnyDirectBool(TypeBuilder typeBuilder, EmittedRuntime runtime,
        string name, bool shortCircuitOnTruthy, bool defaultResult,
        Action<MethodBuilder> assignTo)
    {
        var funcObjectBool = typeof(Func<object, bool>);
        var method = typeBuilder.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject, funcObjectBool]
        );
        assignTo(method);

        var il = method.GetILGenerator();
        var listCountGetter = _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!;
        var listIndexerGetter = _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!;
        var funcInvoke = funcObjectBool.GetMethod("Invoke")!;

        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var advance = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, listCountGetter);
        il.Emit(OpCodes.Bge, loopEnd);

        var elementLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, listIndexerGetter);
        il.Emit(OpCodes.Stloc, elementLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brtrue, advance);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Callvirt, funcInvoke);
        // Result is already a bool — branch directly without IsTruthy.
        il.Emit(shortCircuitOnTruthy ? OpCodes.Brfalse : OpCodes.Brtrue, advance);
        il.Emit(shortCircuitOnTruthy ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(advance);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(defaultResult ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Common emitter for some/every fast paths. Both walk the list, skip
    /// holes, invoke the predicate, and short-circuit on a boundary condition
    /// (truthy for some, falsy for every) returning the opposite boolean.
    /// </summary>
    private void EmitPredicateAllOrAnyDirect(TypeBuilder typeBuilder, EmittedRuntime runtime,
        string name, bool shortCircuitOnTruthy, bool defaultResult,
        Action<MethodBuilder> assignTo)
    {
        var funcObjectObject = typeof(Func<object, object>);
        var method = typeBuilder.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject, funcObjectObject]
        );
        assignTo(method);

        var il = method.GetILGenerator();
        var listCountGetter = _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!;
        var listIndexerGetter = _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!;
        var funcInvoke = funcObjectObject.GetMethod("Invoke")!;

        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var advance = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, listCountGetter);
        il.Emit(OpCodes.Bge, loopEnd);

        // skip if hole (some/every both skip holes per spec)
        var elementLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, listIndexerGetter);
        il.Emit(OpCodes.Stloc, elementLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brtrue, advance);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Callvirt, funcInvoke);
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        // For some: brtrue → return true. For every: brfalse → return false.
        il.Emit(shortCircuitOnTruthy ? OpCodes.Brfalse : OpCodes.Brtrue, advance);
        il.Emit(shortCircuitOnTruthy ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(advance);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(defaultResult ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayFilter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayFilter",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArrayFilter = method;

        var il = method.GetILGenerator();
        EmitThrowIfCallbackNotCallable(il, runtime, 1, "Array.prototype.filter");

        EmitHoistedLazyCheck(il, runtime, out var isLazyLocal, out _);
        EmitInitCallbackArgs(il, runtime, out var argsLocal);
        EmitDetectSkipIndexBox(il, runtime, out var skipIndexBoxLocal);

        // var result = new List<object>(list.Count). Filter's output is bounded
        // by list.Count; pre-sizing avoids backing-array doublings on the worst
        // case (every element kept). Slight over-allocation when filter is
        // selective, but List<>'s growth amortization is the dominant cost we
        // want to skip — a single one-shot capacity is cheaper than log(N)
        // doublings even for sparse outputs.
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.Int32));
        il.Emit(OpCodes.Stloc, resultLocal);

        // var i = 0
        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // ECMA-262 §23.1.3.8 step 2: cache len ONCE before the loop.
        var lenLocalFilter = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocalFilter);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var skipAdd = il.DefineLabel();

        // Loop: for (int i = 0; i < cached_len; i++)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lenLocalFilter);
        il.Emit(OpCodes.Bge, loopEnd);

        // ECMA-262 23.1.3.8: filter skips holes (callback not invoked, element
        // not copied into output — filter densifies).
        EmitSkipIfHole(il, indexLocal, skipAdd, runtime, isLazyLocal);

        EmitCallbackArgsAndInvoke(il, indexLocal, runtime, isLazyLocal, argsLocal, skipIndexBoxLocal);

        // Call IsTruthy
        il.Emit(OpCodes.Call, runtime.IsTruthy);

        // if (!truthy) skip add
        il.Emit(OpCodes.Brfalse, skipAdd);

        // result.Add(<element>) — same lazy-aware load path. ECMA-262 23.1.3.8
        // says filter calls Get(O, k) once per index — this is the second
        // call (the first was inside the callback's args[0]). Spec-strict
        // tests counting getter invocations may flag this; tests that check
        // value-flow correctness pass.
        il.Emit(OpCodes.Ldloc, resultLocal);
        EmitElementLoad(il, indexLocal, runtime, isLazyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.MarkLabel(skipAdd);

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayForEach(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayForEach",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArrayForEach = method;

        var il = method.GetILGenerator();
        EmitThrowIfCallbackNotCallable(il, runtime, 1, "Array.prototype.forEach");

        EmitHoistedLazyCheck(il, runtime, out var isLazyLocal, out _);
        EmitInitCallbackArgs(il, runtime, out var argsLocal);
        EmitDetectSkipIndexBox(il, runtime, out var skipIndexBoxLocal);

        // var i = 0
        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // ECMA-262 §23.1.3.15 step 2: cache len.
        var lenLocalFE = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocalFE);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var advance = il.DefineLabel();

        // Loop: for (int i = 0; i < cached_len; i++)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lenLocalFE);
        il.Emit(OpCodes.Bge, loopEnd);

        // ECMA-262 23.1.3.15: forEach skips holes.
        EmitSkipIfHole(il, indexLocal, advance, runtime, isLazyLocal);

        EmitCallbackArgsAndInvoke(il, indexLocal, runtime, isLazyLocal, argsLocal, skipIndexBoxLocal);
        il.Emit(OpCodes.Pop); // Discard result

        il.MarkLabel(advance);
        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayFind(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayFind",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArrayFind = method;

        var il = method.GetILGenerator();
        EmitThrowIfCallbackNotCallable(il, runtime, 1, "Array.prototype.find");

        EmitHoistedLazyCheck(il, runtime, out var isLazyLocal, out _);
        EmitInitCallbackArgs(il, runtime, out var argsLocal);
        EmitDetectSkipIndexBox(il, runtime, out var skipIndexBoxLocal);

        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // ECMA-262 §23.1.3.10 step 2: cache len.
        var lenLocalFind = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocalFind);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lenLocalFind);
        il.Emit(OpCodes.Bge, loopEnd);

        var notFound = il.DefineLabel();
        EmitCallbackArgsAndInvoke(il, indexLocal, runtime, isLazyLocal, argsLocal, skipIndexBoxLocal);

        // if (IsTruthy(result)) return list[i] (unholed — spec: find returns
        // `undefined` when the matched slot is a hole, not the sentinel).
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        il.Emit(OpCodes.Brfalse, notFound);
        EmitLoadElementUnholed(il, indexLocal, runtime, isLazyLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFound);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        // ECMA-262 23.1.3.10 Array.prototype.find: return undefined when no element matches.
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayFindIndex(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayFindIndex",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArrayFindIndex = method;

        var il = method.GetILGenerator();
        EmitThrowIfCallbackNotCallable(il, runtime, 1, "Array.prototype.findIndex");

        EmitHoistedLazyCheck(il, runtime, out var isLazyLocal, out _);
        EmitInitCallbackArgs(il, runtime, out var argsLocal);
        EmitDetectSkipIndexBox(il, runtime, out var skipIndexBoxLocal);

        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // ECMA-262 §23.1.3.11 step 2: cache len.
        var lenLocalFI = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocalFI);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lenLocalFI);
        il.Emit(OpCodes.Bge, loopEnd);

        var notFound = il.DefineLabel();
        var truthyReturnIndexLabel = il.DefineLabel();
        EmitCallbackArgsAndInvoke(il, indexLocal, runtime, isLazyLocal, argsLocal, skipIndexBoxLocal);
        il.Emit(OpCodes.Call, runtime.IsTruthy);

        il.Emit(OpCodes.Brfalse, notFound);
        il.MarkLabel(truthyReturnIndexLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFound);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArraySome(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArraySome",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,  // Return boxed bool to match ILEmitter expectations
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArraySome = method;

        var il = method.GetILGenerator();
        EmitThrowIfCallbackNotCallable(il, runtime, 1, "Array.prototype.some");

        EmitHoistedLazyCheck(il, runtime, out var isLazyLocal, out _);
        EmitInitCallbackArgs(il, runtime, out var argsLocal);
        EmitDetectSkipIndexBox(il, runtime, out var skipIndexBoxLocal);

        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // ECMA-262 §23.1.3.29 step 2: cache len.
        var lenLocalSome = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocalSome);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var advance = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lenLocalSome);
        il.Emit(OpCodes.Bge, loopEnd);

        // ECMA-262 23.1.3.29: some skips holes.
        EmitSkipIfHole(il, indexLocal, advance, runtime, isLazyLocal);

        EmitCallbackArgsAndInvoke(il, indexLocal, runtime, isLazyLocal, argsLocal, skipIndexBoxLocal);
        il.Emit(OpCodes.Call, runtime.IsTruthy);

        il.Emit(OpCodes.Brfalse, advance);
        il.Emit(OpCodes.Ldc_I4_1); // return true
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(advance);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldc_I4_0); // return false
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayEvery(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayEvery",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,  // Return boxed bool to match ILEmitter expectations
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArrayEvery = method;

        var il = method.GetILGenerator();
        EmitThrowIfCallbackNotCallable(il, runtime, 1, "Array.prototype.every");

        EmitHoistedLazyCheck(il, runtime, out var isLazyLocal, out _);
        EmitInitCallbackArgs(il, runtime, out var argsLocal);
        EmitDetectSkipIndexBox(il, runtime, out var skipIndexBoxLocal);

        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        // ECMA-262 §23.1.3.6 step 2: cache len ONCE before the loop. Callbacks
        // that mutate list.Count (e.g. arr[1000]=v adding a new element) must
        // NOT cause iteration past the original len.
        var lenLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var continueLoop = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // ECMA-262 23.1.3.6: every skips holes (callback not invoked; doesn't
        // affect "all match" result).
        EmitSkipIfHole(il, indexLocal, continueLoop, runtime, isLazyLocal);

        EmitCallbackArgsAndInvoke(il, indexLocal, runtime, isLazyLocal, argsLocal, skipIndexBoxLocal);
        il.Emit(OpCodes.Call, runtime.IsTruthy);

        il.Emit(OpCodes.Brtrue, continueLoop);
        il.Emit(OpCodes.Ldc_I4_0); // return false
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(continueLoop);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldc_I4_1); // return true
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayFindLast(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayFindLast",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArrayFindLast = method;

        var il = method.GetILGenerator();
        EmitThrowIfCallbackNotCallable(il, runtime, 1, "Array.prototype.findLast");

        EmitHoistedLazyCheck(il, runtime, out var isLazyLocal, out _);
        EmitInitCallbackArgs(il, runtime, out var argsLocal);
        EmitDetectSkipIndexBox(il, runtime, out var skipIndexBoxLocal);

        // int i = list.Count - 1
        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // Loop: for (int i = list.Count - 1; i >= 0; i--)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, loopEnd);

        EmitCallbackArgsAndInvoke(il, indexLocal, runtime, isLazyLocal, argsLocal, skipIndexBoxLocal);

        // if (IsTruthy(result)) return list[i] (unholed).
        il.Emit(OpCodes.Call, runtime.IsTruthy);
        var notFound = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notFound);
        EmitLoadElementUnholed(il, indexLocal, runtime, isLazyLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFound);
        // i--
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        // ECMA-262 23.1.3.11 Array.prototype.findLast: return undefined when no element matches.
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayFindLastIndex(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayFindLastIndex",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArrayFindLastIndex = method;

        var il = method.GetILGenerator();
        EmitThrowIfCallbackNotCallable(il, runtime, 1, "Array.prototype.findLastIndex");

        EmitHoistedLazyCheck(il, runtime, out var isLazyLocal, out _);
        EmitInitCallbackArgs(il, runtime, out var argsLocal);
        EmitDetectSkipIndexBox(il, runtime, out var skipIndexBoxLocal);

        // int i = list.Count - 1
        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // Loop: for (int i = list.Count - 1; i >= 0; i--)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, loopEnd);

        EmitCallbackArgsAndInvoke(il, indexLocal, runtime, isLazyLocal, argsLocal, skipIndexBoxLocal);
        il.Emit(OpCodes.Call, runtime.IsTruthy);

        var notFound = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notFound);
        // Return index as double
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFound);
        // i--
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        // Return -1
        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Fast-path reduce — initial value REQUIRED. The slow path's no-initial
    /// branch (scan-for-first-present, kPresent tracking, TypeError on empty)
    /// is intentionally not duplicated here; the call site routes only the
    /// 2-argument form (`arr.reduce(cb, init)`) to this helper. Holes are
    /// skipped (callback not invoked, accumulator carries through).
    /// </summary>
    private void EmitArrayReduceDirect(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var func3 = typeof(Func<object, object, object>);
        var method = typeBuilder.DefineMethod(
            "ArrayReduceDirect",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject, func3, _types.Object]
        );
        runtime.ArrayReduceDirect = method;

        var il = method.GetILGenerator();
        var listCountGetter = _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!;
        var listIndexerGetter = _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!;
        var funcInvoke = func3.GetMethod("Invoke")!;

        // acc = initial; i = 0
        var accLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, accLocal);

        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var advance = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, listCountGetter);
        il.Emit(OpCodes.Bge, loopEnd);

        // skip holes
        var elementLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, listIndexerGetter);
        il.Emit(OpCodes.Stloc, elementLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brtrue, advance);

        // acc = cb(acc, element)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, accLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Callvirt, funcInvoke);
        il.Emit(OpCodes.Stloc, accLocal);

        il.MarkLabel(advance);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, accLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Typed reduce over a promoted <c>List&lt;double&gt;</c> with a <c>Func&lt;double,double,double&gt;</c>
    /// reducer (#861 typed-HOF pipeline): no per-element boxing, no hole check (a promoted list is dense).
    /// <c>double ArrayReduceDouble(List&lt;double&gt; src, Func&lt;double,double,double&gt; f, double init)</c>.
    /// Pure-BCL (no SharpTS reference) — standalone-DLL safe.
    /// </summary>
    private void EmitArrayReduceDouble(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var func3 = typeof(Func<double, double, double>);
        var method = typeBuilder.DefineMethod(
            "ArrayReduceDouble",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.ListOfDouble, func3, _types.Double]
        );
        runtime.ArrayReduceDouble = method;

        var il = method.GetILGenerator();
        var listCountGetter = _types.GetProperty(_types.ListOfDouble, "Count").GetGetMethod()!;
        var listIndexerGetter = _types.GetProperty(_types.ListOfDouble, "Item").GetGetMethod()!;
        var funcInvoke = func3.GetMethod("Invoke")!;

        var accLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, accLocal);

        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd2 = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, listCountGetter);
        il.Emit(OpCodes.Bge, loopEnd2);

        // acc = f(acc, src[i])
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, accLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, listIndexerGetter);
        il.Emit(OpCodes.Callvirt, funcInvoke);
        il.Emit(OpCodes.Stloc, accLocal);

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd2);
        il.Emit(OpCodes.Ldloc, accLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Typed map over a promoted <c>List&lt;double&gt;</c> with a <c>Func&lt;double,double&gt;</c> (#861
    /// typed-HOF pipeline): builds a fresh <c>List&lt;double&gt;</c>, no per-element boxing.
    /// <c>List&lt;double&gt; ArrayMapDouble(List&lt;double&gt; src, Func&lt;double,double&gt; f)</c>.
    /// Pure-BCL — standalone-DLL safe.
    /// </summary>
    private void EmitArrayMapDouble(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var func2 = typeof(Func<double, double>);
        var method = typeBuilder.DefineMethod(
            "ArrayMapDouble",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfDouble,
            [_types.ListOfDouble, func2]
        );
        runtime.ArrayMapDouble = method;

        var il = method.GetILGenerator();
        var listCountGetter = _types.GetProperty(_types.ListOfDouble, "Count").GetGetMethod()!;
        var listIndexerGetter = _types.GetProperty(_types.ListOfDouble, "Item").GetGetMethod()!;
        var listAdd = _types.GetMethod(_types.ListOfDouble, "Add", _types.Double);
        var listCapCtor = _types.GetConstructor(_types.ListOfDouble, _types.Int32);
        var funcInvoke = func2.GetMethod("Invoke")!;

        // result = new List<double>(src.Count)
        var resultLocal = il.DeclareLocal(_types.ListOfDouble);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, listCountGetter);
        il.Emit(OpCodes.Newobj, listCapCtor);
        il.Emit(OpCodes.Stloc, resultLocal);

        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, listCountGetter);
        il.Emit(OpCodes.Bge, loopEnd);

        // result.Add(f(src[i]))
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, listIndexerGetter);
        il.Emit(OpCodes.Callvirt, funcInvoke);
        il.Emit(OpCodes.Callvirt, listAdd);

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Typed filter over a promoted <c>List&lt;double&gt;</c> with a <c>Func&lt;double,bool&gt;</c> (#861
    /// typed-HOF pipeline): builds a fresh <c>List&lt;double&gt;</c> of the kept elements, no boxing.
    /// <c>List&lt;double&gt; ArrayFilterDouble(List&lt;double&gt; src, Func&lt;double,bool&gt; p)</c>.
    /// Pure-BCL — standalone-DLL safe.
    /// </summary>
    private void EmitArrayFilterDouble(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var func2 = typeof(Func<double, bool>);
        var method = typeBuilder.DefineMethod(
            "ArrayFilterDouble",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfDouble,
            [_types.ListOfDouble, func2]
        );
        runtime.ArrayFilterDouble = method;

        var il = method.GetILGenerator();
        var listCountGetter = _types.GetProperty(_types.ListOfDouble, "Count").GetGetMethod()!;
        var listIndexerGetter = _types.GetProperty(_types.ListOfDouble, "Item").GetGetMethod()!;
        var listAdd = _types.GetMethod(_types.ListOfDouble, "Add", _types.Double);
        var funcInvoke = func2.GetMethod("Invoke")!;

        // result = new List<double>() (kept count unknown up front)
        var resultLocal = il.DeclareLocal(_types.ListOfDouble);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfDouble));
        il.Emit(OpCodes.Stloc, resultLocal);

        var eltLocal = il.DeclareLocal(_types.Double);
        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var advance = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, listCountGetter);
        il.Emit(OpCodes.Bge, loopEnd);

        // elt = src[i]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, listIndexerGetter);
        il.Emit(OpCodes.Stloc, eltLocal);

        // if (p(elt)) result.Add(elt)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, eltLocal);
        il.Emit(OpCodes.Callvirt, funcInvoke);
        il.Emit(OpCodes.Brfalse, advance);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, eltLocal);
        il.Emit(OpCodes.Callvirt, listAdd);

        il.MarkLabel(advance);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayReduce(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayReduce",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject, _types.ObjectArray]
        );
        runtime.ArrayReduce = method;

        var il = method.GetILGenerator();

        EmitHoistedLazyCheck(il, runtime, out var isLazyLocal, out _);

        // LengthOfArrayLike is evaluated once before iteration. The backing
        // list may grow or shrink from an indexed getter/callback, but reduce
        // keeps iterating over the original [0, len) range.
        var lenLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);

        // args[0] = callback, args[1] = initial value (optional)
        var accLocal = il.DeclareLocal(_types.Object);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var callbackLocal = il.DeclareLocal(_types.Object);
        var elementLocal = il.DeclareLocal(_types.Object);

        // ECMA-262 23.1.3.21: throw TypeError if callback is missing or not callable.
        // Check args.Length > 0 FIRST — otherwise Ldelem_Ref throws IndexOutOfRange
        // for `Array.prototype.reduce.call(obj)` with zero args, which we want to
        // surface as a TypeError matching spec (and what the caller's assert.throws
        // expects).
        var reduceCallableOk = il.DefineLabel();
        var reduceCallableThrow = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brfalse, reduceCallableThrow);

        // callback = args[0]
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, callbackLocal);

        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Brfalse, reduceCallableThrow);
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, reduceCallableThrow);
        // Positive callable check — bool/number/string/dict/list/etc. all
        // throw TypeError per ECMA-262 IsCallable. Mirrors
        // EmitThrowIfCallbackNotCallable used by every/filter/map.
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, reduceCallableOk);
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, reduceCallableOk);
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Isinst, runtime.FunctionBindWrapperType);
        il.Emit(OpCodes.Brtrue, reduceCallableOk);
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Isinst, runtime.FunctionCallWrapperType);
        il.Emit(OpCodes.Brtrue, reduceCallableOk);
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Isinst, runtime.FunctionApplyWrapperType);
        il.Emit(OpCodes.Brtrue, reduceCallableOk);
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brtrue, reduceCallableOk);
        il.Emit(OpCodes.Br, reduceCallableThrow);
        il.MarkLabel(reduceCallableThrow);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Array.prototype.reduce callback is not callable");
        il.MarkLabel(reduceCallableOk);

        // Hoist the args[4] allocation once per helper invocation; pre-fill
        // args[3] = receiver (constant for the duration). Per-iter writes
        // only touch args[0..2].
        EmitInitReduceArgs(il, runtime, out var argsLocal);

        // Check if initial value provided (args.Length > 1)
        var hasInitial = il.DefineLabel();
        var noInitial = il.DefineLabel();
        var startLoop = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasInitial);

        // No initial value: scan forward for first PRESENT (non-hole) element.
        // ECMA-262 23.1.3.24: "If no initial value was provided, the first
        // present element is used as the initial acc and kPresent tracking
        // starts from there; TypeError if the array has no present elements."
        var scanLocal = il.DeclareLocal(_types.Int32);
        var scanStart = il.DefineLabel();
        var scanEnd = il.DefineLabel();
        var scanFound = il.DefineLabel();
        var scanValueLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, scanLocal);
        il.MarkLabel(scanStart);
        il.Emit(OpCodes.Ldloc, scanLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, scanEnd);
        // Load exactly once. Re-loading at scanFound invoked an indexed getter
        // twice before the first callback.
        EmitElementLoad(il, scanLocal, runtime, isLazyLocal);
        il.Emit(OpCodes.Stloc, scanValueLocal);
        il.Emit(OpCodes.Ldloc, scanValueLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brfalse, scanFound);
        il.Emit(OpCodes.Ldloc, scanLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, scanLocal);
        il.Emit(OpCodes.Br, scanStart);

        il.MarkLabel(scanEnd);
        // No present element — TypeError per spec. Build a real $TypeError
        // instance (not a raw .NET Exception) so test262 patterns like
        // `e instanceof TypeError` and `e.constructor.name === 'TypeError'` hold.
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Reduce of empty array with no initial value");

        il.MarkLabel(scanFound);
        // acc = the already-observed first present value; i = scan + 1.
        il.Emit(OpCodes.Ldloc, scanValueLocal);
        il.Emit(OpCodes.Stloc, accLocal);
        il.Emit(OpCodes.Ldloc, scanLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, startLoop);

        il.MarkLabel(hasInitial);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, accLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(startLoop);
        var loopEnd = il.DefineLabel();
        var reduceAdvance = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // Resolve presence and value once. The hole sentinel represents
        // HasProperty=false; a present accessor must execute exactly one Get.
        EmitElementLoad(il, indexLocal, runtime, isLazyLocal);
        il.Emit(OpCodes.Stloc, elementLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brtrue, reduceAdvance);

        // Per-iter: write into the pre-allocated args[4]. args[3] is constant
        // (receiver), already pre-filled by EmitInitReduceArgs.
        // args[0] = acc
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, accLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // args[1] = element
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // args[2] = (double)i
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);

        // Reduce has no thisArg parameter. Invoke through the receiver-aware
        // path with the JS undefined sentinel. Function-body `this` loading
        // preserves it for strict callbacks and coerces it to globalThis for
        // sloppy callbacks.
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Stloc, accLocal);

        il.MarkLabel(reduceAdvance);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, startLoop);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, accLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayReduceRight(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayReduceRight",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject, _types.ObjectArray]
        );
        runtime.ArrayReduceRight = method;

        var il = method.GetILGenerator();

        EmitHoistedLazyCheck(il, runtime, out var isLazyLocal, out _);

        // Snapshot LengthOfArrayLike once, symmetrically with reduce.
        var lenLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);

        // args[0] = callback, args[1] = initial value (optional)
        var accLocal = il.DeclareLocal(_types.Object);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var callbackLocal = il.DeclareLocal(_types.Object);
        var elementLocal = il.DeclareLocal(_types.Object);

        // ECMA-262 23.1.3.22: throw TypeError if callback is missing or not callable.
        var reduceRCallableOk = il.DefineLabel();
        var reduceRCallableThrow = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brfalse, reduceRCallableThrow);

        // callback = args[0]
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, callbackLocal);

        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Brfalse, reduceRCallableThrow);
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, reduceRCallableThrow);
        // Positive callable check (mirrors reduce / EmitThrowIfCallbackNotCallable).
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, reduceRCallableOk);
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, reduceRCallableOk);
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Isinst, runtime.FunctionBindWrapperType);
        il.Emit(OpCodes.Brtrue, reduceRCallableOk);
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Isinst, runtime.FunctionCallWrapperType);
        il.Emit(OpCodes.Brtrue, reduceRCallableOk);
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Isinst, runtime.FunctionApplyWrapperType);
        il.Emit(OpCodes.Brtrue, reduceRCallableOk);
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brtrue, reduceRCallableOk);
        il.Emit(OpCodes.Br, reduceRCallableThrow);
        il.MarkLabel(reduceRCallableThrow);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Array.prototype.reduceRight callback is not callable");
        il.MarkLabel(reduceRCallableOk);

        // Hoist the args[4] allocation once per helper invocation; pre-fill
        // args[3] = receiver (constant for the duration). Per-iter writes
        // only touch args[0..2].
        EmitInitReduceArgs(il, runtime, out var argsLocal);

        // Check if initial value provided (args.Length > 1)
        var hasInitial = il.DefineLabel();
        var noInitial = il.DefineLabel();
        var startLoop = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bgt, hasInitial);

        // No initial value: scan BACKWARD for last PRESENT (non-hole) element.
        // ECMA-262 23.1.3.25 (symmetric to reduce).
        var scanLocal = il.DeclareLocal(_types.Int32);
        var scanStart = il.DefineLabel();
        var scanEnd = il.DefineLabel();
        var scanFound = il.DefineLabel();
        var scanValueLocal = il.DeclareLocal(_types.Object);
        // scan = list.Count - 1
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, scanLocal);
        il.MarkLabel(scanStart);
        il.Emit(OpCodes.Ldloc, scanLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, scanEnd);
        // if (!(LoadArrayLikeElement(list, scan) is ArrayHole)) goto found (lazy-aware)
        EmitElementLoad(il, scanLocal, runtime, isLazyLocal);
        il.Emit(OpCodes.Stloc, scanValueLocal);
        il.Emit(OpCodes.Ldloc, scanValueLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brfalse, scanFound);
        il.Emit(OpCodes.Ldloc, scanLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, scanLocal);
        il.Emit(OpCodes.Br, scanStart);

        il.MarkLabel(scanEnd);
        // No present element — TypeError per spec. Throw a real $TypeError
        // instance (parity with EmitArrayReduce).
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Reduce of empty array with no initial value");

        il.MarkLabel(scanFound);
        // Reuse the already-observed last present value; do not fire a getter twice.
        il.Emit(OpCodes.Ldloc, scanValueLocal);
        il.Emit(OpCodes.Stloc, accLocal);
        il.Emit(OpCodes.Ldloc, scanLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, startLoop);

        il.MarkLabel(hasInitial);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, accLocal);
        // startIndex = list.Count - 1
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(startLoop);
        var loopEnd = il.DefineLabel();
        var reduceRightAdvance = il.DefineLabel();

        // Loop: for (int i = startIndex; i >= 0; i--)
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, loopEnd);

        // Resolve presence and value once (symmetric to reduce).
        EmitElementLoad(il, indexLocal, runtime, isLazyLocal);
        il.Emit(OpCodes.Stloc, elementLocal);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brtrue, reduceRightAdvance);

        // Per-iter: write into the pre-allocated args[4]. args[3] is constant
        // (receiver), already pre-filled by EmitInitReduceArgs.
        // args[0] = acc
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, accLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // args[1] = element
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // args[2] = (double)i
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stelem_Ref);

        // Symmetric with reduce: callback thisValue is JS undefined.
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ldloc, callbackLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Stloc, accLocal);

        il.MarkLabel(reduceRightAdvance);
        // i--
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, startLoop);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, accLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits ArrayEntries: returns an iterator yielding [index, value] pairs.
    /// </summary>
    private void EmitArrayEntries(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayEntries",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject]
        );
        runtime.ArrayEntries = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newobj, runtime.ArrayIteratorCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits ArrayKeys: returns an iterator yielding array indices.
    /// </summary>
    private void EmitArrayKeys(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayKeys",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject]
        );
        runtime.ArrayKeys = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newobj, runtime.ArrayIteratorCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits ArrayValues: returns an iterator yielding array elements.
    /// </summary>
    private void EmitArrayValues(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayValues",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject]
        );
        runtime.ArrayValues = method;

        var il = method.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newobj, runtime.ArrayIteratorCtor);
        il.Emit(OpCodes.Ret);
    }
}

