using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

// Split out of RuntimeEmitter.CoreUtilities.cs (#1141). Emits the runtime
// coercion helpers: JS ToString/stringify, ToNumber/ToInt32, and IsTruthy.
public partial class RuntimeEmitter
{
    private void EmitStringify(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Signature was forward-declared by DefineRuntimeClassPhase1 so
        // helper types that emit before $Runtime (notably $RegExp's
        // Symbol.* protocol methods) can call us. Just emit the body on
        // the existing MethodBuilder.
        var method = (MethodBuilder)runtime.Stringify;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var undefinedLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var doubleLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // number[] unboxing: materialize a numeric-mode $Array before the `is List<object>` branch reads it.
        EmitDeoptArgIfNumericArray(il, runtime, 0);

        // if (value == null) return "null"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // if (value is SharpTSUndefined) return "undefined"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, undefinedLabel);

        // if (value is bool b) return b ? "true" : "false"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, boolLabel);

        // if (value is double d) return d.ToString()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, doubleLabel);

        // if (value is List<object?>) return array string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listLabel);

        // if (value is BigInteger) return value.ToString() + "n"
        var bigintLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brtrue, bigintLabel);

        // if (value is Dictionary<string, object?>) return "{ key: value, ... }"
        var dictLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Default: return value.ToString() ?? "null"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Dup);
        var notNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "null");
        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Br, endLabel);

        // null case
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Br, endLabel);

        // undefined case
        il.MarkLabel(undefinedLabel);
        il.Emit(OpCodes.Ldstr, "undefined");
        il.Emit(OpCodes.Br, endLabel);

        // bool case
        il.MarkLabel(boolLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        var trueLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, trueLabel);
        il.Emit(OpCodes.Ldstr, "false");
        il.Emit(OpCodes.Br, endLabel);
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldstr, "true");
        il.Emit(OpCodes.Br, endLabel);

        // double case - delegate to $Runtime.FormatNumber (ECMA-262 7.1.12.1
        // Number::toString: shortest round-trip + JS thresholds). Mirrors the
        // interpreter's RuntimeTypes.FormatNumber so the two modes agree.
        il.MarkLabel(doubleLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Call, runtime.FormatNumber);
        il.Emit(OpCodes.Br, endLabel);

        // BigInteger case - format as value.ToString() + "n"
        il.MarkLabel(bigintLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.BigInteger);
        var bigintLocal = il.DeclareLocal(_types.BigInteger);
        il.Emit(OpCodes.Stloc, bigintLocal);
        il.Emit(OpCodes.Ldloca, bigintLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.BigInteger, "ToString"));
        il.Emit(OpCodes.Ldstr, "n");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Br, endLabel);

        // List case - format as "[elem1, elem2, ...]"
        il.MarkLabel(listLabel);
        // Use StringBuilder to build the result
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, sbLocal);

        // Append "["
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "[");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // Loop through list elements
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);

        var indexLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        // if (index >= list.Count) break
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        // if (index > 0) append ", "
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        var skipComma = il.DefineLabel();
        il.Emit(OpCodes.Ble, skipComma);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, ", ");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipComma);

        // Append Stringify(list[index])
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Call, method); // Recursive call to Stringify
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Append "]" and return
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "]");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Br, endLabel);

        // Dictionary case - format as "{ key1: value1, key2: value2, ... }"
        il.MarkLabel(dictLabel);

        // ECMA-262 §7.1.17 ToString of an object goes through ToPrimitive,
        // which (hint "string") tries the object's own toString method first.
        // If the user installed a callable `toString` on the dictionary
        // (`{toString: () => 'foo'}`), invoke it and return the result.
        // This is the path test262's coerce-string.js exercises.
        var dictHasUserToString = il.DeclareLocal(_types.Object);
        var skipUserToStringLabel = il.DefineLabel();
        var castDictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, castDictLocal);

        // Try TryGetValue(d, "toString", out userToString).
        var tryGetValueResult = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Ldloc, castDictLocal);
        il.Emit(OpCodes.Ldstr, "toString");
        il.Emit(OpCodes.Ldloca, dictHasUserToString);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue", _types.String, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, skipUserToStringLabel);

        // If the value is a $TSFunction, invoke it with the dict as `this`
        // and (if it returns a string-coercible value) return the result.
        il.Emit(OpCodes.Ldloc, dictHasUserToString);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, skipUserToStringLabel);

        // result = userToString.InvokeWithThis(dict, []);
        var userToStringResult = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, dictHasUserToString);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldloc, castDictLocal);
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(_types.GetMethod(typeof(Array), "Empty"), _types.Object));
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
        il.Emit(OpCodes.Stloc, userToStringResult);

        // Recursively Stringify the result so non-string returns coerce
        // properly (e.g. number → "42"). The result is normally a string
        // already, so this is a fast path through Stringify's string branch.
        il.Emit(OpCodes.Ldloc, userToStringResult);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(skipUserToStringLabel);

        // Use StringBuilder to build the result
        var dictSbLocal = il.DeclareLocal(_types.StringBuilder);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, dictSbLocal);

        // Append "{ "
        il.Emit(OpCodes.Ldloc, dictSbLocal);
        il.Emit(OpCodes.Ldstr, "{ ");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // Get the dictionary
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // Get enumerator
        var enumeratorLocal = il.DeclareLocal(_types.DictionaryStringObjectEnumerator);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.DictionaryStringObject, "GetEnumerator"));
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        // Track if first element
        var isFirstLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, isFirstLocal);

        var dictLoopStart = il.DefineLabel();
        var dictLoopEnd = il.DefineLabel();

        il.MarkLabel(dictLoopStart);

        // if (!enumerator.MoveNext()) break
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.DictionaryStringObjectEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, dictLoopEnd);

        // if (!isFirst) append ", "
        var dictSkipComma = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, isFirstLocal);
        il.Emit(OpCodes.Brtrue, dictSkipComma);
        il.Emit(OpCodes.Ldloc, dictSbLocal);
        il.Emit(OpCodes.Ldstr, ", ");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(dictSkipComma);

        // isFirst = false
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, isFirstLocal);

        // Get current KeyValuePair
        var kvpLocal = il.DeclareLocal(_types.KeyValuePairStringObject);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.DictionaryStringObjectEnumerator, "Current").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, kvpLocal);

        // Append key
        il.Emit(OpCodes.Ldloc, dictSbLocal);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.KeyValuePairStringObject, "Key").GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // Append ": "
        il.Emit(OpCodes.Ldloc, dictSbLocal);
        il.Emit(OpCodes.Ldstr, ": ");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // Append Stringify(value) - recursive call to emitted method
        il.Emit(OpCodes.Ldloc, dictSbLocal);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.KeyValuePairStringObject, "Value").GetGetMethod()!);
        il.Emit(OpCodes.Call, method); // Recursive call to this Stringify method
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Br, dictLoopStart);

        il.MarkLabel(dictLoopEnd);

        // Append " }" and return
        il.Emit(OpCodes.Ldloc, dictSbLocal);
        il.Emit(OpCodes.Ldstr, " }");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        il.Emit(OpCodes.Ldloc, dictSbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: <c>public static string StringRaw(object template, object[] substitutions)</c>.
    /// Implements <c>String.raw</c> per ECMA-262 22.1.2.4. Accepts:
    /// <list type="bullet">
    /// <item><c>string[]</c> — the legacy tagged-template-literal calling convention
    /// (used by <see cref="EmitStringRawTaggedTemplate"/>); used directly as the rawStrings array.</item>
    /// <item>any object with a <c>raw</c> property — the spec form
    /// (<c>String.raw({raw: [...]}, ...subs)</c>); reads <c>raw</c> via GetProperty,
    /// reads its <c>length</c>, iterates indexed members.</item>
    /// </list>
    /// </summary>
    private void EmitStringRaw(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Second param is `List<object> substitutions` (not object[]) so
        // $TSFunction.AdjustArgs's rest-param recognition kicks in for direct
        // `String.raw(template, ...subs)` calls — otherwise only the first
        // substitution would land in the param.
        var method = typeBuilder.DefineMethod(
            "StringRaw",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object, _types.ListOfObject]
        );
        runtime.StringRaw = method;

        var il = method.GetILGenerator();

        // We unify both shapes by extracting `length` and an indexed-access
        // closure into locals. For string[]: length = arr.Length, get(i) = arr[i].
        // For object: length = ToLength(template.length OR template.raw.length),
        // get(i) = ToString(template.raw[i]).
        var lengthLocal = il.DeclareLocal(_types.Int32);
        var rawListLocal = il.DeclareLocal(_types.Object); // either string[] or List<object> from raw
        var isStringArrayLocal = il.DeclareLocal(_types.Boolean);

        // Detect string[] (legacy tagged-template path).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.StringArray);
        var notStringArrayLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notStringArrayLabel);
        // string[] path: length = ((string[])arg0).Length, rawList = arg0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.StringArray);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lengthLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, rawListLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, isStringArrayLocal);
        var afterDispatchLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, afterDispatchLabel);

        il.MarkLabel(notStringArrayLabel);
        // Object path (spec form): raw = template.raw; length = ToLength(raw.length).
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, isStringArrayLocal);

        // ECMA-262 22.1.2.4 step 2: ? RequireObjectCoercible(template). null/
        // undefined throws TypeError. Catches String.raw(undefined) / .call(null).
        il.Emit(OpCodes.Ldarg_0);
        var notNullishLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notNullishLabel);
        var throwTypeErrorLabel = il.DefineLabel();
        il.MarkLabel(throwTypeErrorLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert undefined or null to object");
        il.MarkLabel(notNullishLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, throwTypeErrorLabel);

        // raw = template.raw  via $Runtime.GetProperty
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "raw");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, rawListLocal);

        // ECMA-262 22.1.2.4 step 4: ? ToObject(raw). If raw is null/undefined,
        // throw TypeError per spec. Required for `String.raw({raw: undefined})`
        // and `String.raw({})` (raw absent → undefined).
        il.Emit(OpCodes.Ldloc, rawListLocal);
        il.Emit(OpCodes.Brfalse, throwTypeErrorLabel);
        il.Emit(OpCodes.Ldloc, rawListLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, throwTypeErrorLabel);

        // ToLength(raw.length): use $Runtime.GetProperty(raw, "length") then
        // $Runtime.ToNumber → clamp to non-negative int.
        il.Emit(OpCodes.Ldloc, rawListLocal);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        var lenDouble = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, lenDouble);
        // NaN / negative / -Infinity → 0
        il.Emit(OpCodes.Ldloc, lenDouble);
        il.Emit(OpCodes.Ldloc, lenDouble);
        var notNaNLabel = il.DefineLabel();
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, notNaNLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lengthLocal);
        il.Emit(OpCodes.Br, afterDispatchLabel);
        il.MarkLabel(notNaNLabel);
        il.Emit(OpCodes.Ldloc, lenDouble);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        var positiveLenLabel = il.DefineLabel();
        il.Emit(OpCodes.Bgt, positiveLenLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, lengthLocal);
        il.Emit(OpCodes.Br, afterDispatchLabel);
        il.MarkLabel(positiveLenLabel);
        // length = (int)Math.Min(d, 1<<24) — guard against runaway alloc.
        il.Emit(OpCodes.Ldloc, lenDouble);
        il.Emit(OpCodes.Ldc_R8, (double)(1 << 24));
        il.Emit(OpCodes.Call, _types.Math.GetMethod("Min", [_types.Double, _types.Double])!);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lengthLocal);

        il.MarkLabel(afterDispatchLabel);

        // ECMA-262 22.1.2.4 step 7: If literalSegments ≤ 0, return the empty string.
        il.Emit(OpCodes.Ldloc, lengthLocal);
        var hasSegmentsLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, hasSegmentsLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(hasSegmentsLabel);

        // var sb = new StringBuilder();
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, Type.EmptyTypes));
        il.Emit(OpCodes.Stloc, sbLocal);

        // for (int i = 0; i < length; i++) { ... }
        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var loopStart = il.DefineLabel();
        var loopCondition = il.DefineLabel();
        il.Emit(OpCodes.Br, loopCondition);

        il.MarkLabel(loopStart);

        // segment = isStringArray ? rawList[i] : ToJsString(GetProperty(rawList, i.ToString()))
        var segmentLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, isStringArrayLocal);
        var segmentObjPathLabel = il.DefineLabel();
        var segmentDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, segmentObjPathLabel);
        // string[] path
        il.Emit(OpCodes.Ldloc, rawListLocal);
        il.Emit(OpCodes.Castclass, _types.StringArray);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, segmentLocal);
        il.Emit(OpCodes.Br, segmentDoneLabel);
        il.MarkLabel(segmentObjPathLabel);
        // object path: ToJsString(GetProperty(raw, i.ToString()))
        il.Emit(OpCodes.Ldloc, rawListLocal);
        il.Emit(OpCodes.Ldloca, iLocal);
        il.Emit(OpCodes.Call, _types.Int32.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, segmentLocal);
        il.MarkLabel(segmentDoneLabel);

        // sb.Append(segment)
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, segmentLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);

        // if (i + 1 < length) append substitution
        var skipSubLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Bge, skipSubLabel);
        // if i < substitutions.Count, append ToJsString(substitutions[i])
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, skipSubLabel);
        var subStrLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Stloc, subStrLocal);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldloc, subStrLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipSubLabel);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopCondition);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Blt, loopStart);

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Ret);
    }

    // Emits an early-return guard for the language-level ToString coercion paths
    // (String(), template-literal interpolation, `+` concat): a bigint coerces to
    // its bare decimal form ("42"), NOT the "42n" debug form that console.log /
    // util.inspect (Stringify) uses. Mirrors the interpreter's Interpreter.Stringify
    // / SharpTSStringNamespace bigint handling so the two modes agree.
    private void EmitBigIntToStringReturn(ILGenerator il)
    {
        var notBigInt = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brfalse, notBigInt);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.BigInteger);
        var loc = il.DeclareLocal(_types.BigInteger);
        il.Emit(OpCodes.Stloc, loc);
        il.Emit(OpCodes.Ldloca, loc);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.BigInteger, "ToString"));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBigInt);
    }

    // StringFromValue — ECMA-262 §22.1.1.1 String(value) constructor called as a
    // function. Identical to ToJsString except that Symbol arguments return
    // SymbolDescriptiveString instead of throwing: the String() call form is the
    // single coercion site the spec exempts from ToString's Symbol TypeError.
    private void EmitStringFromValue(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringFromValue",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]);
        runtime.StringFromValueMethod = method;

        var il = method.GetILGenerator();

        // if (value is $TSSymbol) return value.ToString();  // "Symbol(desc)"
        var notSymbolLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brfalse, notSymbolLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notSymbolLabel);

        // return ToJsString(value);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Ret);
    }

    // StringifyCoerce — Stringify behind the ECMA-262 §7.1.17 Symbol guard.
    // Implicit ToString coercion sites (template-literal interpolation, string
    // +/+= concatenation) must throw TypeError for Symbol operands; everything
    // else keeps Stringify's display semantics. Signature forward-declared by
    // DefineRuntimeClassPhase1 because $Runtime.Add's string-concat arm is
    // emitted before this body is filled.
    private void EmitStringifyCoerce(EmittedRuntime runtime)
    {
        var il = runtime.StringifyCoerce.GetILGenerator();

        // if (value is $TSSymbol) throw TypeError
        var notSymbolLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brfalse, notSymbolLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert a Symbol value to a string");
        il.MarkLabel(notSymbolLabel);

        // A bigint coerces to its bare decimal form ("42"), not Stringify's "42n".
        EmitBigIntToStringReturn(il);

        // An object-like value ($Object / Dictionary / List / user class instance)
        // coerces via ToJsString — the spec ToString → OrdinaryToPrimitive(string)
        // protocol, which honors an own toString override and unwraps a boxed
        // wrapper to its primitive (#574). Plain Stringify returns "[object Object]"
        // for a wrapper and the bare "<Class> instance" CLR form for a user class
        // instance, both wrong for template literals / string concat. A user class
        // instance is identified by the $IHasFields marker — $Error/Map/Set/
        // $TSFunction don't implement it, so they keep Stringify's path (#931).
        // Primitive values stay on the cheap Stringify path.
        var objectLikeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, objectLikeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, objectLikeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, objectLikeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brtrue, objectLikeLabel);

        // return Stringify(value);  (primitives)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Ret);

        // return ToJsString(value);  (object-like)
        il.MarkLabel(objectLikeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Ret);
    }

    // ToJsString — ECMA-262 ToString protocol. For Dictionary/$Object receivers
    // with a user-defined "toString" function, invoke it and use the result.
    // Falls back to Stringify for primitives. Used by String.prototype methods
    // that take an object argument (search/indexOf/etc.).
    private void EmitToJsString(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Signature forward-declared by DefineRuntimeClassPhase1 so $RegExp's
        // Symbol.* helpers (which emit before $Runtime's body) can bind to
        // it. Just fill the body on the existing MethodBuilder.
        var method = (MethodBuilder)runtime.ToJsString;
        var il = method.GetILGenerator();
        var fallbackLabel = il.DefineLabel();

        // number[] unboxing: the `is List<object>` array branch below joins the base
        // list directly, so a numeric-mode $Array (empty base list) must materialize first.
        EmitDeoptArgIfNumericArray(il, runtime, 0);

        // null / undefined → handled by Stringify ("null" / "undefined")
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, fallbackLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, fallbackLabel);

        // ECMA-262 7.1.17 ToString — Symbol primitives throw TypeError.
        var notSymbolLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brfalse, notSymbolLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert a Symbol value to a string");
        il.MarkLabel(notSymbolLabel);

        // A bigint coerces to its bare decimal form ("42"), not Stringify's "42n".
        EmitBigIntToStringReturn(il);

        // Already a string → return as-is (avoid CLR ToString round-trip).
        var alreadyStringLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, alreadyStringLabel);

        // ECMA-262 22.3.7 Arguments.prototype.toString inherits from
        // Object.prototype.toString → "[object Arguments]". Without this
        // check, $Arguments (which extends List<object>) hits the List
        // branch and gets comma-joined. Real-world code rarely relies on
        // this brand string, but Test262's `String.prototype.trim.call(arguments)`
        // test asserts on it.
        var notArgumentsBrandLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.ArgumentsType);
        il.Emit(OpCodes.Brfalse, notArgumentsBrandLabel);
        il.Emit(OpCodes.Ldstr, "[object Arguments]");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notArgumentsBrandLabel);

        // List<object> → ECMA-262 Array.prototype.toString returns join(","),
        // not Stringify's debug-style "[a, b]". Build the comma-joined form
        // inline so `String([1,2,3]) === "1,2,3"`. Recursively Stringify each
        // element (matches join's per-element ToString conversion).
        var notListLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brfalse, notListLabel);
        var joinedListLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, joinedListLocal);
        var sbJoinLocal = il.DeclareLocal(_types.StringBuilder);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, sbJoinLocal);
        var idxJoinLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, idxJoinLocal);
        var joinLoop = il.DefineLabel();
        var joinEnd = il.DefineLabel();
        il.MarkLabel(joinLoop);
        il.Emit(OpCodes.Ldloc, idxJoinLocal);
        il.Emit(OpCodes.Ldloc, joinedListLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, joinEnd);
        // Append "," for index > 0
        var skipJoinComma = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, idxJoinLocal);
        il.Emit(OpCodes.Brfalse, skipJoinComma);
        il.Emit(OpCodes.Ldloc, sbJoinLocal);
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipJoinComma);
        // val = list[index]; null/undefined → empty per spec join behavior; else recursive Stringify.
        var valLocalJ = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, joinedListLocal);
        il.Emit(OpCodes.Ldloc, idxJoinLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, valLocalJ);
        var skipAppend = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valLocalJ);
        il.Emit(OpCodes.Brfalse, skipAppend);
        il.Emit(OpCodes.Ldloc, valLocalJ);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, skipAppend);
        il.Emit(OpCodes.Ldloc, sbJoinLocal);
        il.Emit(OpCodes.Ldloc, valLocalJ);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipAppend);
        il.Emit(OpCodes.Ldloc, idxJoinLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, idxJoinLocal);
        il.Emit(OpCodes.Br, joinLoop);
        il.MarkLabel(joinEnd);
        il.Emit(OpCodes.Ldloc, sbJoinLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notListLabel);

        // KeyValuePair<object, object> → treat as a 2-element [key, value] tuple for
        // string coercion (ECMA-262 Array.prototype.toString ≡ join(",")). Map-spread
        // tuples land here in compiled mode because IterateToList falls through to the
        // IEnumerable path, yielding boxed KVP structs instead of List<object> pairs.
        var notKvpLabel = il.DefineLabel();
        var kvpType = _types.MakeGenericType(_types.KeyValuePairOpen, _types.Object, _types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, kvpType);
        il.Emit(OpCodes.Brfalse, notKvpLabel);
        var kvpLocal = il.DeclareLocal(kvpType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, kvpType);
        il.Emit(OpCodes.Stloc, kvpLocal);
        var sbKvpLocal = il.DeclareLocal(_types.StringBuilder);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, _types.EmptyTypes));
        il.Emit(OpCodes.Stloc, sbKvpLocal);
        // Key (index 0): skip if null/undefined, else recursive ToJsString
        var keyLocal = il.DeclareLocal(_types.Object);
        var skipKeyAppend = il.DefineLabel();
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(kvpType, "Key").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, keyLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Brfalse, skipKeyAppend);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, skipKeyAppend);
        il.Emit(OpCodes.Ldloc, sbKvpLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipKeyAppend);
        il.Emit(OpCodes.Ldloc, sbKvpLocal);
        il.Emit(OpCodes.Ldstr, ",");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        // Value (index 1): skip if null/undefined, else recursive ToJsString
        var valueLocal = il.DeclareLocal(_types.Object);
        var skipValueAppend = il.DefineLabel();
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(kvpType, "Value").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Brfalse, skipValueAppend);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, skipValueAppend);
        il.Emit(OpCodes.Ldloc, sbKvpLocal);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(skipValueAppend);
        il.Emit(OpCodes.Ldloc, sbKvpLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notKvpLabel);

        // Only attempt JS-toString invocation for Dictionary, $Object, or user
        // class instances ($IHasFields marker — #931). $Error/Map/Set/$TSFunction
        // don't implement $IHasFields, so they fall through to Stringify (Error
        // keeps its overridden CLR ToString → "TypeError: x"). A user class
        // instance resolves toString/valueOf through its generated GetProperty,
        // yielding the user override or "[object Object]" when neither exists.
        var isObjectLikeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, isObjectLikeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, isObjectLikeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brtrue, isObjectLikeLabel);
        il.Emit(OpCodes.Br, fallbackLabel);

        il.MarkLabel(isObjectLikeLabel);

        // Boxed primitive marker fast-path: if the receiver carries
        // __primitiveType + __primitiveValue (Stage 4z19 wrappers), Stringify
        // the underlying primitive directly. Without this, toString walks the
        // prototype chain to the StringPrototypeGenericStub which doesn't read
        // the marker — returns receiver-as-string instead of the primitive's
        // natural string repr (`new Object(true).valueOf()` gives wrapper, not true).
        var primValLocal = il.DeclareLocal(_types.Object);
        var notBoxedLabel = il.DefineLabel();
        // #574: an own (instance) toString override must win over the boxed
        // __primitiveValue fast-path — ECMA-262 OrdinaryToPrimitive(O, "string")
        // calls the own toString first. When the wrapper carries an own toString,
        // defer to the OrdinaryToPrimitive section below (which invokes it). An
        // inherited prototype toString is NOT own, so un-overridden wrappers still
        // take the fast-path. (HasOwnPropertyHelper does not walk the prototype.)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "toString");
        il.Emit(OpCodes.Call, runtime.HasOwnPropertyHelperMethod);
        il.Emit(OpCodes.Brtrue, notBoxedLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "__primitiveValue");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, primValLocal);
        il.Emit(OpCodes.Ldloc, primValLocal);
        il.Emit(OpCodes.Brfalse, notBoxedLabel);
        il.Emit(OpCodes.Ldloc, primValLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, notBoxedLabel);
        // ECMA-262 §7.1.17 step 2: throw TypeError if the unwrapped primitive
        // is a Symbol. The entry-point check at line ~1856 only catches raw
        // Symbol values — Object(Symbol("x")) wraps it as $Object with
        // __primitiveValue=sym, and the unwrap below bypasses that guard,
        // letting Stringify run on a Symbol (returns "Symbol(x)" rather than
        // throwing). Required by indexOf/searchstring-tostring-errors et al.
        var unwrapNotSymLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, primValLocal);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brfalse, unwrapNotSymLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert a Symbol value to a string");
        il.MarkLabel(unwrapNotSymLabel);
        // Stringify the primitive — handles bool/double/string identically to
        // the top-level fallback path.
        il.Emit(OpCodes.Ldloc, primValLocal);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBoxedLabel);

        // ECMA-262 7.1.1 ToPrimitive(input, "string"): GetMethod(input, @@toPrimitive)
        // takes priority over OrdinaryToPrimitive. Look up Symbol.toPrimitive in the
        // value's symbol-dict (compiled mode stores symbol-keyed properties separately
        // from string-keyed ones). If found and callable, invoke with hint "string".
        // Per spec, the result must be primitive — if it's an object, throw TypeError.
        var afterToPrimSymLabel = il.DefineLabel();
        var symDictForToPrimLocal = il.DeclareLocal(_types.DictionaryObjectObject);
        var toPrimFnLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Stloc, symDictForToPrimLocal);
        il.Emit(OpCodes.Ldloc, symDictForToPrimLocal);
        il.Emit(OpCodes.Brfalse, afterToPrimSymLabel);
        il.Emit(OpCodes.Ldloc, symDictForToPrimLocal);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolToPrimitive);
        il.Emit(OpCodes.Ldloca, toPrimFnLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue"));
        il.Emit(OpCodes.Brfalse, afterToPrimSymLabel);
        il.Emit(OpCodes.Ldloc, toPrimFnLocal);
        il.Emit(OpCodes.Brfalse, afterToPrimSymLabel);
        il.Emit(OpCodes.Ldloc, toPrimFnLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, afterToPrimSymLabel);

        // Accessor descriptor: $CompiledPropertyDescriptor with a Getter field.
        // Object literals with `get [Symbol.toPrimitive]() {...}` store the descriptor
        // here via $Runtime.DefineSymbolAccessor. Invoke the getter to materialize
        // the actual @@toPrimitive function. If the descriptor's Getter is null, the
        // accessor is set-only — fall through to OrdinaryToPrimitive (treat as if
        // @@toPrimitive is undefined).
        var notDescriptorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, toPrimFnLocal);
        il.Emit(OpCodes.Isinst, runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Brfalse, notDescriptorLabel);
        var descGetterLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, toPrimFnLocal);
        il.Emit(OpCodes.Castclass, runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, descGetterLocal);
        il.Emit(OpCodes.Ldloc, descGetterLocal);
        il.Emit(OpCodes.Brfalse, afterToPrimSymLabel);
        // result = InvokeMethodValue(receiver, getter, [])
        var emptyArgsForGetterStr = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, emptyArgsForGetterStr);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, descGetterLocal);
        il.Emit(OpCodes.Ldloc, emptyArgsForGetterStr);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Stloc, toPrimFnLocal);
        // Re-check that the materialized value is non-null/non-undefined.
        il.Emit(OpCodes.Ldloc, toPrimFnLocal);
        il.Emit(OpCodes.Brfalse, afterToPrimSymLabel);
        il.Emit(OpCodes.Ldloc, toPrimFnLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, afterToPrimSymLabel);
        il.MarkLabel(notDescriptorLabel);

        // Build args array ["string"] and invoke.
        var hintArgsStrLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, hintArgsStrLocal);
        il.Emit(OpCodes.Ldloc, hintArgsStrLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldstr, "string");
        il.Emit(OpCodes.Stelem_Ref);
        var toPrimResultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, toPrimFnLocal);
        il.Emit(OpCodes.Ldloc, hintArgsStrLocal);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Stloc, toPrimResultLocal);

        // If primitive (null/undefined/string/number/bool/BigInt) → ToJsString and return.
        var resIsPrimitiveLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, toPrimResultLocal);
        il.Emit(OpCodes.Brfalse, resIsPrimitiveLabel);
        il.Emit(OpCodes.Ldloc, toPrimResultLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, resIsPrimitiveLabel);
        il.Emit(OpCodes.Ldloc, toPrimResultLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, resIsPrimitiveLabel);
        il.Emit(OpCodes.Ldloc, toPrimResultLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, resIsPrimitiveLabel);
        il.Emit(OpCodes.Ldloc, toPrimResultLocal);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, resIsPrimitiveLabel);
        il.Emit(OpCodes.Ldloc, toPrimResultLocal);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brtrue, resIsPrimitiveLabel);
        // Object result → TypeError per ECMA-262 7.1.1 step 1.b.iii.
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert object to primitive value");
        il.MarkLabel(resIsPrimitiveLabel);
        il.Emit(OpCodes.Ldloc, toPrimResultLocal);
        il.Emit(OpCodes.Call, runtime.ToJsString);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(afterToPrimSymLabel);

        // emptyArgs = new object[0]
        var emptyArgsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, emptyArgsLocal);

        // Track whether either toString or valueOf was defined+callable but
        // returned a non-primitive. ECMA-262 7.1.1.1 OrdinaryToPrimitive
        // requires throwing TypeError in this case (both methods produced
        // objects). The lenient "[object Object]" fallback is only correct
        // when neither method exists.
        var sawNonPrimitiveLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, sawNonPrimitiveLocal);

        // ECMA-262 ToPrimitive(O, "string"): try toString, then valueOf.
        void TryInvoke(string name, Label afterLabel)
        {
            var fnLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Stloc, fnLocal);
            il.Emit(OpCodes.Ldloc, fnLocal);
            il.Emit(OpCodes.Brfalse, afterLabel);
            il.Emit(OpCodes.Ldloc, fnLocal);
            il.Emit(OpCodes.Isinst, runtime.UndefinedType);
            il.Emit(OpCodes.Brtrue, afterLabel);

            // result = $Runtime.InvokeMethodValue(receiver, fn, emptyArgs)
            var resultLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, fnLocal);
            il.Emit(OpCodes.Ldloc, emptyArgsLocal);
            il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
            il.Emit(OpCodes.Stloc, resultLocal);

            // If primitive (string / number / bool / null / undefined / BigInt),
            // ToJsString and return. Per ECMA-262 ToPrimitive, all primitive types
            // — including undefined and null — are valid OrdinaryToPrimitive results.
            var resultIsString = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Brfalse, resultIsString); // null primitive
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Isinst, runtime.UndefinedType);
            il.Emit(OpCodes.Brtrue, resultIsString); // undefined primitive
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Isinst, _types.String);
            il.Emit(OpCodes.Brtrue, resultIsString);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Isinst, _types.Double);
            il.Emit(OpCodes.Brtrue, resultIsString);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Isinst, _types.Boolean);
            il.Emit(OpCodes.Brtrue, resultIsString);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Isinst, _types.BigInteger);
            il.Emit(OpCodes.Brtrue, resultIsString);
            // Not primitive — set the flag and continue to next attempt.
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stloc, sawNonPrimitiveLocal);
            il.Emit(OpCodes.Br, afterLabel);

            il.MarkLabel(resultIsString);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Call, runtime.ToJsString);
            il.Emit(OpCodes.Ret);
        }

        var afterToString = il.DefineLabel();
        TryInvoke("toString", afterToString);
        il.MarkLabel(afterToString);
        var afterValueOf = il.DefineLabel();
        TryInvoke("valueOf", afterValueOf);
        il.MarkLabel(afterValueOf);

        // If at least one of toString/valueOf was defined+callable but returned
        // a non-primitive, ECMA-262 demands TypeError. Otherwise (neither method
        // existed), fall back to "[object Object]" — see comment below.
        var noThrowLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, sawNonPrimitiveLocal);
        il.Emit(OpCodes.Brfalse, noThrowLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert object to primitive value");
        il.MarkLabel(noThrowLabel);

        // No usable toString/valueOf on this object — fall back to "[object Object]"
        // per ECMA-262 19.1.3.6 (Object.prototype.toString returns this for plain objects).
        // Lenient: spec strictly throws TypeError when both are unusable, but the
        // compiled-mode prototype-chain walk doesn't reliably surface
        // Object.prototype's toString for user $TSObject receivers. Throwing
        // here regresses charAt/etc. on borrowed prototypes. Tests that depend
        // on the throw (`{toString: undefined, valueOf: undefined}`) are a
        // smaller bucket than tests that depend on the "[object Object]"
        // fallback for plain user objects.
        il.Emit(OpCodes.Ldstr, "[object Object]");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(alreadyStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(fallbackLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Stringify);
        il.Emit(OpCodes.Ret);
    }

    private void EmitToNumber(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = (MethodBuilder)runtime.ToNumber;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Double);

        // ECMA-262 7.1.4 ToNumber on object: ToPrimitive(value, "number") which
        // tries valueOf first, then toString. Without this, Math.hypot(obj-with-
        // throwing-valueOf) silently returns NaN instead of propagating the
        // throw. Apply for Dictionary or $Object only.
        var argLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, argLocal);

        var skipToPrimLabelTop = il.DefineLabel();
        var doToPrimLabelTop = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, doToPrimLabelTop);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, skipToPrimLabelTop);
        il.MarkLabel(doToPrimLabelTop);

        // ECMA-262 7.1.1 ToPrimitive(input, "number"): @@toPrimitive (if defined and
        // callable) takes priority over OrdinaryToPrimitive. Look up the symbol-keyed
        // method and invoke with hint "number". Result must be primitive or TypeError.
        var afterToPrimSymN = il.DefineLabel();
        var doThrowN = il.DefineLabel();
        var symDictForToPrimN = il.DeclareLocal(_types.DictionaryObjectObject);
        var toPrimFnLocalN = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Stloc, symDictForToPrimN);
        il.Emit(OpCodes.Ldloc, symDictForToPrimN);
        il.Emit(OpCodes.Brfalse, afterToPrimSymN);
        il.Emit(OpCodes.Ldloc, symDictForToPrimN);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolToPrimitive);
        il.Emit(OpCodes.Ldloca, toPrimFnLocalN);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "TryGetValue"));
        il.Emit(OpCodes.Brfalse, afterToPrimSymN);
        il.Emit(OpCodes.Ldloc, toPrimFnLocalN);
        il.Emit(OpCodes.Brfalse, afterToPrimSymN);
        il.Emit(OpCodes.Ldloc, toPrimFnLocalN);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, afterToPrimSymN);

        // Accessor descriptor unwrap (mirrors EmitToJsString — see notes there).
        var notDescLabelN = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, toPrimFnLocalN);
        il.Emit(OpCodes.Isinst, runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Brfalse, notDescLabelN);
        var descGetterLocalN = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, toPrimFnLocalN);
        il.Emit(OpCodes.Castclass, runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, descGetterLocalN);
        il.Emit(OpCodes.Ldloc, descGetterLocalN);
        il.Emit(OpCodes.Brfalse, afterToPrimSymN);
        var emptyArgsForGetterNum = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, emptyArgsForGetterNum);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Ldloc, descGetterLocalN);
        il.Emit(OpCodes.Ldloc, emptyArgsForGetterNum);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Stloc, toPrimFnLocalN);
        il.Emit(OpCodes.Ldloc, toPrimFnLocalN);
        il.Emit(OpCodes.Brfalse, afterToPrimSymN);
        il.Emit(OpCodes.Ldloc, toPrimFnLocalN);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, afterToPrimSymN);
        il.MarkLabel(notDescLabelN);

        var hintArgsNumLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, hintArgsNumLocal);
        il.Emit(OpCodes.Ldloc, hintArgsNumLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldstr, "number");
        il.Emit(OpCodes.Stelem_Ref);
        var toPrimResultLocalN = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Ldloc, toPrimFnLocalN);
        il.Emit(OpCodes.Ldloc, hintArgsNumLocal);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Stloc, toPrimResultLocalN);
        // Object result → TypeError per ECMA-262 7.1.1 step 1.b.iii.
        il.Emit(OpCodes.Ldloc, toPrimResultLocalN);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, doThrowN);
        il.Emit(OpCodes.Ldloc, toPrimResultLocalN);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, doThrowN);
        il.Emit(OpCodes.Ldloc, toPrimResultLocalN);
        il.Emit(OpCodes.Stloc, argLocal);
        il.Emit(OpCodes.Br, afterToPrimSymN);
        il.MarkLabel(doThrowN);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert object to primitive value");
        il.MarkLabel(afterToPrimSymN);

        var emptyArgsLocalT = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, emptyArgsLocalT);

        void TryToPrim2(string name, Label afterLabel)
        {
            var fnLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldloc, argLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Stloc, fnLocal);
            il.Emit(OpCodes.Ldloc, fnLocal);
            il.Emit(OpCodes.Brfalse, afterLabel);
            il.Emit(OpCodes.Ldloc, fnLocal);
            il.Emit(OpCodes.Isinst, runtime.UndefinedType);
            il.Emit(OpCodes.Brtrue, afterLabel);

            var resLoc = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldloc, argLocal);
            il.Emit(OpCodes.Ldloc, fnLocal);
            il.Emit(OpCodes.Ldloc, emptyArgsLocalT);
            il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
            il.Emit(OpCodes.Stloc, resLoc);

            il.Emit(OpCodes.Ldloc, resLoc);
            il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
            il.Emit(OpCodes.Brtrue, afterLabel);
            il.Emit(OpCodes.Ldloc, resLoc);
            il.Emit(OpCodes.Isinst, runtime.TSObjectType);
            il.Emit(OpCodes.Brtrue, afterLabel);
            il.Emit(OpCodes.Ldloc, resLoc);
            il.Emit(OpCodes.Stloc, argLocal);
        }

        var afterValueOfT = il.DefineLabel();
        TryToPrim2("valueOf", afterValueOfT);
        il.MarkLabel(afterValueOfT);

        var afterToStringT = il.DefineLabel();
        var stillObjT = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, stillObjT);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, afterToStringT);
        il.MarkLabel(stillObjT);
        TryToPrim2("toString", afterToStringT);
        il.MarkLabel(afterToStringT);

        // ECMA-262 7.1.1.1: if both methods returned non-primitives, throw TypeError.
        var afterTeT = il.DefineLabel();
        var stillObjTeT = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, stillObjTeT);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, afterTeT);
        il.MarkLabel(stillObjTeT);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert object to primitive value");
        il.MarkLabel(afterTeT);

        il.MarkLabel(skipToPrimLabelTop);

        // ECMA-262 7.1.4 ToNumber on Symbol → throws TypeError. Without this,
        // Convert.ToDouble would catch the InvalidCastException → NaN → 0,
        // silently masking the spec-required throw (e.g. `(0).toFixed(Symbol())`
        // must throw, not silently produce "0").
        var notSymbolLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brfalse, notSymbolLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert a Symbol value to a number");
        il.MarkLabel(notSymbolLabel);

        // ECMA-262 7.1.4 step 2: BigInt → TypeError. `(0).toFixed(0n)` must
        // throw not silently coerce. Convert.ToDouble would otherwise narrow
        // BigInteger to its double value (or throw OverflowException → NaN).
        var notBigIntLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brfalse, notBigIntLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert a BigInt to a number");
        il.MarkLabel(notBigIntLabel);

        // ECMA-262 ToNumber: strings with "0x"/"0X" prefix parse as hex. Convert.ToDouble
        // throws on those, so special-case before the fallback. Without this, tests that
        // set `length: "0x0002"` on array-likes surface as NaN → 0 → empty iteration.
        var tryParseInt64 = _types.GetMethod(_types.Int64, "Parse", _types.String, typeof(System.Globalization.NumberStyles), typeof(System.IFormatProvider));
        var notHexLabel = il.DefineLabel();
        var strLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, strLocal);
        il.Emit(OpCodes.Brfalse, notHexLabel);

        // if (strLocal.Length >= 2 && (strLocal[0] == '0') && (strLocal[1] == 'x' || strLocal[1] == 'X'))
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Blt, notHexLabel);

        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Ldc_I4, (int)'0');
        il.Emit(OpCodes.Bne_Un, notHexLabel);

        // second char == 'x' or 'X': compare with OR'd check. Use (ch | 0x20) == 'x'.
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Ldc_I4, 0x20);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Ldc_I4, (int)'x');
        il.Emit(OpCodes.Bne_Un, notHexLabel);

        // Hex-parse: strLocal.Substring(2), try Int64.Parse with HexNumber style.
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Substring", [_types.Int32])!);
        il.Emit(OpCodes.Ldc_I4, (int)System.Globalization.NumberStyles.HexNumber);
        // CultureInfo.InvariantCulture — property getter, not a static field. Use Call.
        il.Emit(OpCodes.Call, typeof(System.Globalization.CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, tryParseInt64);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.EndExceptionBlock();
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notHexLabel);

        // ECMA-262: strings with "0b"/"0B" parse as binary literals,
        // "0o"/"0O" as octal. Convert.ToDouble doesn't recognize these.
        // Pattern: "0[bB][01]+" or "0[oO][0-7]+".
        EmitParsePrefixedInt(il, strLocal, resultLocal, 'b', 2);
        EmitParsePrefixedInt(il, strLocal, resultLocal, 'o', 8);

        // Handle "Infinity"/"+Infinity"/"-Infinity" strings before Convert.ToDouble
        // (which throws FormatException on those — caught below as NaN, but
        // ECMA-262 specifies +Infinity/-Infinity numeric values).
        var notInfStrLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Brfalse, notInfStrLabel);

        // Trim and check
        var trimmedLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Trim", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, trimmedLocal);

        // "Infinity" → +Inf
        var notPlainInf = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, trimmedLocal);
        il.Emit(OpCodes.Ldstr, "Infinity");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brfalse, notPlainInf);
        il.Emit(OpCodes.Ldc_R8, double.PositiveInfinity);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notPlainInf);

        // "+Infinity" → +Inf
        var notPlusInf = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, trimmedLocal);
        il.Emit(OpCodes.Ldstr, "+Infinity");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brfalse, notPlusInf);
        il.Emit(OpCodes.Ldc_R8, double.PositiveInfinity);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notPlusInf);

        // "-Infinity" → -Inf
        var notMinusInf = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, trimmedLocal);
        il.Emit(OpCodes.Ldstr, "-Infinity");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brfalse, notMinusInf);
        il.Emit(OpCodes.Ldc_R8, double.NegativeInfinity);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notMinusInf);

        il.MarkLabel(notInfStrLabel);

        // Use Convert.ToDouble with try-catch fallback to NaN
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.EndExceptionBlock();
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits JsToInt32(object) → int that implements ECMA-262 ToInt32
    /// (7.1.6 in the spec). Unlike Convert.ToInt32, NaN / ±Infinity / out-of-range
    /// doubles wrap modulo 2^32 instead of throwing, matching JavaScript's
    /// bitwise-op and `x | 0` semantics. Required for packages like lodash
    /// and debug that rely on <c>hash |= 0</c> idioms.
    /// </summary>
    /// <summary>
    /// Emits IL that parses "0[Pp]<digits>" prefixed integer-literal strings
    /// to a double. If the string at <paramref name="strLocal"/> doesn't
    /// match the prefix shape, falls through. Used by ToNumber for binary
    /// (0b/0B, radix 2) and octal (0o/0O, radix 8) literal support per
    /// ECMA-262 7.1.4.1. Hex (0x/0X) has its own inline path since it
    /// predates this helper.
    /// </summary>
    private void EmitParsePrefixedInt(ILGenerator il, LocalBuilder strLocal,
        LocalBuilder resultLocal, char prefix, int radix)
    {
        var skipLabel = il.DefineLabel();

        // strLocal == null? skip.
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Brfalse, skipLabel);

        // Length >= 3?
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Blt, skipLabel);

        // strLocal[0] == '0'?
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Ldc_I4, (int)'0');
        il.Emit(OpCodes.Bne_Un, skipLabel);

        // (strLocal[1] | 0x20) == prefix?
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Ldc_I4, 0x20);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Ldc_I4, (int)prefix);
        il.Emit(OpCodes.Bne_Un, skipLabel);

        // try { result = (double)Convert.ToInt64(strLocal.Substring(2), radix); }
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Substring", [_types.Int32])!);
        il.Emit(OpCodes.Ldc_I4, radix);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt64", _types.String, _types.Int32));
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.EndExceptionBlock();
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(skipLabel);
    }

    private void EmitJsToInt32(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Signature forward-declared by DefineRuntimeClassPhase1 so $RegExp
        // (which emits before $Runtime's body) can call it; reuse that slot.
        var method = (MethodBuilder)runtime.JsToInt32;

        var il = method.GetILGenerator();

        const double TWO_32 = 4294967296.0;
        const double TWO_31 = 2147483648.0;

        // n = ToNumber(arg0)
        var nLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ToNumber);
        il.Emit(OpCodes.Stloc, nLocal);

        // if (!double.IsFinite(n)) return 0
        var finiteLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Call, typeof(double).GetMethod("IsFinite", [typeof(double)])!);
        il.Emit(OpCodes.Brtrue, finiteLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(finiteLabel);

        // truncated = n >= 0 ? Math.Floor(n) : Math.Ceiling(n)
        var truncLocal = il.DeclareLocal(_types.Double);
        var negLabel = il.DefineLabel();
        var truncDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Blt, negLabel);
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Floor", [typeof(double)])!);
        il.Emit(OpCodes.Stloc, truncLocal);
        il.Emit(OpCodes.Br, truncDoneLabel);
        il.MarkLabel(negLabel);
        il.Emit(OpCodes.Ldloc, nLocal);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Ceiling", [typeof(double)])!);
        il.Emit(OpCodes.Stloc, truncLocal);
        il.MarkLabel(truncDoneLabel);

        // int32bit = truncated - Math.Floor(truncated / 2^32) * 2^32   (mathematical mod)
        var int32bitLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldloc, truncLocal);
        il.Emit(OpCodes.Ldloc, truncLocal);
        il.Emit(OpCodes.Ldc_R8, TWO_32);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Floor", [typeof(double)])!);
        il.Emit(OpCodes.Ldc_R8, TWO_32);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, int32bitLocal);

        // return int32bit >= 2^31 ? (int)(int32bit - 2^32) : (int)int32bit
        var smallLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, int32bitLocal);
        il.Emit(OpCodes.Ldc_R8, TWO_31);
        il.Emit(OpCodes.Blt, smallLabel);
        il.Emit(OpCodes.Ldloc, int32bitLocal);
        il.Emit(OpCodes.Ldc_R8, TWO_32);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(smallLabel);
        il.Emit(OpCodes.Ldloc, int32bitLocal);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits ConvertToNumber — matches JS Number(value) semantics.
    /// Differs from ToNumber in that empty/whitespace strings return 0 (not NaN).
    /// </summary>
    /// <summary>
    /// Pre-declares the $Runtime.ConvertToNumber MethodBuilder so its slot is
    /// assigned before any other emitter binds to it. Body is filled in by
    /// <see cref="EmitConvertToNumber"/> later, after GetProperty/InvokeMethodValue
    /// are available (the ToPrimitive(value, "number") chain on Dictionary/$Object
    /// args calls those helpers).
    /// </summary>
    internal void DeclareConvertToNumber(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConvertToNumber",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        runtime.ConvertToNumber = method;
    }

    private void EmitConvertToNumber(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = (MethodBuilder)runtime.ConvertToNumber;

        var il = method.GetILGenerator();

        var nullLabel = il.DefineLabel();
        var undefinedLabel = il.DefineLabel();
        var doubleLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var nanLabel = il.DefineLabel();

        // ECMA-262 7.1.4 ToNumber on object: ToPrimitive(value, "number") which
        // tries valueOf first, then toString. Without this, Number({valueOf:
        // () => 1}) returns NaN (object falls through to nan-label).
        // Apply for Dictionary or $Object only — those hold user-defined
        // valueOf/toString. Boxed primitives (via $Object marker fields) are
        // also $Object, which is correct: their valueOf returns the underlying
        // primitive per ECMA-262 19.x.3.7.
        var argLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, argLocal);

        var skipToPrimLabel = il.DefineLabel();
        var doToPrimLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, doToPrimLabel);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, skipToPrimLabel);
        il.MarkLabel(doToPrimLabel);

        // ToPrimitive: try valueOf, then toString. Mirrors EmitLengthToPrimitive's
        // logic but writes to argLocal so the existing branches below see the
        // (possibly replaced) primitive value.
        var emptyArgsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, emptyArgsLocal);

        void TryToPrim(string name, Label afterLabel)
        {
            var fnLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldloc, argLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, runtime.GetProperty);
            il.Emit(OpCodes.Stloc, fnLocal);
            il.Emit(OpCodes.Ldloc, fnLocal);
            il.Emit(OpCodes.Brfalse, afterLabel);
            il.Emit(OpCodes.Ldloc, fnLocal);
            il.Emit(OpCodes.Isinst, runtime.UndefinedType);
            il.Emit(OpCodes.Brtrue, afterLabel);

            var resultLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldloc, argLocal);
            il.Emit(OpCodes.Ldloc, fnLocal);
            il.Emit(OpCodes.Ldloc, emptyArgsLocal);
            il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
            il.Emit(OpCodes.Stloc, resultLocal);

            // Still object? Fall through to next attempt without committing.
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
            il.Emit(OpCodes.Brtrue, afterLabel);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Isinst, runtime.TSObjectType);
            il.Emit(OpCodes.Brtrue, afterLabel);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Stloc, argLocal);
        }

        var afterValueOf = il.DefineLabel();
        TryToPrim("valueOf", afterValueOf);
        il.MarkLabel(afterValueOf);

        var afterToString = il.DefineLabel();
        var stillObj = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, stillObj);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, afterToString);
        il.MarkLabel(stillObj);
        TryToPrim("toString", afterToString);
        il.MarkLabel(afterToString);

        // ECMA-262 7.1.1.1 OrdinaryToPrimitive: if neither valueOf nor toString
        // returned a primitive (both returned objects, or neither was callable),
        // throw TypeError. Pre-fix: the value fell through to NaN, silently
        // masking the spec-required throw.
        var afterTypeErrorLabel = il.DefineLabel();
        var stillObjAfterToString = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, stillObjAfterToString);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, afterTypeErrorLabel);
        il.MarkLabel(stillObjAfterToString);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert object to primitive value");
        il.MarkLabel(afterTypeErrorLabel);

        il.MarkLabel(skipToPrimLabel);

        // null => 0.0
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // ECMA-262 21.1.1.1 → 7.1.4: Number(Symbol) throws TypeError. Without
        // this branch the value falls through to the "everything else → NaN"
        // tail, masking the spec-required throw.
        var notSymbolConvLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brfalse, notSymbolConvLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Cannot convert a Symbol value to a number");
        il.MarkLabel(notSymbolConvLabel);

        // ECMA-262 21.1.1.1 step 5: Number(BigInt) returns a Number with the
        // same mathematical value (rounded per 21.1.1.1.1 NumberFromBigInt).
        // System.Numerics.BigInteger has an explicit op_Explicit(BigInteger)
        // → double — use it. Don't throw (ToNumber's spec wants throw, but
        // ConvertToNumber backs the Number() constructor's spec which coerces).
        var notBigIntConvLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brfalse, notBigIntConvLabel);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Unbox_Any, _types.BigInteger);
        // System.Numerics.BigInteger has multiple op_Explicit overloads
        // (one per primitive return type). Pick the BigInteger → double one
        // by walking the candidate set explicitly.
        System.Reflection.MethodInfo? explicitToDouble = null;
        foreach (var m in _types.BigInteger.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (m.Name != "op_Explicit") continue;
            if (m.ReturnType != _types.Double) continue;
            var ps = m.GetParameters();
            if (ps.Length == 1 && ps[0].ParameterType == _types.BigInteger)
            {
                explicitToDouble = m;
                break;
            }
        }
        if (explicitToDouble != null)
        {
            il.Emit(OpCodes.Call, explicitToDouble);
            il.Emit(OpCodes.Ret);
        }
        else
        {
            // Fallback: Convert.ToDouble(BigInteger) via boxed object.
            il.Emit(OpCodes.Box, _types.BigInteger);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
            il.Emit(OpCodes.Ret);
        }
        il.MarkLabel(notBigIntConvLabel);

        // undefined => NaN
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, undefinedLabel);

        // double => return as-is
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, doubleLabel);

        // bool => true:1.0, false:0.0
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, boolLabel);

        // string => trim, empty→0, tryparse, else NaN
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        // .NET interop: boxed non-double numerics (float, int, long, decimal, …)
        // arrive via @DotNetType calls. Convert them numerically rather than
        // falling through to NaN (EnsureDouble routes through this method).
        var notConvertibleLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Isinst, typeof(IConvertible));
        il.Emit(OpCodes.Brfalse, notConvertibleLabel);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToDouble", _types.Object));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notConvertibleLabel);

        // everything else => NaN
        il.Emit(OpCodes.Br, nanLabel);

        // null case: return 0.0
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ret);

        // undefined case: return NaN
        il.MarkLabel(undefinedLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);

        // double case: unbox and return
        il.MarkLabel(doubleLabel);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Ret);

        // bool case: unbox, convert to float
        il.MarkLabel(boolLabel);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ret);

        // string case: trim, check empty, try parse
        il.MarkLabel(stringLabel);
        var trimmedLocal = il.DeclareLocal(_types.String);
        var resultLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldloc, argLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.String, "Trim"));
        il.Emit(OpCodes.Stloc, trimmedLocal);

        // if (trimmed.Length == 0) return 0.0
        var nonEmptyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, trimmedLocal);
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, nonEmptyLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ret);

        // ECMA-262 21.1.1.1 / 7.1.4: numeric string can be hex ("0x..."/"0X..."),
        // Infinity (already short-circuited by ToNumber), or float. Try hex first.
        il.MarkLabel(nonEmptyLabel);
        var notHexInConvLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, trimmedLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Blt, notHexInConvLabel);
        il.Emit(OpCodes.Ldloc, trimmedLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Ldc_I4, (int)'0');
        il.Emit(OpCodes.Bne_Un, notHexInConvLabel);
        il.Emit(OpCodes.Ldloc, trimmedLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Ldc_I4, 0x20);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Ldc_I4, (int)'x');
        il.Emit(OpCodes.Bne_Un, notHexInConvLabel);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, trimmedLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Substring", [_types.Int32])!);
        il.Emit(OpCodes.Ldc_I4, (int)System.Globalization.NumberStyles.HexNumber);
        il.Emit(OpCodes.Call, typeof(System.Globalization.CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, typeof(long).GetMethod("Parse", [typeof(string), typeof(System.Globalization.NumberStyles), typeof(IFormatProvider)])!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.BeginCatchBlock(_types.Exception);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.EndExceptionBlock();
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notHexInConvLabel);
        // ECMA-262: "0b"/"0B" → binary, "0o"/"0O" → octal literal parsing.
        EmitParsePrefixedInt(il, trimmedLocal, resultLocal, 'b', 2);
        EmitParsePrefixedInt(il, trimmedLocal, resultLocal, 'o', 8);

        // ECMA-262 7.1.4: only the case-sensitive forms "Infinity", "+Infinity",
        // "-Infinity" are valid Infinity literals. .NET's Double.TryParse
        // (NumberStyles.Float) accepts "infinity"/"INFINITY"/etc case-
        // insensitively — must reject those before TryParse runs.
        var notCiInfLabel = il.DefineLabel();
        // Exact case-sensitive forms first → return ±Infinity.
        var trimEqInfLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, trimmedLocal);
        il.Emit(OpCodes.Ldstr, "Infinity");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, trimEqInfLabel);
        il.Emit(OpCodes.Ldc_R8, double.PositiveInfinity);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(trimEqInfLabel);
        var trimEqPlusInfLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, trimmedLocal);
        il.Emit(OpCodes.Ldstr, "+Infinity");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, trimEqPlusInfLabel);
        il.Emit(OpCodes.Ldc_R8, double.PositiveInfinity);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(trimEqPlusInfLabel);
        var trimEqMinusInfLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, trimmedLocal);
        il.Emit(OpCodes.Ldstr, "-Infinity");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, trimEqMinusInfLabel);
        il.Emit(OpCodes.Ldc_R8, double.NegativeInfinity);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(trimEqMinusInfLabel);
        // Now reject any string that contains "infinity" case-insensitively
        // (since the exact-case forms have already been short-circuited).
        // Use String.Contains(string, StringComparison) — net8+ overload.
        il.Emit(OpCodes.Ldloc, trimmedLocal);
        il.Emit(OpCodes.Ldstr, "infinity");
        il.Emit(OpCodes.Ldc_I4_5); // StringComparison.OrdinalIgnoreCase
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("Contains", [_types.String, typeof(StringComparison)])!);
        il.Emit(OpCodes.Brfalse, notCiInfLabel);
        // Case-insensitive but not exact → NaN.
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notCiInfLabel);

        // double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
        il.Emit(OpCodes.Ldloc, trimmedLocal);
        il.Emit(OpCodes.Ldc_I4, (int)System.Globalization.NumberStyles.Float);
        il.Emit(OpCodes.Call, typeof(System.Globalization.CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, resultLocal);
        il.Emit(OpCodes.Call, typeof(double).GetMethod("TryParse", [typeof(string), typeof(System.Globalization.NumberStyles), typeof(IFormatProvider), typeof(double).MakeByRefType()])!);
        var parseSuccessLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, parseSuccessLabel);
        // parse failed => NaN
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);
        // parse succeeded => return result
        il.MarkLabel(parseSuccessLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // NaN fallback
        il.MarkLabel(nanLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Ret);
    }

    private void EmitIsTruthy(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Signature forward-declared by DefineRuntimeClassPhase1 so $RegExp
        // (which emits before $Runtime's body) can call it; reuse that slot.
        var method = (MethodBuilder)runtime.IsTruthy;

        var il = method.GetILGenerator();
        var falseLabel = il.DefineLabel();
        var checkBool = il.DefineLabel();
        var checkDouble = il.DefineLabel();
        var checkString = il.DefineLabel();
        var checkBigInt = il.DefineLabel();
        var trueLabel = il.DefineLabel();

        // null => false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, falseLabel);

        // undefined => false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, falseLabel);

        // bool => return value
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, checkBool);

        // double => check for 0 and NaN
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, checkDouble);

        // string => check for empty
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, checkString);

        // bigint => 0n is falsy (ToBoolean(bigint): false iff zero)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brtrue, checkBigInt);

        // everything else => true
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // Check bool value
        il.MarkLabel(checkBool);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // Check double: 0 and NaN are falsy
        il.MarkLabel(checkDouble);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        var dLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, dLocal);
        // Check if d == 0
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, falseLabel);
        // Check if d is NaN (NaN != NaN)
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brfalse, falseLabel); // If d != d, it's NaN
        // Not 0 and not NaN => truthy
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // Check string: empty is falsy
        il.MarkLabel(checkString);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "get_Length"));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Cgt);
        il.Emit(OpCodes.Ret);

        // Check bigint: truthy iff non-zero (value != BigInteger.Zero).
        il.MarkLabel(checkBigInt);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.BigInteger);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.BigInteger, "get_Zero"));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.BigInteger, "op_Inequality", _types.BigInteger, _types.BigInteger));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }
}
