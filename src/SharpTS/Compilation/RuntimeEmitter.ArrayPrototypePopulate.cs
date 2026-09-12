using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits a private static method that populates the
    /// <c>Array.prototype</c> singleton dictionary (<see cref="EmittedArrayOperationsRuntime.PrototypeField"/>)
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
    private void DefineArrayPrototypePopulateShell(TypeBuilder typeBuilder, EmittedArrayOperationsRuntime arrays)
    {
        arrays.PrototypePopulateMethod = typeBuilder.DefineMethod(
            "_ArrayPrototypePopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
    }

    private void EmitArrayPrototypePopulate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = runtime.ArrayOperations.PrototypePopulateMethod;

        var il = method.GetILGenerator();
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);

        EmitPrototypePopulateGuard(il, runtime.ArrayOperations.PrototypeField);

        // ECMA-262 23.1.3 Array prototype "length" property is 0. Without
        // this entry, `Array.prototype.length` reads as undefined.
        il.Emit(OpCodes.Ldsfld, runtime.ArrayOperations.PrototypeField);
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
        EmitInstallConstructor(il, runtime, runtime.ArrayOperations.PrototypeField, arrDescLocal, setItem, () =>
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
        // $Runtime.LikeMaterialize before the helper's Castclass —
        // unblocks borrowed Array.prototype.X patterns
        // (`obj.map = Array.prototype.map; obj.map(cb)`).
        void Wire(string jsName, MethodBuilder? helper, int jsLength)
            => EmitWirePrototypeMethod(il, runtime, runtime.ArrayOperations.PrototypeField, arrDescLocal,
                setItem, jsName, helper, jsLength);

        Wire("map",            runtime.ArrayOperations.Map,            1);
        Wire("filter",         runtime.ArrayOperations.Filter,         1);
        Wire("forEach",        runtime.ArrayOperations.ForEach,        1);
        Wire("find",           runtime.ArrayOperations.Find,           1);
        Wire("findIndex",      runtime.ArrayOperations.FindIndex,      1);
        Wire("findLast",       runtime.ArrayOperations.FindLast,       1);
        Wire("findLastIndex",  runtime.ArrayOperations.FindLastIndex,  1);
        Wire("some",           runtime.ArrayOperations.Some,           1);
        Wire("every",          runtime.ArrayOperations.Every,          1);
        Wire("reduce",         runtime.ArrayOperations.Reduce,         1);
        Wire("reduceRight",    runtime.ArrayOperations.ReduceRight,    1);
        Wire("includes",       runtime.ArrayOperations.IncludesProto,  1);
        Wire("indexOf",        runtime.ArrayOperations.IndexOf,        1);
        Wire("lastIndexOf",    runtime.ArrayOperations.LastIndexOf,    1);
        Wire("join",           runtime.ArrayOperations.Join,           1);
        Wire("concat",         runtime.ArrayOperations.Concat,         1);
        Wire("reverse",        runtime.ArrayOperations.ReverseProto,   0);
        Wire("flat",           runtime.ArrayOperations.Flat,           0);
        Wire("flatMap",        runtime.ArrayOperations.FlatMap,        1);
        Wire("sort",           runtime.ArrayOperations.SortProto,      1);
        Wire("toSorted",       runtime.ArrayOperations.ToSorted,       1);
        Wire("splice",         runtime.ArrayOperations.SpliceProto,    2);
        Wire("toSpliced",      runtime.ArrayOperations.ToSplicedProto, 2);
        Wire("toReversed",     runtime.ArrayOperations.ToReversed,     0);
        Wire("with",           runtime.ArrayOperations.With,           2);
        Wire("at",             runtime.ArrayOperations.At,             1);
        Wire("fill",           runtime.ArrayOperations.FillProto,      1);
        Wire("copyWithin",     runtime.ArrayOperations.CopyWithinProto, 2);
        Wire("entries",        runtime.ArrayOperations.Entries,        0);
        Wire("keys",           runtime.ArrayOperations.Keys,           0);
        Wire("values",         runtime.ArrayOperations.Values,         0);
        Wire("slice",          runtime.ArrayOperations.Slice,          2);
        // push/unshift must be variadic — the proto-only wrappers loop over the
        // params object[] so `Array.prototype.push.apply(arr, items)` spreads.
        // Inline `arr.push(x)` continues to call the single-element ArrayPush
        // helper directly via the inline emitter / $BoundArrayMethod paths.
        Wire("push",           runtime.ArrayOperations.PushProto,      1);
        Wire("pop",            runtime.ArrayOperations.PopProto,       0);
        Wire("shift",          runtime.ArrayOperations.ShiftProto,     0);
        Wire("unshift",        runtime.ArrayOperations.UnshiftProto,   1);

        // ECMA-262 23.1.3.32 Array.prototype.toString — returns the join with
        // default separator. Borrowed-method dispatch (`arr.toString =
        // Array.prototype.toString; arr.toString()`) and direct `[1,2].toString()`
        // both flow through this slot when the typed inline-emit path falls
        // back to dynamic dispatch. The helper is `__this`-named so the
        // receiver flows through InvokeWithThis correctly.
        Wire("toString",       runtime.ArrayOperations.ProtoToStringHelper, 0);
        Wire("toLocaleString", runtime.ArrayOperations.ProtoToLocaleStringHelper, 0);

        // Per ECMA-262 §23.1.3 Array.prototype's [[Prototype]] is %Object.prototype%.
        il.Emit(OpCodes.Ldsfld, runtime.ArrayOperations.PrototypeField);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
        il.Emit(OpCodes.Call, runtime.PDSSetPrototype);

        // ECMA-262 §23.1.3.34: Array.prototype[@@iterator] === Array.prototype.values.
        // Symbol-keyed entry pointing to the SAME wrapper that "values" resolves to.
        // Lazy retrieval — read it back out of the dict so the values entry's
        // $TSFunction identity is preserved (`arr[Symbol.iterator] === arr.values`).
        var iterFnLocal = il.DeclareLocal(_types.Object);
        var valuesGet = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, runtime.ArrayOperations.PrototypeField);
        il.Emit(OpCodes.Ldstr, "values");
        il.Emit(OpCodes.Ldloca, iterFnLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
            _types.String, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, valuesGet); // shouldn't happen — values was just installed
        // GetSymbolDict(ArrayPrototype)[SymbolIterator] = valuesFn
        il.Emit(OpCodes.Ldsfld, runtime.ArrayOperations.PrototypeField);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolIterator);
        il.Emit(OpCodes.Ldloc, iterFnLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "set_Item",
            _types.Object, _types.Object));
        il.MarkLabel(valuesGet);

        il.Emit(OpCodes.Ret);
    }
}
