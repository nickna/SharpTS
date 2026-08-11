using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits a private static method that populates the
    /// <c>Array.prototype</c> singleton dictionary (<see cref="EmittedRuntime.ArrayPrototypeField"/>)
    /// with <c>$TSFunction</c> wrappers around the <c>$Runtime.Array*</c>
    /// helpers. Called from the static cctor's tail (the cctor's <c>Ret</c>
    /// is patched to <c>Call</c> this method first).
    /// </summary>
    /// <remarks>
    /// Must be emitted AFTER all <c>EmitArray*</c> helpers so the wrapped
    /// MethodBuilders are non-null. Most Test262 tests don't directly invoke
    /// the wrappers — they probe via <c>typeof Array.prototype.X</c> /
    /// <c>isConstructor(Array.prototype.X)</c>. The pattern matcher in
    /// <c>ILEmitter.Calls.cs</c> still handles
    /// <c>Array.prototype.X.call(receiver, …)</c> syntactically.
    /// </remarks>
    /// <summary>
    /// Defines the populate-method shell early so other emitters
    /// (GetListProperty's prototype-chain fallback) can reference the
    /// MethodBuilder before all Array* helper bodies have been emitted.
    /// </summary>
    private void DefineArrayPrototypePopulateShell(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.ArrayPrototypePopulateMethod = typeBuilder.DefineMethod(
            "_ArrayPrototypePopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
    }

    private void EmitArrayPrototypePopulate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = runtime.ArrayPrototypePopulateMethod;

        var il = method.GetILGenerator();
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);

        EmitPrototypePopulateGuard(il, runtime.ArrayPrototypeField);

        // ECMA-262 23.1.3 Array prototype "length" property is 0. Without
        // this entry, `Array.prototype.length` reads as undefined.
        il.Emit(OpCodes.Ldsfld, runtime.ArrayPrototypeField);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, setItem);

        var arrDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);

        // ECMA-262 23.1.3 Array.prototype.constructor === Array. Compiled
        // bare `Array` resolves to typeof(IList<object>) (per
        // GlobalThisStaticEmitter). Mirror it here so
        // `Array.prototype.hasOwnProperty("constructor") === true` and
        // `Array.prototype.constructor === Array` both hold.
        EmitInstallConstructor(il, runtime, runtime.ArrayPrototypeField, arrDescLocal, setItem, () =>
        {
            il.Emit(OpCodes.Ldtoken, _types.IListOfObject);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        });

        // For each named method: dict[jsName] = new $TSFunction(null, methodInfo)
        // The 2-arg ctor without name/length is fine — IsConstructor only needs
        // DeclaringType to detect "$Runtime", and typeof returns "function".
        // Method signatures don't match what TSFunction.Invoke expects (helpers
        // take a List receiver as first arg, not the user args), so direct
        // .call/.apply through these wrappers won't dispatch correctly. The
        // pattern matcher in ILEmitter.Calls.cs intercepts the syntactic
        // Array.prototype.X.call form and bypasses these wrappers.

        // Wire with explicit JS-spec name + length per ECMA-262.
        // Length is the user-callable arg count (the receiver is implicit).
        // Also install a non-enumerable PDS descriptor (built-in §17 attrs)
        // so `gOPD(Array.prototype, "push").enumerable === false` per spec.
        // The "__this" rename lets $TSFunction.InvokeWithThis prepend the
        // call-site receiver. Stage 4z35 added a List<object> coercion branch
        // in CoercePrimitiveArgs that materializes non-list receivers via
        // $Runtime.ArrayLikeMaterialize before the helper's Castclass —
        // unblocks borrowed Array.prototype.X patterns
        // (`obj.map = Array.prototype.map; obj.map(cb)`).
        void Wire(string jsName, MethodBuilder? helper, int jsLength)
            => EmitWirePrototypeMethod(il, runtime, runtime.ArrayPrototypeField, arrDescLocal,
                setItem, jsName, helper, jsLength);

        Wire("map",            runtime.ArrayMap,            1);
        Wire("filter",         runtime.ArrayFilter,         1);
        Wire("forEach",        runtime.ArrayForEach,        1);
        Wire("find",           runtime.ArrayFind,           1);
        Wire("findIndex",      runtime.ArrayFindIndex,      1);
        Wire("findLast",       runtime.ArrayFindLast,       1);
        Wire("findLastIndex",  runtime.ArrayFindLastIndex,  1);
        Wire("some",           runtime.ArraySome,           1);
        Wire("every",          runtime.ArrayEvery,          1);
        Wire("reduce",         runtime.ArrayReduce,         1);
        Wire("reduceRight",    runtime.ArrayReduceRight,    1);
        Wire("includes",       runtime.ArrayIncludes,       1);
        Wire("indexOf",        runtime.ArrayIndexOf,        1);
        Wire("lastIndexOf",    runtime.ArrayLastIndexOf,    1);
        Wire("join",           runtime.ArrayJoin,           1);
        Wire("concat",         runtime.ArrayConcat,         1);
        Wire("reverse",        runtime.ArrayReverse,        0);
        Wire("flat",           runtime.ArrayFlat,           0);
        Wire("flatMap",        runtime.ArrayFlatMap,        1);
        Wire("sort",           runtime.ArraySort,           1);
        Wire("toSorted",       runtime.ArrayToSorted,       1);
        Wire("splice",         runtime.ArraySplice,         2);
        Wire("toSpliced",      runtime.ArrayToSpliced,      2);
        Wire("toReversed",     runtime.ArrayToReversed,     0);
        Wire("with",           runtime.ArrayWith,           2);
        Wire("at",             runtime.ArrayAt,             1);
        Wire("fill",           runtime.ArrayFill,           1);
        Wire("copyWithin",     runtime.ArrayCopyWithin,     2);
        Wire("entries",        runtime.ArrayEntries,        0);
        Wire("keys",           runtime.ArrayKeys,           0);
        Wire("values",         runtime.ArrayValues,         0);
        Wire("slice",          runtime.ArraySlice,          2);
        // push/unshift must be variadic — the proto-only wrappers loop over the
        // params object[] so `Array.prototype.push.apply(arr, items)` spreads.
        // Inline `arr.push(x)` continues to call the single-element ArrayPush
        // helper directly via the inline emitter / $BoundArrayMethod paths.
        Wire("push",           runtime.ArrayPushProto,      1);
        Wire("pop",            runtime.ArrayPopProto,       0);
        Wire("shift",          runtime.ArrayShiftProto,     0);
        Wire("unshift",        runtime.ArrayUnshiftProto,   1);

        // ECMA-262 23.1.3.32 Array.prototype.toString — returns the join with
        // default separator. Borrowed-method dispatch (`arr.toString =
        // Array.prototype.toString; arr.toString()`) and direct `[1,2].toString()`
        // both flow through this slot when the typed inline-emit path falls
        // back to dynamic dispatch. The helper is `__this`-named so the
        // receiver flows through InvokeWithThis correctly.
        Wire("toString",       runtime.ArrayProtoToStringHelper, 0);
        Wire("toLocaleString", runtime.ArrayProtoToStringHelper, 0);

        // Per ECMA-262 §23.1.3 Array.prototype's [[Prototype]] is %Object.prototype%.
        il.Emit(OpCodes.Ldsfld, runtime.ArrayPrototypeField);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
        il.Emit(OpCodes.Call, runtime.PDSSetPrototype);

        // ECMA-262 §23.1.3.34: Array.prototype[@@iterator] === Array.prototype.values.
        // Symbol-keyed entry pointing to the SAME wrapper that "values" resolves to.
        // Lazy retrieval — read it back out of the dict so the values entry's
        // $TSFunction identity is preserved (`arr[Symbol.iterator] === arr.values`).
        var iterFnLocal = il.DeclareLocal(_types.Object);
        var valuesGet = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, runtime.ArrayPrototypeField);
        il.Emit(OpCodes.Ldstr, "values");
        il.Emit(OpCodes.Ldloca, iterFnLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
            _types.String, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, valuesGet); // shouldn't happen — values was just installed
        // GetSymbolDict(ArrayPrototype)[SymbolIterator] = valuesFn
        il.Emit(OpCodes.Ldsfld, runtime.ArrayPrototypeField);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolIterator);
        il.Emit(OpCodes.Ldloc, iterFnLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "set_Item",
            _types.Object, _types.Object));
        il.MarkLabel(valuesGet);

        il.Emit(OpCodes.Ret);
    }
}
