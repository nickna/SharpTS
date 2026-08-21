using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits JsonStringifyFull as pure IL for standalone support.
    /// Signature: JsonStringifyFull(object? value, object? replacer, object? space) -> object?
    /// </summary>
    private void EmitJsonStringifyFull(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // First emit the escape helper (needed by stringify)
        EmitEscapeJsonStringHelper(typeBuilder);

        // Then emit the helper method for recursive stringification
        var stringifyFullHelper = EmitStringifyValueFullHelper(typeBuilder, runtime);

        var method = typeBuilder.DefineMethod(
            "JsonStringifyFull",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]  // value, replacer, space
        );
        runtime.JsonStringifyFull = method;

        var il = method.GetILGenerator();

        // Locals
        var indentStrLocal = il.DeclareLocal(_types.String);     // string indentStr
        var replacerFuncLocal = il.DeclareLocal(_types.Object);  // $TSFunction or null
        var allowedKeysLocal = il.DeclareLocal(_types.ListOfString);  // HashSet<string> or null
        var spaceDoubleLocal = il.DeclareLocal(_types.Double);
        var countLocal = il.DeclareLocal(_types.Int32);

        // Labels
        var spaceIsStringLabel = il.DefineLabel();
        var spaceIsNullLabel = il.DefineLabel();
        var spaceDoneLabel = il.DefineLabel();
        var replacerIsListLabel = il.DefineLabel();
        var replacerDoneLabel = il.DefineLabel();

        // ============ Parse space parameter ============
        // indentStr = ""
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, indentStrLocal);

        // ECMA-262 25.5.2.1 step 5: a boxed Number/String wrapper `space` contributes
        // its primitive (Number → ToNumber, String → ToString) before the numeric/
        // string indent rules below — honoring an own valueOf/toString override (#574).
        // See EmitBoxedPrimitiveJsonCoerce (RuntimeEmitter.Json.Stringify.cs).
        var spaceLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, spaceLocal);
        EmitBoxedPrimitiveJsonCoerce(il, spaceLocal, runtime);

        // if (space == null) goto spaceDoneLabel
        il.Emit(OpCodes.Ldloc, spaceLocal);
        il.Emit(OpCodes.Brfalse, spaceDoneLabel);

        // if (space is double)
        il.Emit(OpCodes.Ldloc, spaceLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, spaceIsStringLabel);

        // space is double - convert to int spaces
        // count = (int)Math.Min(Math.Max((double)space, 0), 10)
        il.Emit(OpCodes.Ldloc, spaceLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, spaceDoubleLocal);

        // Math.Max(space, 0)
        il.Emit(OpCodes.Ldloc, spaceDoubleLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Max", [_types.Double, _types.Double]));

        // Math.Min(result, 10)
        il.Emit(OpCodes.Ldc_R8, 10.0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Min", [_types.Double, _types.Double]));

        // Convert to int
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, countLocal);

        // indentStr = new string(' ', count)
        il.Emit(OpCodes.Ldc_I4, (int)' ');
        il.Emit(OpCodes.Ldloc, countLocal);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.String, [_types.Char, _types.Int32]));
        il.Emit(OpCodes.Stloc, indentStrLocal);
        il.Emit(OpCodes.Br, spaceDoneLabel);

        // space is string
        il.MarkLabel(spaceIsStringLabel);
        il.Emit(OpCodes.Ldloc, spaceLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, spaceDoneLabel);

        // indentStr = space.Length > 10 ? space.Substring(0, 10) : space
        il.Emit(OpCodes.Ldloc, spaceLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, 10);
        var noTruncateLabel = il.DefineLabel();
        il.Emit(OpCodes.Ble, noTruncateLabel);

        // Truncate to 10 chars
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4, 10);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", [_types.Int32, _types.Int32]));
        il.Emit(OpCodes.Stloc, indentStrLocal);
        il.Emit(OpCodes.Br, spaceDoneLabel);

        il.MarkLabel(noTruncateLabel);
        il.Emit(OpCodes.Stloc, indentStrLocal);

        il.MarkLabel(spaceDoneLabel);

        // ============ Parse replacer parameter ============
        // replacerFunc = null, allowedKeys = null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, replacerFuncLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, allowedKeysLocal);

        // if (replacer == null) goto replacerDoneLabel
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, replacerDoneLabel);

        // if (replacer is List<object?>)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, replacerIsListLabel);

        // ECMA-262 25.5.2.1 step 4: a non-callable, non-array replacer is silently
        // ignored (PropertyList stays empty / replacerFunction stays null).
        // Only treat as function if it's actually a callable type.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        var replacerIsFnLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, replacerIsFnLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, replacerIsFnLabel);
        // Not callable, not array → ignore (replacerFunc stays null).
        il.Emit(OpCodes.Br, replacerDoneLabel);
        il.MarkLabel(replacerIsFnLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, replacerFuncLocal);
        il.Emit(OpCodes.Br, replacerDoneLabel);

        // replacer is List - convert to HashSet<string>
        il.MarkLabel(replacerIsListLabel);
        EmitConvertListToHashSet(il, allowedKeysLocal, runtime);

        il.MarkLabel(replacerDoneLabel);

        // ECMA-262 25.5.2.1 step 12: SerializeJSONProperty("", { "": value }).
        // The replacer function (if any) is called at the root with key=""
        // and value=value, and the return value is what gets stringified.
        // Without this root-level invocation, `JSON.stringify({prop:1}, fn)`
        // bypasses fn for the outermost wrapper — the inner object iteration
        // calls fn for "prop" but fn never sees ("", {prop:1}). Per spec, an
        // undefined return at root makes JSON.stringify return undefined too.
        var rootValueLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, rootValueLocal);

        // Create the spec's synthetic ordinary-object holder with one own
        // writable/enumerable/configurable data property named "". Populate
        // the backing dictionary directly so an inherited Object.prototype
        // setter for "" is not invoked.
        var rootHolderFieldsLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var rootHolderLocal = il.DeclareLocal(runtime.TSObjectType);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, rootHolderFieldsLocal);
        il.Emit(OpCodes.Ldloc, rootHolderFieldsLocal);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryStringObject, "Add", _types.String, _types.Object));
        il.Emit(OpCodes.Ldloc, rootHolderFieldsLocal);
        il.Emit(OpCodes.Newobj, runtime.TSObjectCtor);
        il.Emit(OpCodes.Stloc, rootHolderLocal);
        il.Emit(OpCodes.Call, runtime.ObjectPrototypePopulateMethod);
        il.Emit(OpCodes.Ldloc, rootHolderLocal);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
        il.Emit(OpCodes.Call, runtime.PDSSetPrototype);

        // Per ECMA-262 25.5.2.3 SerializeJSONProperty step 2: toJSON runs
        // BEFORE step 3 (replacer). At the root, key = "" — the synthetic
        // wrapper is `{ "": value }` per step 12. Pass "" so toJSON sees
        // the spec-required key arg.
        EmitToJsonCheck(il, rootValueLocal, runtime, "");

        var skipRootReplacerLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, replacerFuncLocal);
        il.Emit(OpCodes.Brfalse, skipRootReplacerLabel);

        var rootArgsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, rootValueLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Stloc, rootArgsLocal);

        var rootIsTSFunctionLabel = il.DefineLabel();
        var rootIsBoundLabel = il.DefineLabel();
        var rootCallDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, replacerFuncLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, rootIsTSFunctionLabel);
        il.Emit(OpCodes.Ldloc, replacerFuncLocal);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, rootIsBoundLabel);
        il.Emit(OpCodes.Br, rootCallDoneLabel);

        il.MarkLabel(rootIsTSFunctionLabel);
        il.Emit(OpCodes.Ldloc, replacerFuncLocal);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldloc, rootHolderLocal);
        il.Emit(OpCodes.Ldloc, rootArgsLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
        il.Emit(OpCodes.Stloc, rootValueLocal);
        il.Emit(OpCodes.Br, rootCallDoneLabel);

        il.MarkLabel(rootIsBoundLabel);
        il.Emit(OpCodes.Ldloc, replacerFuncLocal);
        il.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Ldloc, rootHolderLocal);
        il.Emit(OpCodes.Ldloc, rootArgsLocal);
        il.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvokeWithThis);
        il.Emit(OpCodes.Stloc, rootValueLocal);

        il.MarkLabel(rootCallDoneLabel);
        il.MarkLabel(skipRootReplacerLabel);

        // ============ Call helper method ============
        // return StringifyValueFull(value, replacerFunc, allowedKeys, indentStr, 0, "")
        // ECMA-262: a null helper return at root means "JSON.stringify returns
        // undefined" (e.g. `JSON.stringify(undefined)` or replacer returning
        // undefined for the root). Map null → $Undefined.Instance so the JS
        // surface sees the spec's `undefined`. Root key is "" per
        // ECMA-262 25.5.2.1 step 12 (synthetic wrapper `{ "": value }`).
        var resultLocalRoot = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, rootValueLocal);     // value (post-replacer)
        il.Emit(OpCodes.Ldloc, replacerFuncLocal);   // replacer
        il.Emit(OpCodes.Ldloc, allowedKeysLocal);    // allowedKeys
        il.Emit(OpCodes.Ldloc, indentStrLocal);      // indentStr
        il.Emit(OpCodes.Ldc_I4_0);                   // depth = 0
        il.Emit(OpCodes.Ldstr, "");                  // key = ""
        il.Emit(OpCodes.Call, stringifyFullHelper);
        il.Emit(OpCodes.Stloc, resultLocalRoot);
        il.Emit(OpCodes.Ldloc, resultLocalRoot);
        var resultNonNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, resultNonNullLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(resultNonNullLabel);
        il.Emit(OpCodes.Ldloc, resultLocalRoot);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits code to convert List&lt;object?&gt; to HashSet&lt;string&gt;. Per ECMA-262
    /// 25.5.2.1 step 4.b: strings pass through; numbers ToString-coerce; a boxed
    /// <c>new String</c>/<c>new Number</c> wrapper (object with a [[StringData]]/
    /// [[NumberData]] slot) ToString-coerces too — honoring an own toString/valueOf
    /// override (#574, via <c>$Runtime.ToJsString</c>'s string-hint ToPrimitive);
    /// other types are dropped. Mirrors
    /// <c>Interpreter.TryCoerceReplacerArrayKey</c>.
    /// </summary>
    private void EmitConvertListToHashSet(ILGenerator il, LocalBuilder allowedKeysLocal, EmittedRuntime runtime)
    {
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        var iLocal = il.DeclareLocal(_types.Int32);
        var elemLocal = il.DeclareLocal(_types.Object);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var skipLabel = il.DefineLabel();
        var addLabel = il.DefineLabel();
        var keyLocal = il.DeclareLocal(_types.String);
        var tagLocal = il.DeclareLocal(_types.String);

        // number[] unboxing: materialize a numeric-mode $Array replacer before reading its base list.
        EmitDeoptArgIfNumericArray(il, runtime, 1);

        // Cast replacer to List<object?>
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);

        // allowedKeys = new HashSet<string>()
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfString, Type.EmptyTypes));
        il.Emit(OpCodes.Stloc, allowedKeysLocal);

        // for (int i = 0; i < list.Count; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // elem = list[i]
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", [_types.Int32]));
        il.Emit(OpCodes.Stloc, elemLocal);

        // if (elem is string) keyLocal = (string)elem
        il.Emit(OpCodes.Ldloc, elemLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        var notStringLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notStringLabel);
        il.Emit(OpCodes.Ldloc, elemLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, keyLocal);
        il.Emit(OpCodes.Br, addLabel);
        il.MarkLabel(notStringLabel);

        // if (elem is double) keyLocal = $Runtime.Stringify(elem)
        var notDoubleLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, elemLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notDoubleLabel);
        il.Emit(OpCodes.Ldloc, elemLocal);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Stloc, keyLocal);
        il.Emit(OpCodes.Br, addLabel);

        // else if elem is a boxed String/Number wrapper → ToString (#574).
        // Only an $Object / Dictionary carrying a "Number"/"String" __primitiveType
        // tag is a wrapper; any other object element is dropped per spec.
        il.MarkLabel(notDoubleLabel);
        var checkTagLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, elemLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, checkTagLabel);
        il.Emit(OpCodes.Ldloc, elemLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, skipLabel);
        il.MarkLabel(checkTagLabel);
        il.Emit(OpCodes.Ldloc, elemLocal);
        il.Emit(OpCodes.Ldstr, "__primitiveType");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, tagLocal);
        il.Emit(OpCodes.Ldloc, tagLocal);
        il.Emit(OpCodes.Brfalse, skipLabel);
        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);
        var wrapperKeyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tagLocal);
        il.Emit(OpCodes.Ldstr, "Number");
        il.Emit(OpCodes.Call, strEq);
        il.Emit(OpCodes.Brtrue, wrapperKeyLabel);
        il.Emit(OpCodes.Ldloc, tagLocal);
        il.Emit(OpCodes.Ldstr, "String");
        il.Emit(OpCodes.Call, strEq);
        il.Emit(OpCodes.Brfalse, skipLabel);
        il.MarkLabel(wrapperKeyLabel);
        il.Emit(OpCodes.Ldloc, elemLocal);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, keyLocal);

        il.MarkLabel(addLabel);
        // Dedup before Add (List<T>.Contains is O(n) but replacer arrays are
        // typically small; preserves insertion order per spec PropertyList).
        il.Emit(OpCodes.Ldloc, allowedKeysLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfString, "Contains", [_types.String]));
        il.Emit(OpCodes.Brtrue, skipLabel);
        il.Emit(OpCodes.Ldloc, allowedKeysLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfString, "Add", [_types.String]));

        il.MarkLabel(skipLabel);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
    }

    /// <summary>
    /// Emits the StringifyValueFull helper method for recursive JSON stringification with full options.
    /// Signature: StringifyValueFull(object? value, object? replacer, HashSet&lt;string&gt;? allowedKeys, string indentStr, int depth, string key) -> string?
    /// The trailing key is the property name passed to toJSON / replacer per ECMA-262 25.5.2.3
    /// (array recursion passes ToString(index); object recursion passes the property name).
    /// </summary>
    private MethodBuilder EmitStringifyValueFullHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringifyValueFull",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Object, _types.ListOfString, _types.String, _types.Int32, _types.String]
            // value, replacer, allowedKeys, indentStr, depth, key
        );

        var il = method.GetILGenerator();
        var valueLocal = il.DeclareLocal(_types.Object);
        var allowPooledDictionaryKeysLocal = il.DeclareLocal(_types.Boolean);

        var nullLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var doubleLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var classInstanceLabel = il.DefineLabel();

        // Depth cap — recursive cycles (`a.self = a`) would otherwise recurse
        // unbounded and stack-overflow. ECMA-262 requires TypeError; the cap
        // is sized well above any legitimate nesting (512). Arg 4 = depth.
        var depthOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Ldc_I4, 512);
        il.Emit(OpCodes.Blt, depthOkLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Converting circular structure to JSON");
        il.MarkLabel(depthOkLabel);

        // Store value in local
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, allowPooledDictionaryKeysLocal);

        // ECMA-262 25.5.2.1: undefined values are dropped — for arrays the
        // caller maps null→"null" via `?? "null"`, for objects the caller
        // skips the key on null. So return C# null here for $Undefined.
        // `JSON.stringify(undefined)` returns undefined; `[undefined]` →
        // `"[null]"`; `{a: undefined}` → `"{}"`. Replacer-returned undefined
        // also flows through this path.
        var undefRetNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, undefRetNullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(undefRetNullLabel);

        // if (value == null) return "null";
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Brfalse, nullLabel);

        var notRawJsonLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.TSRawJsonType);
        il.Emit(OpCodes.Brfalse, notRawJsonLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, runtime.TSRawJsonType);
        il.Emit(OpCodes.Callvirt, runtime.TSRawJsonTextGetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notRawJsonLabel);

        // ECMA-262 25.5.2.3 step 9: skip callable values (return undefined).
        EmitFunctionSkipCheck(il, valueLocal, runtime);

        // Boxed-primitive unwrap (ECMA-262 25.5.2.3 step 4.a-c), honoring an own
        // valueOf/toString override (#574). See EmitBoxedPrimitiveJsonCoerce
        // (RuntimeEmitter.Json.Stringify.cs) for the rationale.
        EmitBoxedPrimitiveJsonCoerce(il, valueLocal, runtime);

        // The caller has already applied toJSON and the replacer. Unwrap a
        // boxed BigInt result, then perform step 10's mandatory rejection.
        EmitBigIntCheck(il, valueLocal, runtime);

        // Proxy materialization (#92): if value is SharpTSProxy, dispatch its
        // [[OwnPropertyKeys]] / [[Get]] traps and substitute a Dictionary so the
        // existing dict path serializes the proxied view. A revoked proxy throws
        // from TrapOwnKeys → naturally surfaces the spec-required TypeError.
        var notProxyLabelFull = il.DefineLabel();
        EmitProxyMaterializeForJson(il, valueLocal, notProxyLabelFull, runtime);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, allowPooledDictionaryKeysLocal);
        il.Emit(OpCodes.Br, dictLabel);
        il.MarkLabel(notProxyLabelFull);

        // Type checks
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, boolLabel);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, doubleLabel);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listLabel);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // ECMA-262 25.5.2.3: $RegExp has no own enumerable properties, so
        // SerializeJSONObject yields "{}". Pre-fix fell through to "null".
        // Skip when UsesRegExp gated off — no RegExp values exist.
        if (_features.UsesRegExp)
        {
            var notRegExpLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            il.Emit(OpCodes.Brfalse, notRegExpLabel);
            il.Emit(OpCodes.Ldstr, "{}");
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notRegExpLabel);
        }

        // Check for emitted $Object instance
        EmitIsClassInstanceCheck(il, valueLocal, classInstanceLabel, runtime);

        // Default: return "null"
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Ret);

        // bool
        il.MarkLabel(boolLabel);
        var trueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Ldstr, "false");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldstr, "true");
        il.Emit(OpCodes.Ret);

        // double
        il.MarkLabel(doubleLabel);
        EmitFormatNumber(il, valueLocal, runtime);

        // string - escape for JSON
        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, _escapeJsonStringMethod!);
        il.Emit(OpCodes.Ret);

        // List<object?> - stringify array with full options
        il.MarkLabel(listLabel);
        EmitStringifyArrayFull(il, method, valueLocal, runtime);

        // Dictionary<string, object?> - stringify object with full options
        il.MarkLabel(dictLabel);
        EmitStringifyObjectFull(
            il, method, valueLocal, runtime, allowPooledDictionaryKeysLocal);

        // Class instance
        il.MarkLabel(classInstanceLabel);
        var classFieldsLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var classHolderLocal = il.DeclareLocal(_types.Object);
        var noClassFieldsLabel = il.DefineLabel();
        // Use TSObjectMergeEnumerable to include accessor (getter) properties
        // alongside data props per ECMA-262 25.5.2.4 EnumerableOwnPropertyNames.
        // For non-$Object IHasFields receivers, this falls back to the same
        // Fields getter that the original code used.
        // Preserve the original object identity for replacer `this`.  The
        // enumerable snapshot below is only a serialization view; exposing it
        // as the holder breaks SameValue checks for compact-record sources.
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Stloc, classHolderLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, runtime.TSObjectMergeEnumerable);
        il.Emit(OpCodes.Stloc, classFieldsLocal);
        il.Emit(OpCodes.Ldloc, classFieldsLocal);
        il.Emit(OpCodes.Brfalse, noClassFieldsLabel);
        il.Emit(OpCodes.Ldloc, classFieldsLocal);
        il.Emit(OpCodes.Stloc, valueLocal);
        EmitStringifyObjectFull(
            il, method, valueLocal, runtime, null, classHolderLocal);
        il.MarkLabel(noClassFieldsLabel);
        il.Emit(OpCodes.Ldstr, "{}");
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits array stringification with full options (replacer, indentation).
    /// </summary>
    private void EmitStringifyArrayFull(ILGenerator il, MethodBuilder stringifyMethod, LocalBuilder valueLocal, EmittedRuntime runtime)
    {
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        var arrLocal = il.DeclareLocal(_types.ListOfObject);
        var iLocal = il.DeclareLocal(_types.Int32);
        var newlineLocal = il.DeclareLocal(_types.String);
        var closeLocal = il.DeclareLocal(_types.String);
        var elemLocal = il.DeclareLocal(_types.Object);
        var strResultLocal = il.DeclareLocal(_types.String);
        var returnValueLocal = il.DeclareLocal(_types.String);

        // number[] unboxing: materialize a numeric-mode $Array before reading its base list.
        EmitDeoptIfNumericArray(il, runtime, () => il.Emit(OpCodes.Ldloc, valueLocal));

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var hasIndentLabel = il.DefineLabel();
        var noIndentLabel = il.DefineLabel();
        var indentDoneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, arrLocal);

        // if (arr.Count == 0) return "[]";
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notEmpty);
        il.Emit(OpCodes.Ldstr, "[]");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEmpty);

        // Check if indent is needed (indentStr.Length > 0)
        il.Emit(OpCodes.Ldarg_3);  // indentStr
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, hasIndentLabel);

        // No indent
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, newlineLocal);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, closeLocal);
        il.Emit(OpCodes.Br, indentDoneLabel);

        // Has indent - compute newline and close strings
        il.MarkLabel(hasIndentLabel);
        // newline = "\n" + RepeatString(indentStr, depth + 1)
        EmitComputeNewline(il, newlineLocal, closeLocal);

        il.MarkLabel(indentDoneLabel);

        il.Emit(OpCodes.Call, _jsonRentStringBuilderMethod!);
        il.Emit(OpCodes.Stloc, sbLocal);
        var cleanupDone = il.DefineLabel();
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "[");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // for loop
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // if (i > 0) sb.Append(",");
        il.Emit(OpCodes.Ldloc, iLocal);
        var skipComma = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, skipComma);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipComma);

        // sb.Append(newline);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, newlineLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // elem = arr[i]
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", [_types.Int32]));
        il.Emit(OpCodes.Stloc, elemLocal);

        // Stage E.2 M5: ECMA-262 25.5.2.4 SerializeJSONArray — a hole slot
        // serializes as "null". The replacer is NOT invoked for holes
        // (SerializeJSONProperty short-circuits on missing properties).
        var holeAppendedLabel = il.DefineLabel();
        var notHoleLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, elemLocal);
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brfalse, notHoleLabel);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, holeAppendedLabel);

        il.MarkLabel(notHoleLabel);

        // SerializeJSONProperty order: toJSON first, then replacer. Both see
        // the string form of the array index as the property key.
        var elemKeyLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloca, iLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int32, "ToString"));
        il.Emit(OpCodes.Stloc, elemKeyLocal);
        EmitToJsonCheck(il, elemLocal, runtime, keyLocal: elemKeyLocal);
        EmitCallReplacerIfNeeded(il, elemLocal, elemKeyLocal, arrLocal, runtime);

        // strResult = StringifyValueFull(elem, replacer, allowedKeys, indentStr, depth + 1, i.ToString())
        // ECMA-262 25.5.2.4 SerializeJSONArray step 8.a — the key passed down
        // is ToString(F(I)). Int32.ToString() with no args is culture-invariant
        // for non-negative ints (digit chars only).
        il.Emit(OpCodes.Ldloc, elemLocal);
        il.Emit(OpCodes.Ldarg_1);  // replacer
        il.Emit(OpCodes.Ldarg_2);  // allowedKeys
        il.Emit(OpCodes.Ldarg_3);  // indentStr
        il.Emit(OpCodes.Ldarg, 4); // depth
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, elemKeyLocal);
        il.Emit(OpCodes.Call, stringifyMethod);
        il.Emit(OpCodes.Stloc, strResultLocal);

        // sb.Append(strResult ?? "null")
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, strResultLocal);
        il.Emit(OpCodes.Dup);
        var notNullResult = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notNullResult);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "null");
        il.MarkLabel(notNullResult);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        il.MarkLabel(holeAppendedLabel);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // sb.Append(close);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, closeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // sb.Append("]");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "]");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, returnValueLocal);
        il.Emit(OpCodes.Leave, cleanupDone);

        il.BeginFinallyBlock();
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Call, _jsonReturnStringBuilderMethod!);
        il.EndExceptionBlock();

        il.MarkLabel(cleanupDone);
        il.Emit(OpCodes.Ldloc, returnValueLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits code to compute newline and close strings for indentation.
    /// </summary>
    private void EmitComputeNewline(ILGenerator il, LocalBuilder newlineLocal, LocalBuilder closeLocal)
    {
        // newline = "\n" + RepeatString(indentStr, depth + 1)
        // We need String.Concat and a loop or use string constructor

        // For simplicity, use StringBuilder to build the indent
        var sbTemp = il.DeclareLocal(_types.StringBuilder);
        var jLocal = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // Build newline: "\n" + (indentStr * (depth + 1))
        il.Emit(OpCodes.Ldstr, "\n");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, [_types.String]));
        il.Emit(OpCodes.Stloc, sbTemp);

        // for (int j = 0; j <= depth; j++) sb.Append(indentStr);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, jLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldarg, 4);  // depth
        il.Emit(OpCodes.Bgt, loopEnd);

        il.Emit(OpCodes.Ldloc, sbTemp);
        il.Emit(OpCodes.Ldarg_3);  // indentStr
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, jLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        il.Emit(OpCodes.Ldloc, sbTemp);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, newlineLocal);

        // Build close: "\n" + (indentStr * depth)
        var sbTemp2 = il.DeclareLocal(_types.StringBuilder);
        var loopStart2 = il.DefineLabel();
        var loopEnd2 = il.DefineLabel();

        il.Emit(OpCodes.Ldstr, "\n");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, [_types.String]));
        il.Emit(OpCodes.Stloc, sbTemp2);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, jLocal);

        il.MarkLabel(loopStart2);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldarg, 4);  // depth
        il.Emit(OpCodes.Bge, loopEnd2);

        il.Emit(OpCodes.Ldloc, sbTemp2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, jLocal);
        il.Emit(OpCodes.Br, loopStart2);

        il.MarkLabel(loopEnd2);

        il.Emit(OpCodes.Ldloc, sbTemp2);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, closeLocal);
    }

    /// <summary>
    /// Emits code to call the replacer function if it exists.
    /// For arrays: replacer.call(holder, index, elem) -> elem.
    /// Per ECMA-262 25.5.2.1 SerializeJSONProperty step 3.a, the replacer is
    /// invoked with the holder (parent array) as `this`.
    /// Handles both $TSFunction and $BoundTSFunction.
    /// </summary>
    private void EmitCallReplacerIfNeeded(ILGenerator il, LocalBuilder elemLocal, LocalBuilder keyLocal, LocalBuilder holderLocal, EmittedRuntime runtime)
    {
        var skipLabel = il.DefineLabel();
        var isTSFunctionLabel = il.DefineLabel();
        var isBoundLabel = il.DefineLabel();
        var callDoneLabel = il.DefineLabel();
        var argsLocal = il.DeclareLocal(_types.ObjectArray);

        // if (replacer == null) skip
        il.Emit(OpCodes.Ldarg_1);  // replacer
        il.Emit(OpCodes.Brfalse, skipLabel);

        // Create object[] { key, elem } and store in local
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, elemLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Stloc, argsLocal);

        // Check if replacer is $TSFunction
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, isTSFunctionLabel);

        // Check if replacer is $BoundTSFunction
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, isBoundLabel);

        // Unknown type - use InvokeValue fallback
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Stloc, elemLocal);
        il.Emit(OpCodes.Br, callDoneLabel);

        // isTSFunctionLabel: call $TSFunction.InvokeWithThis(holder, args).
        // Per spec the replacer's `this` is the parent (holder); function
        // expressions emit a leading `__this` parameter so InvokeWithThis is
        // the right entry point — prepends holder under the __this slot so the
        // user's `(k, v)` lines up with [key, val].
        il.MarkLabel(isTSFunctionLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldloc, holderLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
        il.Emit(OpCodes.Stloc, elemLocal);
        il.Emit(OpCodes.Br, callDoneLabel);

        // isBoundLabel: call $BoundTSFunction.InvokeWithThis(holder, args)
        il.MarkLabel(isBoundLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Ldloc, holderLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvokeWithThis);
        il.Emit(OpCodes.Stloc, elemLocal);

        il.MarkLabel(callDoneLabel);
        il.MarkLabel(skipLabel);
    }

    /// <summary>
    /// Emits code to call the replacer function with a string key.
    /// Spec: replacer.call(holder, key, value) where holder is the parent.
    /// Handles both $TSFunction and $BoundTSFunction.
    /// </summary>
    private void EmitCallReplacerWithKey(ILGenerator il, LocalBuilder valueLocal, LocalBuilder keyLocal, LocalBuilder holderLocal, EmittedRuntime runtime)
    {
        var skipLabel = il.DefineLabel();
        var isTSFunctionLabel = il.DefineLabel();
        var isBoundLabel = il.DefineLabel();
        var callDoneLabel = il.DefineLabel();
        var argsLocal = il.DeclareLocal(_types.ObjectArray);

        il.Emit(OpCodes.Ldarg_1);  // replacer
        il.Emit(OpCodes.Brfalse, skipLabel);

        // Create object[] { key, value } and store in local
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Stloc, argsLocal);

        // Check if replacer is $TSFunction
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, isTSFunctionLabel);

        // Check if replacer is $BoundTSFunction
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, isBoundLabel);

        // Unknown type - use InvokeValue fallback
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, callDoneLabel);

        // isTSFunctionLabel: call $TSFunction.InvokeWithThis(holder, args).
        il.MarkLabel(isTSFunctionLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldloc, holderLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, callDoneLabel);

        // isBoundLabel: call $BoundTSFunction.InvokeWithThis(holder, args)
        il.MarkLabel(isBoundLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Ldloc, holderLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvokeWithThis);
        il.Emit(OpCodes.Stloc, valueLocal);

        il.MarkLabel(callDoneLabel);
        il.MarkLabel(skipLabel);
    }

    /// <summary>
    /// Emits object stringification with full options (replacer, allowedKeys, indentation).
    /// </summary>
    private void EmitStringifyObjectFull(
        ILGenerator il,
        MethodBuilder stringifyMethod,
        LocalBuilder valueLocal,
        EmittedRuntime runtime,
        LocalBuilder? allowPooledDictionaryKeysLocal,
        LocalBuilder? replacerHolderLocal = null)
    {
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var sourceKeysLocal = il.DeclareLocal(_types.ListOfObject);
        var firstLocal = il.DeclareLocal(_types.Boolean);
        var newlineLocal = il.DeclareLocal(_types.String);
        var closeLocal = il.DeclareLocal(_types.String);
        var keyLocal = il.DeclareLocal(_types.String);
        var valLocal = il.DeclareLocal(_types.Object);
        var strResultLocal = il.DeclareLocal(_types.String);
        var rentedKeysLocal = il.DeclareLocal(_types.Boolean);
        var returnValueLocal = il.DeclareLocal(_types.String);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var hasIndentLabel = il.DefineLabel();
        var indentDoneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // A replacer PropertyList supplies the iteration order and needs no own
        // key snapshot. Otherwise use the same pooled ordinary-dictionary path
        // as simple stringify, falling back for descriptors, proxies, numeric
        // keys, class materializations, and other exotic shapes.
        var fallbackSnapshot = il.DefineLabel();
        var snapshotReady = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brtrue, snapshotReady);
        if (allowPooledDictionaryKeysLocal is not null)
        {
            il.Emit(OpCodes.Ldloc, allowPooledDictionaryKeysLocal);
            il.Emit(OpCodes.Brfalse, fallbackSnapshot);
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Call, runtime.PDSHasPropertyDescriptors);
            il.Emit(OpCodes.Brtrue, fallbackSnapshot);
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Call, _jsonTryRentDictionaryKeysMethod!);
            il.Emit(OpCodes.Stloc, sourceKeysLocal);
            il.Emit(OpCodes.Ldloc, sourceKeysLocal);
            il.Emit(OpCodes.Brfalse, fallbackSnapshot);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stloc, rentedKeysLocal);
            il.Emit(OpCodes.Br, snapshotReady);
        }

        il.MarkLabel(fallbackSnapshot);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, runtime.GetKeys);
        il.Emit(OpCodes.Stloc, sourceKeysLocal);
        il.MarkLabel(snapshotReady);

        var cleanupDone = il.DefineLabel();
        il.BeginExceptionBlock();

        // Check indent
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, hasIndentLabel);

        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, newlineLocal);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Stloc, closeLocal);
        il.Emit(OpCodes.Br, indentDoneLabel);

        il.MarkLabel(hasIndentLabel);
        EmitComputeNewline(il, newlineLocal, closeLocal);

        il.MarkLabel(indentDoneLabel);

        il.Emit(OpCodes.Call, _jsonRentStringBuilderMethod!);
        il.Emit(OpCodes.Stloc, sbLocal);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "{");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // bool first = true;
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, firstLocal);

        // ECMA-262 25.5.2.4 step 5: when PropertyList is provided, iterate
        // PropertyList order; else iterate own enumerable keys of the source.
        // We pre-bind keyLocal/valLocal at the top of each iteration so the
        // shared body below works for both shapes.
        var iLocalObj = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocalObj);

        il.MarkLabel(loopStart);

        // Dispatch: if allowedKeys != null, advance via i over allowedKeys;
        // else advance over the snapshotted own-key list.
        var sourcePathLabel = il.DefineLabel();
        var iterDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, sourcePathLabel);

        // allowedKeys path: bounds-check, fetch key, look up in dict (skip if absent)
        il.Emit(OpCodes.Ldloc, iLocalObj);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfString, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);
        // key = allowedKeys[i]
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, iLocalObj);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfString, "get_Item", [_types.Int32]));
        il.Emit(OpCodes.Stloc, keyLocal);
        // i++ (bump now so `continue` (Br loopStart) advances)
        il.Emit(OpCodes.Ldloc, iLocalObj);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocalObj);
        // Get(holder, key); absent keys become undefined and are omitted below.
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, _jsonGetDictionaryPropertyMethod!);
        il.Emit(OpCodes.Stloc, valLocal);
        il.Emit(OpCodes.Br, iterDoneLabel);

        // Source-key snapshot path
        il.MarkLabel(sourcePathLabel);
        il.Emit(OpCodes.Ldloc, iLocalObj);
        il.Emit(OpCodes.Ldloc, sourceKeysLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);
        il.Emit(OpCodes.Ldloc, sourceKeysLocal);
        il.Emit(OpCodes.Ldloc, iLocalObj);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", [_types.Int32]));
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, keyLocal);
        il.Emit(OpCodes.Ldloc, iLocalObj);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocalObj);
        var generalSourceRead = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, rentedKeysLocal);
        il.Emit(OpCodes.Brfalse, generalSourceRead);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloca, valLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryStringObject,
            "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brtrue, iterDoneLabel);

        il.MarkLabel(generalSourceRead);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, _jsonGetDictionaryPropertyMethod!);
        il.Emit(OpCodes.Stloc, valLocal);

        il.MarkLabel(iterDoneLabel);

        // SerializeJSONProperty step 2 precedes the replacer step.
        EmitToJsonCheck(il, valLocal, runtime, keyLocal: keyLocal);
        EmitCallReplacerWithKey(
            il, valLocal, keyLocal, replacerHolderLocal ?? dictLocal, runtime);

        // strResult = StringifyValueFull(val, replacer, allowedKeys, indentStr, depth + 1, keyLocal)
        // ECMA-262 25.5.2.5 SerializeJSONObject step 6.a — the recursive key
        // is the property name (already the right shape).
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, stringifyMethod);
        il.Emit(OpCodes.Stloc, strResultLocal);

        // if (strResult == null) continue;
        il.Emit(OpCodes.Ldloc, strResultLocal);
        il.Emit(OpCodes.Brfalse, loopStart);

        // if (!first) sb.Append(",");
        il.Emit(OpCodes.Ldloc, firstLocal);
        var skipComma = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, skipComma);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipComma);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, firstLocal);

        // sb.Append(newline);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, newlineLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // sb.Append(EscapeJsonString(key));
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, _appendEscapedJsonStringMethod!);

        // sb.Append(indentStr.Length > 0 ? ": " : ":");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        var colonNoSpace = il.DefineLabel();
        var colonDone = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, colonNoSpace);
        il.Emit(OpCodes.Ldstr, ": ");
        il.Emit(OpCodes.Br, colonDone);
        il.MarkLabel(colonNoSpace);
        il.Emit(OpCodes.Ldstr, ":");
        il.MarkLabel(colonDone);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // sb.Append(strResult);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, strResultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // sb.Append(close);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, closeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // sb.Append("}");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "}");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, returnValueLocal);
        il.Emit(OpCodes.Leave, cleanupDone);

        il.BeginFinallyBlock();
        var skipReturn = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, rentedKeysLocal);
        il.Emit(OpCodes.Brfalse, skipReturn);
        il.Emit(OpCodes.Ldloc, sourceKeysLocal);
        il.Emit(OpCodes.Call, _jsonReturnDictionaryKeysMethod!);
        il.MarkLabel(skipReturn);
        var skipBuilderReturn = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Brfalse, skipBuilderReturn);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Call, _jsonReturnStringBuilderMethod!);
        il.MarkLabel(skipBuilderReturn);
        il.EndExceptionBlock();

        il.MarkLabel(cleanupDone);
        il.Emit(OpCodes.Ldloc, returnValueLocal);
        il.Emit(OpCodes.Ret);
    }

}
