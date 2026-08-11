using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitArrayIncludes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayIncludes",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,  // Return boxed bool to match ILEmitter expectations
            [_types.ListOfObject, _types.Object, _types.Object]
        );
        runtime.ArrayIncludes = method;

        var il = method.GetILGenerator();

        EmitHoistedLazyCheck(il, runtime, out var isLazyLocal, out _);

        var lenLocal = il.DeclareLocal(_types.Int32);
        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);

        var returnFalse = il.DefineLabel();
        // Step 3 returns before fromIndex coercion when len is zero.
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Brfalse, returnFalse);

        // includes shares indexOf's ToIntegerOrInfinity clamping, but unlike
        // indexOf it observes holes as undefined instead of skipping them.
        var startLocal = il.DeclareLocal(_types.Int32);
        EmitComputeIndexOfStart(il, runtime, lenLocal, startLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Beq, returnFalse);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, returnFalse);

        // ECMA-262 23.1.3.13 Array.prototype.includes: DOES NOT skip holes.
        // A hole reads as undefined, so `[,].includes(undefined) === true`.
        // Lazy loads preserve Proxy/accessor order for borrowed calls.
        var elementLocal = il.DeclareLocal(_types.Object);
        EmitLoadElementUnholed(il, indexLocal, runtime, isLazyLocal);
        il.Emit(OpCodes.Stloc, elementLocal);

        // ECMA-262 SameValueZero: StrictEquals plus NaN equal to NaN.
        var ordinaryCompare = il.DefineLabel();
        var matched = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, ordinaryCompare);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, ordinaryCompare);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Call, _types.DoubleIsNaN);
        il.Emit(OpCodes.Brfalse, ordinaryCompare);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Call, _types.DoubleIsNaN);
        il.Emit(OpCodes.Brtrue, matched);

        il.MarkLabel(ordinaryCompare);
        il.Emit(OpCodes.Ldloc, elementLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.StrictEquals);

        var notMatch = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notMatch);
        il.MarkLabel(matched);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notMatch);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(returnFalse);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>$Runtime.ArrayLikeMaterialize(object receiver) -&gt; List&lt;object&gt;</c>.
    /// Mirrors <c>ArrayPrototypeMethodWrapper.TryMaterializeArrayLike</c> on the
    /// interpreter side (<c>Runtime/Types/SharpTSArrayGlobal.cs</c>) — ECMA-262
    /// requires Array.prototype.* to accept any array-like (anything with a
    /// <c>length</c> + indexed properties) as <c>this</c>. Supported receivers:
    /// <list type="bullet">
    /// <item>null / <c>$Undefined</c> → TypeError (spec step: ToObject(this)).</item>
    /// <item><c>List&lt;object&gt;</c> → pass-through.</item>
    /// <item><c>$Array</c> (emitted TSArray wrapper) → unwrap via <c>.Elements</c>.</item>
    /// <item><c>string</c> → one-char-per-index materialization.</item>
    /// <item><c>Dictionary&lt;string, object&gt;</c> (JS object literals in compiled
    ///       mode) → read <c>length</c>, then indexed properties 0..len-1; absent
    ///       slots materialize as <c>$ArrayHole</c>.Instance.</item>
    /// </list>
    /// Holes are preserved so downstream hole-skipping methods (every/map/reduce/etc.)
    /// behave correctly. Length is clamped at 1M to guard against accidental
    /// runaway <c>length: 2**53-1</c> configurations.
    /// </summary>
    /// <summary>
    /// Phase 1 — declare the MethodBuilder so call sites emitted before
    /// <see cref="EmitArrayLikeMaterialize"/> (notably InvokeMethodValue's
    /// $BoundArrayMethod receiver-rebind path) can reference it. Body is
    /// filled in by EmitArrayLikeMaterialize, which depends on $Runtime
    /// helpers (GetProperty, ToNumber) emitted later in EmitRuntimeClass.
    /// </summary>
    internal void DeclareArrayLikeMaterialize(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.ArrayLikeMaterialize = typeBuilder.DefineMethod(
            "ArrayLikeMaterialize",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object]
        );
    }

    private void EmitArrayLikeMaterialize(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = runtime.ArrayLikeMaterialize;
        var il = method.GetILGenerator();

        var throwLabel = il.DefineLabel();

        // null / undefined → TypeError
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, throwLabel);

        var notUndefined = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, notUndefined);
        il.Emit(OpCodes.Br, throwLabel);
        il.MarkLabel(notUndefined);

        // $Arguments → take only the first `_length` elements per ECMA-262
        // sloppy arguments semantics. `arguments[N] = v` for N >= length does
        // NOT update length, so a materialized array-like view must not see
        // those out-of-range slots. Without this slice, `every.call(arguments, cb)`
        // visits the out-of-range index 2 in test patterns like
        // `function(a, b) { arguments[2] = 9; ...every.call(arguments, ...) }`.
        var notArguments = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.ArgumentsType);
        il.Emit(OpCodes.Brfalse, notArguments);
        // result = new List<object>(); for (i = 0; i < _length; i++) result.Add(args[i]);
        var argsLocal = il.DeclareLocal(runtime.ArgumentsType);
        var argsResultLocal = il.DeclareLocal(_types.ListOfObject);
        var argsIdxLocal = il.DeclareLocal(_types.Int32);
        var argsLenLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.ArgumentsType);
        il.Emit(OpCodes.Stloc, argsLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldfld, runtime.ArgumentsLengthField);
        il.Emit(OpCodes.Stloc, argsLenLocal);
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.Int32));
        il.Emit(OpCodes.Stloc, argsResultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, argsIdxLocal);
        var argsLoop = il.DefineLabel();
        var argsLoopEnd = il.DefineLabel();
        il.MarkLabel(argsLoop);
        il.Emit(OpCodes.Ldloc, argsIdxLocal);
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Bge, argsLoopEnd);
        // Bounds check against List.Count too — _length could in principle exceed Count
        // if length was lifted programmatically; treat absent slots as undefined.
        il.Emit(OpCodes.Ldloc, argsIdxLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        var argsHaveLabel = il.DefineLabel();
        var argsAfterAddLabel = il.DefineLabel();
        il.Emit(OpCodes.Blt, argsHaveLabel);
        il.Emit(OpCodes.Ldloc, argsResultLocal);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Br, argsAfterAddLabel);
        il.MarkLabel(argsHaveLabel);
        il.Emit(OpCodes.Ldloc, argsResultLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldloc, argsIdxLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.MarkLabel(argsAfterAddLabel);
        il.Emit(OpCodes.Ldloc, argsIdxLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, argsIdxLocal);
        il.Emit(OpCodes.Br, argsLoop);
        il.MarkLabel(argsLoopEnd);
        il.Emit(OpCodes.Ldloc, argsResultLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notArguments);

        // List<object> → passthrough
        var notList = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, notList);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notList);

        // $Array → .Elements
        var notTSArray = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, notTSArray);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayElementsGetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTSArray);

        // string → materialize char-by-char
        var notString = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notString);
        EmitMaterializeString(il);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notString);

        // object[] → wrap as List<object>. This hits the compiled-mode
        // `arguments` representation (thread-static object[] per
        // $ArgumentsContext). Tests pass `arguments` as a receiver to
        // Array.prototype.* and expect array-like iteration.
        var notObjectArray = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ObjectArray);
        il.Emit(OpCodes.Brfalse, notObjectArray);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.IEnumerableOfObject);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.IEnumerableOfObject));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notObjectArray);

        // Dictionary<string, object> → materialize from length + indexed
        var notDict = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, notDict);
        EmitMaterializeDictionary(il, runtime);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notDict);

        // $Object → materialize via $Runtime.GetProperty so prototype-chain
        // length + indexed reads fire (Test262 patterns: `Con.prototype = proto;
        // obj = new Con(); obj[i] = …; Array.prototype.X.call(obj, …)`).
        var notTSObject = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notTSObject);
        EmitMaterializeViaGetProperty(il, runtime);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTSObject);

        // Bool primitive → materialize from Boolean.prototype singleton.
        // Per spec, ToObject(false) creates a Boolean wrapper that inherits
        // from Boolean.prototype. Test262 patterns customize Boolean.prototype
        // (`Boolean.prototype[0] = true; Boolean.prototype.length = 1;`)
        // before calling Array.prototype.X.call(false, ...) — those reads must
        // surface here. Routes through MaterializeFromPrototype which reads
        // length + indexed properties from the supplied dict.
        var notBool = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, notBool);
        il.Emit(OpCodes.Ldsfld, runtime.BooleanPrototypeField);
        EmitMaterializeFromPrototypeDict(il, runtime);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBool);

        // Double (number) primitive → materialize from Number.prototype singleton
        // (mirrors the bool case for `Array.prototype.X.call(42, cb)`).
        var notNumber = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notNumber);
        il.Emit(OpCodes.Ldsfld, runtime.NumberPrototypeField);
        EmitMaterializeFromPrototypeDict(il, runtime);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notNumber);

        // Generic fallback for any non-null receiver: materialize via
        // $Runtime.GetProperty(receiver, "length") + indexed reads. Unlocks
        // Date / RegExp / $TSPromise (paired with SetFieldsProperty's scoped
        // PDS-store fallback) and other receivers that expose length+indexed
        // properties. Receivers without length yield NaN → 0 → empty list,
        // matching the previous fallback's silent-empty behavior.
        EmitMaterializeViaGetProperty(il, runtime);
        il.Emit(OpCodes.Ret);

        // null / undefined: ECMA-262 ToObject(null) / ToObject(undefined) throws
        // TypeError per 7.1.18. `Array.prototype.X.call(undefined, ...)` and
        // `.call(null, ...)` must surface this throw rather than silently
        // iterate an empty list.
        il.MarkLabel(throwLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert undefined or null to object");
    }

    /// <summary>
    /// Emits <c>$Runtime.RequireObjectCoercibleThis(object)</c> — combines
    /// ECMA-262 7.1.18 RequireObjectCoercible (null/undefined → TypeError)
    /// and the Symbol-rejection that ToString performs on receivers. All
    /// <c>String.prototype.*</c> methods do "Let O = ? RequireObjectCoercible(this)"
    /// followed by "Let S = ? ToString(O)" — both can throw TypeError, so a
    /// single guard at the dispatch site catches both for any string-typed
    /// <c>__this</c> slot.
    /// </summary>
    /// <remarks>
    /// Called from <c>$TSFunction.CoercePrimitiveArgs</c> via late-bound
    /// reflection (<c>Type.GetType("$Runtime").GetMethod("RequireObjectCoercibleThis")</c>),
    /// because TSFunction's IL is emitted before the TSError class is built.
    /// Routing through this helper lets us throw a real <c>$TypeError</c>
    /// instance that <c>e instanceof TypeError</c> sees correctly, without
    /// each <c>String.prototype.X</c> helper repeating the null/Symbol check.
    /// </remarks>
    private void EmitRequireObjectCoercibleThis(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RequireObjectCoercibleThis",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        runtime.RequireObjectCoercibleThis = method;

        var il = method.GetILGenerator();
        var passThroughLabel = il.DefineLabel();

        // null → throw TypeError "null/undefined"
        il.Emit(OpCodes.Ldarg_0);
        var notNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notNullLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert undefined or null to object");
        il.MarkLabel(notNullLabel);

        // $Undefined → throw TypeError "null/undefined"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        var notUndefLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notUndefLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert undefined or null to object");
        il.MarkLabel(notUndefLabel);

        // Symbol → throw TypeError "Cannot convert a Symbol to a string".
        // ECMA-262 7.1.5 ToString(symbol) throws — every String.prototype.*
        // does this implicitly via "Let S = ? ToString(O)". Catches the
        // `return-abrupt-from-this-as-symbol.js` cluster (~6 tests).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brfalse, passThroughLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert a Symbol value to a string");

        // Pass-through (string, $TSObject, etc.).
        il.MarkLabel(passThroughLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Materializes a list from any receiver via $Runtime.GetProperty calls
    /// (length + indexed reads). Used for $Object instances where prototype-
    /// chain walks for `length` matter and indexed reads must go through the
    /// public property pipeline (getters / accessors / chain). Stack-in: arg0
    /// holds the receiver. Stack-out: [List&lt;object&gt;].
    /// </summary>
    private void EmitMaterializeViaGetProperty(ILGenerator il, EmittedRuntime runtime)
    {
        var lenLocal = il.DeclareLocal(_types.Int32);
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        var idxLocal = il.DeclareLocal(_types.Int32);
        var idxAsIntLocal = il.DeclareLocal(_types.Int32);

        // lenVal = $Runtime.GetProperty(receiver, "length")
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        // double d = $Runtime.ToNumber(lenVal);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        var dLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, dLocal);

        // NaN → 0
        var afterLen = il.DefineLabel();
        var notNaN = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, notNaN);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.Emit(OpCodes.Br, afterLen);
        il.MarkLabel(notNaN);

        // ±Infinity / finite branches
        var notPosInf = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsPositiveInfinity", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, notPosInf);
        il.Emit(OpCodes.Ldc_I4, 1 << 20);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.Emit(OpCodes.Br, afterLen);
        il.MarkLabel(notPosInf);

        var notNegInf = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNegativeInfinity", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, notNegInf);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.Emit(OpCodes.Br, afterLen);
        il.MarkLabel(notNegInf);

        // Finite: clamp to [0, 1<<20]
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        var notNeg = il.DefineLabel();
        il.Emit(OpCodes.Bge, notNeg);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.MarkLabel(notNeg);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldc_I4, 1 << 20);
        var notTooBig = il.DefineLabel();
        il.Emit(OpCodes.Ble, notTooBig);
        il.Emit(OpCodes.Ldc_I4, 1 << 20);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.MarkLabel(notTooBig);

        il.MarkLabel(afterLen);

        // list = new List<object>(len)
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.Int32));
        il.Emit(OpCodes.Stloc, listLocal);

        // for (i = 0; i < len; i++) list.Add($Runtime.GetProperty(receiver, i.ToString()))
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, idxLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // val = $Runtime.GetProperty(receiver, i.ToString())
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Stloc, idxAsIntLocal);
        il.Emit(OpCodes.Ldloca, idxAsIntLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, idxLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, listLocal);
    }

    private void EmitMaterializeString(ILGenerator il)
    {
        // str = (string)receiver; list = new List<object>(str.Length);
        // for (int i = 0; i < str.Length; i++) list.Add(str[i].ToString());
        // return list;
        var strLocal = il.DeclareLocal(_types.String);
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        var idxLocal = il.DeclareLocal(_types.Int32);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, strLocal);

        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.Int32));
        il.Emit(OpCodes.Stloc, listLocal);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, idxLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // list.Add(str[i].ToString())
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        // Box char to string via ToString()
        var charLocal = il.DeclareLocal(_types.Char);
        il.Emit(OpCodes.Stloc, charLocal);
        il.Emit(OpCodes.Ldloca, charLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Char, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, idxLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, listLocal);
    }

    /// <summary>
    /// Reads length + indexed slots from a prototype-singleton dict that's
    /// already on top of the stack and emits IL that produces a List&lt;object&gt;.
    /// Used by the materializer's primitive-receiver branches (bool, double).
    /// Stack-in: [dict]. Stack-out: [list].
    /// Distinct from <see cref="EmitMaterializeDictionary"/> which expects the
    /// receiver in arg0 and routes through GetProperty (for accessor getters);
    /// prototype singletons hold plain dict entries written directly by user
    /// code, so we use TryGetValue on the dict for both length and indexed reads.
    /// </summary>
    private void EmitMaterializeFromPrototypeDict(ILGenerator il, EmittedRuntime runtime)
    {
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        var lenLocal = il.DeclareLocal(_types.Int32);
        var lenValLocal = il.DeclareLocal(_types.Object);
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        var idxLocal = il.DeclareLocal(_types.Int32);
        var valLocal = il.DeclareLocal(_types.Object);

        var tryGetValue = _types.GetMethod(_types.DictionaryStringObject,
            "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!;

        // if (dict.TryGetValue("length", out lenVal)) ... else len = 0
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Ldloca, lenValLocal);
        il.Emit(OpCodes.Callvirt, tryGetValue);
        var haveLen = il.DefineLabel();
        var afterLen = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, haveLen);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.Emit(OpCodes.Br, afterLen);

        il.MarkLabel(haveLen);
        // len = (int)$Runtime.ToNumber(lenVal); clamp [0, 1<<20]
        il.Emit(OpCodes.Ldloc, lenValLocal);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        var lenAsDouble = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, lenAsDouble);
        // NaN/Infinity → 0; else clamp
        il.Emit(OpCodes.Ldloc, lenAsDouble);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsFinite", [_types.Double])!);
        var finiteLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, finiteLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.Emit(OpCodes.Br, afterLen);
        il.MarkLabel(finiteLabel);
        il.Emit(OpCodes.Ldloc, lenAsDouble);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lenLocal);
        // clamp to [0, 1<<20]
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        var notNegLabel = il.DefineLabel();
        il.Emit(OpCodes.Bge, notNegLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.MarkLabel(notNegLabel);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldc_I4, 1 << 20);
        var notTooBigLabel = il.DefineLabel();
        il.Emit(OpCodes.Ble, notTooBigLabel);
        il.Emit(OpCodes.Ldc_I4, 1 << 20);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.MarkLabel(notTooBigLabel);

        il.MarkLabel(afterLen);

        // list = new List<object>(len)
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.Int32));
        il.Emit(OpCodes.Stloc, listLocal);

        // i = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, idxLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // if (dict.TryGetValue(i.ToString(), out val)) list.Add(val); else list.Add(ArrayHole.Instance)
        il.Emit(OpCodes.Ldloc, dictLocal);
        var idxAsIntLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Stloc, idxAsIntLocal);
        il.Emit(OpCodes.Ldloca, idxAsIntLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldloca, valLocal);
        il.Emit(OpCodes.Callvirt, tryGetValue);
        var noEntry = il.DefineLabel();
        var afterEntry = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, noEntry);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Br, afterEntry);
        il.MarkLabel(noEntry);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldsfld, runtime.ArrayHoleInstance);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.MarkLabel(afterEntry);

        // i++
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, idxLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, listLocal);
    }

    private void EmitMaterializeDictionary(ILGenerator il, EmittedRuntime runtime)
    {
        // dict = (Dictionary<string,object>)receiver;
        // Read length via $Runtime.GetProperty(receiver, "length") so accessor
        // getters defined via Object.defineProperty are invoked correctly
        // (TryGetValue would only see direct dictionary entries, missing PDS-
        // stored accessors). Clamp to [0, 1<<20]. For i in [0..len): use
        // GetProperty for the same reason — supports indexed accessors and
        // tests that iterate with side-effecting getters.
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var lenLocal = il.DeclareLocal(_types.Int32);
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        var idxLocal = il.DeclareLocal(_types.Int32);
        var valLocal = il.DeclareLocal(_types.Object);
        var lenValLocal = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // Reused below for indexed reads (must distinguish "absent" → hole
        // from "present" → value, which GetProperty can't do alone).
        var tryGetValue = _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", [_types.String, _types.Object.MakeByRefType()])!;

        // lenVal = $Runtime.GetProperty(receiver, "length")
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, lenValLocal);
        var haveLen = il.DefineLabel();
        var afterLen = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lenValLocal);
        il.Emit(OpCodes.Brtrue, haveLen);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.Emit(OpCodes.Br, afterLen);
        il.MarkLabel(haveLen);

        // ECMA-262 ToPrimitive: if lenVal is an object (Dictionary OR $Object —
        // the latter when `obj.length` is `new Con()` whose proto carries
        // valueOf/toString), try valueOf() then toString() to coerce to a
        // primitive. Without this, tests that use `length: child` where
        // `child = new Con(); Con.prototype.valueOf = () => 2` get NaN from
        // ToNumber and iterate nothing. The $Object branch matters because
        // user constructors emit instances as $Object, not as Dictionary.
        var notObjLen = il.DefineLabel();
        var doToPrimLen = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lenValLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, doToPrimLen);
        il.Emit(OpCodes.Ldloc, lenValLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notObjLen);
        il.MarkLabel(doToPrimLen);
        EmitLengthToPrimitive(il, runtime, lenValLocal);
        il.MarkLabel(notObjLen);

        // len = clamp(ToInteger($Runtime.ToNumber(lenVal)), 0, 1<<20).
        // ToNumber catches conversion failures and returns NaN — matches ECMA-262
        // ToLength semantics for non-numeric `length` (e.g. `length: undefined`,
        // `length: "asdf!_"`). Special-case +/-Infinity since Conv_I4 on those
        // produces undefined behavior (typically int.MinValue), which would clamp
        // wrongly to 0 instead of 1<<20 / 0 respectively.
        il.Emit(OpCodes.Ldloc, lenValLocal);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        var dLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, dLocal);

        // NaN → 0
        var notNaN2 = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, notNaN2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.Emit(OpCodes.Br, afterLen);
        il.MarkLabel(notNaN2);

        // +Infinity → 1<<20 (clamp), -Infinity → 0
        var notPosInf = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsPositiveInfinity", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, notPosInf);
        il.Emit(OpCodes.Ldc_I4, 1 << 20);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.Emit(OpCodes.Br, afterLen);
        il.MarkLabel(notPosInf);

        var notNegInf = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNegativeInfinity", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, notNegInf);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.Emit(OpCodes.Br, afterLen);
        il.MarkLabel(notNegInf);

        // Finite: Conv_I4 + clamp [0, 1<<20]
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Conv_I4);
        // clamp < 0 → 0
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        var nonNeg = il.DefineLabel();
        il.Emit(OpCodes.Bge, nonNeg);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_0);
        il.MarkLabel(nonNeg);
        // clamp > 1<<20 → 1<<20
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, 1 << 20);
        var notTooBig = il.DefineLabel();
        il.Emit(OpCodes.Ble, notTooBig);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4, 1 << 20);
        il.MarkLabel(notTooBig);
        il.Emit(OpCodes.Stloc, lenLocal);

        il.MarkLabel(afterLen);

        // list = new List<object>(len)
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.Int32));
        il.Emit(OpCodes.Stloc, listLocal);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, idxLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // First try TryGetValue on the dict to distinguish "absent" (hole) from
        // "present-but-undefined". If present, push the value. If absent, check
        // PDS for an accessor descriptor (Object.defineProperty(obj, "1",
        // {get: ...})); invoke the getter via GetProperty so the throw
        // propagates. If no PDS entry either, push ArrayHole sentinel.
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloca, idxLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldloca, valLocal);
        il.Emit(OpCodes.Callvirt, tryGetValue);
        var wasPresent = il.DefineLabel();
        var afterPush = il.DefineLabel();
        var pushHole_dict = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, wasPresent);
        // Absent from _fields: ask GetProperty (which checks PDS getter first).
        // If GetProperty returns null/$Undefined, the property genuinely doesn't
        // exist — push ArrayHole. Otherwise the getter ran and returned a value.
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloca, idxLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, valLocal);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Brfalse, pushHole_dict);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, pushHole_dict);
        il.Emit(OpCodes.Br, wasPresent);
        il.MarkLabel(pushHole_dict);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldsfld, runtime.ArrayHoleInstance);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Br, afterPush);
        il.MarkLabel(wasPresent);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.MarkLabel(afterPush);

        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, idxLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, listLocal);
    }

    /// <summary>
    /// ECMA-262 ToPrimitive applied to the length property value in the
    /// array-like materializer. If <paramref name="lenValLocal"/> holds a
    /// Dictionary with a callable <c>valueOf</c>, invokes it; if the result is
    /// still a Dictionary, tries <c>toString</c>. Updates <paramref name="lenValLocal"/>
    /// with the first primitive encountered. No-op if neither protocol method
    /// exists or both return objects (falls through to ToNumber → NaN → 0).
    /// </summary>
    private void EmitLengthToPrimitive(ILGenerator il, EmittedRuntime runtime, LocalBuilder lenValLocal)
    {
        var emptyArgsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, emptyArgsLocal);

        void TryInvoke(string name, Label afterLabel)
        {
            var fnLocal = il.DeclareLocal(_types.Object);
            // fn = $Runtime.GetProperty(lenVal, name)
            il.Emit(OpCodes.Ldloc, lenValLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Stloc, fnLocal);
            il.Emit(OpCodes.Ldloc, fnLocal);
            il.Emit(OpCodes.Brfalse, afterLabel);
            // GetProperty returns $Undefined.Instance (not null) for absent
            // properties on Dictionary receivers — exclude that too.
            il.Emit(OpCodes.Ldloc, fnLocal);
            il.Emit(OpCodes.Isinst, runtime.UndefinedType);
            il.Emit(OpCodes.Brtrue, afterLabel);

            // result = $Runtime.InvokeMethodValue(lenVal, fn, emptyArgs)
            var resultLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldloc, lenValLocal);
            il.Emit(OpCodes.Ldloc, fnLocal);
            il.Emit(OpCodes.Ldloc, emptyArgsLocal);
            il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
            il.Emit(OpCodes.Stloc, resultLocal);

            // If result is still an object (Dictionary or $Object), don't
            // commit — fall through so the outer toString fallback runs.
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
            il.Emit(OpCodes.Brtrue, afterLabel);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Isinst, runtime.TSObjectType);
            il.Emit(OpCodes.Brtrue, afterLabel);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Stloc, lenValLocal);
        }

        var afterValueOf = il.DefineLabel();
        TryInvoke("valueOf", afterValueOf);
        il.MarkLabel(afterValueOf);

        // If still an object (Dictionary or $Object), try toString.
        var afterToString = il.DefineLabel();
        var stillObjForToString = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lenValLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, stillObjForToString);
        il.Emit(OpCodes.Ldloc, lenValLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, afterToString);
        il.MarkLabel(stillObjForToString);
        TryInvoke("toString", afterToString);
        il.MarkLabel(afterToString);

        // ECMA-262 ToPrimitive: if both valueOf and toString returned non-
        // primitives (lenVal still an object), throw TypeError.
        var afterTypeErrorCheck = il.DefineLabel();
        var stillObjForThrow = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lenValLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, stillObjForThrow);
        il.Emit(OpCodes.Ldloc, lenValLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, afterTypeErrorCheck);
        il.MarkLabel(stillObjForThrow);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert object to primitive value");
        il.MarkLabel(afterTypeErrorCheck);
    }

    private void EmitArrayIndexOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ECMA-262 23.1.3.17 — fromIndex coerced via ToIntegerOrInfinity (spec).
        // fromIndex=null means "not provided" → start from 0.
        var method = typeBuilder.DefineMethod(
            "ArrayIndexOf",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.ListOfObject, _types.Object, _types.Object]
        );
        runtime.ArrayIndexOf = method;

        var il = method.GetILGenerator();

        EmitHoistedLazyCheck(il, runtime, out var isLazyLocal, out _);

        var lenLocal = il.DeclareLocal(_types.Int32);
        var indexLocal = il.DeclareLocal(_types.Int32);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);

        // If len == 0 return -1 early (spec step 3, and avoids edge cases).
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Brtrue, notEmpty);
        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notEmpty);

        // start = ComputeIndexOfStart(fromIndex, len). Returns -1 if search
        // should be skipped entirely (+Inf or fromIndex >= len).
        var startLocal = il.DeclareLocal(_types.Int32);
        EmitComputeIndexOfStart(il, runtime, lenLocal, startLocal);

        var returnMinusOne = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Beq, returnMinusOne);

        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var advance = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // ECMA-262 23.1.3.14: indexOf SKIPS holes.
        EmitSkipIfHole(il, indexLocal, advance, runtime, isLazyLocal);

        // Spec uses StrictEqualityComparison — null and undefined are distinct.
        EmitElementLoad(il, indexLocal, runtime, isLazyLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.StrictEquals);

        var notMatch = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notMatch);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notMatch);
        il.MarkLabel(advance);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.MarkLabel(returnMinusOne);
        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits IL that reads arg2 (fromIndex, object?) and arg lenLocal (len) and
    /// leaves the indexOf starting index in startLocal. -1 indicates "skip —
    /// return -1" (fromIndex is +Infinity or >= len). Mirrors
    /// <c>ArrayBuiltIns.IndexOfV2</c>'s spec clamping.
    /// </summary>
    private void EmitComputeIndexOfStart(ILGenerator il, EmittedRuntime runtime, LocalBuilder lenLocal, LocalBuilder startLocal)
    {
        var nLocal = il.DeclareLocal(_types.Int32);
        var hasFromIndex = il.DefineLabel();
        var done = il.DefineLabel();

        // If fromIndex is null → start = 0
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brtrue, hasFromIndex);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, startLocal);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(hasFromIndex);
        // n = ToIntegerOrInfinity(fromIndex, 0)  — +Inf→MaxValue, -Inf→MinValue, NaN→0
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, nLocal);

        // +Inf sentinel → return -1
        var notPosInf = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Ldc_I4, int.MaxValue);
        il.Emit(OpCodes.Bne_Un, notPosInf);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stloc, startLocal);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(notPosInf);
        // -Inf sentinel → start = 0
        var notNegInf = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Ldc_I4, int.MinValue);
        il.Emit(OpCodes.Bne_Un, notNegInf);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, startLocal);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(notNegInf);
        // if n >= len → return -1
        var notTooBig = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Blt, notTooBig);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stloc, startLocal);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(notTooBig);
        // if n >= 0 → start = n
        var negFromIndex = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, negFromIndex);
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Stloc, startLocal);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(negFromIndex);
        // start = max(len + n, 0)
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Add);
        var sumLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Stloc, sumLocal);
        il.Emit(OpCodes.Ldloc, sumLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        var isNeg = il.DefineLabel();
        il.Emit(OpCodes.Blt, isNeg);
        il.Emit(OpCodes.Ldloc, sumLocal);
        il.Emit(OpCodes.Stloc, startLocal);
        il.Emit(OpCodes.Br, done);
        il.MarkLabel(isNeg);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, startLocal);

        il.MarkLabel(done);
    }

    private void EmitArrayLastIndexOf(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ECMA-262 23.1.3.18 Array.prototype.lastIndexOf — reverse scan; skips
        // holes (strict equality, kPresent only). fromIndex coerced via
        // ToIntegerOrInfinity: -Inf → return -1; +Inf → scan from len-1;
        // fromIndex >= 0 → start = min(fromIndex, len-1); else → start = len + n
        // (if still negative, spec says no iteration → return -1).
        var method = typeBuilder.DefineMethod(
            "ArrayLastIndexOf",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.ListOfObject, _types.Object, _types.Object]
        );
        runtime.ArrayLastIndexOf = method;

        var il = method.GetILGenerator();

        EmitHoistedLazyCheck(il, runtime, out var isLazyLocal, out _);

        var lenLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);

        // If len == 0 return -1 early.
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Brtrue, notEmpty);
        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notEmpty);

        var startLocal = il.DeclareLocal(_types.Int32);
        var indexLocal = il.DeclareLocal(_types.Int32);
        EmitComputeLastIndexOfStart(il, runtime, lenLocal, startLocal);

        var returnMinusOne = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Beq, returnMinusOne);

        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var advance = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, loopEnd);

        EmitSkipIfHole(il, indexLocal, advance, runtime, isLazyLocal);

        // ECMA-262 23.1.3.18 lastIndexOf — StrictEqualityComparison (===).
        EmitElementLoad(il, indexLocal, runtime, isLazyLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.StrictEquals);

        var notMatch = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notMatch);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notMatch);
        il.MarkLabel(advance);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.MarkLabel(returnMinusOne);
        il.Emit(OpCodes.Ldc_R8, -1.0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitComputeLastIndexOfStart(ILGenerator il, EmittedRuntime runtime, LocalBuilder lenLocal, LocalBuilder startLocal)
    {
        var nLocal = il.DeclareLocal(_types.Int32);
        var hasFromIndex = il.DefineLabel();
        var done = il.DefineLabel();

        // If fromIndex is null → start = len - 1
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brtrue, hasFromIndex);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, startLocal);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(hasFromIndex);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.ToIntegerOrInfinity);
        il.Emit(OpCodes.Stloc, nLocal);

        // -Inf sentinel → return -1 (no iteration)
        var notNegInf = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Ldc_I4, int.MinValue);
        il.Emit(OpCodes.Bne_Un, notNegInf);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Stloc, startLocal);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(notNegInf);
        // +Inf sentinel → start = len - 1
        var notPosInf = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Ldc_I4, int.MaxValue);
        il.Emit(OpCodes.Bne_Un, notPosInf);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, startLocal);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(notPosInf);
        // If n >= 0 → start = min(n, len-1)
        var negFromIndex = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, negFromIndex);

        var useN = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Blt, useN);
        // start = len - 1
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, startLocal);
        il.Emit(OpCodes.Br, done);
        il.MarkLabel(useN);
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Stloc, startLocal);
        il.Emit(OpCodes.Br, done);

        il.MarkLabel(negFromIndex);
        // start = len + n; if still < 0 → -1 (no iteration)
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, startLocal);

        il.MarkLabel(done);
    }

    private void EmitArrayJoin(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ArrayJoin",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.ListOfObject, _types.Object]
        );
        runtime.ArrayJoin = method;

        var il = method.GetILGenerator();

        // separator = (arg1 == null || arg1 is $Undefined) ? "," : Stringify(arg1).
        // ECMA-262 23.1.3.16 step 4 specifies undefined → ",". The compiler's
        // TSFunction wrapper pads missing args with `null` (not $Undefined),
        // so wrapper-invoked join() with no args reaches here as null, which
        // we also treat as the default-trigger. This trades spec compliance
        // for `arr.join(null) === "null"` against correctness for the more
        // common `arr.join()` case.
        var sepLocal = il.DeclareLocal(_types.String);
        var hasSep = il.DefineLabel();
        var afterSep = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, afterSep);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, afterSep);
        il.Emit(OpCodes.Br, hasSep);
        il.MarkLabel(afterSep);
        il.Emit(OpCodes.Ldstr, ",");
        var setSepLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, setSepLabel);
        il.MarkLabel(hasSep);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.MarkLabel(setSepLabel);
        il.Emit(OpCodes.Stloc, sepLocal);

        // StringBuilder sb = new()
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, sbLocal);

        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // if (i > 0) sb.Append(separator)
        var skipSep = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, skipSep);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, sepLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skipSep);
        // ECMA-262 23.1.3.16: skip null, undefined, and holes (treat as empty
        // string in the join output). Stringify normally returns "null"/"undefined"
        // for those, but join's spec text says they must render as empty.
        var skipAppend = il.DefineLabel();
        var elemLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, elemLocal);

        // hole?
        il.Emit(OpCodes.Ldloc, elemLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brtrue, skipAppend);
        // null?
        il.Emit(OpCodes.Ldloc, elemLocal);
        il.Emit(OpCodes.Brfalse, skipAppend);
        // undefined?
        il.Emit(OpCodes.Ldloc, elemLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, skipAppend);

        // sb.Append(ToJsString(elem)) — ECMA-262 23.1.3.16 step 7 ToString per element:
        // a nested array joins recursively and an object / class instance (incl. Error)
        // dispatches its toString, rather than the console/debug Stringify form
        // ("[1, 2]" / "{ a: 1 }" / "ClassName") (#922 follow-up).
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, elemLocal);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skipAppend);

        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
        il.Emit(OpCodes.Ret);
    }

    private void EmitArrayConcat(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ECMA-262 23.1.3.1: concat takes ...items (variadic). Signature widened
        // to accept object[] so `arr.concat(a, b, c)` spreads each argument.
        // The trailing object[] is marked params via ParamArrayAttribute so
        // reflection-via-$TSFunction auto-packs trailing args into the array.
        var method = typeBuilder.DefineMethod(
            "ArrayConcat",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.ListOfObject, _types.ObjectArray]
        );
        var paramArrayCtor = typeof(ParamArrayAttribute).GetConstructor(Type.EmptyTypes)!;
        method.DefineParameter(2, System.Reflection.ParameterAttributes.None, "items")
            .SetCustomAttribute(paramArrayCtor, CustomAttributeEncoder.EmptyBlob);
        runtime.ArrayConcat = method;

        var il = method.GetILGenerator();

        // result = new List<object>(list)
        var resultLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.IEnumerableOfObject));
        il.Emit(OpCodes.Stloc, resultLocal);

        // for each arg in args[]: spread (if Array/List) or append.
        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        var idxLocal = il.DeclareLocal(_types.Int32);
        var argLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, idxLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var advance = il.DefineLabel();
        var notTSArray = il.DefineLabel();
        var notList = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // arg = args[i]
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, argLocal);

        // if (arg is $Array) AddRange(elements)
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, notTSArray);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayElementsGetter);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "AddRange", _types.IEnumerableOfObject));
        il.Emit(OpCodes.Br, advance);

        il.MarkLabel(notTSArray);
        // if (arg is List<object>) AddRange
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, notList);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "AddRange", _types.IEnumerableOfObject));
        il.Emit(OpCodes.Br, advance);

        il.MarkLabel(notList);
        // else: append the arg as a single element
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        il.MarkLabel(advance);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, idxLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>$Runtime.ArrayLikeMaterializeForIteration(object receiver) -&gt; List&lt;object&gt;</c>.
    /// Iterator-helper companion to <see cref="DeclareArrayLikeMaterialize"/>:
    /// for receivers that may carry descriptor side effects (Dictionary,
    /// $Object), returns a placeholder list of <c>length</c> nulls. The
    /// dispatch site has already stored the receiver in
    /// <c>_currentArrayLikeReceiver</c>; iterator helpers calling
    /// <see cref="EmitLoadArrayLikeElement"/> re-read each slot via
    /// <c>$Runtime.GetProperty</c> per iteration so that ECMA-262 23.1.3.*
    /// element-read ordering (getter at index N mutating index N+1 must be
    /// observed) is honored. Eager-receiver branches (List, $Array, string,
    /// $Arguments, ObjectArray, primitives) delegate to ArrayLikeMaterialize.
    /// </summary>
    internal void DeclareArrayLikeMaterializeForIteration(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.ArrayLikeMaterializeForIteration = typeBuilder.DefineMethod(
            "ArrayLikeMaterializeForIteration",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.Object]
        );
    }

    private void EmitArrayLikeMaterializeForIteration(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = runtime.ArrayLikeMaterializeForIteration;
        var il = method.GetILGenerator();

        // null/undefined → throw via the existing materializer (it handles the
        // ToObject(null) TypeError uniformly).
        var delegateLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, delegateLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, delegateLabel);

        // Array/List receivers can carry index accessors installed by
        // defineProperty and inherited indexed properties. Snapshot their raw
        // storage to preserve the iteration length while LoadArrayLikeElement re-reads observable
        // slots from the original receiver on each iteration.
        var notListReceiver = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.ArgumentsType);
        il.Emit(OpCodes.Brtrue, delegateLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, notListReceiver);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.IEnumerableOfObject);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.IEnumerableOfObject));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notListReceiver);

        // Lazy-eligible: Dictionary<string, object> — pre-detect holes via
        // ContainsKey (cheap, no getter fire) so EmitSkipIfHole's direct
        // list[i] check works while values are still re-read at iteration.
        var notDict = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, notDict);
        EmitLazyMaterializePath(il, runtime, holeAware: true);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notDict);

        // Lazy-eligible: $Object — placeholder is all-null. Holes aren't
        // detected (matches baseline TSObject behavior, which always visits
        // every index); element values are re-read at iteration so
        // descriptor side effects (issue #90) propagate.
        var notTSObject = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notTSObject);
        EmitLazyMaterializePath(il, runtime, holeAware: false);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTSObject);

        // Eager paths (List, $Array, string, ObjectArray, $Arguments, primitives,
        // null/undefined): defer to ArrayLikeMaterialize. The dispatch site's
        // _currentArrayLikeReceiver still points at the original receiver, but
        // LoadArrayLikeElement's type check will see it's NOT a Dict/$Object
        // and use list[i] instead of GetProperty.
        il.MarkLabel(delegateLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ArrayLikeMaterialize);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Reads <c>length</c> from arg0 (with full ToNumber side effects), clamps
    /// it to <c>[0, 1&lt;&lt;20]</c>, allocates a <c>List&lt;object&gt;</c> of
    /// that size, and fills it. When <paramref name="holeAware"/> is true (Dict
    /// path), each slot is checked via <c>ContainsKey</c>; absent slots get
    /// <c>$ArrayHole.Instance</c> so <see cref="EmitSkipIfHole"/>'s direct
    /// <c>list[idx]</c> check fires correctly. Present slots get null
    /// placeholders that <see cref="EmitLoadArrayLikeElement"/> overrides via
    /// <c>GetProperty</c> at iteration time. When false (TSObject path), all
    /// slots are filled with null. The dispatch site already stored the
    /// receiver in <c>_currentArrayLikeReceiver</c>.
    /// </summary>
    private void EmitLazyMaterializePath(ILGenerator il, EmittedRuntime runtime, bool holeAware)
    {
        var lenLocal = il.DeclareLocal(_types.Int32);
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        var idxLocal = il.DeclareLocal(_types.Int32);
        var dLocal = il.DeclareLocal(_types.Double);

        // d = ToNumber(GetProperty(receiver, "length"))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, dLocal);

        // NaN → 0
        var afterLen = il.DefineLabel();
        var notNaN = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, notNaN);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.Emit(OpCodes.Br, afterLen);
        il.MarkLabel(notNaN);

        // +Inf → 1<<20 cap (matches ArrayLikeMaterialize's existing clamp)
        var notPosInf = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsPositiveInfinity", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, notPosInf);
        il.Emit(OpCodes.Ldc_I4, 1 << 20);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.Emit(OpCodes.Br, afterLen);
        il.MarkLabel(notPosInf);

        // -Inf → 0
        var notNegInf = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNegativeInfinity", [_types.Double])!);
        il.Emit(OpCodes.Brfalse, notNegInf);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.Emit(OpCodes.Br, afterLen);
        il.MarkLabel(notNegInf);

        // Finite: clamp [0, 1<<20]
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        var notNeg = il.DefineLabel();
        il.Emit(OpCodes.Bge, notNeg);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.MarkLabel(notNeg);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldc_I4, 1 << 20);
        var notTooBig = il.DefineLabel();
        il.Emit(OpCodes.Ble, notTooBig);
        il.Emit(OpCodes.Ldc_I4, 1 << 20);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.MarkLabel(notTooBig);

        il.MarkLabel(afterLen);

        // list = new List<object>(len)
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.Int32));
        il.Emit(OpCodes.Stloc, listLocal);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, idxLocal);
        var fillLoop = il.DefineLabel();
        var fillDone = il.DefineLabel();

        if (holeAware)
        {
            // Dict path: probe ContainsKey AND PDS for each idx. ContainsKey
            // catches direct entries (`obj[k] = v`); PDS catches accessor-
            // defined slots (`Object.defineProperty(obj, k, {get/set: ...})`).
            // Either is "present" — push null placeholder so
            // LoadArrayLikeElement re-reads via GetProperty (firing the
            // getter for accessors). Neither → push $ArrayHole so
            // EmitSkipIfHole skips. Crucially, neither check fires the
            // getter, preserving lazy semantics (issue #90).
            var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
            il.Emit(OpCodes.Stloc, dictLocal);

            var containsKey = _types.GetMethod(_types.DictionaryStringObject,
                "ContainsKey", [_types.String])!;

            var keyStrLocal = il.DeclareLocal(_types.String);

            il.MarkLabel(fillLoop);
            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Bge, fillDone);

            // keyStr = idx.ToString()
            il.Emit(OpCodes.Ldloca, idxLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", Type.EmptyTypes)!);
            il.Emit(OpCodes.Stloc, keyStrLocal);

            var present = il.DefineLabel();
            var afterAdd = il.DefineLabel();

            // dict.ContainsKey(keyStr)?
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Ldloc, keyStrLocal);
            il.Emit(OpCodes.Callvirt, containsKey);
            il.Emit(OpCodes.Brtrue, present);

            // PDSGetPropertyDescriptor(dict, keyStr) — non-null = accessor present
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Ldloc, keyStrLocal);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Brtrue, present);

            // Absent: list.Add($ArrayHole.Instance)
            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Ldsfld, runtime.ArrayHoleInstance);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
            il.Emit(OpCodes.Br, afterAdd);

            il.MarkLabel(present);
            // Present: list.Add(null) — LoadArrayLikeElement overrides via GetProperty
            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
            il.MarkLabel(afterAdd);

            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, idxLocal);
            il.Emit(OpCodes.Br, fillLoop);
            il.MarkLabel(fillDone);
        }
        else
        {
            // TSObject path: all-null placeholder, no hole detection.
            il.MarkLabel(fillLoop);
            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Bge, fillDone);
            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
            il.Emit(OpCodes.Ldloc, idxLocal);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, idxLocal);
            il.Emit(OpCodes.Br, fillLoop);
            il.MarkLabel(fillDone);
        }

        // The dispatch site has already stored the receiver in
        // _currentArrayLikeReceiver; LoadArrayLikeElement reads it. Leave the
        // placeholder list on the stack for the caller's Ret.
        il.Emit(OpCodes.Ldloc, listLocal);
    }

    /// <summary>
    /// Emits <c>$Runtime.LoadArrayLikeElement(List&lt;object&gt; list, int idx) -&gt; object</c>.
    /// Replaces the direct <c>list[idx]</c> callvirt in iterator-helper IL.
    /// Uses the dispatch-site-set <c>_currentArrayLikeReceiver</c>, or the list
    /// argument for a direct array-method call. Descriptor-capable receivers
    /// re-read the observable slot through <c>$Runtime.GetProperty</c> for
    /// per-iteration accessor and prototype side effects (issue #90).
    /// </summary>
    /// <remarks>
    /// The type check disambiguates the two roles of
    /// <c>_currentArrayLikeReceiver</c>: it's also read by
    /// <see cref="EmitCallbackArgsAndInvoke"/> as the callback's array-slot
    /// arg. We can't tell from the field alone whether dispatch chose the
    /// lazy or the eager materializer. Strings, <c>$Arguments</c>, and other
    /// non-lazy receivers are fully populated by the eager materializer, so
    /// falling through to <c>list[idx]</c> remains correct for them.
    /// </remarks>
    internal void DeclareLoadArrayLikeElement(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.LoadArrayLikeElement = typeBuilder.DefineMethod(
            "LoadArrayLikeElement",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject, _types.Int32]
        );
    }

    private void EmitLoadArrayLikeElement(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = runtime.LoadArrayLikeElement;
        var il = method.GetILGenerator();

        var rcvrLocal = il.DeclareLocal(_types.Object);
        var listValLocal = il.DeclareLocal(_types.Object);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var keyStrLocal = il.DeclareLocal(_types.String);
        var dictValLocal = il.DeclareLocal(_types.Object);
        var pdsDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var returnListValLabel = il.DefineLabel();
        var returnHoleLabel = il.DefineLabel();
        var loadArrayPropertyLabel = il.DefineLabel();

        // listVal = list[idx]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, listValLocal);

        // var rcvr = _currentArrayLikeReceiver ?? list. Direct array method
        // calls bypass the generic dispatcher that initializes the field, but
        // arg0 is still their original observable receiver.
        il.Emit(OpCodes.Ldsfld, runtime.LazyArrayLikeReceiverField);
        il.Emit(OpCodes.Stloc, rcvrLocal);
        var haveReceiverLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, rcvrLocal);
        il.Emit(OpCodes.Brtrue, haveReceiverLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, rcvrLocal);
        il.MarkLabel(haveReceiverLabel);

        // $Array path: own descriptor accessors override the raw dense/sparse
        // slot. A raw hole is absent, so consult the prototype chain before
        // deciding whether iteration skips the index.
        var notTSArrayReceiver = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, rcvrLocal);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, notTSArrayReceiver);
        il.Emit(OpCodes.Ldarga_S, (byte)1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, keyStrLocal);
        il.Emit(OpCodes.Ldloc, rcvrLocal);
        il.Emit(OpCodes.Ldloc, keyStrLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, loadArrayPropertyLabel);
        il.Emit(OpCodes.Ldloc, listValLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brfalse, returnListValLabel);
        il.Emit(OpCodes.Ldloc, rcvrLocal);
        il.Emit(OpCodes.Ldloc, keyStrLocal);
        il.Emit(OpCodes.Call, runtime.HasArrayLikeProperty);
        il.Emit(OpCodes.Brfalse, returnHoleLabel);
        il.MarkLabel(loadArrayPropertyLabel);
        il.Emit(OpCodes.Ldloc, rcvrLocal);
        il.Emit(OpCodes.Ldloc, keyStrLocal);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTSArrayReceiver);

        // Plain compiled arrays are List<object>. They use the same observable
        // descriptor/prototype path as $Array above.
        var notListReceiver = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, rcvrLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, notListReceiver);
        il.Emit(OpCodes.Ldarga_S, (byte)1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, keyStrLocal);
        il.Emit(OpCodes.Ldloc, rcvrLocal);
        il.Emit(OpCodes.Ldloc, keyStrLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, loadArrayPropertyLabel);
        il.Emit(OpCodes.Ldloc, listValLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brfalse, returnListValLabel);
        il.Emit(OpCodes.Ldloc, rcvrLocal);
        il.Emit(OpCodes.Ldloc, keyStrLocal);
        il.Emit(OpCodes.Call, runtime.HasArrayLikeProperty);
        il.Emit(OpCodes.Brfalse, returnHoleLabel);
        il.Emit(OpCodes.Br, loadArrayPropertyLabel);
        il.MarkLabel(notListReceiver);

        // Dict path: per-iteration HasProperty check via TryGetValue + PDS.
        // - dict._fields hit: return value directly (fast path for plain
        //   `obj[k] = v` data entries; SetIndex routes accessor writes through
        //   SetProperty so _fields and PDS are mutually exclusive for any
        //   given key).
        // - PDS hit: invoke GetProperty so the accessor's get function fires
        //   (also handles accessors dynamically added during iteration —
        //   Test262 -b-X tests).
        // - Neither: return $ArrayHole so SkipIfHole skips this slot.
        // This re-check supersedes the materializer's static pre-detection
        // (which can't see slots added during iteration).
        il.Emit(OpCodes.Ldloc, rcvrLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        var notDict = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notDict);
        il.Emit(OpCodes.Ldloc, rcvrLocal);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);
        // keyStr = idx.ToString()
        il.Emit(OpCodes.Ldarga_S, (byte)1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, keyStrLocal);
        // if (dict.TryGetValue(keyStr, out dictVal)) return dictVal;
        var tryGetValue = _types.GetMethod(_types.DictionaryStringObject,
            "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, keyStrLocal);
        il.Emit(OpCodes.Ldloca, dictValLocal);
        il.Emit(OpCodes.Callvirt, tryGetValue);
        var notInDict = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notInDict);
        il.Emit(OpCodes.Ldloc, dictValLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notInDict);
        // Not in dict._fields: check own PDS for accessor descriptor.
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, keyStrLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, pdsDescLocal);
        il.Emit(OpCodes.Ldloc, pdsDescLocal);
        var noOwnPds = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, noOwnPds);
        // Own PDS descriptor exists — fire GetProperty so the accessor runs
        // and any throw propagates. Note: an own accessor with no `get`
        // returns undefined; we propagate that undefined (HasProperty is
        // still true) instead of treating it as a hole.
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, keyStrLocal);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(noOwnPds);
        // Not own (neither in _fields nor own PDS). ECMA-262 7.3.10
        // HasProperty walks the prototype chain — invoke the existence-only
        // helper so we distinguish HasProperty=true (callback fires with
        // value, even if undefined from a set-only accessor or a getter
        // returning undefined) from HasProperty=false (skip via $ArrayHole).
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, keyStrLocal);
        il.Emit(OpCodes.Call, runtime.HasArrayLikeProperty);
        il.Emit(OpCodes.Brfalse, returnHoleLabel);
        // Found somewhere on the chain — fire GetProperty (which walks the
        // chain itself and invokes accessors).
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, keyStrLocal);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notDict);

        // $Object path: same own-or-chain HasProperty check as Dict, then
        // either fire GetProperty (which fires the get accessor / walks the
        // chain) or signal hole. Pre-fix this branch unconditionally invoked
        // GetProperty regardless of HasProperty, which matched baseline
        // TSObject materializer behavior but skipped the spec-correct
        // distinction; with HasArrayLikeProperty, callbacks correctly fire
        // for inherited set-only accessors (val=undefined) and skip for
        // truly absent slots.
        il.Emit(OpCodes.Ldloc, rcvrLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, returnListValLabel);
        var tsoKeyStrLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarga_S, (byte)1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, tsoKeyStrLocal);
        il.Emit(OpCodes.Ldloc, rcvrLocal);
        il.Emit(OpCodes.Ldloc, tsoKeyStrLocal);
        il.Emit(OpCodes.Call, runtime.HasArrayLikeProperty);
        il.Emit(OpCodes.Brfalse, returnHoleLabel);
        il.Emit(OpCodes.Ldloc, rcvrLocal);
        il.Emit(OpCodes.Ldloc, tsoKeyStrLocal);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnHoleLabel);
        il.Emit(OpCodes.Ldsfld, runtime.ArrayHoleInstance);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnListValLabel);
        il.Emit(OpCodes.Ldloc, listValLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits <c>$Runtime.HasArrayLikeProperty(object obj, string key) -&gt; bool</c>:
    /// existence-only check (no get accessors fire) for the iterator-helper
    /// element loader. Walks own dict._fields, own PDS, then prototype chain
    /// (Dict and $Object) via PDSGetPrototype until null. Used by
    /// <see cref="EmitLoadArrayLikeElement"/> to distinguish ECMA-262
    /// HasProperty=false (absent → return $ArrayHole) from HasProperty=true
    /// with undefined value (present → callback fires with undefined).
    /// </summary>
    internal void DeclareHasArrayLikeProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        runtime.HasArrayLikeProperty = typeBuilder.DefineMethod(
            "HasArrayLikeProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.String]
        );
    }

    private void EmitHasArrayLikeProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = runtime.HasArrayLikeProperty;
        var il = method.GetILGenerator();

        var currentLocal = il.DeclareLocal(_types.Object);
        var dictTmpLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var loopStart = il.DefineLabel();
        var checkObjectPrototypeLabel = il.DefineLabel();
        var trueLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();

        // current = obj. Track whether we've already inspected
        // ObjectPrototypeField — once obj's explicit chain runs out
        // (PDSGetPrototype returns null) we look there one final time, the
        // same fallback GetProperty uses for default-prototype Dicts.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, currentLocal);

        il.MarkLabel(loopStart);
        // if (current == null) → check Object.prototype as final fallback
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Brfalse, checkObjectPrototypeLabel);

        // Proxy [[HasProperty]] is observable and must dispatch the `has`
        // trap. This also applies when a proxy appears later in the receiver's
        // prototype chain.
        var notProxyLabel = il.DefineLabel();
        EmitProxyHasCheck(
            il,
            () => il.Emit(OpCodes.Ldloc, currentLocal),
            () => il.Emit(OpCodes.Ldarg_1),
            notProxyLabel,
            runtime);
        il.MarkLabel(notProxyLabel);

        // Dict branch: ContainsKey OR PDS
        var notDictLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, notDictLabel);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictTmpLocal);
        // dict.ContainsKey(key)
        il.Emit(OpCodes.Ldloc, dictTmpLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", [_types.String])!);
        il.Emit(OpCodes.Brtrue, trueLabel);
        // PDSGetPropertyDescriptor(current, key) — non-null = own accessor.
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, trueLabel);
        // Walk prototype: current = PDSGetPrototype(current)
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPrototype);
        il.Emit(OpCodes.Stloc, currentLocal);
        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(notDictLabel);

        // $Object branch: TSObjectHasProperty for own check, then chain walk.
        var notTSObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notTSObjectLabel);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectHasProperty);
        il.Emit(OpCodes.Brtrue, trueLabel);
        // PDS check on TSObject too — defineProperty on a $Object lands in PDS.
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, trueLabel);
        // Walk prototype
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPrototype);
        il.Emit(OpCodes.Stloc, currentLocal);
        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(notTSObjectLabel);

        // $Array branch: check a real (non-hole) own index, then continue via
        // its intrinsic/custom prototype when absent.
        var notTSArrayLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, notTSArrayLabel);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, trueLabel);
        var tsArrayIdxLocal = il.DeclareLocal(_types.Int64);
        var tsArrayWalkPrototype = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, tsArrayIdxLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int64, "TryParse", [_types.String, _types.Int64.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, tsArrayWalkPrototype);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Ldloc, tsArrayIdxLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayHasIndex);
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.MarkLabel(tsArrayWalkPrototype);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Call, runtime.ObjectGetPrototypeOf);
        il.Emit(OpCodes.Stloc, currentLocal);
        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(notTSArrayLabel);

        // List<object> branch: when the prototype chain walks into
        // an array (e.g., `foo.prototype = new Array(1,2,3); var f = new foo();
        // f.forEach(cb)` — `f`'s prototype IS the array, and array indices
        // count as inherited "own" properties). Numeric key + in-range index
        // → HasProperty true.
        var notListLabel = il.DefineLabel();
        var walkListPrototypeLabel = il.DefineLabel();
        var listForCheckLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, notListLabel);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, listForCheckLocal);

        var checkListIndex = il.DefineLabel();
        il.MarkLabel(checkListIndex);
        // Try parsing key as int. If it parses to a non-negative idx < count,
        // HasProperty true.
        var listIdxLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, listIdxLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", [_types.String, _types.Int32.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, walkListPrototypeLabel);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, walkListPrototypeLabel);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Ldloc, listForCheckLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, walkListPrototypeLabel);
        il.Emit(OpCodes.Ldloc, listForCheckLocal);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brtrue, walkListPrototypeLabel);
        il.Emit(OpCodes.Br, trueLabel);

        il.MarkLabel(walkListPrototypeLabel);
        il.Emit(OpCodes.Ldloc, currentLocal);
        il.Emit(OpCodes.Call, runtime.ObjectGetPrototypeOf);
        il.Emit(OpCodes.Stloc, currentLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(notListLabel);
        // Other types in the chain (primitives, etc.): bail out.
        il.Emit(OpCodes.Br, falseLabel);

        // Default-prototype fallback: when the explicit PDS prototype chain
        // is exhausted, mirror GetProperty's check of the lazily-populated
        // ObjectPrototypeField singleton (where toString/valueOf/etc. live,
        // and where Test262 tests like `Object.prototype[0] = true` install
        // inherited indexed data properties — those would otherwise look
        // absent here).
        il.MarkLabel(checkObjectPrototypeLabel);
        il.Emit(OpCodes.Call, runtime.ObjectPrototypePopulateMethod);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", [_types.String])!);
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Br, falseLabel);

        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }
}

