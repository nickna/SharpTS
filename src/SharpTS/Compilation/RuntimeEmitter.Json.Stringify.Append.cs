using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private MethodBuilder? _appendJsonValueMethod;
    private MethodBuilder? _appendJsonNumberMethod;

    /// <summary>
    /// Emits integer-valued finite doubles directly into a StringBuilder-owned
    /// span. Other numbers retain the shared ECMAScript formatter.
    /// </summary>
    private MethodBuilder EmitAppendJsonNumberHelper(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime)
    {
        if (_appendJsonNumberMethod is not null)
            return _appendJsonNumberMethod;

        var method = typeBuilder.DefineMethod(
            "AppendJsonNumber",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Void,
            [_types.StringBuilder, _types.Double]);
        method.SetImplementationFlags(MethodImplAttributes.AggressiveInlining);
        _appendJsonNumberMethod = method;

        var numberBufferField = typeBuilder.DefineField(
            "_jsonNumberBuffer",
            typeof(char[]),
            FieldAttributes.Private | FieldAttributes.Static);
        numberBufferField.SetCustomAttribute(
            typeof(ThreadStaticAttribute).GetConstructor(Type.EmptyTypes)!,
            CustomAttributeEncoder.EmptyBlob);

        var il = method.GetILGenerator();
        var spanLocal = il.DeclareLocal(typeof(Span<char>));
        var bufferLocal = il.DeclareLocal(typeof(char[]));
        var formatLocal = il.DeclareLocal(typeof(ReadOnlySpan<char>));
        var charsWrittenLocal = il.DeclareLocal(_types.Int32);
        var integerLocal = il.DeclareLocal(typeof(long));
        var appendNull = il.DefineLabel();
        var fallback = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", [_types.Double]));
        il.Emit(OpCodes.Brtrue, appendNull);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsInfinity", [_types.Double]));
        il.Emit(OpCodes.Brtrue, appendNull);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Truncate", [typeof(double)])!);
        il.Emit(OpCodes.Bne_Un, fallback);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Abs", [typeof(double)])!);
        il.Emit(OpCodes.Ldc_R8, 9_007_199_254_740_992.0);
        il.Emit(OpCodes.Bge_Un, fallback);

        // Reuse a bounded per-thread maximum-width buffer. Formatting and
        // StringBuilder.Append(char[], start, count) create no child string.
        var bufferReady = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, numberBufferField);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, bufferReady);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4, 20);
        il.Emit(OpCodes.Newarr, _types.Char);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stsfld, numberBufferField);
        il.MarkLabel(bufferReady);
        il.Emit(OpCodes.Stloc, bufferLocal);
        il.Emit(OpCodes.Ldloc, bufferLocal);
        il.Emit(OpCodes.Call, typeof(Span<char>).GetMethod(
            "op_Implicit",
            [typeof(char[])])!);
        il.Emit(OpCodes.Stloc, spanLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Stloc, integerLocal);
        il.Emit(OpCodes.Ldloca, formatLocal);
        il.Emit(OpCodes.Initobj, typeof(ReadOnlySpan<char>));
        il.Emit(OpCodes.Ldloca, integerLocal);
        il.Emit(OpCodes.Ldloc, spanLocal);
        il.Emit(OpCodes.Ldloca, charsWrittenLocal);
        il.Emit(OpCodes.Ldloc, formatLocal);
        il.Emit(OpCodes.Call, typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, typeof(long).GetMethod(
            "TryFormat",
            [
                typeof(Span<char>),
                typeof(int).MakeByRefType(),
                typeof(ReadOnlySpan<char>),
                typeof(IFormatProvider)
            ])!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, bufferLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, charsWrittenLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.StringBuilder,
            "Append",
            [typeof(char[]), _types.Int32, _types.Int32])!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(appendNull);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(fallback);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.FormatNumber);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>
    /// Emits the no-replacer/no-space single-destination serializer. One root
    /// builder owns the complete result; recursive calls append into it and
    /// return false only for JSON-undefined values.
    /// </summary>
    private MethodBuilder EmitAppendJsonValueHelper(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime)
    {
        if (_appendJsonValueMethod is not null)
            return _appendJsonValueMethod;

        var appendNumber = EmitAppendJsonNumberHelper(typeBuilder, runtime);
        var method = typeBuilder.DefineMethod(
            "AppendJsonValue",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Boolean,
            [
                _types.StringBuilder, // destination
                _types.Object,        // value
                _types.Int32,         // depth
                _types.String,        // string/root key (null for array indices)
                _types.Int32,         // array index
                _types.Boolean,       // key is array index
                _types.Boolean,       // emit object-property prefix
                _types.Boolean        // prefix needs comma
            ]);
        _appendJsonValueMethod = method;

        var il = method.GetILGenerator();
        var valueLocal = il.DeclareLocal(_types.Object);
        var allowPooledKeysLocal = il.DeclareLocal(_types.Boolean);
        var rawJsonLabel = il.DefineLabel();
        var nullLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var numberLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var arrayLabel = il.DefineLabel();
        var objectLabel = il.DefineLabel();
        var classLabel = il.DefineLabel();
        var regexpLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();
        var dispatchLabel = il.DefineLabel();

        var depthOk = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4, 512);
        il.Emit(OpCodes.Blt, depthOk);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Converting circular structure to JSON");
        il.MarkLabel(depthOk);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, allowPooledKeysLocal);

        // Undefined is the only ordinary false result: roots map it to the
        // singleton, arrays substitute null, and objects omit the property.
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Brfalse, dispatchLabel);

        EmitToJsonCheck(
            il,
            valueLocal,
            runtime,
            keyArgIndex: 3,
            keyIndexArgIndex: 4,
            keyIsIndexArgIndex: 5);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Brfalse, dispatchLabel);

        // Callable and symbol values serialize as undefined.
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, falseLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, falseLabel);

        EmitBoxedPrimitiveJsonCoerce(il, valueLocal, runtime);
        EmitBigIntCheck(il, valueLocal, runtime);

        var notProxy = il.DefineLabel();
        EmitProxyMaterializeForJson(il, valueLocal, notProxy, runtime);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, allowPooledKeysLocal);
        il.Emit(OpCodes.Br, dispatchLabel);
        il.MarkLabel(notProxy);

        // Nothing below can produce JSON-undefined, so the object-property
        // prefix is safe to append exactly once now.
        il.MarkLabel(dispatchLabel);
        EmitAppendJsonPropertyPrefix(il);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Brfalse, nullLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.TSRawJsonType);
        il.Emit(OpCodes.Brtrue, rawJsonLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, boolLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, numberLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, arrayLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, objectLabel);
        if (_features.UsesRegExp)
        {
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            il.Emit(OpCodes.Brtrue, regexpLabel);
        }
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brtrue, classLabel);
        il.Emit(OpCodes.Br, nullLabel);

        il.MarkLabel(rawJsonLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, runtime.TSRawJsonType);
        il.Emit(OpCodes.Callvirt, runtime.TSRawJsonTextGetter);
        EmitStringBuilderAppendString(il);
        EmitTrueReturn(il);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "null");
        EmitStringBuilderAppendString(il);
        EmitTrueReturn(il);

        il.MarkLabel(boolLabel);
        var appendFalse = il.DefineLabel();
        var boolDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brfalse, appendFalse);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "true");
        EmitStringBuilderAppendString(il);
        il.Emit(OpCodes.Br, boolDone);
        il.MarkLabel(appendFalse);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "false");
        EmitStringBuilderAppendString(il);
        il.MarkLabel(boolDone);
        EmitTrueReturn(il);

        il.MarkLabel(numberLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Call, appendNumber);
        EmitTrueReturn(il);

        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, _appendEscapedJsonStringMethod!);
        EmitTrueReturn(il);

        il.MarkLabel(arrayLabel);
        EmitAppendJsonArray(il, method, valueLocal, runtime);

        il.MarkLabel(objectLabel);
        EmitAppendJsonObject(
            il,
            method,
            valueLocal,
            allowPooledKeysLocal,
            runtime);

        il.MarkLabel(classLabel);
        var fieldsLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, runtime.TSObjectMergeEnumerable);
        il.Emit(OpCodes.Stloc, fieldsLocal);
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Brfalse, regexpLabel);
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, allowPooledKeysLocal);
        il.Emit(OpCodes.Br, objectLabel);

        il.MarkLabel(regexpLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "{}");
        EmitStringBuilderAppendString(il);
        EmitTrueReturn(il);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private void EmitAppendJsonPropertyPrefix(ILGenerator il)
    {
        var noPrefix = il.DefineLabel();
        var noComma = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, 6);
        il.Emit(OpCodes.Brfalse, noPrefix);
        il.Emit(OpCodes.Ldarg, 7);
        il.Emit(OpCodes.Brfalse, noComma);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)',');
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.StringBuilder, "Append", [_types.Char]));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(noComma);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, _appendEscapedJsonStringMethod!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)':');
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.StringBuilder, "Append", [_types.Char]));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(noPrefix);
    }

    private void EmitAppendJsonArray(
        ILGenerator il,
        MethodBuilder appendValue,
        LocalBuilder valueLocal,
        EmittedRuntime runtime)
    {
        var arrayLocal = il.DeclareLocal(_types.ListOfObject);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var loop = il.DefineLabel();
        var end = il.DefineLabel();
        var noComma = il.DefineLabel();
        var notHole = il.DefineLabel();
        var appended = il.DefineLabel();

        EmitDeoptIfNumericArray(il, runtime, () => il.Emit(OpCodes.Ldloc, valueLocal));
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, arrayLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)'[');
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.StringBuilder, "Append", [_types.Char]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, arrayLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, end);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Brfalse, noComma);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)',');
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.StringBuilder, "Append", [_types.Char]));
        il.Emit(OpCodes.Pop);
        il.MarkLabel(noComma);

        il.Emit(OpCodes.Ldloc, arrayLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.ListOfObject, "get_Item", [_types.Int32]));
        il.Emit(OpCodes.Isinst, runtime.ArrayHoleType);
        il.Emit(OpCodes.Brfalse, notHole);
        EmitAppendNullLiteral(il);
        il.Emit(OpCodes.Br, appended);

        il.MarkLabel(notHole);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, arrayLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.ListOfObject, "get_Item", [_types.Int32]));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, appendValue);
        il.Emit(OpCodes.Brtrue, appended);
        EmitAppendNullLiteral(il);

        il.MarkLabel(appended);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(end);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)']');
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.StringBuilder, "Append", [_types.Char]));
        il.Emit(OpCodes.Pop);
        EmitTrueReturn(il);
    }

    private void EmitAppendJsonObject(
        ILGenerator il,
        MethodBuilder appendValue,
        LocalBuilder valueLocal,
        LocalBuilder allowPooledKeysLocal,
        EmittedRuntime runtime)
    {
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var keysLocal = il.DeclareLocal(_types.ListOfObject);
        var keyLocal = il.DeclareLocal(_types.String);
        var dictValueLocal = il.DeclareLocal(_types.Object);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var firstLocal = il.DeclareLocal(_types.Boolean);
        var rentedLocal = il.DeclareLocal(_types.Boolean);
        var fallbackSnapshot = il.DefineLabel();
        var snapshotReady = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, allowPooledKeysLocal);
        il.Emit(OpCodes.Brfalse, fallbackSnapshot);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Call, runtime.PDSHasPropertyDescriptors);
        il.Emit(OpCodes.Brtrue, fallbackSnapshot);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Call, _jsonTryRentDictionaryKeysMethod!);
        il.Emit(OpCodes.Stloc, keysLocal);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Brfalse, fallbackSnapshot);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, rentedLocal);
        il.Emit(OpCodes.Br, snapshotReady);

        il.MarkLabel(fallbackSnapshot);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, runtime.GetKeys);
        il.Emit(OpCodes.Stloc, keysLocal);
        il.MarkLabel(snapshotReady);

        var cleanupDone = il.DefineLabel();
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)'{');
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.StringBuilder, "Append", [_types.Char]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, firstLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loop = il.DefineLabel();
        var end = il.DefineLabel();
        var generalRead = il.DefineLabel();
        var valueReady = il.DefineLabel();
        var omitted = il.DefineLabel();
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, end);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.ListOfObject, "get_Item", [_types.Int32]));
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, keyLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.Emit(OpCodes.Ldloc, rentedLocal);
        il.Emit(OpCodes.Brfalse, generalRead);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldloca, dictValueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryStringObject,
            "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brtrue, valueReady);
        il.MarkLabel(generalRead);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, _jsonGetDictionaryPropertyMethod!);
        il.Emit(OpCodes.Stloc, dictValueLocal);
        il.MarkLabel(valueReady);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, dictValueLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, firstLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Call, appendValue);
        il.Emit(OpCodes.Brfalse, omitted);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, firstLocal);
        il.MarkLabel(omitted);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(end);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)'}');
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.StringBuilder, "Append", [_types.Char]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Leave, cleanupDone);

        il.BeginFinallyBlock();
        var notRented = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, rentedLocal);
        il.Emit(OpCodes.Brfalse, notRented);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Call, _jsonReturnDictionaryKeysMethod!);
        il.MarkLabel(notRented);
        il.EndExceptionBlock();

        il.MarkLabel(cleanupDone);
        EmitTrueReturn(il);
    }

    private void EmitAppendNullLiteral(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "null");
        EmitStringBuilderAppendString(il);
    }

    private void EmitStringBuilderAppendString(ILGenerator il)
    {
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
    }

    private static void EmitTrueReturn(ILGenerator il)
    {
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }
}
