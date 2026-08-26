using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for array method calls and property access.
/// Handles all TypeScript array methods like push, pop, map, filter, etc.
/// </summary>
public sealed class ArrayEmitter : ITypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a method call on an array receiver.
    /// </summary>
    public bool TryEmitMethodCall(IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments)
        => TryEmitMethodCall(emitter, receiver, methodName, arguments, leaveResultAsBareList: false);

    /// <summary>
    /// Core array-method emitter. <paramref name="leaveResultAsBareList"/> is set by the chained-stage
    /// fast path (#861 L2): when this call's array result flows DIRECTLY into another array-method call,
    /// the returnsNewArray <c>$Array</c> wrap is skipped so the bare <c>List&lt;object&gt;</c> feeds
    /// straight into the next stage — eliminating the wrap-then-unwrap round-trip.
    /// </summary>
    private bool TryEmitMethodCall(IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments, bool leaveResultAsBareList)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (methodName is "shift" or "unshift"
            && (ctx.RuntimeFeatures?.UsesDynamicPropertyDescriptors != false
                || ctx.RuntimeFeatures.UsesArrayPrototypeMutation))
            return false;

        if (methodName == "slice" && TryEmitNumericSliceCall(emitter, receiver, arguments))
            return true;
        if (methodName == "sort" && TryEmitNumericSortCall(emitter, receiver, arguments))
            return true;

        if (methodName == "push"
            && receiver is Expr.Variable stableReceiver
            && ctx.TypeMap?.IsStablePrimitivePromiseAllPushReceiver(stableReceiver) == true
            && arguments is [var value])
        {
            emitter.EmitExpression(receiver);
            emitter.EnsureBoxed();
            il.Emit(OpCodes.Castclass, ctx.Types.ListOfDouble);
            emitter.EmitExpressionAsDouble(value);
            il.Emit(OpCodes.Call, ctx.Runtime!.ArrayPushDouble);
            emitter.SetStackType(StackType.Double);
            return true;
        }

        // Methods whose spec says "return this" — the caller expects the same reference the receiver
        // started with (sort/reverse/fill/copyWithin). Since we unwrap to a List, the helper returns the
        // inner List, not the $Array wrapper; to preserve `arr === arr.sort()` we stash the wrapper and
        // push it back at the end.
        bool returnsReceiver = methodName is "sort" or "reverse" or "fill" or "copyWithin";
        // Methods whose spec says "return a new Array" — callers should keep seeing a $Array after them
        // (not a bare List<object?>), so downstream array methods / runtime dispatch still match.
        bool returnsNewArray = IsReturnsNewArrayMethod(methodName);

        // #952: `concat(...spread)` must expand the spread BEFORE concat (which only flattens one level
        // per *argument*). The spread path below routes through EmitArgsArrayWithSpread, which is itself
        // await-safe (it evaluates each arg into a temp with a clear stack), so it must NOT also go through
        // the #850 pre-spill — that would double-evaluate the args.
        bool concatSpread = methodName == "concat" && arguments.Any(a => a is Expr.Spread);

        // #850: when an argument can suspend (await/yield), evaluating it while the receiver list sits
        // on the IL evaluation stack produces invalid IL across a state-machine suspension. Pre-spill the
        // receiver + args into await-safe locals (callback methods never trigger this — an arrow body's
        // await belongs to its own state machine).
        bool plainArgSuspension = arguments.Count > 0 && MethodTakesPlainArgs(methodName)
            && emitter.ArgsContainSuspension(arguments)
            && !concatSpread;

        // The receiver local holds the ORIGINAL receiver, used by returnsReceiver methods and the
        // suspension spill below; unused for the other methods.
        LocalBuilder receiverLocal;

        // #861 L2: if the receiver is itself an array-method call producing a fresh array via THIS
        // emitter (e.g. the a.map(f) in a.map(f).filter(g)), emit it leaving a bare List<object> (its
        // $Array wrap suppressed) and skip the unwrap here — killing the round-trip at the boundary. The
        // intermediate array is anonymous (it can only be this one call's receiver), so dropping its
        // $Array identity is unobservable. Disabled when the outer method needs the original receiver
        // wrapper (returnsReceiver) or pre-spills suspending plain args (the chained receiver would sit
        // on the stack across the suspension).
        if (!returnsReceiver && !plainArgSuspension
            && TryEmitChainedArrayReceiverAsBareList(emitter, receiver))
        {
            // Bare List<object> already on the stack; receiverLocal is never read in this path.
            receiverLocal = il.DeclareLocal(ctx.Types.ListOfObject);
        }
        else
        {
            // General path: emit the array object and unwrap $Array → List<object>.
            emitter.EmitExpression(receiver);
            emitter.EmitBoxIfNeeded(receiver);
            receiverLocal = EmitGetListFromArrayOrList(il, ctx);
        }

        LocalBuilder[]? argLocals = null;
        if (plainArgSuspension)
        {
            var listSafe = emitter.SpillStackToObjectLocal();      // [list] -> await-safe local
            il.Emit(OpCodes.Ldloc, receiverLocal);                 // the original receiver must also
            receiverLocal = emitter.SpillStackToObjectLocal();     // survive the suspension (returnsReceiver methods)
            argLocals = new LocalBuilder[arguments.Count];
            for (int i = 0; i < arguments.Count; i++)
                argLocals[i] = emitter.SpillBoxedArg(arguments[i]);   // suspensions happen here, with a clear stack
            il.Emit(OpCodes.Ldloc, listSafe);
            il.Emit(OpCodes.Castclass, ctx.Types.ListOfObject);    // restore [list]
        }

        switch (methodName)
        {
            case "pop":
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayPop);
                break;

            case "shift":
                // Mutators must perform the observable Set/Delete/Set-length
                // sequence on the array object. The List fast path bypasses
                // indexed prototype accessors and length descriptors.
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ldloc, receiverLocal);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayShiftProto);
                break;

            case "unshift":
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ldloc, receiverLocal);
                emitter.EmitArgsArray(arguments, argLocals);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayUnshiftProto);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                break;

            case "push":
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ldloc, receiverLocal);
                emitter.EmitArgsArray(arguments, argLocals);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayPushProto);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                break;

            case "slice":
                emitter.EmitArgsArray(arguments, argLocals);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArraySlice);
                break;

            case "map":
            {
                // Fast path: arr.map(literal-non-capturing-arrow) — direct
                // delegate dispatch, skipping $TSFunction allocation and the
                // MethodInvoker boundary. See issue #96 Phase A.
                if (TryEmitDirectDelegateCall(emitter, arguments, ctx.Runtime!.ArrayMapDirect))
                    break;
                var saved = EmitCallbackAndStashThisArg(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayMap);
                var resLocal = il.DeclareLocal(ctx.Types.ListOfObject);
                il.Emit(OpCodes.Stloc, resLocal);
                EmitRestoreCallbackThisArg(emitter, saved);
                il.Emit(OpCodes.Ldloc, resLocal);
                break;
            }

            case "filter":
            {
                if (TryEmitDirectDelegateCall(emitter, arguments, ctx.Runtime!.ArrayFilterDirect, ctx.Runtime!.ArrayFilterDirectBool))
                    break;
                var saved = EmitCallbackAndStashThisArg(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayFilter);
                var resLocal = il.DeclareLocal(ctx.Types.ListOfObject);
                il.Emit(OpCodes.Stloc, resLocal);
                EmitRestoreCallbackThisArg(emitter, saved);
                il.Emit(OpCodes.Ldloc, resLocal);
                break;
            }

            case "forEach":
            {
                // Spec: forEach returns undefined (not null). Push
                // $Undefined.Instance so `arr.forEach(...) === undefined`
                // holds — test262 callback-related tests rely on this.
                if (TryEmitDirectDelegateCall(emitter, arguments, ctx.Runtime!.ArrayForEachDirect))
                {
                    il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
                    break;
                }
                var saved = EmitCallbackAndStashThisArg(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayForEach);
                EmitRestoreCallbackThisArg(emitter, saved);
                il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
                break;
            }

            case "find":
            {
                if (TryEmitDirectDelegateCall(emitter, arguments, ctx.Runtime!.ArrayFindDirect, ctx.Runtime!.ArrayFindDirectBool))
                    break;
                var saved = EmitCallbackAndStashThisArg(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayFind);
                var resLocal = il.DeclareLocal(ctx.Types.Object);
                il.Emit(OpCodes.Stloc, resLocal);
                EmitRestoreCallbackThisArg(emitter, saved);
                il.Emit(OpCodes.Ldloc, resLocal);
                break;
            }

            case "findIndex":
            {
                if (TryEmitDirectDelegateCall(emitter, arguments, ctx.Runtime!.ArrayFindIndexDirect, ctx.Runtime!.ArrayFindIndexDirectBool))
                {
                    il.Emit(OpCodes.Box, ctx.Types.Double); // helper returns double
                    break;
                }
                var saved = EmitCallbackAndStashThisArg(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayFindIndex);
                var resLocal = il.DeclareLocal(ctx.Types.Double);
                il.Emit(OpCodes.Stloc, resLocal);
                EmitRestoreCallbackThisArg(emitter, saved);
                il.Emit(OpCodes.Ldloc, resLocal);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                break;
            }

            case "some":
            {
                if (TryEmitDirectDelegateCall(emitter, arguments, ctx.Runtime!.ArraySomeDirect, ctx.Runtime!.ArraySomeDirectBool))
                    break;
                var saved = EmitCallbackAndStashThisArg(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArraySome);
                var resLocal = il.DeclareLocal(ctx.Types.Object);
                il.Emit(OpCodes.Stloc, resLocal);
                EmitRestoreCallbackThisArg(emitter, saved);
                il.Emit(OpCodes.Ldloc, resLocal);
                break;
            }

            case "every":
            {
                if (TryEmitDirectDelegateCall(emitter, arguments, ctx.Runtime!.ArrayEveryDirect, ctx.Runtime!.ArrayEveryDirectBool))
                    break;
                var saved = EmitCallbackAndStashThisArg(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayEvery);
                var resLocal = il.DeclareLocal(ctx.Types.Object);
                il.Emit(OpCodes.Stloc, resLocal);
                EmitRestoreCallbackThisArg(emitter, saved);
                il.Emit(OpCodes.Ldloc, resLocal);
                break;
            }

            case "reduce":
                // The direct-call fast path re-evaluates the callback/initial-value from the AST, so
                // skip it when arguments were pre-spilled for await-safety (#850) and use the
                // argLocals-aware array path instead.
                if (argLocals == null && TryEmitReduceDirectCall(emitter, arguments))
                    break;
                emitter.EmitArgsArray(arguments, argLocals);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayReduce);
                break;

            case "reduceRight":
                emitter.EmitArgsArray(arguments, argLocals);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayReduceRight);
                break;

            case "join":
                if (arguments.Count > 0)
                    EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0, argLocals);
                else
                    il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayJoin);
                break;

            case "concat":
                if (concatSpread)
                {
                    // #952: the receiver [list] is on the stack. Spread args must be flattened
                    // BEFORE concat (concat itself only flattens one level per argument). Stash the
                    // list, build the spread-expanded args array await-safely (EmitArgsArrayWithSpread
                    // evaluates into temps with a clear stack — the spread expr may contain await),
                    // then reload [list] under [items] so ArrayConcat sees (list, items).
                    var concatListTmp = il.DeclareLocal(ctx.Types.ListOfObject);
                    il.Emit(OpCodes.Stloc, concatListTmp);
                    emitter.EmitArgsArrayWithSpread(arguments);
                    var concatItemsTmp = il.DeclareLocal(ctx.Types.ObjectArray);
                    il.Emit(OpCodes.Stloc, concatItemsTmp);
                    il.Emit(OpCodes.Ldloc, concatListTmp);
                    il.Emit(OpCodes.Ldloc, concatItemsTmp);
                }
                else
                {
                    emitter.EmitArgsArray(arguments, argLocals);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayConcat);
                break;

            case "reverse":
                // Reverse is observable through inherited indexed properties,
                // accessors, and deletions. Keep the statically-typed array
                // fast path on the same generic algorithm as
                // Array.prototype.reverse.call rather than List.Reverse().
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ldloc, receiverLocal);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayReverseProto);
                break;

            case "flat":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0, argLocals);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayFlat);
                break;

            case "flatMap":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0, argLocals);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayFlatMap);
                break;

            case "includes":
                if (arguments.Count == 0)
                    il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
                else
                    EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0, argLocals);
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 1, argLocals);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayIncludes);
                break;

            case "indexOf":
            case "lastIndexOf":
                // searchElement (arg 0) + optional fromIndex (arg 1).
                if (arguments.Count == 0)
                    il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
                else
                    EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0, argLocals);
                if (arguments.Count >= 2)
                {
                    if (argLocals != null)
                    {
                        il.Emit(OpCodes.Ldloc, argLocals[1]);
                    }
                    else
                    {
                        emitter.EmitExpression(arguments[1]);
                        emitter.EmitBoxIfNeeded(arguments[1]);
                    }
                }
                else
                {
                    il.Emit(OpCodes.Ldsfld, ctx.Runtime!.ArrayHoleInstance);
                }
                il.Emit(OpCodes.Call, methodName == "indexOf" ? ctx.Runtime!.ArrayIndexOf : ctx.Runtime!.ArrayLastIndexOf);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                break;

            case "sort":
                // The direct path re-emits the comparator from its AST, so an
                // await-safe pre-spill must continue through the general path.
                if (argLocals == null && TryEmitSortDirectCall(emitter, arguments))
                    break;
                if (arguments.Count == 0)
                    il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
                else
                    EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0, argLocals);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArraySort);
                break;

            case "toSorted":
                if (arguments.Count == 0)
                    il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
                else
                    EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0, argLocals);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayToSorted);
                break;

            case "splice":
                emitter.EmitArgsArray(arguments, argLocals);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArraySplice);
                break;

            case "toSpliced":
                emitter.EmitArgsArray(arguments, argLocals);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayToSpliced);
                break;

            case "findLast":
            {
                var saved = EmitCallbackAndStashThisArg(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayFindLast);
                var resLocal = il.DeclareLocal(ctx.Types.Object);
                il.Emit(OpCodes.Stloc, resLocal);
                EmitRestoreCallbackThisArg(emitter, saved);
                il.Emit(OpCodes.Ldloc, resLocal);
                break;
            }

            case "findLastIndex":
            {
                var saved = EmitCallbackAndStashThisArg(emitter, arguments);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayFindLastIndex);
                var resLocal = il.DeclareLocal(ctx.Types.Double);
                il.Emit(OpCodes.Stloc, resLocal);
                EmitRestoreCallbackThisArg(emitter, saved);
                il.Emit(OpCodes.Ldloc, resLocal);
                il.Emit(OpCodes.Box, ctx.Types.Double);
                break;
            }

            case "toReversed":
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayToReversed);
                break;

            case "with":
                emitter.EmitArgsArray(arguments, argLocals);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayWith);
                break;

            case "at":
                EmitterArgumentHelpers.EmitBoxedArgumentOrNull(emitter, arguments, 0, argLocals);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayAt);
                break;

            case "fill":
                emitter.EmitArgsArray(arguments, argLocals);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayFill);
                break;

            case "copyWithin":
                emitter.EmitArgsArray(arguments, argLocals);
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayCopyWithin);
                break;

            case "entries":
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayEntries);
                break;

            case "keys":
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayKeys);
                break;

            case "values":
                il.Emit(OpCodes.Call, ctx.Runtime!.ArrayValues);
                break;

            // ECMA-262 23.1.3.32 / 23.1.3.33: Array.prototype.toString /
            // toLocaleString. Both invoke join with default separator.
            // Helper takes (object) — we have the List on stack, which is
            // also the ArrayLikeMaterialize pass-through for List, so
            // route there. Discards args (toString/toLocaleString ignore
            // their args per spec).
            case "toString":
            case "toLocaleString":
                // Prototype methods are writable. Resolve the live prototype
                // slot instead of hard-wiring the intrinsic join helper so
                // `Array.prototype.toString = Object.prototype.toString` is
                // observed by subsequently-created and existing arrays.
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ldsfld, ctx.Runtime!.ArrayPrototypeField);
                il.Emit(OpCodes.Ldstr, methodName);
                il.Emit(OpCodes.Call, ctx.Runtime!.GetProperty);
                var toStringMethodLocal = il.DeclareLocal(ctx.Types.Object);
                il.Emit(OpCodes.Stloc, toStringMethodLocal);
                il.Emit(OpCodes.Ldloc, receiverLocal);
                il.Emit(OpCodes.Ldloc, toStringMethodLocal);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Newarr, ctx.Types.Object);
                il.Emit(OpCodes.Call, ctx.Runtime!.InvokeMethodValue);
                break;

            default:
                return false;
        }

        // #861 L2: when this result flows straight into a chained array call, skip the $Array wrap and
        // leave the bare List<object> on the stack for the next stage.
        if (!(leaveResultAsBareList && returnsNewArray))
            EmitPostCallAdjust(il, ctx, receiverLocal, returnsReceiver, returnsNewArray);
        return true;
    }

    /// <summary>The methods whose spec result is a freshly-allocated array (kept as a $Array for callers).</summary>
    private static bool IsReturnsNewArrayMethod(string methodName) => methodName is
        "slice" or "concat" or "map" or "filter" or "flat" or "flatMap"
        or "splice" or "toReversed" or "toSorted" or "toSpliced" or "with";

    /// <summary>
    /// #861 L2 chained-stage detection. If <paramref name="receiver"/> is a call to a fresh-array-
    /// producing array method on an array receiver (the <c>a.map(f)</c> in <c>a.map(f).filter(g)</c>),
    /// emit that inner call leaving a bare <c>List&lt;object&gt;</c> on the stack — its <c>$Array</c> wrap
    /// suppressed — and return true. The intermediate array is anonymous (it can only be this one outer
    /// call's receiver), so dropping its <c>$Array</c> identity is unobservable. Returns false (emitting
    /// nothing) otherwise.
    /// </summary>
    private bool TryEmitChainedArrayReceiverAsBareList(IEmitterContext emitter, Expr receiver)
    {
        if (receiver is not Expr.Call { Optional: false, Callee: Expr.Get { Optional: false } innerGet } innerCall)
            return false;
        string innerMethod = innerGet.Name.Lexeme;
        // Must be a returnsNewArray method handled by THIS emitter (so it leaves a List on the stack).
        if (!IsReturnsNewArrayMethod(innerMethod)) return false;
        // The inner call's receiver must statically be an array, so the inner call goes through this
        // emitter's returnsNewArray path and the recursion leaves a List<object> (not some other value).
        if (emitter.Context.TypeMap?.Get(innerGet.Object) is not SharpTS.TypeSystem.TypeInfo.Array)
            return false;
        return TryEmitMethodCall(emitter, innerGet.Object, innerMethod, innerCall.Arguments, leaveResultAsBareList: true);
    }

    /// <summary>
    /// After a call that leaves a List&lt;object?&gt; on the stack, adjust the
    /// top-of-stack so downstream code sees the expected JS value:
    /// - For "return this" methods: pop the list, push the saved <c>$Array</c>
    ///   receiver (or the bare list if receiver wasn't a <c>$Array</c>).
    /// - For "return new array" methods: wrap the list in a fresh <c>$Array</c>.
    /// Called from per-case branches in <see cref="TryEmitMethodCall"/>.
    /// </summary>
    private static void EmitPostCallAdjust(ILGenerator il, CompilationContext ctx, LocalBuilder receiverLocal, bool returnsReceiver, bool returnsNewArray)
    {
        if (returnsReceiver)
        {
            // Stack: [list (the mutated inner List<object?>)]
            // We want: the ORIGINAL receiver (the $Array wrapper if it was one).
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldloc, receiverLocal);
            return;
        }

        if (returnsNewArray)
        {
            // Stack: [list]  → want: [new $Array(list)]
            il.Emit(OpCodes.Newobj, ctx.Runtime!.TSArrayCtor);
        }
    }

    /// <summary>
    /// Attempts to emit IL for a property get on an array receiver.
    /// Handles .length by emitting direct List&lt;T&gt;.Count access,
    /// bypassing the full GetProperty runtime dispatch chain.
    /// </summary>
    public bool TryEmitPropertyGet(IEmitterContext emitter, Expr receiver, string propertyName)
    {
        if (propertyName != "length") return false;

        var ctx = emitter.Context;
        var il = ctx.IL;

        // Resolve the descriptor from the receiver's static type
        var desc = ArrayElements.Resolve(ctx.TypeMap?.Get(receiver));
        if (desc == null) return false;

        // Hoisted path: use cached typed local from loop preamble
        if (receiver is Expr.Variable arrVar)
        {
            var hoisted = ctx.TryGetHoistedArray(arrVar.Name.Lexeme);
            if (hoisted.HasValue)
            {
                var h = hoisted.Value;
                var fallbackLabel = il.DefineLabel();
                var endLabel = il.DefineLabel();

                il.Emit(OpCodes.Ldloc, h.TypedLocal);
                il.Emit(OpCodes.Brfalse, fallbackLabel);

                if (h.Descriptor.Kind == ArrayElementsKind.Double)
                {
                    // Hoisted numeric $Array (#927 step 1): the typed local is a $Array (the preamble
                    // hoists `isinst $Array`, not `isinst List<double>`), so read its authoritative length
                    // via the $Array length getter — numeric-aware, maintained by SetDouble/PushDouble.
                    // Calling List<float64>::Count here would read the EMPTY base list of a numeric $Array
                    // and report 0, so a loop-condition `i < arr.length` never runs. A $Array is never the
                    // arguments object, so no $Arguments check is needed on this arm.
                    il.Emit(OpCodes.Ldloc, h.TypedLocal);
                    il.Emit(OpCodes.Callvirt, ctx.Runtime!.TSArrayLongLengthGetter);
                    il.Emit(OpCodes.Conv_R8);
                    il.Emit(OpCodes.Box, ctx.Types.Double);
                    il.Emit(OpCodes.Br, endLabel);
                }
                else
                {
                    var listType = h.Descriptor.GetListType(ctx.Types);

                    // $Arguments check: use _length, not Count, per ECMA-262 sloppy
                    // arguments. Only emits the runtime check when ArgumentsType is
                    // wired (production: always; tests may skip).
                    if (ctx.Runtime?.ArgumentsType != null && ctx.Runtime?.ArgumentsLengthField != null)
                    {
                        var notArgsLengthLabel = il.DefineLabel();
                        il.Emit(OpCodes.Ldloc, h.TypedLocal);
                        il.Emit(OpCodes.Isinst, ctx.Runtime!.ArgumentsType);
                        il.Emit(OpCodes.Brfalse, notArgsLengthLabel);
                        il.Emit(OpCodes.Ldloc, h.TypedLocal);
                        il.Emit(OpCodes.Castclass, ctx.Runtime!.ArgumentsType);
                        il.Emit(OpCodes.Ldfld, ctx.Runtime!.ArgumentsLengthField);
                        il.Emit(OpCodes.Conv_R8);
                        il.Emit(OpCodes.Box, ctx.Types.Double);
                        il.Emit(OpCodes.Br, endLabel);
                        il.MarkLabel(notArgsLengthLabel);
                    }

                    il.Emit(OpCodes.Ldloc, h.TypedLocal);
                    il.Emit(OpCodes.Callvirt, ctx.Types.GetProperty(listType, "Count").GetGetMethod()!);
                    il.Emit(OpCodes.Conv_R8);
                    il.Emit(OpCodes.Box, ctx.Types.Double);
                    il.Emit(OpCodes.Br, endLabel);
                }

                il.MarkLabel(fallbackLabel);
                emitter.EmitExpression(receiver);
                emitter.EmitBoxIfNeeded(receiver);
                il.Emit(OpCodes.Call, ctx.Runtime!.GetLength);
                il.Emit(OpCodes.Conv_R8);
                il.Emit(OpCodes.Box, ctx.Types.Double);

                il.MarkLabel(endLabel);
                return true;
            }
        }

        // Non-hoisted path: per-access isinst guard
        var listTypeNH = desc.GetListType(ctx.Types);

        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);

        var objLocal = il.DeclareLocal(ctx.Types.Object);
        il.Emit(OpCodes.Stloc, objLocal);

        var fallbackLabelNH = il.DefineLabel();
        var endLabelNH = il.DefineLabel();
        // Stage E.2 M2/M3: $Array inherits List<object?>, so `isinst List<object?>`
        // below matches $Array instances — but base `Count` only sees the dense
        // prefix, missing any sparse tail. Check $Array first and use its
        // LongLength getter (int-clamped Length would truncate lengths past
        // int.MaxValue; M3 acceptance demands `a.length === 2147483649` works).
        var tsArrayCheckLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Isinst, ctx.Runtime!.TSArrayType);
        il.Emit(OpCodes.Brfalse, tsArrayCheckLabel);
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Castclass, ctx.Runtime!.TSArrayType);
        il.Emit(OpCodes.Callvirt, ctx.Runtime!.TSArrayLongLengthGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, ctx.Types.Double);
        il.Emit(OpCodes.Br, endLabelNH);

        il.MarkLabel(tsArrayCheckLabel);

        // $Arguments check: use _length per ECMA-262 sloppy arguments.
        if (ctx.Runtime?.ArgumentsType != null && ctx.Runtime?.ArgumentsLengthField != null)
        {
            var notArgsLengthNH = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, objLocal);
            il.Emit(OpCodes.Isinst, ctx.Runtime!.ArgumentsType);
            il.Emit(OpCodes.Brfalse, notArgsLengthNH);
            il.Emit(OpCodes.Ldloc, objLocal);
            il.Emit(OpCodes.Castclass, ctx.Runtime!.ArgumentsType);
            il.Emit(OpCodes.Ldfld, ctx.Runtime!.ArgumentsLengthField);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, ctx.Types.Double);
            il.Emit(OpCodes.Br, endLabelNH);
            il.MarkLabel(notArgsLengthNH);
        }

        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Isinst, listTypeNH);
        il.Emit(OpCodes.Brfalse, fallbackLabelNH);

        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Castclass, listTypeNH);
        il.Emit(OpCodes.Callvirt, ctx.Types.GetProperty(listTypeNH, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, ctx.Types.Double);
        il.Emit(OpCodes.Br, endLabelNH);

        il.MarkLabel(fallbackLabelNH);
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Call, ctx.Runtime!.GetLength);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, ctx.Types.Double);

        il.MarkLabel(endLabelNH);
        return true;
    }

    /// <summary>
    /// Attempts to emit IL for a property set on an array receiver.
    /// Array properties are not directly settable.
    /// </summary>
    public bool TryEmitPropertySet(IEmitterContext emitter, Expr receiver, string propertyName, Expr value)
    {
        return false;
    }

    #region Helper Methods

    /// <summary>
    /// Emits code to convert an array value (either List&lt;object&gt; or $Array) to List&lt;object&gt;.
    /// The value is expected to be on the stack; leaves List&lt;object&gt; on the stack.
    /// Returns the local that stashes the ORIGINAL receiver — callers emitting
    /// identity-preserving methods (sort/reverse/fill/copyWithin) use this to
    /// push the receiver back after the runtime helper returns.
    /// </summary>
    private static LocalBuilder EmitGetListFromArrayOrList(ILGenerator il, CompilationContext ctx)
    {
        var objLocal = il.DeclareLocal(ctx.Types.Object);
        il.Emit(OpCodes.Stloc, objLocal);

        // Deopt: if this is a numeric-mode $Array, materialize its unboxed
        // double store into the base List<object?> first. The `isinst
        // List<object?>` check below matches $Array (it inherits List<object?>)
        // and would otherwise read the empty base list. EnsureBoxed is a no-op
        // for boxed-mode arrays.
        var deoptDone = il.DefineLabel();
        var arrLocal = il.DeclareLocal(ctx.Runtime!.TSArrayType);
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Isinst, ctx.Runtime.TSArrayType);
        il.Emit(OpCodes.Stloc, arrLocal);
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Brfalse, deoptDone);
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Callvirt, ctx.Runtime.TSArrayEnsureBoxed);
        il.MarkLabel(deoptDone);

        var isListLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if it's already a List<object>
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Isinst, ctx.Types.ListOfObject);
        il.Emit(OpCodes.Brtrue, isListLabel);

        // Check if it's $Array - get Elements
        var tsArrayLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Isinst, ctx.Runtime!.TSArrayType);
        il.Emit(OpCodes.Brtrue, tsArrayLabel);

        // Check if it's a typed array (List<double>, List<bool>) via IList interface.
        // Convert to List<object?> by iterating and boxing elements.
        // This is a slow path only hit when array methods are called on typed arrays.
        var ilistType = typeof(System.Collections.IList);
        var typedListLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Isinst, ilistType);
        il.Emit(OpCodes.Brtrue, typedListLabel);

        // Unknown type - push null as fallback (shouldn't happen)
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, endLabel);

        // $Array path
        il.MarkLabel(tsArrayLabel);
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Castclass, ctx.Runtime!.TSArrayType);
        il.Emit(OpCodes.Callvirt, ctx.Runtime.TSArrayElementsGetter);
        il.Emit(OpCodes.Br, endLabel);

        // Typed list path: convert IList to List<object?> via boxing loop.
        // Loop uses condition-at-top so the backward-branch target (loopStart) is
        // reachable from forward flow, keeping the IL verifiable even when the
        // caller's evaluation stack is non-empty (e.g. left side of a + expr).
        il.MarkLabel(typedListLabel);
        var ilistLocal = il.DeclareLocal(ilistType);
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Castclass, ilistType);
        il.Emit(OpCodes.Stloc, ilistLocal);
        // new List<object?>(ilist.Count)
        il.Emit(OpCodes.Ldloc, ilistLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.ICollection).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Newobj, ctx.Types.GetConstructor(ctx.Types.ListOfObject, ctx.Types.Int32));
        var resultLocal = il.DeclareLocal(ctx.Types.ListOfObject);
        il.Emit(OpCodes.Stloc, resultLocal);
        // for (int i = 0; i < ilist.Count; i++) result.Add(ilist[i]);
        var idxLocal = il.DeclareLocal(ctx.Types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, idxLocal);
        var loopStart = il.DefineLabel(); // condition at top — reachable via fall-through from init
        var loopEnd   = il.DefineLabel();
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldloc, ilistLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.ICollection).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd); // exit if i >= count
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, ilistLocal);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Callvirt, ilistType.GetMethod("get_Item", [typeof(int)])!); // returns boxed object
        il.Emit(OpCodes.Callvirt, ctx.Types.GetMethod(ctx.Types.ListOfObject, "Add", [ctx.Types.Object])!);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, idxLocal);
        il.Emit(OpCodes.Br, loopStart); // backward branch to a forward-reachable label
        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Br, endLabel);

        // Already a List - cast it
        il.MarkLabel(isListLabel);
        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Castclass, ctx.Types.ListOfObject);

        il.MarkLabel(endLabel);
        return objLocal;
    }

    /// <summary>
    /// Array methods that evaluate plain-expression arguments (via <see cref="EmitVariadicListMutation"/>,
    /// <see cref="IEmitterContext.EmitArgsArray"/>, <see cref="EmitterArgumentHelpers.EmitBoxedArgumentOrNull"/>, or inline) rather than dispatching
    /// a callback arrow from the AST. Only these can hit the #850 stack-spill bug when an argument
    /// contains <c>await</c>/<c>yield</c>, so only these get await-safe argument pre-spilling. The
    /// callback methods (map/filter/forEach/find/findIndex/some/every/findLast/findLastIndex) are
    /// excluded — their arrow argument is matched on the AST before evaluation, and a nested arrow's
    /// <c>await</c> belongs to its own state machine (never reported by ArgsContainSuspension).
    /// </summary>
    private static bool MethodTakesPlainArgs(string methodName) => methodName is
        "push" or "unshift" or "slice" or "reduce" or "reduceRight" or "join" or "concat" or
        "flat" or "flatMap" or "includes" or "indexOf" or "lastIndexOf" or "sort" or "toSorted" or
        "splice" or "toSpliced" or "with" or "at" or "fill" or "copyWithin";

    /// <summary>
    /// Emits the callback (arg 0) and stashes optional thisArg (arg 1) into
    /// `$Runtime._currentCallbackThisArg` so EmitCallbackArgsAndInvoke uses it
    /// as the receiver when invoking the callback. Returns a local holding the
    /// previous thread-static value so the caller can restore it via
    /// <see cref="EmitRestoreCallbackThisArg"/> after the helper call. Null
    /// means no thisArg was passed; per ECMA-262 the callback's `this` becomes
    /// undefined (here passed as null to InvokeMethodValue, which is treated
    /// as no-receiver).
    /// </summary>
    private static LocalBuilder EmitCallbackAndStashThisArg(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        var runtime = ctx.Runtime!;

        // Emit callback (arg 0) onto the stack first.
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Save previous thread-static so nested forEach/map calls don't leak.
        var savedLocal = il.DeclareLocal(ctx.Types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.CurrentCallbackThisArgField);
        il.Emit(OpCodes.Stloc, savedLocal);

        // Stash thisArg (arg 1) into the thread-static. When no thisArg is
        // provided, push $Undefined so strict-mode callbacks see
        // `this === undefined` per ECMA-262 (sloppy mode coerces undefined to
        // globalThis at the callback's prologue if needed).
        if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        }
        il.Emit(OpCodes.Stsfld, runtime.CurrentCallbackThisArgField);

        return savedLocal;
    }

    /// <summary>
    /// Issue #96 Phase A — direct delegate dispatch fast path.
    /// Detects <c>arr.map(literal-non-capturing-arrow)</c> at the call site
    /// and emits IL that invokes the arrow's static method through a
    /// <c>Func&lt;object, object&gt;</c> delegate (<c>ldftn</c> + delegate
    /// ctor) instead of allocating <c>$TSFunction</c> and dispatching through
    /// <c>MethodInvoker</c>.
    ///
    /// Stack on entry: <c>[List&lt;object&gt;]</c> (the unwrapped receiver).
    /// On success: emits IL ending with <c>[List&lt;object&gt;]</c> on the
    /// stack (the helper's return value).
    /// On failure (returns false): stack unchanged; caller falls through to
    /// the slow path.
    ///
    /// Preconditions enforced here:
    /// - Exactly one argument (no <c>thisArg</c> — bypassed for arrows
    ///   anyway, but skipping keeps observable evaluation semantics for the
    ///   slow path).
    /// - Argument is <c>Expr.ArrowFunction</c> (literal, not a variable
    ///   binding).
    /// - Arity 0 or 1 (Map's callback gets element + index + receiver, but
    ///   arrows can ignore any of those).
    /// - Not async, not generator, no <c>__this</c> param.
    /// - No type annotations on params — <see cref="ParameterTypeResolver"/>
    ///   falls back to <c>object</c> when annotation is null, guaranteeing
    ///   the static method's parameter types match <c>Func&lt;object, object&gt;</c>.
    /// - Static method's <c>ReturnType</c> is exactly <c>object</c> — even
    ///   without an AST return annotation, type inference from the body
    ///   (<c>x =&gt; x*2</c> → returnType <c>double</c>) can resolve to a
    ///   non-object type, which would mismatch <c>Func&lt;object, object&gt;</c>
    ///   and produce undefined-behavior signature mismatch at delegate
    ///   construction. Inspecting <see cref="MethodBuilder.ReturnType"/>
    ///   directly catches this; it's set at <c>DefineMethod</c> time and
    ///   reliable pre-CreateType.
    /// - Arrow is non-capturing (per <see cref="ClosureAnalyzer"/>) — implies
    ///   no <c>arguments</c> reference and no display-class allocation.
    /// </summary>
    /// <summary>
    /// Resolves the callback expression at an iterator-helper call site to a
    /// literal <see cref="Expr.ArrowFunction"/> AST node. Accepts both the
    /// inline form (<c>arr.map(x =&gt; …)</c>) and the const-bound form
    /// (<c>const sq = x =&gt; …; arr.map(sq)</c>) — for the latter we walk
    /// to the registered top-level const→arrow map. Returns null if the
    /// callback isn't statically resolvable to a literal arrow.
    /// </summary>
    private static Expr.ArrowFunction? ResolveCallbackArrow(IEmitterContext emitter, Expr callbackArg)
    {
        if (callbackArg is Expr.ArrowFunction af) return af;
        if (callbackArg is Expr.Variable v
            && emitter.Context.ConstArrowBindings.TryGetValue(v.Name.Lexeme, out var bound))
        {
            return bound;
        }
        return null;
    }

    /// <summary>
    /// Keeps a zero-argument slice of a statically numeric array in the
    /// packed-double $Array representation. The runtime helper repeats the
    /// dense ordinary-array proof and falls back to observable generic slice.
    /// </summary>
    private static bool TryEmitNumericSliceCall(
        IEmitterContext emitter,
        Expr receiver,
        List<Expr> arguments)
    {
        var ctx = emitter.Context;
        if (arguments.Count != 0
            || ArrayElements.Resolve(ctx.TypeMap?.Get(receiver)) is not
                { Kind: ArrayElementsKind.Double }
            || ctx.RuntimeFeatures?.UsesDynamicPropertyDescriptors != false
            || ctx.RuntimeFeatures.UsesArrayPrototypeMutation)
        {
            return false;
        }

        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);
        ctx.IL.Emit(OpCodes.Call, ctx.Runtime!.ArraySliceNumber);
        emitter.SetStackUnknown();
        return true;
    }

    /// <summary>
    /// Routes the closed, non-throwing numeric comparator <c>(a,b) =&gt; a-b</c>
    /// to the packed stable merge kernel. Captures, calls, property reads,
    /// statements, and every other comparator shape retain the observable sort
    /// path, including comparator-driven mutation and abrupt completion.
    /// </summary>
    private static bool TryEmitNumericSortCall(
        IEmitterContext emitter,
        Expr receiver,
        List<Expr> arguments)
    {
        var ctx = emitter.Context;
        if (receiver is not Expr.Variable stableSlice
            || ctx.TypeMap?.IsStableNumericSliceSortReceiver(stableSlice.Name) != true
            || arguments.Count != 1
            || ArrayElements.Resolve(ctx.TypeMap?.Get(receiver)) is not
                { Kind: ArrayElementsKind.Double }
            || ctx.RuntimeFeatures?.UsesDynamicPropertyDescriptors != false
            || ctx.RuntimeFeatures.UsesArrayPrototypeMutation)
        {
            return false;
        }

        var af = ResolveCallbackArrow(emitter, arguments[0]);
        if (af is null
            || af.HasOwnThis
            || af.IsAsync
            || af.IsGenerator
            || af.Parameters.Count != 2
            || af.Parameters.Any(parameter =>
                parameter.IsRest || parameter.IsOptional || parameter.DefaultValue != null)
            || !IsPureNumericDifference(af)
            || ctx.DisplayClasses.ContainsKey(af)
            || !ctx.ArrowMethods.TryGetValue(af, out var arrowMethod)
            || arrowMethod.ReturnType != ctx.Types.Double
            || arrowMethod.GetParameters() is not { Length: 2 } parameters
            || parameters[0].ParameterType != ctx.Types.Double
            || parameters[1].ParameterType != ctx.Types.Double
            || arrowMethod.DeclaringType is not TypeBuilder programType)
        {
            return false;
        }

        var typedComparator = typeof(Func<double, double, double>);
        var boxedComparator = typeof(Func<object, object, double>);
        var boxedAdapter = ctx.ArrowBoxedAdapters.GetOrEmit(
            programType,
            arrowMethod,
            funcArity: 2,
            instance: false,
            boolReturn: false,
            numberReturn: true);

        emitter.EmitExpression(receiver);
        emitter.EmitBoxIfNeeded(receiver);
        ctx.IL.Emit(OpCodes.Ldnull);
        ctx.IL.Emit(OpCodes.Ldftn, arrowMethod);
        ctx.IL.Emit(OpCodes.Newobj,
            typedComparator.GetConstructor([typeof(object), typeof(IntPtr)])!);
        ctx.IL.Emit(OpCodes.Ldnull);
        ctx.IL.Emit(OpCodes.Ldftn, boxedAdapter);
        ctx.IL.Emit(OpCodes.Newobj,
            boxedComparator.GetConstructor([typeof(object), typeof(IntPtr)])!);
        ctx.IL.Emit(OpCodes.Call, ctx.Runtime!.ArraySortNumeric);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool IsPureNumericDifference(Expr.ArrowFunction af)
    {
        if (af.ExpressionBody is not Expr.Binary
            {
                Operator.Type: TokenType.MINUS,
                Left: Expr.Variable left,
                Right: Expr.Variable right
            })
        {
            return false;
        }

        return left.Name.Lexeme == af.Parameters[0].Name.Lexeme
            && right.Name.Lexeme == af.Parameters[1].Name.Lexeme;
    }

    private static bool TryEmitDirectDelegateCall(IEmitterContext emitter, List<Expr> arguments, System.Reflection.Emit.MethodBuilder helperMethod, System.Reflection.Emit.MethodBuilder? boolHelper = null)
    {
        var ctx = emitter.Context;
        if (arguments.Count != 1) return false;
        var af = ResolveCallbackArrow(emitter, arguments[0]);
        if (af is null) return false;
        if (af.HasOwnThis || af.IsAsync || af.IsGenerator) return false;
        if (af.Parameters.Count > 1) return false;
        foreach (var p in af.Parameters)
        {
            // Annotated params (p.Type != null) are now supported via a boxed
            // adapter (#861) — see the typed-param path below. Rest/optional/
            // defaulted params still bail (they need the reflective arg shape).
            if (p.IsRest || p.IsOptional) return false;
            if (p.DefaultValue != null) return false;
        }
        if (!ctx.ArrowMethods.TryGetValue(af, out var staticMethod)) return false;

        var sParams = staticMethod.GetParameters();
        bool allParamsObject = true;
        foreach (var sp in sParams)
            if (sp.ParameterType != ctx.Types.Object) { allParamsObject = false; break; }

        if (allParamsObject)
        {
            // Untyped fast path (unchanged). The arrow's static method already has
            // `object` parameter slots (unannotated params; ParameterTypeResolver
            // falls back to object), so it binds directly to Func<object, …>. The
            // return type is the discriminator: `object` (any-typed body) uses
            // Func<object, object> + helperMethod; `bool` (predicate body like
            // `v => v > 10`) uses Func<object, bool> + boolHelper where present.
            // Capturing arrows go through emitter.TryEmitArrowAsDelegate, which
            // builds the display instance + binds to its Invoke method.
            var ret = staticMethod.ReturnType;
            System.Reflection.Emit.MethodBuilder chosenHelper;
            Type funcType;
            if (ret == ctx.Types.Object)
            {
                chosenHelper = helperMethod;
                funcType = typeof(Func<object, object>);
            }
            else if (ret == ctx.Types.Boolean && boolHelper != null)
            {
                chosenHelper = boolHelper;
                funcType = typeof(Func<object, bool>);
            }
            else
            {
                return false;
            }

            if (!emitter.TryEmitArrowAsDelegate(af, funcType))
                return false;
            ctx.IL.Emit(OpCodes.Call, chosenHelper);
            return true;
        }

        // Typed-param adapter path (#861): an annotated callback compiles to a
        // typed static method (e.g. double(double)/bool(double)) that cannot bind
        // to Func<object, object> directly. Bind a per-arrow boxed adapter that
        // marshals object↔typed around the arrow call, then drive the matching
        // Array*Direct helper.
        //
        // #861 L4: when the predicate arrow returns `bool` and a Func<object, bool>
        // helper variant exists (filter/find/findIndex/some/every), emit a
        // bool-returning adapter and call that variant — skipping the per-element
        // result box + IsTruthy the object helper would otherwise do.
        bool wantBool = boolHelper != null && staticMethod.ReturnType == ctx.Types.Boolean;
        if (!TryBindTypedArrowAdapter(emitter, af, staticMethod, funcArity: 1, boolReturn: wantBool))
            return false;
        ctx.IL.Emit(OpCodes.Call, wantBool ? boolHelper! : helperMethod);
        return true;
    }

    /// <summary>
    /// Binds a per-arrow boxed adapter (object(object[,object])) for an annotated,
    /// non-capturing callback arrow to a Func&lt;object,…&gt; of
    /// <paramref name="funcArity"/> parameters, leaving the delegate on the stack
    /// for the caller to pass to an Array*Direct helper. Returns false (reflective
    /// fallback) for capturing arrows or any non-marshallable param/return type.
    /// </summary>
    private static bool TryBindTypedArrowAdapter(
        IEmitterContext emitter,
        Expr.ArrowFunction af,
        System.Reflection.Emit.MethodBuilder arrowMethod,
        int funcArity,
        bool boolReturn,
        bool numberReturn = false)
    {
        var ctx = emitter.Context;

        // Marshallable gate (shared): stay in the reflective path's no-arg-conversion regime
        // (concrete double/bool/string, or object) so the adapter's unbox/cast matches MethodInvoker
        // semantics exactly. Union/nullable params already widen to object in ParameterTypeResolver, so
        // a non-object slot here is a concrete value/reference type.
        if (!IsAdapterMarshallable(ctx, arrowMethod.ReturnType)) return false;
        foreach (var p in arrowMethod.GetParameters())
            if (!IsAdapterMarshallable(ctx, p.ParameterType)) return false;

        // boolReturn (#861 L4) binds Func<object, bool> for a bool-returning predicate so the
        // *DirectBool helper consumes an unboxed bool. Only the 1-arg predicate helpers pass it.
        Type funcType = boolReturn
            ? typeof(Func<object, bool>)
            : numberReturn
                ? typeof(Func<object, object, double>)
            : (funcArity == 2 ? typeof(Func<object, object, object>) : typeof(Func<object, object>));
        var funcCtor = funcType.GetConstructor([typeof(object), typeof(IntPtr)])!;

        if (ctx.DisplayClasses.TryGetValue(af, out var displayClass))
        {
            // #861 L3: capturing arrow — arrowMethod is the instance Invoke on the display class.
            // Emit an INSTANCE adapter, build the display instance (capturing live locals), then bind
            // (displayInstance, ldftn adapter). The display instance is built fresh at the call site,
            // exactly as the reflective $TSFunction path does.
            var adapter = ctx.ArrowBoxedAdapters.GetOrEmit(
                displayClass, arrowMethod, funcArity,
                instance: true, boolReturn: boolReturn,
                numberReturn: numberReturn);
            if (!emitter.TryEmitCapturingArrowDisplayInstance(af))
                return false;                                          // Stack: [displayInstance]
            ctx.IL.Emit(OpCodes.Ldftn, adapter);
            ctx.IL.Emit(OpCodes.Newobj, funcCtor);
            return true;
        }

        // #861 L1: non-capturing arrow — arrowMethod is a static method on its declaring $Program type.
        // Using DeclaringType (rather than ctx.ProgramType, set only on the top-level context) lets the
        // adapter fire inside function/method bodies too. Bind (null, ldftn adapter).
        if (arrowMethod.DeclaringType is not TypeBuilder programType) return false;
        var staticAdapter = ctx.ArrowBoxedAdapters.GetOrEmit(
            programType, arrowMethod, funcArity,
            instance: false, boolReturn: boolReturn,
            numberReturn: numberReturn);
        ctx.IL.Emit(OpCodes.Ldnull);
        ctx.IL.Emit(OpCodes.Ldftn, staticAdapter);
        ctx.IL.Emit(OpCodes.Newobj, funcCtor);
        return true;
    }

    private static bool IsAdapterMarshallable(CompilationContext ctx, Type t)
        => t == ctx.Types.Object || t == ctx.Types.Double
        || t == ctx.Types.Boolean || t == ctx.Types.String;

    /// <summary>
    /// Reduce-specific fast path: the callback is binary
    /// <c>(acc, element) =&gt; newAcc</c>, requires <c>Func&lt;object, object,
    /// object&gt;</c>, and initial value must be present (scope-out for the
    /// no-initial form's kPresent semantics). Stack on entry: <c>[list]</c>.
    /// On success, the call site has already pushed the receiver; this
    /// emits the delegate construction, the initial-value, and the call.
    /// </summary>
    private static bool TryEmitReduceDirectCall(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        // Reduce direct form requires the 2-arg shape: (callback, initialValue).
        // No-initial would need scan-for-first-present; punt to the slow path.
        if (arguments.Count != 2) return false;
        var af = ResolveCallbackArrow(emitter, arguments[0]);
        if (af is null) return false;
        if (af.HasOwnThis || af.IsAsync || af.IsGenerator) return false;
        if (af.Parameters.Count != 2) return false;
        foreach (var p in af.Parameters)
        {
            // Annotated params now supported via a boxed adapter (#861).
            if (p.IsRest || p.IsOptional) return false;
            if (p.DefaultValue != null) return false;
        }
        if (!ctx.ArrowMethods.TryGetValue(af, out var staticMethod)) return false;

        var sParams = staticMethod.GetParameters();
        bool allObject = staticMethod.ReturnType == ctx.Types.Object;
        if (allObject)
            foreach (var sp in sParams)
                if (sp.ParameterType != ctx.Types.Object) { allObject = false; break; }

        // Stack: [list]. Build the (acc, element) => newAcc delegate.
        if (allObject)
        {
            // Untyped fast path (unchanged): object(object,object) binds directly.
            if (!emitter.TryEmitArrowAsDelegate(af, typeof(Func<object, object, object>)))
                return false;
        }
        else
        {
            // Annotated reducer (e.g. double(double,double)): bridge via a boxed
            // adapter object(object,object).
            if (!TryBindTypedArrowAdapter(emitter, af, staticMethod, funcArity: 2, boolReturn: false))
                return false;
        }
        // Push initial value (boxed object).
        emitter.EmitExpression(arguments[1]);
        emitter.EmitBoxIfNeeded(arguments[1]);
        ctx.IL.Emit(OpCodes.Call, ctx.Runtime!.ArrayReduceDirect);
        return true;
    }

    /// <summary>
    /// Sort-specific fast path for a statically resolved two-parameter arrow.
    /// The runtime helper still snapshots the dense backing store and validates
    /// it after every comparator call, preserving observable mutation semantics;
    /// only the boxed callable-dispatch boundary is removed.
    /// </summary>
    private static bool TryEmitSortDirectCall(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        if (arguments.Count != 1) return false;
        var af = ResolveCallbackArrow(emitter, arguments[0]);
        if (af is null) return false;
        if (af.HasOwnThis || af.IsAsync || af.IsGenerator) return false;
        if (af.Parameters.Count != 2) return false;
        foreach (var p in af.Parameters)
        {
            if (p.IsRest || p.IsOptional) return false;
            if (p.DefaultValue != null) return false;
        }
        if (!ctx.ArrowMethods.TryGetValue(af, out var staticMethod)) return false;

        bool allObject = staticMethod.ReturnType == ctx.Types.Object;
        if (allObject)
        {
            foreach (var sp in staticMethod.GetParameters())
            {
                if (sp.ParameterType != ctx.Types.Object)
                {
                    allObject = false;
                    break;
                }
            }
        }

        bool numberReturn = staticMethod.ReturnType == ctx.Types.Double;
        if (allObject)
        {
            if (!emitter.TryEmitArrowAsDelegate(af, typeof(Func<object, object, object>)))
                return false;
        }
        else if (!TryBindTypedArrowAdapter(
                     emitter,
                     af,
                     staticMethod,
                     funcArity: 2,
                     boolReturn: false,
                     numberReturn: numberReturn))
        {
            return false;
        }

        ctx.IL.Emit(OpCodes.Call, numberReturn
            ? ctx.Runtime!.ArraySortDirectNumber
            : ctx.Runtime!.ArraySortDirect);
        return true;
    }

    /// <summary>
    /// Restores the previous `$Runtime._currentCallbackThisArg` value saved
    /// by <see cref="EmitCallbackAndStashThisArg"/>. Stack-neutral.
    /// </summary>
    private static void EmitRestoreCallbackThisArg(IEmitterContext emitter, LocalBuilder savedLocal)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        il.Emit(OpCodes.Ldloc, savedLocal);
        il.Emit(OpCodes.Stsfld, ctx.Runtime!.CurrentCallbackThisArgField);
    }

    #endregion
}
