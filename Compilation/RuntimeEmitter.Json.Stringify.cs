using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private MethodBuilder? _escapeJsonStringMethod;

    /// <summary>
    /// Emits a helper method that escapes a string for JSON output.
    /// This replaces dependency on System.Text.Json.JsonSerializer.
    /// </summary>
    private MethodBuilder EmitEscapeJsonStringHelper(TypeBuilder typeBuilder)
    {
        if (_escapeJsonStringMethod != null)
            return _escapeJsonStringMethod;

        var method = typeBuilder.DefineMethod(
            "EscapeJsonString",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.String,
            [_types.String]
        );

        var il = method.GetILGenerator();
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        var iLocal = il.DeclareLocal(_types.Int32);
        var cLocal = il.DeclareLocal(_types.Char);
        var lenLocal = il.DeclareLocal(_types.Int32);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var checkBackslash = il.DefineLabel();
        var checkBackspace = il.DefineLabel();
        var checkFormFeed = il.DefineLabel();
        var checkNewline = il.DefineLabel();
        var checkReturn = il.DefineLabel();
        var checkTab = il.DefineLabel();
        var checkControl = il.DefineLabel();
        var checkSurrogate = il.DefineLabel();
        var appendNormal = il.DefineLabel();
        var nextChar = il.DefineLabel();

        // sb = new StringBuilder("\"");
        il.Emit(OpCodes.Ldstr, "\"");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, [_types.String]));
        il.Emit(OpCodes.Stloc, sbLocal);

        // len = s.Length;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);

        // i = 0;
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        // while (i < len)
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // c = s[i];
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32]));
        il.Emit(OpCodes.Stloc, cLocal);

        // if (c == '"') sb.Append("\\\"");
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'"');
        il.Emit(OpCodes.Bne_Un, checkBackslash);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "\\\"");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, nextChar);

        // if (c == '\\') sb.Append("\\\\");
        il.MarkLabel(checkBackslash);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'\\');
        il.Emit(OpCodes.Bne_Un, checkBackspace);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "\\\\");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, nextChar);

        // if (c == '\b') sb.Append("\\b");  -- ECMA-262 24.5.2.2 QuoteJSONString
        il.MarkLabel(checkBackspace);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'\b');
        il.Emit(OpCodes.Bne_Un, checkFormFeed);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "\\b");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, nextChar);

        // if (c == '\f') sb.Append("\\f");
        il.MarkLabel(checkFormFeed);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'\f');
        il.Emit(OpCodes.Bne_Un, checkNewline);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "\\f");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, nextChar);

        // if (c == '\n') sb.Append("\\n");
        il.MarkLabel(checkNewline);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'\n');
        il.Emit(OpCodes.Bne_Un, checkReturn);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "\\n");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, nextChar);

        // if (c == '\r') sb.Append("\\r");
        il.MarkLabel(checkReturn);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'\r');
        il.Emit(OpCodes.Bne_Un, checkTab);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "\\r");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, nextChar);

        // if (c == '\t') sb.Append("\\t");
        il.MarkLabel(checkTab);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'\t');
        il.Emit(OpCodes.Bne_Un, checkControl);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "\\t");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, nextChar);

        // if (c < 32) sb.Append("\\u" + ((int)c).ToString("x4"));
        il.MarkLabel(checkControl);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Bge, checkSurrogate);
        // Control character - emit \uXXXX
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "\\u");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, sbLocal);
        // Convert char to int and format as 4-digit hex
        var charAsIntLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Stloc, charAsIntLocal);
        il.Emit(OpCodes.Ldloca, charAsIntLocal);
        il.Emit(OpCodes.Ldstr, "x4");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", [_types.String]));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, nextChar);

        // Surrogate handling per ECMA-262 well-formed JSON.stringify (2019).
        // High surrogate (0xD800-0xDBFF) followed by low surrogate (0xDC00-0xDFFF)
        // is a valid pair → emit both as-is (they encode a code point > U+FFFF).
        // Otherwise (lone high, lone low) → escape as \uXXXX.
        il.MarkLabel(checkSurrogate);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, 0xD800);
        il.Emit(OpCodes.Blt, appendNormal);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, 0xE000);
        il.Emit(OpCodes.Bge, appendNormal);

        // c is in [0xD800, 0xE000): a surrogate. Determine high vs low.
        var lowSurrogateLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, 0xDC00);
        il.Emit(OpCodes.Bge, lowSurrogateLabel);

        // High surrogate (0xD800-0xDBFF): peek next char. If valid low → emit pair.
        var loneHighLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, loneHighLabel);
        var nextCharCheckLocal = il.DeclareLocal(_types.Char);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32]));
        il.Emit(OpCodes.Stloc, nextCharCheckLocal);
        il.Emit(OpCodes.Ldloc, nextCharCheckLocal);
        il.Emit(OpCodes.Ldc_I4, 0xDC00);
        il.Emit(OpCodes.Blt, loneHighLabel);
        il.Emit(OpCodes.Ldloc, nextCharCheckLocal);
        il.Emit(OpCodes.Ldc_I4, 0xE000);
        il.Emit(OpCodes.Bge, loneHighLabel);
        // Valid pair: emit both chars as-is, advance by 2.
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.Char]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, nextCharCheckLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.Char]));
        il.Emit(OpCodes.Pop);
        // i += 2 (extra +1 here, the +1 in nextChar advances normally).
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, nextChar);

        // Lone high surrogate (no low after): escape as \uXXXX.
        il.MarkLabel(loneHighLabel);
        il.MarkLabel(lowSurrogateLabel);
        // Both lone-high and lone-low fall here: emit \uXXXX.
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "\\u");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, sbLocal);
        var surCharIntLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Stloc, surCharIntLocal);
        il.Emit(OpCodes.Ldloca, surCharIntLocal);
        il.Emit(OpCodes.Ldstr, "x4");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", [_types.String]));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, nextChar);

        // Normal character - append as-is
        il.MarkLabel(appendNormal);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.Char]));
        il.Emit(OpCodes.Pop);

        // i++;
        il.MarkLabel(nextChar);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        // sb.Append("\"");
        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "\"");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // return sb.ToString();
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Ret);

        _escapeJsonStringMethod = method;
        return method;
    }

    private void EmitJsonStringify(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // First emit the escape helper (needed by stringify)
        EmitEscapeJsonStringHelper(typeBuilder);

        // Then emit the main stringify helper
        var stringifyHelper = EmitJsonStringifyHelper(typeBuilder, runtime);

        var method = typeBuilder.DefineMethod(
            "JsonStringify",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.JsonStringify = method;

        var il = method.GetILGenerator();

        // ECMA-262 25.5.2.1 step 12: SerializeJSONProperty("", { "": value }).
        // toJSON (step 2) runs before recursion. Pre-invoke at the root with
        // key="" so toJSON's first arg is correctly observed by tests like
        // value-tojson-arguments.js. The duplicate check inside StringifyValue
        // is a no-op when the value has already been replaced.
        var rootValueLocalSimple = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, rootValueLocalSimple);
        EmitToJsonCheck(il, rootValueLocalSimple, runtime, "");

        // Map $Undefined → JS undefined directly here, since StringifyValue
        // returns C# null for $Undefined and we'd map back to $Undefined below.
        // Skip the helper for that case to short-circuit cleanly.
        var notUndefRootLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, rootValueLocalSimple);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, notUndefRootLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notUndefRootLabel);

        // Call our emitted StringifyValue helper. Map null → $Undefined.Instance
        // because StringifyValue returns null for undefined inputs and the
        // spec wants `JSON.stringify(undefined) === undefined`.
        var resultRootLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, rootValueLocalSimple);
        il.Emit(OpCodes.Ldc_I4_0); // indent = 0
        il.Emit(OpCodes.Ldc_I4_0); // depth = 0
        il.Emit(OpCodes.Ldstr, ""); // key = "" (root per ECMA-262 25.5.2.1 step 12)
        il.Emit(OpCodes.Call, stringifyHelper);
        il.Emit(OpCodes.Stloc, resultRootLocal);
        il.Emit(OpCodes.Ldloc, resultRootLocal);
        var nonNullJsonLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, nonNullJsonLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(nonNullJsonLabel);
        il.Emit(OpCodes.Ldloc, resultRootLocal);
        il.Emit(OpCodes.Ret);
    }

    private MethodBuilder EmitJsonStringifyHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringifyValue",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.Int32, _types.Int32, _types.String] // value, indent, depth, key
        );

        var il = method.GetILGenerator();
        var valueLocal = il.DeclareLocal(_types.Object);

        var nullLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var doubleLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var classInstanceLabel = il.DefineLabel();

        // Depth cap — recursive cycles (`a.self = a`) would otherwise recurse
        // unbounded and stack-overflow. ECMA-262 requires TypeError; the cap is
        // sized well above any legitimate nesting (512). Check is cheap; runs
        // at every entry so the throw fires before another frame is pushed.
        var depthOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4, 512);
        il.Emit(OpCodes.Blt, depthOkLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Converting circular structure to JSON");
        il.MarkLabel(depthOkLabel);

        // Store value in local (we may modify it via toJSON)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, valueLocal);

        // ECMA-262 25.5.2.1: undefined values are dropped — for arrays the
        // caller maps null→"null" via `?? "null"`, for objects the caller
        // skips the key on null. So return C# null here for $Undefined.
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

        // Check for toJSON() method and call it if present. ECMA-262 25.5.2.3
        // step 2.b.i requires toJSON's first arg to be the property key — read
        // it from arg 3 (the helper's key parameter, threaded by all recursive
        // callers).
        EmitToJsonCheck(il, valueLocal, runtime, keyArgIndex: 3);

        // BigInt throws only after its prototype toJSON hook has had a chance
        // to replace the primitive (SerializeJSONProperty steps 2 then 10).
        EmitBigIntCheck(il, valueLocal, runtime);

        // toJSON may have returned $Undefined — re-check and return C# null
        // so the caller treats it as JSON-undefined (root: returns undefined,
        // array: emits "null", object: omits key). Without this re-check,
        // $Undefined falls through to the bottom nullLabel which returns the
        // literal string "null" — wrong for all three cases.
        var afterToJsonUndefLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brfalse, afterToJsonUndefLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(afterToJsonUndefLabel);

        // JSON.rawJSON carries validated source text in an unforgeable emitted
        // type. Serialize it verbatim after the toJSON step.
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

        // Boxed-primitive unwrap (ECMA-262 25.5.2.3 step 4.a-c). $Object and
        // Dictionary<string,object> instances created via `new Number(x)`,
        // `new String(x)`, `new Boolean(x)` carry a __primitiveValue field
        // (Stage 4z19 marker). SerializeJSONProperty must pull out the
        // primitive — without this, JSON.stringify(new Boolean(true)) returns
        // the marker dict instead of "true". Check both $Object and Dictionary
        // since either may be the receiver shape.
        EmitBoxedPrimitiveJsonCoerce(il, valueLocal, runtime);

        // Proxy materialization (#92): if value is SharpTSProxy, dispatch its
        // [[OwnPropertyKeys]] / [[Get]] traps and substitute a Dictionary so the
        // existing dict path serializes the proxied view.
        var notProxyLabelSimple = il.DefineLabel();
        EmitProxyMaterializeForJson(il, valueLocal, notProxyLabelSimple);
        il.Emit(OpCodes.Br, dictLabel);
        il.MarkLabel(notProxyLabelSimple);

        // if (value is bool)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, boolLabel);

        // if (value is double)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, doubleLabel);

        // if (value is string)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        // if (value is List<object>)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listLabel);

        // if (value is Dictionary<string, object>)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // ECMA-262 25.5.2.3: $RegExp has no own enumerable properties → "{}".
        // Skip the check when UsesRegExp is gated off — no RegExp values can
        // exist at runtime in that build.
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

        // Check if it's an emitted $Object instance
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

        // List<object> - stringify array
        il.MarkLabel(listLabel);
        EmitStringifyArray(il, method, valueLocal, runtime);

        // Dictionary<string, object> - stringify object
        il.MarkLabel(dictLabel);
        EmitStringifyObject(il, method, valueLocal);

        // Class instance - stringify via $IHasFields fields dictionary.
        // Use TSObjectMergeEnumerable to also include accessor (getter)
        // properties for $Object receivers per ECMA-262 EnumerableOwnPropertyNames.
        il.MarkLabel(classInstanceLabel);
        var classFieldsLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var noClassFieldsLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, runtime.TSObjectMergeEnumerable);
        il.Emit(OpCodes.Stloc, classFieldsLocal);

        il.Emit(OpCodes.Ldloc, classFieldsLocal);
        il.Emit(OpCodes.Brfalse, noClassFieldsLabel);
        il.Emit(OpCodes.Ldloc, classFieldsLocal);
        il.Emit(OpCodes.Stloc, valueLocal);
        EmitStringifyObject(il, method, valueLocal);

        il.MarkLabel(noClassFieldsLabel);
        il.Emit(OpCodes.Ldstr, "{}");
        il.Emit(OpCodes.Ret);

        return method;
    }

    private void EmitBigIntCheck(ILGenerator il, LocalBuilder valueLocal, EmittedRuntime runtime)
    {
        var notBigIntLabel = il.DefineLabel();
        var typeLocal = il.DeclareLocal(_types.Type);
        var nameLocal = il.DeclareLocal(_types.String);

        // var type = value.GetType();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Stloc, typeLocal);

        // var name = type.Name;
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "Name").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, nameLocal);

        // if (name == "SharpTSBigInt" || name == "BigInteger")
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "SharpTSBigInt");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String]));
        var throwLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, throwLabel);

        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldstr, "BigInteger");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String]));
        il.Emit(OpCodes.Brfalse, notBigIntLabel);

        il.MarkLabel(throwLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "BigInt value can't be serialized in JSON");

        il.MarkLabel(notBigIntLabel);
    }

    /// <summary>
    /// ECMA-262 25.5.2.3 step 9: if Type(value) is Object and IsCallable(value)
    /// is true, set value to undefined. We model "value becomes undefined" by
    /// returning C# null from the helper — the caller treats null as "drop"
    /// for object properties and "null" for array elements, matching the spec.
    /// Functions/bound functions/arrow functions all isinst $TSFunction or
    /// $BoundTSFunction (the only callable shapes the compiler emits for JS).
    /// </summary>
    private void EmitFunctionSkipCheck(ILGenerator il, LocalBuilder valueLocal, EmittedRuntime runtime)
    {
        var skipLabel = il.DefineLabel();
        var notSkippedLabel = il.DefineLabel();

        // Symbols (ECMA-262 25.5.2.3 step 3) are ignored as values.
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brtrue, skipLabel);

        // Functions / bound functions (ECMA-262 25.5.2.3 step 9) → undefined.
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, skipLabel);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brfalse, notSkippedLabel);

        il.MarkLabel(skipLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notSkippedLabel);
    }

    private void EmitToJsonCheck(ILGenerator il, LocalBuilder valueLocal, EmittedRuntime runtime, string? key = null, int? keyArgIndex = null)
    {
        var noToJsonLabel = il.DefineLabel();

        // First, check if value is a Dictionary<string, object?> (object literal).
        // If not, check for emitted $Object instance and read toJSON via TSObject.GetProperty.
        var notDictionaryLabel = il.DefineLabel();
        var notTsObjectLabel = il.DefineLabel();
        var toJsonFieldLocal = il.DeclareLocal(_types.Object);
        var argsLocal = il.DeclareLocal(_types.ObjectArray);

        // Build args = [key] when a key source is provided, else [].
        // ECMA-262 25.5.2.3 step 2.b.i: Call(toJSON, value, « key »).
        // The key may be a compile-time literal (root call sites pass "") or a
        // runtime string in a method arg slot (recursive paths read the key
        // from the helper's key parameter).
        void BuildArgs()
        {
            if (key != null || keyArgIndex.HasValue)
            {
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Newarr, _types.Object);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Ldc_I4_0);
                if (keyArgIndex.HasValue)
                {
                    il.Emit(OpCodes.Ldarg, keyArgIndex.Value);
                }
                else
                {
                    il.Emit(OpCodes.Ldstr, key!);
                }
                il.Emit(OpCodes.Stelem_Ref);
                il.Emit(OpCodes.Stloc, argsLocal);
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Newarr, _types.Object);
                il.Emit(OpCodes.Stloc, argsLocal);
            }
        }

        // BigInt is a primitive, but Get(value, "toJSON") boxes it and walks
        // BigInt.prototype. The general IHasFields branch below only handles
        // object receivers, so perform that prototype lookup explicitly while
        // preserving the primitive as the call receiver.
        var notBigIntLabel = il.DefineLabel();
        var bigIntNotTsFunctionLabel = il.DefineLabel();
        var bigIntNotBoundLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brfalse, notBigIntLabel);

        il.Emit(OpCodes.Ldsfld, runtime.BigIntPrototypeField);
        il.Emit(OpCodes.Ldstr, "toJSON");
        il.Emit(OpCodes.Ldloca, toJsonFieldLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
            _types.String, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, noToJsonLabel);

        il.Emit(OpCodes.Ldloc, toJsonFieldLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, bigIntNotTsFunctionLabel);
        BuildArgs();
        il.Emit(OpCodes.Ldloc, toJsonFieldLocal);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, noToJsonLabel);

        il.MarkLabel(bigIntNotTsFunctionLabel);
        il.Emit(OpCodes.Ldloc, toJsonFieldLocal);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brfalse, bigIntNotBoundLabel);
        BuildArgs();
        il.Emit(OpCodes.Ldloc, toJsonFieldLocal);
        il.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvokeWithThis);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, noToJsonLabel);

        il.MarkLabel(bigIntNotBoundLabel);
        il.Emit(OpCodes.Br, noToJsonLabel);
        il.MarkLabel(notBigIntLabel);

        // if (value is Dictionary<string, object?>)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, notDictionaryLabel);

        // dict.TryGetValue("toJSON", out var fn)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldstr, "toJSON");
        il.Emit(OpCodes.Ldloca, toJsonFieldLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
            _types.String, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, notDictionaryLabel);

        // Check if field is a TSFunction
        var notTSFunctionLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, toJsonFieldLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, notTSFunctionLabel);

        // ECMA-262 25.5.2.3 step 2.b.i: Call(toJSON, value, « key »).
        // \`this\` = value via InvokeWithThis; args = [key] when caller
        // provided one, else [] (when called from inside the recursive
        // StringifyValueFull where the key is no longer in scope).
        BuildArgs();

        il.Emit(OpCodes.Ldloc, toJsonFieldLocal);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, noToJsonLabel);

        il.MarkLabel(notTSFunctionLabel);
        // Check for BoundTSFunction
        var notBoundLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, toJsonFieldLocal);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brfalse, notBoundLabel);

        BuildArgs();

        il.Emit(OpCodes.Ldloc, toJsonFieldLocal);
        il.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvokeWithThis);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, noToJsonLabel);

        il.MarkLabel(notBoundLabel);
        il.MarkLabel(notDictionaryLabel);

        // if (!(value is $IHasFields)) return;
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brfalse, notTsObjectLabel);

        // toJsonField = (($IHasFields)value).GetProperty("toJSON");
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Ldstr, "toJSON");
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsGetProperty);
        il.Emit(OpCodes.Stloc, toJsonFieldLocal);
        il.Emit(OpCodes.Ldloc, toJsonFieldLocal);
        il.Emit(OpCodes.Brfalse, noToJsonLabel);

        // Reuse callable checks from dictionary branch.
        il.Emit(OpCodes.Ldloc, toJsonFieldLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, notTsObjectLabel);

        // Same InvokeWithThis pattern as the dict branch above.
        BuildArgs();
        il.Emit(OpCodes.Ldloc, toJsonFieldLocal);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, noToJsonLabel);

        il.MarkLabel(notTsObjectLabel);
        il.MarkLabel(noToJsonLabel);
    }

    private void EmitIsClassInstanceCheck(ILGenerator il, LocalBuilder valueLocal, Label classInstanceLabel, EmittedRuntime runtime)
    {
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brtrue, classInstanceLabel);
    }

    private void EmitFormatNumber(ILGenerator il, LocalBuilder valueLocal, EmittedRuntime runtime)
    {
        var local = il.DeclareLocal(_types.Double);
        var nullLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, local);

        // ECMA-262 JSON.stringify: NaN and Infinity serialize as null; everything
        // else uses the shared Number::toString ($Runtime.FormatNumber).
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", [_types.Double]));
        il.Emit(OpCodes.Brtrue, nullLabel);
        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsInfinity", [_types.Double]));
        il.Emit(OpCodes.Brtrue, nullLabel);

        il.Emit(OpCodes.Ldloc, local);
        il.Emit(OpCodes.Call, runtime.FormatNumber);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringifyArray(ILGenerator il, MethodBuilder stringifyMethod, LocalBuilder valueLocal, EmittedRuntime runtime)
    {
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        var arrLocal = il.DeclareLocal(_types.ListOfObject);
        var iLocal = il.DeclareLocal(_types.Int32);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        // number[] unboxing: materialize a numeric-mode $Array before reading its base list.
        EmitDeoptIfNumericArray(il, runtime, () => il.Emit(OpCodes.Ldloc, valueLocal));
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

        // StringBuilder sb = new StringBuilder("[");
        il.Emit(OpCodes.Ldstr, "[");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, [_types.String]));
        il.Emit(OpCodes.Stloc, sbLocal);

        // for (int i = 0; i < arr.Count; i++)
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

        // Stage E.2 M5: ECMA-262 25.5.2.4 SerializeJSONArray — a hole slot
        // serializes as "null" (SerializeJSONProperty returns undefined for
        // holes, which SerializeJSONArray substitutes with "null"). Without
        // this check the $ArrayHole sentinel would flow to StringifyValue
        // and render as "undefined" or similar.
        var notHoleLabel = il.DefineLabel();
        var appendedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", [_types.Int32]));
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brfalse, notHoleLabel);
        // Hole: append "null" and skip.
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, appendedLabel);

        il.MarkLabel(notHoleLabel);
        // strResult = StringifyValue(arr[i], indent, depth + 1, i.ToString())
        // sb.Append(strResult ?? "null"); — null means the slot's value was
        // undefined; arrays render those as "null" per SerializeJSONArray 8.b.
        // ECMA-262 25.5.2.4 step 8.a: pass ToString(F(I)) as the key for
        // the recursive SerializeJSONProperty call so toJSON sees the index.
        var arrElemStrLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", [_types.Int32]));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloca, iLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int32, "ToString"));
        il.Emit(OpCodes.Call, stringifyMethod);
        il.Emit(OpCodes.Stloc, arrElemStrLocal);
        var arrElemNonNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, arrElemStrLocal);
        il.Emit(OpCodes.Brtrue, arrElemNonNullLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Stloc, arrElemStrLocal);
        il.MarkLabel(arrElemNonNullLabel);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, arrElemStrLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        il.MarkLabel(appendedLabel);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // sb.Append("]");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "]");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Ret);
    }

    private void EmitStringifyObject(ILGenerator il, MethodBuilder stringifyMethod, LocalBuilder valueLocal)
    {
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var enumeratorLocal = il.DeclareLocal(_types.DictionaryStringObjectEnumerator);
        var currentLocal = il.DeclareLocal(_types.KeyValuePairStringObject);
        var firstLocal = il.DeclareLocal(_types.Boolean);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // if (dict.Count == 0) return "{}";
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryStringObject, "Count").GetGetMethod()!);
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notEmpty);
        il.Emit(OpCodes.Ldstr, "{}");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEmpty);

        // StringBuilder sb = new StringBuilder("{");
        il.Emit(OpCodes.Ldstr, "{");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, [_types.String]));
        il.Emit(OpCodes.Stloc, sbLocal);

        // bool first = true;
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, firstLocal);

        // Get enumerator
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.DictionaryStringObjectEnumerator, "MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEnd);

        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.DictionaryStringObjectEnumerator, "Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, currentLocal);

        // strResult = StringifyValue(currentValue, indent, depth + 1, currentKey)
        // Compute first; if null, the value was undefined → skip entry per
        // ECMA-262 25.5.2.1 SerializeJSONObject step 7.b.
        // ECMA-262 25.5.2.5 step 6.a: the recursive key is the property name
        // so toJSON can branch on it.
        var dictValStrLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.KeyValuePairStringObject, "Value").GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.KeyValuePairStringObject, "Key").GetGetMethod()!);
        il.Emit(OpCodes.Call, stringifyMethod);
        il.Emit(OpCodes.Stloc, dictValStrLocal);
        il.Emit(OpCodes.Ldloc, dictValStrLocal);
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

        // sb.Append(EscapeJsonString(key));
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloca, currentLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.KeyValuePairStringObject, "Key").GetGetMethod()!);
        il.Emit(OpCodes.Call, _escapeJsonStringMethod!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // sb.Append(":");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ":");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // sb.Append(strResult)
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, dictValStrLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Dispose enumerator
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Constrained, _types.DictionaryStringObjectEnumerator);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IDisposable, "Dispose"));

        // sb.Append("}");
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "}");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// ECMA-262 §25.5.2.3 step 4 / §25.5.2.1 step 5: coerce a boxed
    /// Number/String/Boolean wrapper held in <paramref name="valueLocal"/> to the
    /// primitive JSON serializes. Number → <c>$Runtime.ToNumber</c>, String →
    /// <c>$Runtime.ToJsString</c> (both run ECMA-262 ToPrimitive, honoring an own
    /// <c>valueOf</c>/<c>toString</c> override — #574), Boolean → its
    /// <c>__primitiveValue</c> (no coercion per spec). A non-wrapper value is left
    /// unchanged. #565: only an object carrying a string <c>__primitiveType</c> tag
    /// is treated as a wrapper. Mirrors
    /// <c>Interpreter.TryCoerceBoxedPrimitiveForJson</c>; used by both the simple and
    /// full (replacer/space) stringify helpers, and for a boxed Number/String
    /// <c>space</c> argument.
    /// </summary>
    private void EmitBoxedPrimitiveJsonCoerce(ILGenerator il, LocalBuilder valueLocal, EmittedRuntime runtime)
    {
        var notBoxed = il.DefineLabel();
        var doUnwrap = il.DefineLabel();
        var done = il.DefineLabel();

        // Only an $Object / Dictionary shape can be a wrapper.
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, doUnwrap);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, notBoxed);
        il.MarkLabel(doUnwrap);

        // tag = (string)GetProperty("__primitiveType"); must be a string (#565).
        var tag = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldstr, "__primitiveType");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, tag);
        il.Emit(OpCodes.Ldloc, tag);
        il.Emit(OpCodes.Brfalse, notBoxed);

        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);
        var numberCase = il.DefineLabel();
        var booleanCase = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, tag);
        il.Emit(OpCodes.Ldstr, "Number");
        il.Emit(OpCodes.Call, strEq);
        il.Emit(OpCodes.Brtrue, numberCase);
        il.Emit(OpCodes.Ldloc, tag);
        il.Emit(OpCodes.Ldstr, "Boolean");
        il.Emit(OpCodes.Call, strEq);
        il.Emit(OpCodes.Brtrue, booleanCase);

        // String tag → ToString (string-hint ToPrimitive → toString first).
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, done);

        // Number tag → ToNumber (number-hint ToPrimitive → valueOf first).
        il.MarkLabel(numberCase);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, done);

        // Boolean tag → [[BooleanData]] directly (no coercion per ECMA-262).
        il.MarkLabel(booleanCase);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldstr, "__primitiveValue");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, valueLocal);

        il.MarkLabel(notBoxed);
        il.MarkLabel(done);
    }

}

