using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $Array class for standalone array support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSArray.
/// </summary>
/// <remarks>
/// Stage E.2 M1 (infrastructure, no behavior change): the class now carries the
/// sparse/hole-aware fields and long-indexed methods needed by later milestones.
/// Existing callers (IL emitters that invoke <see cref="EmittedRuntime.TSArrayGet"/>,
/// <see cref="EmittedRuntime.TSArraySet"/>, <see cref="EmittedRuntime.TSArrayElementsGetter"/>)
/// continue to observe pre-Stage-E semantics: constructor populates <c>_dense</c>
/// from the input list, <c>_length == _dense.Count</c>, <c>_sparse == null</c>,
/// and legacy int-indexed Get/Set bounds-check against <c>_dense.Count</c>. The
/// long-indexed API is present and correct but unused until M2 wires runtime
/// dispatch through it.
/// </remarks>
public partial class RuntimeEmitter
{
    // Fields. Since $Array now *inherits* from List<object?>, the dense
    // backing IS the instance itself — no separate field. _sparse / _length
    // are new (Stage E.2); frozen/sealed match pre-refactor layout.
    private FieldBuilder _tsArraySparseField = null!;
    private FieldBuilder _tsArrayLengthField = null!;
    private FieldBuilder _tsArrayIsFrozenField = null!;
    private FieldBuilder _tsArrayIsSealedField = null!;
    // Set true by Object.seal / Object.preventExtensions on a $Array (those runtime helpers don't touch
    // the instance _isFrozen/_isSealed, only the external _sealedObjects/_nonExtensibleObjects collections
    // that the boxed ArrayPush consults). The unboxed PushDouble fast path can't reach those collections,
    // so it checks this cheap instance flag to refuse appending to a non-extensible array.
    private FieldBuilder _tsArrayNonExtensibleField = null!;

    // ── Unboxed "packed-double" elements-kind (project: number[] unboxing) ────
    // When _isNumeric is true the dense prefix [0, _numCount) lives unboxed in
    // _numStore (a double[] grown geometrically), NOT in the inherited
    // List<object?> base (which is empty in numeric mode). Holes are the
    // reserved HoleNanBits pattern; genuine NaN is canonicalized on write so it
    // never collides. A non-double write (or any base-List consumer) triggers
    // EnsureBoxed, which materializes _numStore back into the base list and
    // clears _isNumeric (deopt). All three default to the boxed mode
    // (_isNumeric=false, _numStore=null, _numCount=0) so an unmodified program
    // behaves exactly as before until creation/fast-path emission is wired.
    private FieldBuilder _tsArrayNumStoreField = null!;
    private FieldBuilder _tsArrayNumCountField = null!;
    private FieldBuilder _tsArrayIsNumericField = null!;

    /// <summary>
    /// Reserved IEEE-754 bit pattern marking a HOLE in <c>_numStore</c>. A
    /// quiet NaN distinct from the canonical JS NaN (<c>0x7FF8000000000000</c>),
    /// so a genuine <c>NaN</c> element (canonicalized to the standard bits on
    /// write) is never mistaken for a hole. Matches V8's hole-NaN choice.
    /// </summary>

    /// <summary>Canonical JS NaN bits — what a genuine NaN element is stored as.</summary>

    // Cached generic type for the sparse dictionary backing.
    private Type _tsArraySparseType = null!;

    private MethodInfo? _tsArraySparseTryGetValue;
    private MethodInfo? _tsArraySparseCountGetter;
    private MethodInfo? _tsArraySparseRemove;
    private MethodInfo? _tsArraySparseSetItem;
    private MethodInfo? _tsArrayListCountGetter;
    private MethodInfo? _tsArrayListAdd;
    private MethodInfo? _tsArrayListRemoveAt;
    private MethodInfo? _tsArrayListGetItem;
    private MethodInfo? _tsArrayListSetItem;

    // The deopt method (numeric -> boxed). Defined early in EmitTSArrayClass so
    // the base-list methods emitted below can call it via EmitTSArrayDeoptGuard;
    // its body is emitted later in EmitTSArrayNumericAccessors.
    private MethodBuilder _tsArrayEnsureBoxedMethod = null!;

    private void InitTSArrayMethodCache()
    {
        _tsArraySparseTryGetValue = _types.GetMethod(_tsArraySparseType, "TryGetValue", [_types.UInt32, _types.Object.MakeByRefType()])!;
        _tsArraySparseCountGetter = _types.GetProperty(_tsArraySparseType, "Count").GetGetMethod()!;
        _tsArraySparseRemove = _types.GetMethod(_tsArraySparseType, "Remove", [_types.UInt32])!;
        _tsArraySparseSetItem = _types.GetMethod(_tsArraySparseType, "set_Item", [_types.UInt32, _types.Object])!;
        _tsArrayListCountGetter = _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!;
        _tsArrayListAdd = _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!;
        _tsArrayListRemoveAt = _types.GetMethod(_types.ListOfObject, "RemoveAt", [_types.Int32])!;
        _tsArrayListGetItem = _types.GetMethod(_types.ListOfObject, "get_Item", [_types.Int32])!;
        _tsArrayListSetItem = _types.GetMethod(_types.ListOfObject, "set_Item", [_types.Int32, _types.Object])!;
    }

    private void EmitTSArrayClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        _tsArraySparseType = _types.MakeGenericType(_types.DictionaryOpen, _types.UInt32, _types.Object);
        InitTSArrayMethodCache();

        // public class $Array : List<object?>
        //
        // Design rationale (Stage E.2 M2): $Array *inherits* from List<object?>
        // rather than owning one as a field. This matters because ~48 IL
        // emission sites throughout Compilation/ still do
        //   Isinst List<object?> → Castclass List<object?>
        // on JS array values. Inheritance makes those sites pass-through
        // automatically — the emitted `$Array` IS a List<object?> at the CLR
        // type-check level. M3's runtime-dispatch cleanup can then prefer the
        // long-indexed API incrementally, without each intermediate commit
        // breaking 100+ tests.
        //
        // The interpreter's SharpTSArray uses composition (owns a Deque<object?>)
        // because it's C# code and C# callers use its public API directly.
        // The emitted class has no such luxury — legacy IL emitters expect
        // List<object?> semantics and will keep expecting them until M3.
        //
        // Dense prefix = the inherited List's own storage (indices [0, Count)).
        // Sparse tail and true _length live in added fields. Past Stage-E,
        // base Count may diverge from _length once sparse writes occur; built-
        // in emitters that care use the explicit getters (Length / LongLength /
        // HasIndex) rather than falling through to base Count.
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$Array",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
            _types.ListOfObject
        );
        runtime.TSArrayType = typeBuilder;

        // Sparse-aware fields. The dense backing is the base-List's own storage;
        // `_dense` is kept as an alias field for code clarity but points at `this`.
        _tsArraySparseField = typeBuilder.DefineField("_sparse", _tsArraySparseType, FieldAttributes.Private);
        _tsArrayLengthField = typeBuilder.DefineField("_length", _types.Int64, FieldAttributes.Private);
        _tsArrayIsFrozenField = typeBuilder.DefineField("_isFrozen", _types.Boolean, FieldAttributes.Private);
        _tsArrayIsSealedField = typeBuilder.DefineField("_isSealed", _types.Boolean, FieldAttributes.Private);
        _tsArrayNonExtensibleField = typeBuilder.DefineField("_isNonExtensible", _types.Boolean, FieldAttributes.Private);
        // _tsArrayDenseField retired; every previous read-of-_dense is replaced
        // by a no-op (receiver is already the List via inheritance).

        // Unboxed packed-double elements-kind (number[] unboxing project). All
        // default to boxed mode; nothing reads them until creation/fast-path
        // emission is wired in a later phase, so this is a zero-behavior-change
        // addition (CLR zero-inits: _isNumeric=false, _numStore=null, _numCount=0).
        _tsArrayNumStoreField = typeBuilder.DefineField("_numStore", _types.DoubleArray, FieldAttributes.Private);
        _tsArrayNumCountField = typeBuilder.DefineField("_numCount", _types.Int32, FieldAttributes.Private);
        _tsArrayIsNumericField = typeBuilder.DefineField("_isNumeric", _types.Boolean, FieldAttributes.Private);

        // Define the deopt method up front (body emitted in
        // EmitTSArrayNumericAccessors) so the base-list methods below can guard
        // themselves with EmitTSArrayDeoptGuard — any boxed-representation access
        // on a numeric-mode array first materializes the unboxed store.
        _tsArrayEnsureBoxedMethod = typeBuilder.DefineMethod("EnsureBoxed",
            MethodAttributes.Public | MethodAttributes.HideBySig, _types.Void, System.Type.EmptyTypes);
        runtime.TSArrayEnsureBoxed = _tsArrayEnsureBoxedMethod;

        EmitTSArrayConstructor(typeBuilder, runtime);

        // Elements returns `this` (the inherited List<object?>). In practice
        // callers that cared about the sparse tail have migrated to the
        // long-indexed accessors (Get/Set/HasIndex/LongLength); Elements
        // remains for (a) call sites whose semantics are "get the dense
        // prefix as a List" (e.g. GroupBy, DNS results feed the list into
        // interop paths), and (b) as a semantic marker in emitted IL vs.
        // an opaque Castclass. Kept post-Stage E as a shallow forward-
        // compatible helper; safe because the dense prefix IS the instance.
        EmitTSArrayElementsProperty(typeBuilder, runtime);

        // Private helpers emitted first — all public getters/methods below
        // call SyncLength to absorb mutations that went through inherited
        // List<T>.Add / Insert / RemoveAt (which bump base.Count without
        // touching our _length field). Without this sync, e.g. GroupBy's
        // `new $Array(list).Add(x)` path sees stale _length and reads return
        // undefined.
        var syncLength = EmitTSArraySyncLength(typeBuilder, runtime);
        var materializeDense = EmitTSArrayMaterializeDense(typeBuilder, runtime);
        var tryCollapseSparse = EmitTSArrayTryCollapseSparse(typeBuilder, runtime);
        var getCore = EmitTSArrayGetCore(typeBuilder, runtime);
        var setCore = EmitTSArraySetCore(typeBuilder, runtime);
        var setCoreWithExtend = EmitTSArraySetCoreWithExtend(typeBuilder, runtime, setCore);
        _ = materializeDense;  // reserved for M5 mutator emitters

        EmitTSArrayLongLengthProperty(typeBuilder, runtime, syncLength);
        EmitTSArrayLengthProperty(typeBuilder, runtime, syncLength);
        // No custom Count property — inherited from List<object?>. The public
        // Count + interface ICollection<T>.Count / IReadOnlyCollection<T>.Count
        // all come from the base class. `arr.Count == arr._dense.Count`, which
        // matches pre-refactor semantics; callers wanting the full sparse
        // length use LongLength.

        EmitTSArrayIsFrozenProperty(typeBuilder, runtime);
        EmitTSArrayIsSealedProperty(typeBuilder, runtime);

        EmitTSArrayFreeze(typeBuilder, runtime);
        EmitTSArraySeal(typeBuilder, runtime);

        // Legacy int-indexed methods (pre-Stage-E surface; unchanged semantics).
        EmitTSArrayGet(typeBuilder, runtime);
        EmitTSArraySet(typeBuilder, runtime);

        EmitTSArrayHasIndex(typeBuilder, runtime, syncLength);
        EmitTSArrayGetRaw(typeBuilder, runtime, getCore, syncLength);
        EmitTSArrayGetLong(typeBuilder, runtime, getCore, syncLength);
        EmitTSArraySetLong(typeBuilder, runtime, setCoreWithExtend, syncLength);
        EmitTSArraySetStrictLong(typeBuilder, runtime, setCoreWithExtend, syncLength);
        EmitTSArraySetLength(typeBuilder, runtime, tryCollapseSparse, syncLength);
        EmitTSArrayDeleteAt(typeBuilder, runtime, syncLength);

        // Constructor-args overload for guest classes extending Array (#233) —
        // must be emitted after SetLength, whose MethodBuilder it calls.
        EmitTSArraySubclassConstructor(typeBuilder, runtime);

        EmitTSArrayToString(typeBuilder, runtime);
        // IList<object?> impl is inherited from List<object?> — no explicit
        // bridges needed (was the old composition-based approach).

        // Unboxed packed-double accessors (number[] unboxing project). Emitted
        // after SetLong (SetDouble delegates the boxed/gap paths to it). Mode is
        // never entered until creation/fast-path emission is wired, so these are
        // dead-but-valid IL today.
        EmitTSArrayNumericAccessors(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Prepends a deopt guard to a base-list method body: <c>if (_isNumeric)
    /// this.EnsureBoxed();</c>. Any method that reads/writes the inherited
    /// <c>List&lt;object?&gt;</c>, <c>_sparse</c>, or reconciles <c>_length</c>
    /// against base <c>Count</c> must call this first — in numeric mode the base
    /// list is empty and those operations would corrupt. Costs one field load +
    /// branch in the common boxed case (mode off); only numeric arrays pay the
    /// materialization. Idempotent (EnsureBoxed self-guards), so redundant
    /// guards across delegating methods are harmless.
    /// </summary>
    private void EmitTSArrayDeoptGuard(ILGenerator il)
    {
        var skip = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayIsNumericField);
        il.Emit(OpCodes.Brfalse, skip);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _tsArrayEnsureBoxedMethod);
        il.MarkLabel(skip);
    }

    /// <summary>
    /// Emits, into ANY runtime helper (not just <c>$Array</c>'s own methods), a
    /// deopt of a numeric-mode <c>$Array</c> that the helper is about to read AS a
    /// base <c>List&lt;object?&gt;</c> directly — the generic-consumption sites that
    /// bypass <c>$Array</c>'s guarded methods because <c>$Array : List&lt;object?&gt;</c>
    /// (string coercion / JSON / <c>Object.*</c> / call-spread / …). <paramref name="loadValue"/>
    /// pushes the candidate value; this emits <c>if (v is $Array a) a.EnsureBoxed();</c>
    /// (value re-loaded for the cast, so it leaves the stack exactly as found).
    /// <c>EnsureBoxed</c> self-guards (no-op unless actually numeric) and materializes
    /// <c>_numStore</c> back into the base list, so the subsequent <c>is List&lt;object&gt;</c>
    /// read sees the elements. Costs one isinst on the cold generic-consumption path.
    /// </summary>
    private void EmitDeoptIfNumericArray(ILGenerator il, EmittedRuntime runtime, Action loadValue)
    {
        var skip = il.DefineLabel();
        loadValue();
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, skip);
        loadValue();
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayEnsureBoxed);
        il.MarkLabel(skip);
    }

    /// <summary>Convenience: <see cref="EmitDeoptIfNumericArray"/> for a method argument by index.</summary>
    private void EmitDeoptArgIfNumericArray(ILGenerator il, EmittedRuntime runtime, int argIndex)
        => EmitDeoptIfNumericArray(il, runtime, () => il.Emit(OpCodes.Ldarg, checked((short)argIndex)));

    /// <summary>
    /// Emits the unboxed packed-double accessors on <c>$Array</c>:
    /// <c>GetDouble</c>/<c>SetDouble</c>/<c>PushDouble</c> (the fast paths the
    /// compiler will emit at statically-<c>number[]</c> sites) and
    /// <c>EnsureBoxed</c> (the deopt that materializes the <c>double[]</c> store
    /// back into the boxed base list). Numeric mode is dense-only for now: a
    /// gap-creating write (<c>index &gt; _numCount</c>) deopts via EnsureBoxed
    /// rather than punching a NaN-sentinel hole (that holey-fast upgrade is a
    /// later phase). Until creation/fast-path emission is wired these are
    /// emitted-but-uncalled — valid IL, zero behavior change.
    /// </summary>
    private void EmitTSArrayNumericAccessors(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var arrayResize = EmitGenerics.MakeGenericMethod(typeof(System.Array).GetMethod("Resize")!, _types.Double);

        // EnsureBoxed's builder was defined early (so base-list methods can guard
        // on it); we only emit its body here. The other accessors are defined now.
        var ensureBoxed = _tsArrayEnsureBoxedMethod;
        var canGetDouble = typeBuilder.DefineMethod("CanGetDouble",
            MethodAttributes.Public | MethodAttributes.HideBySig, _types.Boolean, [_types.Int32]);
        runtime.TSArrayCanGetDouble = canGetDouble;
        var getDouble = typeBuilder.DefineMethod("GetDouble",
            MethodAttributes.Public | MethodAttributes.HideBySig, _types.Double, [_types.Int32]);
        runtime.TSArrayGetDouble = getDouble;
        var setDouble = typeBuilder.DefineMethod("SetDouble",
            MethodAttributes.Public | MethodAttributes.HideBySig, _types.Void, [_types.Int32, _types.Double]);
        runtime.TSArraySetDouble = setDouble;
        var pushDouble = typeBuilder.DefineMethod("PushDouble",
            MethodAttributes.Public | MethodAttributes.HideBySig, _types.Void, [_types.Double]);
        runtime.TSArrayPushDouble = pushDouble;
        // #927 step 2: these are the per-element hot paths the compiler emits at statically-number[]
        // sites. They are non-virtual (HideBySig), so a `callvirt` on a $Array-typed receiver
        // devirtualizes; AggressiveInlining lets the JIT fold the field loads + bounds check into the
        // caller's loop (the $Array-wrapper equivalent of List<double>.Add's inlined fast path).
        canGetDouble.SetImplementationFlags(MethodImplAttributes.AggressiveInlining);
        getDouble.SetImplementationFlags(MethodImplAttributes.AggressiveInlining);
        setDouble.SetImplementationFlags(MethodImplAttributes.AggressiveInlining);
        pushDouble.SetImplementationFlags(MethodImplAttributes.AggressiveInlining);
        var markNumeric = typeBuilder.DefineMethod("MarkNumeric",
            MethodAttributes.Public | MethodAttributes.HideBySig, _types.Void, System.Type.EmptyTypes);
        runtime.TSArrayMarkNumeric = markNumeric;
        var markNonExtensible = typeBuilder.DefineMethod("MarkNonExtensible",
            MethodAttributes.Public | MethodAttributes.HideBySig, _types.Void, System.Type.EmptyTypes);
        runtime.TSArrayMarkNonExtensible = markNonExtensible;
        var isNumericGetter = typeBuilder.DefineMethod("get_IsNumeric",
            MethodAttributes.Assembly | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Boolean, Type.EmptyTypes);
        runtime.TSArrayIsNumericGetter = isNumericGetter;
        var numericCountGetter = typeBuilder.DefineMethod("get_NumericCount",
            MethodAttributes.Assembly | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Int32, Type.EmptyTypes);
        runtime.TSArrayNumericCountGetter = numericCountGetter;
        var canMutateNumericGetter = typeBuilder.DefineMethod("get_CanMutateNumeric",
            MethodAttributes.Assembly | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Boolean, Type.EmptyTypes);
        runtime.TSArrayCanMutateNumericGetter = canMutateNumericGetter;
        var shiftNumeric = typeBuilder.DefineMethod("ShiftNumeric",
            MethodAttributes.Assembly | MethodAttributes.HideBySig,
            _types.Object, Type.EmptyTypes);
        runtime.TSArrayShiftNumeric = shiftNumeric;
        var unshiftNumeric = typeBuilder.DefineMethod("UnshiftNumeric",
            MethodAttributes.Assembly | MethodAttributes.HideBySig,
            _types.Double, [_types.DoubleArray]);
        runtime.TSArrayUnshiftNumeric = unshiftNumeric;
        var cloneNumeric = typeBuilder.DefineMethod("CloneNumeric",
            MethodAttributes.Assembly | MethodAttributes.HideBySig,
            typeBuilder, Type.EmptyTypes);
        runtime.TSArrayCloneNumeric = cloneNumeric;
        var numericComparatorType = typeof(Func<double, double, double>);
        var sortNumeric = typeBuilder.DefineMethod("SortNumeric",
            MethodAttributes.Assembly | MethodAttributes.HideBySig,
            _types.Void, [numericComparatorType]);
        sortNumeric.SetImplementationFlags(MethodImplAttributes.AggressiveOptimization);
        runtime.TSArraySortNumeric = sortNumeric;

        // These representation probes are consumed only by guarded emitted-runtime
        // helpers. They expose no backing storage and do not force materialization.
        {
            var il = isNumericGetter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayIsNumericField);
            il.Emit(OpCodes.Ret);
        }
        {
            var il = numericCountGetter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Ret);
        }
        {
            var il = canMutateNumericGetter.GetILGenerator();
            var unavailable = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayIsNumericField);
            il.Emit(OpCodes.Brfalse, unavailable);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayIsFrozenField);
            il.Emit(OpCodes.Brtrue, unavailable);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayIsSealedField);
            il.Emit(OpCodes.Brtrue, unavailable);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNonExtensibleField);
            il.Emit(OpCodes.Brtrue, unavailable);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(unavailable);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
        }

        // Packed numeric shift/unshift kernels. Their callers require the exact
        // $Array type and CanMutateNumeric; all observable or deoptimized shapes
        // stay on ArrayShiftProto/ArrayUnshiftProto.
        {
            var il = shiftNumeric.GetILGenerator();
            var nonEmpty = il.DefineLabel();
            var skipCopy = il.DefineLabel();
            var first = il.DeclareLocal(_types.Double);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Brtrue, nonEmpty);
            il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(nonEmpty);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldelem_R8);
            il.Emit(OpCodes.Stloc, first);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ble, skipCopy);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Call, _types.ArrayCopy5);

            il.MarkLabel(skipCopy);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Stfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Stfld, _tsArrayLengthField);
            il.Emit(OpCodes.Ldloc, first);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ret);
        }
        {
            var il = unshiftNumeric.GetILGenerator();
            var nonEmptyItems = il.DefineLabel();
            var haveStore = il.DefineLabel();
            var capacityReady = il.DefineLabel();
            var required = il.DeclareLocal(_types.Int32);
            var newCapacity = il.DeclareLocal(_types.Int32);

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Brtrue, nonEmptyItems);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(nonEmptyItems);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, required);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Brtrue, haveStore);
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Ldloc, required);
            il.Emit(OpCodes.Call, _types.MathMaxInt32);
            il.Emit(OpCodes.Stloc, newCapacity);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, newCapacity);
            il.Emit(OpCodes.Newarr, _types.Double);
            il.Emit(OpCodes.Stfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Br, capacityReady);

            il.MarkLabel(haveStore);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldloc, required);
            il.Emit(OpCodes.Bge, capacityReady);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Ldloc, required);
            il.Emit(OpCodes.Call, _types.MathMaxInt32);
            il.Emit(OpCodes.Stloc, newCapacity);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldflda, _tsArrayNumStoreField);
            il.Emit(OpCodes.Ldloc, newCapacity);
            il.Emit(OpCodes.Call, arrayResize);

            il.MarkLabel(capacityReady);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Call, _types.ArrayCopy5);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Call, _types.ArrayCopy5);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, required);
            il.Emit(OpCodes.Stfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, required);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Stfld, _tsArrayLengthField);
            il.Emit(OpCodes.Ldloc, required);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Ret);
        }

        // CloneNumeric() preserves the packed-double representation for a fresh
        // zero-argument slice. The caller proves numeric mode plus ordinary dense
        // array observability before entering, so one exact-sized double[] is the
        // complete slice storage and no element is boxed.
        {
            var il = cloneNumeric.GetILGenerator();
            var result = il.DeclareLocal(typeBuilder);
            var index = il.DeclareLocal(_types.Int32);
            var loop = il.DefineLabel();
            var done = il.DefineLabel();

            il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
            il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
            il.Emit(OpCodes.Stloc, result);
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stfld, _tsArrayIsNumericField);
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Stfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Stfld, _tsArrayLengthField);
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Newarr, _types.Double);
            il.Emit(OpCodes.Stfld, _tsArrayNumStoreField);

            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, index);
            il.MarkLabel(loop);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Bge, done);
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldelem_R8);
            il.Emit(OpCodes.Stelem_R8);
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, index);
            il.Emit(OpCodes.Br, loop);
            il.MarkLabel(done);
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ret);
        }

        // Stable bottom-up merge sort over the packed store. The persistent
        // slice buffer is one side of the merge and a single double[] is the
        // scratch side; after the last pass the sorted buffer becomes the store.
        // The proven comparator has signature double(double,double), so neither
        // elements nor comparison results cross an object boundary. A bgt on the
        // result treats NaN like zero and takes the left run, preserving stability.
        {
            var il = sortNumeric.GetILGenerator();
            var n = il.DeclareLocal(_types.Int32);
            var src = il.DeclareLocal(_types.DoubleArray);
            var dst = il.DeclareLocal(_types.DoubleArray);
            var swap = il.DeclareLocal(_types.DoubleArray);
            var width = il.DeclareLocal(_types.Int32);
            var lo = il.DeclareLocal(_types.Int32);
            var mid = il.DeclareLocal(_types.Int32);
            var hi = il.DeclareLocal(_types.Int32);
            var i = il.DeclareLocal(_types.Int32);
            var j = il.DeclareLocal(_types.Int32);
            var k = il.DeclareLocal(_types.Int32);

            var done = il.DefineLabel();
            var widthCond = il.DefineLabel();
            var widthBody = il.DefineLabel();
            var loCond = il.DefineLabel();
            var loBody = il.DefineLabel();
            var loNext = il.DefineLabel();
            var midAdd = il.DefineLabel();
            var midDone = il.DefineLabel();
            var hiAdd = il.DefineLabel();
            var hiDone = il.DefineLabel();
            var widthDouble = il.DefineLabel();
            var mergeCond = il.DefineLabel();
            var mergeBody = il.DefineLabel();
            var takeLeft = il.DefineLabel();
            var takeRight = il.DefineLabel();
            var afterTake = il.DefineLabel();
            var drainLeftCond = il.DefineLabel();
            var drainLeftBody = il.DefineLabel();
            var drainRightCond = il.DefineLabel();
            var drainRightBody = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Stloc, n);
            il.Emit(OpCodes.Ldloc, n);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Blt, done);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Stloc, src);
            il.Emit(OpCodes.Ldloc, n);
            il.Emit(OpCodes.Newarr, _types.Double);
            il.Emit(OpCodes.Stloc, dst);

            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stloc, width);
            il.Emit(OpCodes.Br, widthCond);
            il.MarkLabel(widthBody);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, lo);
            il.Emit(OpCodes.Br, loCond);
            il.MarkLabel(loBody);

            il.Emit(OpCodes.Ldloc, width);
            il.Emit(OpCodes.Ldloc, n);
            il.Emit(OpCodes.Ldloc, lo);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Blt, midAdd);
            il.Emit(OpCodes.Ldloc, n);
            il.Emit(OpCodes.Stloc, mid);
            il.Emit(OpCodes.Br, midDone);
            il.MarkLabel(midAdd);
            il.Emit(OpCodes.Ldloc, lo);
            il.Emit(OpCodes.Ldloc, width);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, mid);
            il.MarkLabel(midDone);

            il.Emit(OpCodes.Ldloc, width);
            il.Emit(OpCodes.Ldloc, n);
            il.Emit(OpCodes.Ldloc, mid);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Blt, hiAdd);
            il.Emit(OpCodes.Ldloc, n);
            il.Emit(OpCodes.Stloc, hi);
            il.Emit(OpCodes.Br, hiDone);
            il.MarkLabel(hiAdd);
            il.Emit(OpCodes.Ldloc, mid);
            il.Emit(OpCodes.Ldloc, width);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, hi);
            il.MarkLabel(hiDone);

            il.Emit(OpCodes.Ldloc, lo);
            il.Emit(OpCodes.Stloc, i);
            il.Emit(OpCodes.Ldloc, mid);
            il.Emit(OpCodes.Stloc, j);
            il.Emit(OpCodes.Ldloc, lo);
            il.Emit(OpCodes.Stloc, k);
            il.Emit(OpCodes.Br, mergeCond);

            il.MarkLabel(mergeBody);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, src);
            il.Emit(OpCodes.Ldloc, i);
            il.Emit(OpCodes.Ldelem_R8);
            il.Emit(OpCodes.Ldloc, src);
            il.Emit(OpCodes.Ldloc, j);
            il.Emit(OpCodes.Ldelem_R8);
            il.Emit(OpCodes.Callvirt, numericComparatorType.GetMethod("Invoke")!);
            il.Emit(OpCodes.Ldc_R8, 0.0);
            il.Emit(OpCodes.Bgt, takeRight);

            il.MarkLabel(takeLeft);
            il.Emit(OpCodes.Ldloc, dst);
            il.Emit(OpCodes.Ldloc, k);
            il.Emit(OpCodes.Ldloc, src);
            il.Emit(OpCodes.Ldloc, i);
            il.Emit(OpCodes.Ldelem_R8);
            il.Emit(OpCodes.Stelem_R8);
            il.Emit(OpCodes.Ldloc, i);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, i);
            il.Emit(OpCodes.Br, afterTake);

            il.MarkLabel(takeRight);
            il.Emit(OpCodes.Ldloc, dst);
            il.Emit(OpCodes.Ldloc, k);
            il.Emit(OpCodes.Ldloc, src);
            il.Emit(OpCodes.Ldloc, j);
            il.Emit(OpCodes.Ldelem_R8);
            il.Emit(OpCodes.Stelem_R8);
            il.Emit(OpCodes.Ldloc, j);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, j);

            il.MarkLabel(afterTake);
            il.Emit(OpCodes.Ldloc, k);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, k);
            il.MarkLabel(mergeCond);
            il.Emit(OpCodes.Ldloc, i);
            il.Emit(OpCodes.Ldloc, mid);
            il.Emit(OpCodes.Bge, drainLeftCond);
            il.Emit(OpCodes.Ldloc, j);
            il.Emit(OpCodes.Ldloc, hi);
            il.Emit(OpCodes.Blt, mergeBody);

            il.MarkLabel(drainLeftCond);
            il.Emit(OpCodes.Ldloc, i);
            il.Emit(OpCodes.Ldloc, mid);
            il.Emit(OpCodes.Bge, drainRightCond);
            il.MarkLabel(drainLeftBody);
            il.Emit(OpCodes.Ldloc, dst);
            il.Emit(OpCodes.Ldloc, k);
            il.Emit(OpCodes.Ldloc, src);
            il.Emit(OpCodes.Ldloc, i);
            il.Emit(OpCodes.Ldelem_R8);
            il.Emit(OpCodes.Stelem_R8);
            il.Emit(OpCodes.Ldloc, i);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, i);
            il.Emit(OpCodes.Ldloc, k);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, k);
            il.Emit(OpCodes.Ldloc, i);
            il.Emit(OpCodes.Ldloc, mid);
            il.Emit(OpCodes.Blt, drainLeftBody);

            il.MarkLabel(drainRightCond);
            il.Emit(OpCodes.Ldloc, j);
            il.Emit(OpCodes.Ldloc, hi);
            il.Emit(OpCodes.Bge, loNext);
            il.MarkLabel(drainRightBody);
            il.Emit(OpCodes.Ldloc, dst);
            il.Emit(OpCodes.Ldloc, k);
            il.Emit(OpCodes.Ldloc, src);
            il.Emit(OpCodes.Ldloc, j);
            il.Emit(OpCodes.Ldelem_R8);
            il.Emit(OpCodes.Stelem_R8);
            il.Emit(OpCodes.Ldloc, j);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, j);
            il.Emit(OpCodes.Ldloc, k);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, k);
            il.Emit(OpCodes.Ldloc, j);
            il.Emit(OpCodes.Ldloc, hi);
            il.Emit(OpCodes.Blt, drainRightBody);

            il.MarkLabel(loNext);
            il.Emit(OpCodes.Ldloc, hi);
            il.Emit(OpCodes.Stloc, lo);
            il.MarkLabel(loCond);
            il.Emit(OpCodes.Ldloc, lo);
            il.Emit(OpCodes.Ldloc, n);
            il.Emit(OpCodes.Blt, loBody);

            il.Emit(OpCodes.Ldloc, src);
            il.Emit(OpCodes.Stloc, swap);
            il.Emit(OpCodes.Ldloc, dst);
            il.Emit(OpCodes.Stloc, src);
            il.Emit(OpCodes.Ldloc, swap);
            il.Emit(OpCodes.Stloc, dst);
            il.Emit(OpCodes.Ldloc, width);
            il.Emit(OpCodes.Ldc_I4, int.MaxValue / 2);
            il.Emit(OpCodes.Ble, widthDouble);
            il.Emit(OpCodes.Ldc_I4, int.MaxValue);
            il.Emit(OpCodes.Stloc, width);
            il.Emit(OpCodes.Br, widthCond);
            il.MarkLabel(widthDouble);
            il.Emit(OpCodes.Ldloc, width);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Stloc, width);
            il.MarkLabel(widthCond);
            il.Emit(OpCodes.Ldloc, width);
            il.Emit(OpCodes.Ldloc, n);
            il.Emit(OpCodes.Blt, widthBody);

            il.Emit(OpCodes.Ldloc, src);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Beq, done);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, src);
            il.Emit(OpCodes.Stfld, _tsArrayNumStoreField);
            il.MarkLabel(done);
            il.Emit(OpCodes.Ret);
        }

        // ── void EnsureBoxed() ── numeric -> boxed: box each _numStore[i] into
        // the (empty) base list, then clear the numeric mode.
        {
            var il = ensureBoxed.GetILGenerator();
            var done = il.DefineLabel();
            var loop = il.DefineLabel();
            var loopCheck = il.DefineLabel();
            var i = il.DeclareLocal(_types.Int32);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayIsNumericField);
            il.Emit(OpCodes.Brfalse, done);

            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, i);
            il.Emit(OpCodes.Br, loopCheck);
            il.MarkLabel(loop);
            il.Emit(OpCodes.Ldarg_0);                       // Add receiver (this : List)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Ldloc, i);
            il.Emit(OpCodes.Ldelem_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Callvirt, _tsArrayListAdd!);
            il.Emit(OpCodes.Ldloc, i);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, i);
            il.MarkLabel(loopCheck);
            il.Emit(OpCodes.Ldloc, i);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Blt, loop);

            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stfld, _tsArrayIsNumericField);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stfld, _tsArrayNumCountField);

            il.MarkLabel(done);
            il.Emit(OpCodes.Ret);
        }

        // ── bool CanGetDouble(int index) ── numeric-mode and dense in-range guard.
        // The compiler calls this before GetDouble so boxed arrays, holes, and OOB
        // reads retain the ordinary property lookup path.
        {
            var il = canGetDouble.GetILGenerator();
            var unavailable = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayIsNumericField);
            il.Emit(OpCodes.Brfalse, unavailable);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Clt_Un); // unsigned comparison rejects negative indices too
            il.Emit(OpCodes.Ret);
            il.MarkLabel(unavailable);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
        }

        // ── double GetDouble(int index) ──
        {
            var il = getDouble.GetILGenerator();
            var boxed = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayIsNumericField);
            il.Emit(OpCodes.Brfalse, boxed);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldelem_R8);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(boxed);                            // (double) base[index]
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, _tsArrayListGetItem!);
            il.Emit(OpCodes.Unbox_Any, _types.Double);
            il.Emit(OpCodes.Ret);
        }

        // ── void SetDouble(int index, double value) ──
        {
            var il = setDouble.GetILGenerator();
            var notNumeric = il.DefineLabel();
            var notOverwrite = il.DefineLabel();
            var gap = il.DefineLabel();
            var hasStore = il.DefineLabel();
            var doStore = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayIsNumericField);
            il.Emit(OpCodes.Brfalse, notNumeric);

            // overwrite in range: if (index < _numCount) { _numStore[index] = value; return; }
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Bge, notOverwrite);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stelem_R8);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(notOverwrite);
            // gap write (index > _numCount): deopt then boxed set
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Bgt, gap);

            // append at _numCount: ensure capacity
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Brtrue, hasStore);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Newarr, _types.Double);
            il.Emit(OpCodes.Stfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Br, doStore);
            il.MarkLabel(hasStore);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Bne_Un, doStore);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldflda, _tsArrayNumStoreField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Call, arrayResize);

            il.MarkLabel(doStore);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stelem_R8);
            il.Emit(OpCodes.Ldarg_0);                       // _numCount++
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Ldarg_0);                       // _length = (long)_numCount
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Stfld, _tsArrayLengthField);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(gap);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, ensureBoxed);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Call, runtime.TSArraySetLong);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(notNumeric);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Call, runtime.TSArraySetLong);
            il.Emit(OpCodes.Ret);
        }

        // ── void PushDouble(double value) ── append at the current length. (#927 step 2: dedicated
        // numeric-append fast path. Previously delegated to SetDouble, which re-checked _isNumeric and
        // ran two dead index branches (index<_numCount, index>_numCount) on every push. The numeric
        // branch is now straight-line ensure-capacity + store + _numCount++/_length=, mirroring
        // List<double>.Add, so AggressiveInlining can fold it into the caller's push loop.)
        {
            var il = pushDouble.GetILGenerator();
            var boxedPath = il.DefineLabel();
            var hasStore = il.DefineLabel();
            var doStore = il.DefineLabel();
            var blockedReturn = il.DefineLabel();

            // Non-extensible (frozen / sealed / preventExtensions) → push is a no-op, matching the boxed
            // ArrayPush (which consults the external _frozenObjects/_sealedObjects/_nonExtensibleObjects
            // collections this unboxed path can't reach). _isFrozen is set by $Array.Freeze();
            // _isNonExtensible by Object.seal / Object.preventExtensions via MarkNonExtensible.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayIsFrozenField);
            il.Emit(OpCodes.Brtrue, blockedReturn);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNonExtensibleField);
            il.Emit(OpCodes.Brtrue, blockedReturn);

            // Boxed mode → delegate to SetDouble at the base-list count (rare; non-numeric $Array).
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayIsNumericField);
            il.Emit(OpCodes.Brfalse, boxedPath);

            // Numeric append: ensure capacity (null → new double[4]; full → Array.Resize ×2).
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Brtrue, hasStore);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_4);
            il.Emit(OpCodes.Newarr, _types.Double);
            il.Emit(OpCodes.Stfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Br, doStore);
            il.MarkLabel(hasStore);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Bne_Un, doStore);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldflda, _tsArrayNumStoreField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Call, arrayResize);

            il.MarkLabel(doStore);
            il.Emit(OpCodes.Ldarg_0);                       // _numStore[_numCount] = value
            il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stelem_R8);
            il.Emit(OpCodes.Ldarg_0);                       // _numCount++
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Ldarg_0);                       // _length = (long)_numCount
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Stfld, _tsArrayLengthField);
            il.Emit(OpCodes.Ret);

            // Boxed append: index = base-list Count, then SetDouble (boxed delegate path).
            il.MarkLabel(boxedPath);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _tsArrayListCountGetter!);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, setDouble);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(blockedReturn);                    // non-extensible: leave length unchanged
            il.Emit(OpCodes.Ret);
        }

        // ── void MarkNumeric() ── flip an EMPTY dense array into numeric mode.
        // The compiler emits this at statically-number[] creation sites (e.g.
        // `const a: number[] = []`) so escaping number[] arrays start unboxed.
        // Guarded so it is a safe no-op on anything that isn't an empty dense
        // array (already numeric, sparse, or holding elements) — those stay
        // boxed and correct (non-empty element-move is a later phase). The
        // invariant "numeric ⟹ base list empty" therefore holds by construction.
        {
            var il = markNumeric.GetILGenerator();
            var done = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);                       // if (_isNumeric) return;
            il.Emit(OpCodes.Ldfld, _tsArrayIsNumericField);
            il.Emit(OpCodes.Brtrue, done);
            il.Emit(OpCodes.Ldarg_0);                       // if (_sparse != null) return;
            il.Emit(OpCodes.Ldfld, _tsArraySparseField);
            il.Emit(OpCodes.Brtrue, done);
            il.Emit(OpCodes.Ldarg_0);                       // if (this.Count != 0) return;
            il.Emit(OpCodes.Callvirt, _tsArrayListCountGetter!);
            il.Emit(OpCodes.Brtrue, done);
            il.Emit(OpCodes.Ldarg_0);                       // _isNumeric = true;
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stfld, _tsArrayIsNumericField);
            il.MarkLabel(done);
            il.Emit(OpCodes.Ret);
        }

        // ── void MarkNonExtensible() ── set _isNonExtensible so the unboxed PushDouble fast path refuses
        // to append; called by Object.seal / Object.preventExtensions on a $Array (Object.freeze already
        // sets _isFrozen via $Array.Freeze()). No deopt — the array may stay numeric and simply stop growing.
        {
            var il = markNonExtensible.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stfld, _tsArrayNonExtensibleField);
            il.Emit(OpCodes.Ret);
        }
    }

    private void EmitTSArrayConstructor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitTSArrayElementsConstructor(typeBuilder, runtime, _types.ListOfObject, false);
        EmitTSArrayElementsConstructor(typeBuilder, runtime, _types.IEnumerableOfObject, true);
    }

    private void EmitTSArrayElementsConstructor(
        TypeBuilder typeBuilder, EmittedRuntime runtime, Type inputType, bool fromElements)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [inputType]
        );
        if (fromElements) runtime.TSArrayCtorFromElements = ctor;
        else runtime.TSArrayCtor = ctor;

        var il = ctor.GetILGenerator();

        // Copy into our own dense storage. The IEnumerable overload accepts an
        // object[] directly without constructing an intermediate list/backing array.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetConstructor(_types.ListOfObject, _types.IEnumerableOfObject));

        // _length = (long)this.Count  (read AFTER base ctor has populated it).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _tsArrayListCountGetter!);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Stfld, _tsArrayLengthField);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// <c>public $Array(object?[] ctorArgs)</c> — ECMA-262 §23.1.1.1 Array
    /// constructor semantics applied to a fresh instance, for guest classes
    /// extending Array (#233): their emitted constructors chain here (both the
    /// implicit-constructor path and explicit <c>super(...)</c> calls). A
    /// single numeric argument sets the length (holes); any other shape
    /// appends the arguments as elements.
    /// </summary>
    private void EmitTSArraySubclassConstructor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.ObjectArray]
        );
        runtime.TSArrayCtorFromCtorArgs = ctor;

        var il = ctor.GetILGenerator();
        var elementsLabel = il.DefineLabel();
        var loopStart = il.DefineLabel();
        var loopCheck = il.DefineLabel();
        var doneLabel = il.DefineLabel();
        var iLocal = il.DeclareLocal(_types.Int32);

        // base() — empty List<object?>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.ListOfObject));
        // _length starts at 0 (field default)

        // if (ctorArgs.Length == 1 && ctorArgs[0] is double) { SetLength((long)d); return; }
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bne_Un, elementsLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, elementsLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Call, runtime.TSArraySetLength);
        il.Emit(OpCodes.Br, doneLabel);

        // else: append each ctor arg as an element, then sync _length.
        il.MarkLabel(elementsLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopCheck);
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, _tsArrayListAdd!);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(loopCheck);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Blt, loopStart);

        // _length = (long)Count
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _tsArrayListCountGetter!);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Stfld, _tsArrayLengthField);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSArrayElementsProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var prop = typeBuilder.DefineProperty("Elements", PropertyAttributes.None, _types.ListOfObject, null);
        var getter = typeBuilder.DefineMethod(
            "get_Elements",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.ListOfObject, Type.EmptyTypes);
        runtime.TSArrayElementsGetter = getter;

        var il = getter.GetILGenerator();
        // Elements hands out the inherited List<object?> directly; a numeric-mode
        // array's base list is empty, so materialize first.
        EmitTSArrayDeoptGuard(il);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }

    private void EmitTSArrayLongLengthProperty(TypeBuilder typeBuilder, EmittedRuntime runtime, MethodBuilder syncLength)
    {
        var prop = typeBuilder.DefineProperty("LongLength", PropertyAttributes.None, _types.Int64, null);
        var getter = typeBuilder.DefineMethod(
            "get_LongLength",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Int64, Type.EmptyTypes);
        runtime.TSArrayLongLengthGetter = getter;

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, syncLength);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayLengthField);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }

    private void EmitTSArrayLengthProperty(TypeBuilder typeBuilder, EmittedRuntime runtime, MethodBuilder syncLength)
    {
        var prop = typeBuilder.DefineProperty("Length", PropertyAttributes.None, _types.Int32, null);
        var getter = typeBuilder.DefineMethod(
            "get_Length",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Int32, Type.EmptyTypes);
        runtime.TSArrayLengthGetter = getter;

        var il = getter.GetILGenerator();
        var clampLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, syncLength);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayLengthField);
        il.Emit(OpCodes.Ldc_I8, (long)int.MaxValue);
        il.Emit(OpCodes.Bgt, clampLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayLengthField);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(clampLabel);
        il.Emit(OpCodes.Ldc_I4, int.MaxValue);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }

    private void EmitTSArrayIsFrozenProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var prop = typeBuilder.DefineProperty("IsFrozen", PropertyAttributes.None, _types.Boolean, null);
        var getter = typeBuilder.DefineMethod(
            "get_IsFrozen",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Boolean, Type.EmptyTypes);
        _ = getter;

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayIsFrozenField);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }

    private void EmitTSArrayIsSealedProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var prop = typeBuilder.DefineProperty("IsSealed", PropertyAttributes.None, _types.Boolean, null);
        var getter = typeBuilder.DefineMethod(
            "get_IsSealed",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.Boolean, Type.EmptyTypes);
        _ = getter;

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayIsSealedField);
        il.Emit(OpCodes.Ret);

        prop.SetGetMethod(getter);
    }

    private void EmitTSArrayFreeze(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("Freeze", MethodAttributes.Public, _types.Void, Type.EmptyTypes);
        runtime.TSArrayFreeze = method;

        var il = method.GetILGenerator();
        EmitTSArrayDeoptGuard(il);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsArrayIsFrozenField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsArrayIsSealedField);

        il.Emit(OpCodes.Ret);
    }

    private void EmitTSArraySeal(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("Seal", MethodAttributes.Public, _types.Void, Type.EmptyTypes);
        _ = method;

        var il = method.GetILGenerator();
        EmitTSArrayDeoptGuard(il);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, _tsArrayIsSealedField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSArrayGet(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("Get", MethodAttributes.Public, _types.Object, [_types.Int32]);
        runtime.TSArrayGet = method;

        var il = method.GetILGenerator();
        EmitTSArrayDeoptGuard(il);
        var throwLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, throwLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _tsArrayListCountGetter!);
        il.Emit(OpCodes.Bge, throwLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _tsArrayListGetItem!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "Index out of bounds.");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, [_types.String])!);
        il.Emit(OpCodes.Throw);
    }

    private void EmitTSArraySet(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("Set", MethodAttributes.Public, _types.Void, [_types.Int32, _types.Object]);
        runtime.TSArraySet = method;

        var il = method.GetILGenerator();
        EmitTSArrayDeoptGuard(il);
        var frozenLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayIsFrozenField);
        il.Emit(OpCodes.Brtrue, frozenLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, throwLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _tsArrayListCountGetter!);
        il.Emit(OpCodes.Bge, throwLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _tsArrayListSetItem!);

        il.MarkLabel(frozenLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "Index out of bounds.");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, [_types.String])!);
        il.Emit(OpCodes.Throw);
    }
    // -----------------------------------------------------------------------
    // Long-indexed / sparse-aware API — mirrors SharpTSArray semantics.
    // -----------------------------------------------------------------------

    /// <summary>
    /// <c>private void SyncLength()</c> — reconciles <c>_length</c> with the
    /// inherited <c>List&lt;T&gt;.Count</c> whenever the array is purely dense.
    /// Needed because external IL emitters push/pop via base-class List methods
    /// (Add, Insert, RemoveAt), which don't touch our <c>_length</c> field.
    /// Every public entry point calls this first so reads see a consistent
    /// view.
    /// </summary>
    private MethodBuilder EmitTSArraySyncLength(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("SyncLength", MethodAttributes.Private, _types.Void, Type.EmptyTypes);
        var il = method.GetILGenerator();
        var doneLabel = il.DefineLabel();

        // Numeric-aware (number[] unboxing): in numeric mode _length is the
        // authoritative length (maintained by SetDouble/PushDouble) and the
        // inherited base list is empty, so there is nothing to reconcile.
        // Critically we must NOT deopt here — doing so would materialize the
        // unboxed store on every `arr.length` read, defeating the win. Callers
        // that genuinely touch the base list (GetLong/SetLong/HasIndex/GetRaw/
        // SetLength/DeleteAt) carry their own EmitTSArrayDeoptGuard, which fires
        // before they call SyncLength; by then mode is boxed and the reconcile
        // below runs normally. The only callers reaching here still-numeric are
        // the Length / LongLength getters, which read the authoritative _length.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayIsNumericField);
        il.Emit(OpCodes.Brtrue, doneLabel);

        // if (_sparse != null) return;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArraySparseField);
        il.Emit(OpCodes.Brtrue, doneLabel);

        // _length = (long)base.Count;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _tsArrayListCountGetter!);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Stfld, _tsArrayLengthField);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// <c>private void MaterializeDense()</c> — flattens the sparse tail into
    /// <c>_dense</c>. Throws RangeError if length > int.MaxValue.
    /// </summary>
    private MethodBuilder EmitTSArrayMaterializeDense(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("MaterializeDense", MethodAttributes.Private, _types.Void, Type.EmptyTypes);

        var il = method.GetILGenerator();
        EmitTSArrayDeoptGuard(il);
        var sparseBranch = il.DefineLabel();
        var throwRangeLabel = il.DefineLabel();
        var loopHead = il.DefineLabel();
        var loopExit = il.DefineLabel();
        var padHoleLabel = il.DefineLabel();
        var afterAddLabel = il.DefineLabel();

        // if (_sparse == null) return;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArraySparseField);
        il.Emit(OpCodes.Brtrue, sparseBranch);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(sparseBranch);

        // if (_length > int.MaxValue) throw RangeError
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayLengthField);
        il.Emit(OpCodes.Ldc_I8, (long)int.MaxValue);
        il.Emit(OpCodes.Bgt, throwRangeLabel);

        var idxLocal = il.DeclareLocal(_types.Int32);
        var sparseValueLocal = il.DeclareLocal(_types.Object);

        il.MarkLabel(loopHead);
        // while (_dense.Count < _length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _tsArrayListCountGetter!);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayLengthField);
        il.Emit(OpCodes.Bge, loopExit);

        // int i = _dense.Count;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _tsArrayListCountGetter!);
        il.Emit(OpCodes.Stloc, idxLocal);

        // if (_sparse.TryGetValue((uint)i, out v)) _dense.Add(v); else _dense.Add($ArrayHole.Instance);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArraySparseField);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldloca, sparseValueLocal);
        il.Emit(OpCodes.Callvirt, _tsArraySparseTryGetValue!);
        il.Emit(OpCodes.Brfalse, padHoleLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, sparseValueLocal);
        il.Emit(OpCodes.Callvirt, _tsArrayListAdd!);
        il.Emit(OpCodes.Br, afterAddLabel);

        il.MarkLabel(padHoleLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.ArrayHoleInstance);
        il.Emit(OpCodes.Callvirt, _tsArrayListAdd!);

        il.MarkLabel(afterAddLabel);
        il.Emit(OpCodes.Br, loopHead);

        il.MarkLabel(loopExit);

        // _sparse = null;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, _tsArraySparseField);
        il.Emit(OpCodes.Ret);

        EmitInlineThrowError(il, runtime, "Array operation requires materializing a sparse array whose length exceeds int.MaxValue.", runtime.TSRangeErrorCtor, throwRangeLabel);

        return method;
    }

    /// <summary>
    /// <c>private void TryCollapseSparse()</c> — drops the sparse dict when
    /// empty OR fully covered by the dense prefix.
    /// </summary>
    private MethodBuilder EmitTSArrayTryCollapseSparse(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("TryCollapseSparse", MethodAttributes.Private, _types.Void, Type.EmptyTypes);

        var il = method.GetILGenerator();
        EmitTSArrayDeoptGuard(il);
        var collapseLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArraySparseField);
        il.Emit(OpCodes.Brfalse, doneLabel);

        // if (_sparse.Count == 0) collapse
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArraySparseField);
        il.Emit(OpCodes.Callvirt, _tsArraySparseCountGetter!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Beq, collapseLabel);

        // else if (_length <= (long)_dense.Count) collapse
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayLengthField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _tsArrayListCountGetter!);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Bgt, doneLabel);

        il.MarkLabel(collapseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, _tsArraySparseField);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// <c>private object? GetCore(long index)</c> — raw read, returns
    /// <c>$ArrayHole.Instance</c> for holes. Callers ensure index is in range
    /// (<c>0 &lt;= index &lt; _length</c>).
    /// </summary>
    private MethodBuilder EmitTSArrayGetCore(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("GetCore", MethodAttributes.Private, _types.Object, [_types.Int64]);

        var il = method.GetILGenerator();
        EmitTSArrayDeoptGuard(il);
        var sparsePathLabel = il.DefineLabel();
        var returnHoleLabel = il.DefineLabel();
        var lookupLabel = il.DefineLabel();

        // if (_sparse != null && index >= _dense.Count) goto sparsePath; else goto lookup;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArraySparseField);
        il.Emit(OpCodes.Brfalse, lookupLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _tsArrayListCountGetter!);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Bge, sparsePathLabel);

        il.MarkLabel(lookupLabel);
        // return _dense[(int)index];
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, _tsArrayListGetItem!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(sparsePathLabel);
        // if (index > uint.MaxValue) return $ArrayHole.Instance;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I8, (long)uint.MaxValue);
        il.Emit(OpCodes.Bgt, returnHoleLabel);

        var vLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArraySparseField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_U4);
        il.Emit(OpCodes.Ldloca, vLocal);
        il.Emit(OpCodes.Callvirt, _tsArraySparseTryGetValue!);
        il.Emit(OpCodes.Brfalse, returnHoleLabel);
        il.Emit(OpCodes.Ldloc, vLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnHoleLabel);
        il.Emit(OpCodes.Ldsfld, runtime.ArrayHoleInstance);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// <c>private void SetCore(long index, object? value)</c> — in-place write,
    /// does NOT extend length. Caller ensures index is in a writable slot.
    /// </summary>
    private MethodBuilder EmitTSArraySetCore(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod("SetCore", MethodAttributes.Private, _types.Void,
            [_types.Int64, _types.Object]);

        var il = method.GetILGenerator();
        EmitTSArrayDeoptGuard(il);
        var sparseLabel = il.DefineLabel();
        var denseLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArraySparseField);
        il.Emit(OpCodes.Brfalse, denseLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _tsArrayListCountGetter!);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Bge, sparseLabel);

        il.MarkLabel(denseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _tsArrayListSetItem!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(sparseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArraySparseField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_U4);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _tsArraySparseSetItem!);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// <c>private void SetCoreWithExtend(long index, object? value)</c> —
    /// storage-aware writer: dense fast path, sparse transition past
    /// <see cref="SharpTS.Runtime.Types.SharpTSArray"/>'s SparseThreshold,
    /// and writes within an already-sparse array.
    /// </summary>
    private MethodBuilder EmitTSArraySetCoreWithExtend(
        TypeBuilder typeBuilder, EmittedRuntime runtime, MethodBuilder setCore)
    {
        var method = typeBuilder.DefineMethod("SetCoreWithExtend", MethodAttributes.Private, _types.Void,
            [_types.Int64, _types.Object]);

        var il = method.GetILGenerator();
        EmitTSArrayDeoptGuard(il);
        var denseEntryLabel = il.DefineLabel();
        var sparseWriteReturn = il.DefineLabel();
        var skipLenUpdate = il.DefineLabel();
        var padDenseLabel = il.DefineLabel();
        var transitionSparseLabel = il.DefineLabel();
        var padLoopHead = il.DefineLabel();
        var padLoopExit = il.DefineLabel();

        // if (_sparse != null) { SetCore(index, value); if (index >= _length) _length = index + 1; return; }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArraySparseField);
        il.Emit(OpCodes.Brfalse, denseEntryLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, setCore);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayLengthField);
        il.Emit(OpCodes.Blt, sparseWriteReturn);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stfld, _tsArrayLengthField);

        il.MarkLabel(sparseWriteReturn);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(denseEntryLabel);

        // Pure-dense path.
        // if (index < _length) { _dense[(int)index] = value; return; }
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayLengthField);
        il.Emit(OpCodes.Bge, padDenseLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _tsArrayListSetItem!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(padDenseLabel);

        // long growth = index + 1 - _length;
        // if (growth <= SparseThreshold && index + 1 <= int.MaxValue) { pad dense; return; }
        // else transition sparse.
        var growthLocal = il.DeclareLocal(_types.Int64);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayLengthField);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, growthLocal);

        // growth > SparseThreshold → sparse
        il.Emit(OpCodes.Ldloc, growthLocal);
        il.Emit(OpCodes.Ldc_I8, (long)TSArraySparseThreshold);
        il.Emit(OpCodes.Bgt, transitionSparseLabel);

        // index + 1 > int.MaxValue → sparse
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I8, (long)int.MaxValue);
        il.Emit(OpCodes.Bgt, transitionSparseLabel);

        // Pad with $ArrayHole.Instance, then write value at index.
        // while (_dense.Count <= (int)index) _dense.Add($ArrayHole.Instance);
        il.MarkLabel(padLoopHead);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _tsArrayListCountGetter!);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Bgt, padLoopExit);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.ArrayHoleInstance);
        il.Emit(OpCodes.Callvirt, _tsArrayListAdd!);
        il.Emit(OpCodes.Br, padLoopHead);

        il.MarkLabel(padLoopExit);

        // _dense[(int)index] = value;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _tsArrayListSetItem!);

        // _length = (long)_dense.Count;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _tsArrayListCountGetter!);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Stfld, _tsArrayLengthField);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(transitionSparseLabel);
        // _sparse = new Dictionary<uint,object?> { [(uint)index] = value };
        // _length = index + 1;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_tsArraySparseType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, _tsArraySparseField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArraySparseField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_U4);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _tsArraySparseSetItem!);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stfld, _tsArrayLengthField);
        il.Emit(OpCodes.Ret);

        _ = skipLenUpdate;
        return method;
    }

    /// <summary>
    /// <c>public bool HasIndex(long index)</c> — ECMA-262 HasProperty for
    /// numeric indices. False for holes and out-of-range indices.
    /// </summary>
    private void EmitTSArrayHasIndex(TypeBuilder typeBuilder, EmittedRuntime runtime, MethodBuilder syncLength)
    {
        var method = typeBuilder.DefineMethod("HasIndex", MethodAttributes.Public, _types.Boolean, [_types.Int64]);
        runtime.TSArrayHasIndex = method;

        var il = method.GetILGenerator();
        EmitTSArrayDeoptGuard(il);
        var returnFalseLabel = il.DefineLabel();
        var sparseBranchLabel = il.DefineLabel();
        var checkDenseHoleLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, syncLength);

        // if ((ulong)index >= (ulong)_length) return false;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Blt, returnFalseLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayLengthField);
        il.Emit(OpCodes.Bge, returnFalseLabel);

        // if (_sparse == null || index < _dense.Count) -> check dense hole
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArraySparseField);
        il.Emit(OpCodes.Brfalse, checkDenseHoleLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _tsArrayListCountGetter!);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Bge, sparseBranchLabel);

        il.MarkLabel(checkDenseHoleLabel);
        // return !(_dense[(int)index] is $ArrayHole)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Callvirt, _tsArrayListGetItem!);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ceq);   // 1 if result was null → not a hole → true
        il.Emit(OpCodes.Ret);

        il.MarkLabel(sparseBranchLabel);
        // if (index > uint.MaxValue) return false;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I8, (long)uint.MaxValue);
        il.Emit(OpCodes.Bgt, returnFalseLabel);

        // return _sparse.ContainsKey((uint)index);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArraySparseField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_U4);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_tsArraySparseType, "ContainsKey", [_types.UInt32])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// <c>public object? GetRaw(long index)</c> — returns <c>$ArrayHole.Instance</c>
    /// for holes (in-range but not written); <c>$Undefined.Instance</c> for OOB.
    /// Built-ins that distinguish holes from explicit undefined use this.
    /// </summary>
    private void EmitTSArrayGetRaw(TypeBuilder typeBuilder, EmittedRuntime runtime, MethodBuilder getCore, MethodBuilder syncLength)
    {
        var method = typeBuilder.DefineMethod("GetRaw", MethodAttributes.Public, _types.Object, [_types.Int64]);
        _ = method;

        var il = method.GetILGenerator();
        EmitTSArrayDeoptGuard(il);
        var oobLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, syncLength);

        // if ((ulong)index >= (ulong)_length) return $Undefined.Instance;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Blt, oobLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayLengthField);
        il.Emit(OpCodes.Bge, oobLabel);

        // return GetCore(index);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, getCore);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(oobLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// <c>public object? Get(long index)</c> — JS-semantic read: OOB and holes
    /// both return <c>$Undefined.Instance</c>.
    /// </summary>
    private void EmitTSArrayGetLong(TypeBuilder typeBuilder, EmittedRuntime runtime, MethodBuilder getCore, MethodBuilder syncLength)
    {
        var method = typeBuilder.DefineMethod("Get", MethodAttributes.Public, _types.Object, [_types.Int64]);
        runtime.TSArrayGetLong = method;

        var il = method.GetILGenerator();
        var oobLabel = il.DefineLabel();
        var notHoleLabel = il.DefineLabel();
        var notNumeric = il.DefineLabel();

        // Numeric-aware read (number[] unboxing): in numeric mode the elements live unboxed in _numStore;
        // read directly + box the double, OOB → undefined, and crucially do NOT deopt — so a numeric array
        // stays numeric across index reads, keeping interleaved read/write on the fast path. _numCount is
        // authoritative; the unsigned compare also rejects negative indices. (Numeric mode is dense, so no
        // hole check is needed here — gap writes deopt before they can punch a hole.)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayIsNumericField);
        il.Emit(OpCodes.Brfalse, notNumeric);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayNumCountField);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Bge_Un, oobLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayNumStoreField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldelem_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notNumeric);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, syncLength);

        // if ((ulong)index >= (ulong)_length) return $Undefined.Instance;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Blt, oobLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayLengthField);
        il.Emit(OpCodes.Bge, oobLabel);

        // var v = GetCore(index); if (v is $ArrayHole) return $Undefined.Instance; return v;
        var vLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, getCore);
        il.Emit(OpCodes.Stloc, vLocal);

        il.Emit(OpCodes.Ldloc, vLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brfalse, notHoleLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notHoleLabel);
        il.Emit(OpCodes.Ldloc, vLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(oobLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// <c>public void Set(long index, object? value)</c> — JS-semantic write.
    /// Extends the array; intermediate positions become holes. Transitions to
    /// sparse past SparseThreshold. Throws RangeError for negative indices and
    /// indices beyond the ECMA-262 uint32 maximum.
    /// </summary>
    private void EmitTSArraySetLong(TypeBuilder typeBuilder, EmittedRuntime runtime, MethodBuilder setCoreWithExtend, MethodBuilder syncLength)
    {
        var method = typeBuilder.DefineMethod("Set", MethodAttributes.Public, _types.Void,
            [_types.Int64, _types.Object]);
        runtime.TSArraySetLong = method;

        var il = method.GetILGenerator();
        EmitTSArrayDeoptGuard(il);
        var frozenReturnLabel = il.DefineLabel();
        var negThrowLabel = il.DefineLabel();
        var maxThrowLabel = il.DefineLabel();
        var notExtensibleReturnLabel = il.DefineLabel();
        var indexInRangeLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, syncLength);

        // if (_isFrozen) return;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayIsFrozenField);
        il.Emit(OpCodes.Brtrue, frozenReturnLabel);

        // if (index < 0) throw RangeError;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Blt, negThrowLabel);

        // if (index > MaxWriteIndex) throw RangeError;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I8, TSArrayMaxWriteIndex);
        il.Emit(OpCodes.Bgt, maxThrowLabel);

        // SetCoreWithExtend(index, value);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, setCoreWithExtend);

        il.MarkLabel(frozenReturnLabel);
        il.MarkLabel(notExtensibleReturnLabel);
        il.MarkLabel(indexInRangeLabel);
        il.Emit(OpCodes.Ret);

        EmitInlineThrowError(il, runtime, "Index out of bounds.", runtime.TSRangeErrorCtor, negThrowLabel);
        EmitInlineThrowError(il, runtime, "Array index exceeds ECMA-262 uint32 maximum.", runtime.TSRangeErrorCtor, maxThrowLabel);
    }

    /// <summary>
    /// <c>public void SetStrict(long index, object? value, bool strictMode)</c> —
    /// like <see cref="EmitTSArraySetLong"/> but throws TypeError for writes to
    /// frozen arrays in strict mode rather than silently no-op'ing.
    /// </summary>
    private void EmitTSArraySetStrictLong(TypeBuilder typeBuilder, EmittedRuntime runtime, MethodBuilder setCoreWithExtend, MethodBuilder syncLength)
    {
        var method = typeBuilder.DefineMethod("SetStrict", MethodAttributes.Public, _types.Void,
            [_types.Int64, _types.Object, _types.Boolean]);
        runtime.TSArraySetStrictLong = method;

        var il = method.GetILGenerator();
        EmitTSArrayDeoptGuard(il);
        var notFrozenLabel = il.DefineLabel();
        var frozenReturnLabel = il.DefineLabel();
        var negThrowLabel = il.DefineLabel();
        var maxThrowLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, syncLength);

        // if (_isFrozen)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayIsFrozenField);
        il.Emit(OpCodes.Brfalse, notFrozenLabel);

        // if (!strictMode) return;
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brfalse, frozenReturnLabel);

        // throw TypeError
        EmitInlineThrowErrorInline(il, "TypeError: Cannot assign to read only property of array", runtime.TSTypeErrorCtor);

        il.MarkLabel(frozenReturnLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFrozenLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Blt, negThrowLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I8, TSArrayMaxWriteIndex);
        il.Emit(OpCodes.Bgt, maxThrowLabel);

        // SetCoreWithExtend(index, value); return;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, setCoreWithExtend);
        il.Emit(OpCodes.Ret);

        EmitInlineThrowError(il, runtime, "Index out of bounds.", runtime.TSRangeErrorCtor, negThrowLabel);
        EmitInlineThrowError(il, runtime, "Array index exceeds ECMA-262 uint32 maximum.", runtime.TSRangeErrorCtor, maxThrowLabel);
    }

    /// <summary>
    /// <c>public void SetLength(long newLength)</c> — implements <c>arr.length = N</c>.
    /// Truncates or extends with holes. Respects frozen state.
    /// </summary>
    private void EmitTSArraySetLength(TypeBuilder typeBuilder, EmittedRuntime runtime, MethodBuilder tryCollapseSparse, MethodBuilder syncLength)
    {
        var method = typeBuilder.DefineMethod("SetLength", MethodAttributes.Public, _types.Void, [_types.Int64]);
        runtime.TSArraySetLength = method;

        var il = method.GetILGenerator();
        EmitTSArrayDeoptGuard(il);
        var negThrowLabel = il.DefineLabel();
        var tooBigThrowLabel = il.DefineLabel();
        var notFrozenLabel = il.DefineLabel();
        var lengthChangedLabel = il.DefineLabel();
        var extendPathLabel = il.DefineLabel();
        var padHolesLabel = il.DefineLabel();
        var sparseExtendLabel = il.DefineLabel();
        var padLoopHead = il.DefineLabel();
        var padLoopExit = il.DefineLabel();
        var truncateSparseDoneLabel = il.DefineLabel();
        var truncateDenseLoopHead = il.DefineLabel();
        var truncateDenseLoopExit = il.DefineLabel();
        var truncateForLoopHead = il.DefineLabel();
        var truncateForLoopBody = il.DefineLabel();
        var truncateForLoopNext = il.DefineLabel();
        var truncateForLoopExit = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, syncLength);

        // if (_isFrozen) return;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayIsFrozenField);
        il.Emit(OpCodes.Brfalse, notFrozenLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notFrozenLabel);

        // if (newLength < 0) throw;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Blt, negThrowLabel);

        // if (newLength > MaxLength) throw;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I8, TSArrayMaxLength);
        il.Emit(OpCodes.Bgt, tooBigThrowLabel);

        // if (newLength == _length) return;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayLengthField);
        il.Emit(OpCodes.Bne_Un, lengthChangedLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(lengthChangedLabel);

        // if (newLength >= _length) goto extendPath;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayLengthField);
        il.Emit(OpCodes.Bge, extendPathLabel);

        // ArraySetLength must also truncate indexed properties represented by
        // the descriptor store. First raise the effective length above the
        // highest non-configurable indexed descriptor, then delete every
        // configurable indexed descriptor at or above that effective length.
        // Dense/sparse storage is truncated to the same boundary below.
        var descriptorKeysLocal = il.DeclareLocal(_types.ListOfObject);
        var descriptorKeyLocal = il.DeclareLocal(_types.String);
        var descriptorIndexLocal = il.DeclareLocal(_types.UInt32);
        var descriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var descriptorLoopIndexLocal = il.DeclareLocal(_types.Int32);
        var descriptorLoopCountLocal = il.DeclareLocal(_types.Int32);
        var descriptorFirstLoop = il.DefineLabel();
        var descriptorFirstNext = il.DefineLabel();
        var descriptorFirstDone = il.DefineLabel();
        var descriptorSecondLoop = il.DefineLabel();
        var descriptorSecondNext = il.DefineLabel();
        var descriptorSecondDone = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, runtime.PDSGetAllExtraKeys);
        il.Emit(OpCodes.Stloc, descriptorKeysLocal);
        il.Emit(OpCodes.Ldloc, descriptorKeysLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, descriptorLoopCountLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, descriptorLoopIndexLocal);

        // Pass 1: compute the effective lower bound imposed by
        // non-configurable indexed descriptors.
        il.MarkLabel(descriptorFirstLoop);
        il.Emit(OpCodes.Ldloc, descriptorLoopIndexLocal);
        il.Emit(OpCodes.Ldloc, descriptorLoopCountLocal);
        il.Emit(OpCodes.Bge, descriptorFirstDone);
        il.Emit(OpCodes.Ldloc, descriptorKeysLocal);
        il.Emit(OpCodes.Ldloc, descriptorLoopIndexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, descriptorKeyLocal);
        il.Emit(OpCodes.Ldloc, descriptorKeyLocal);
        il.Emit(OpCodes.Brfalse, descriptorFirstNext);
        il.Emit(OpCodes.Ldloc, descriptorKeyLocal);
        il.Emit(OpCodes.Ldloca, descriptorIndexLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.UInt32, "TryParse", [_types.String, _types.UInt32.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, descriptorFirstNext);
        il.Emit(OpCodes.Ldloc, descriptorIndexLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Conv_U4);
        il.Emit(OpCodes.Beq, descriptorFirstNext);
        il.Emit(OpCodes.Ldloc, descriptorKeyLocal);
        il.Emit(OpCodes.Ldloca, descriptorIndexLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.UInt32, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, descriptorFirstNext);
        il.Emit(OpCodes.Ldloc, descriptorIndexLocal);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Blt_Un, descriptorFirstNext);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, descriptorKeyLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, descriptorLocal);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Brfalse, descriptorFirstNext);
        il.Emit(OpCodes.Ldloc, descriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, descriptorFirstNext);
        il.Emit(OpCodes.Ldloc, descriptorIndexLocal);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Starg_S, (byte)1);
        il.MarkLabel(descriptorFirstNext);
        il.Emit(OpCodes.Ldloc, descriptorLoopIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, descriptorLoopIndexLocal);
        il.Emit(OpCodes.Br, descriptorFirstLoop);

        // Pass 2: remove configurable indexed descriptors that are actually
        // truncated at the effective length.
        il.MarkLabel(descriptorFirstDone);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, descriptorLoopIndexLocal);
        il.MarkLabel(descriptorSecondLoop);
        il.Emit(OpCodes.Ldloc, descriptorLoopIndexLocal);
        il.Emit(OpCodes.Ldloc, descriptorLoopCountLocal);
        il.Emit(OpCodes.Bge, descriptorSecondDone);
        il.Emit(OpCodes.Ldloc, descriptorKeysLocal);
        il.Emit(OpCodes.Ldloc, descriptorLoopIndexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, descriptorKeyLocal);
        il.Emit(OpCodes.Ldloc, descriptorKeyLocal);
        il.Emit(OpCodes.Brfalse, descriptorSecondNext);
        il.Emit(OpCodes.Ldloc, descriptorKeyLocal);
        il.Emit(OpCodes.Ldloca, descriptorIndexLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.UInt32, "TryParse", [_types.String, _types.UInt32.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, descriptorSecondNext);
        il.Emit(OpCodes.Ldloc, descriptorIndexLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Conv_U4);
        il.Emit(OpCodes.Beq, descriptorSecondNext);
        il.Emit(OpCodes.Ldloc, descriptorKeyLocal);
        il.Emit(OpCodes.Ldloca, descriptorIndexLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.UInt32, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, descriptorSecondNext);
        il.Emit(OpCodes.Ldloc, descriptorIndexLocal);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Blt_Un, descriptorSecondNext);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, descriptorKeyLocal);
        il.Emit(OpCodes.Call, runtime.PDSDeleteProperty);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(descriptorSecondNext);
        il.Emit(OpCodes.Ldloc, descriptorLoopIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, descriptorLoopIndexLocal);
        il.Emit(OpCodes.Br, descriptorSecondLoop);
        il.MarkLabel(descriptorSecondDone);

        // Truncate path.
        // if (_sparse != null) remove keys >= newLength.
        var sparseCheckSkipLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArraySparseField);
        il.Emit(OpCodes.Brfalse, sparseCheckSkipLabel);

        // Snapshot keys into a List<uint> (can't remove while iterating Dictionary.Keys),
        // then index it with a plain for-loop to avoid struct-enumerator complexity.
        var keysEnumerableType = _types.MakeGenericType(_types.IEnumerableOpen, _types.UInt32);
        var keysCollectionGetter = _types.GetProperty(_tsArraySparseType, "Keys")!.GetGetMethod()!;
        var listUIntType = _types.MakeGenericType(_types.ListOpen, _types.UInt32);
        var listUIntCtorFromEnum = _types.GetConstructor(listUIntType, [keysEnumerableType])!;
        var listUIntGetCount = _types.GetProperty(listUIntType, "Count")!.GetGetMethod()!;
        var listUIntGetItem = _types.GetMethod(listUIntType, "get_Item", [_types.Int32])!;

        var keysListLocal = il.DeclareLocal(listUIntType);
        var iLocal = il.DeclareLocal(_types.Int32);
        var countLocal = il.DeclareLocal(_types.Int32);
        var keyLocal = il.DeclareLocal(_types.UInt32);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArraySparseField);
        il.Emit(OpCodes.Callvirt, keysCollectionGetter);
        il.Emit(OpCodes.Newobj, listUIntCtorFromEnum);
        il.Emit(OpCodes.Stloc, keysListLocal);

        il.Emit(OpCodes.Ldloc, keysListLocal);
        il.Emit(OpCodes.Callvirt, listUIntGetCount);
        il.Emit(OpCodes.Stloc, countLocal);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(truncateForLoopHead);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Bge, truncateForLoopExit);

        // uint key = keysList[i];
        il.Emit(OpCodes.Ldloc, keysListLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, listUIntGetItem);
        il.Emit(OpCodes.Stloc, keyLocal);

        // if ((ulong)key < (ulong)newLength) skip;
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Blt_Un, truncateForLoopNext);

        // _sparse.Remove(key);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArraySparseField);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, _tsArraySparseRemove!);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(truncateForLoopNext);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, truncateForLoopHead);
        il.MarkLabel(truncateForLoopExit);

        il.MarkLabel(sparseCheckSkipLabel);

        // while (_dense.Count > newLength) _dense.RemoveAt(_dense.Count - 1);
        il.MarkLabel(truncateDenseLoopHead);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _tsArrayListCountGetter!);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ble, truncateDenseLoopExit);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _tsArrayListCountGetter!);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, _tsArrayListRemoveAt!);
        il.Emit(OpCodes.Br, truncateDenseLoopHead);

        il.MarkLabel(truncateDenseLoopExit);

        // _length = newLength; TryCollapseSparse(); return;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _tsArrayLengthField);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, tryCollapseSparse);

        il.MarkLabel(truncateSparseDoneLabel);
        il.Emit(OpCodes.Ret);

        // Extend path.
        il.MarkLabel(extendPathLabel);

        // long growth = newLength - _length;
        var growthLocal = il.DeclareLocal(_types.Int64);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayLengthField);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, growthLocal);

        // if (_sparse != null || growth > SparseThreshold || newLength > int.MaxValue) → sparse extend
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArraySparseField);
        il.Emit(OpCodes.Brtrue, sparseExtendLabel);

        il.Emit(OpCodes.Ldloc, growthLocal);
        il.Emit(OpCodes.Ldc_I8, (long)TSArraySparseThreshold);
        il.Emit(OpCodes.Bgt, sparseExtendLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I8, (long)int.MaxValue);
        il.Emit(OpCodes.Bgt, sparseExtendLabel);

        il.MarkLabel(padHolesLabel);

        // while (_dense.Count < newLength) _dense.Add($ArrayHole.Instance);
        il.MarkLabel(padLoopHead);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _tsArrayListCountGetter!);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Bge, padLoopExit);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.ArrayHoleInstance);
        il.Emit(OpCodes.Callvirt, _tsArrayListAdd!);
        il.Emit(OpCodes.Br, padLoopHead);

        il.MarkLabel(padLoopExit);
        // _length = (long)_dense.Count;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _tsArrayListCountGetter!);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Stfld, _tsArrayLengthField);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(sparseExtendLabel);
        // _sparse ??= new Dictionary<uint,object?>();
        var hasSparseLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArraySparseField);
        il.Emit(OpCodes.Brtrue, hasSparseLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_tsArraySparseType, Type.EmptyTypes)!);
        il.Emit(OpCodes.Stfld, _tsArraySparseField);

        il.MarkLabel(hasSparseLabel);
        // _length = newLength;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _tsArrayLengthField);
        il.Emit(OpCodes.Ret);

        // Throw a real $RangeError instance wrapped in a CLR Exception so
        // catch blocks unwrap to a RangeError per ECMA-262 22.1.5.4. The
        // wrapping is inlined here because CreateException isn't yet emitted
        // at TSArray-time. Uses Exception.Data["__tsValue"] = $RangeError so
        // WrapException's existing __tsValue path returns the error instance.
        EmitInlineThrowError(il, runtime, "Invalid array length.", runtime.TSRangeErrorCtor, negThrowLabel);
        EmitInlineThrowError(il, runtime, "Array length exceeds ECMA-262 uint32 maximum.", runtime.TSRangeErrorCtor, tooBigThrowLabel);
    }

    /// <summary>
    /// Inlined equivalent of `$Runtime.CreateException(new $XError(message))`
    /// + Throw. Used by emitters that run before $Runtime is built (e.g.
    /// $Array, $TSObject). Stores the original `$XError` instance in
    /// `ex.Data["__tsValue"]` so `WrapException` returns it on catch.
    /// </summary>
    private void EmitInlineThrowError(ILGenerator il, EmittedRuntime runtime, string message, ConstructorBuilder errorCtor, System.Reflection.Emit.Label markLabel)
    {
        il.MarkLabel(markLabel);
        EmitInlineThrowErrorInline(il, message, errorCtor);
    }

    /// <summary>Inline form of <see cref="EmitInlineThrowError"/> that doesn't mark a label first.</summary>
    private void EmitInlineThrowErrorInline(ILGenerator il, string message, ConstructorBuilder errorCtor)
    {
        var errLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldstr, message);
        il.Emit(OpCodes.Newobj, errorCtor);
        il.Emit(OpCodes.Stloc, errLocal);
        var exLocal2 = il.DeclareLocal(_types.Exception);
        il.Emit(OpCodes.Ldstr, message);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Stloc, exLocal2);
        il.Emit(OpCodes.Ldloc, exLocal2);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Data").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__tsValue");
        il.Emit(OpCodes.Ldloc, errLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "set_Item"));
        il.Emit(OpCodes.Ldloc, exLocal2);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// <c>public void DeleteAt(long index)</c> — ECMA-262 <c>delete arr[i]</c>.
    /// Turns the slot into a hole; length is unchanged. No-op for frozen
    /// arrays or out-of-range indices.
    /// </summary>
    private void EmitTSArrayDeleteAt(TypeBuilder typeBuilder, EmittedRuntime runtime, MethodBuilder syncLength)
    {
        var method = typeBuilder.DefineMethod("DeleteAt", MethodAttributes.Public, _types.Void, [_types.Int64]);
        runtime.TSArrayDeleteAt = method;

        var il = method.GetILGenerator();
        EmitTSArrayDeoptGuard(il);
        var retLabel = il.DefineLabel();
        var denseDeleteLabel = il.DefineLabel();
        var sparseDeleteCheckLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, syncLength);

        // if (_isFrozen) return;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayIsFrozenField);
        il.Emit(OpCodes.Brtrue, retLabel);

        // if ((ulong)index >= (ulong)_length) return;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Blt, retLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArrayLengthField);
        il.Emit(OpCodes.Bge, retLabel);

        // if (_sparse == null || index < _dense.Count) _dense[(int)index] = $ArrayHole.Instance;
        // else if (index <= uint.MaxValue) _sparse.Remove((uint)index);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArraySparseField);
        il.Emit(OpCodes.Brfalse, denseDeleteLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _tsArrayListCountGetter!);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Bge, sparseDeleteCheckLabel);

        il.MarkLabel(denseDeleteLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldsfld, runtime.ArrayHoleInstance);
        il.Emit(OpCodes.Callvirt, _tsArrayListSetItem!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(sparseDeleteCheckLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I8, (long)uint.MaxValue);
        il.Emit(OpCodes.Bgt, retLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsArraySparseField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_U4);
        il.Emit(OpCodes.Callvirt, _tsArraySparseRemove!);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(retLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSArrayToString(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        _ = method;

        var il = method.GetILGenerator();
        EmitTSArrayDeoptGuard(il);

        // ToString renders _dense elements joined by comma — matches pre-refactor
        // behavior exactly. Hole-aware join lives in the array built-ins (M5).
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.ListOfObject, "ToArray"));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Join", [_types.String, _types.ObjectArray])!);
        il.Emit(OpCodes.Ret);
    }


    // Reserved for a future int→long convenience shim if needed by legacy call
    // sites. M1 does not add one because the legacy int-indexed Get/Set above
    // already cover the pre-Stage-E surface.
    private const long TSArraySparseThreshold = 1024;
    private const long TSArrayMaxWriteIndex = (long)uint.MaxValue - 1;
    private const long TSArrayMaxLength = (long)uint.MaxValue;
}
