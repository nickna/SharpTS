using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private MethodBuilder? _escapeJsonStringMethod;
    private MethodBuilder? _appendEscapedJsonStringMethod;
    private MethodBuilder? _jsonGetDictionaryPropertyMethod;
    private MethodBuilder? _jsonGetDictionaryToJsonMethod;
    private MethodBuilder? _jsonTryRentDictionaryKeysMethod;
    private MethodBuilder? _jsonReturnDictionaryKeysMethod;
    private MethodBuilder? _jsonRentStringBuilderMethod;
    private MethodBuilder? _jsonReturnStringBuilderMethod;

    /// <summary>
    /// Emits a JSON-only thread-local pool for ordinary dictionary key
    /// snapshots. The list remains rented while values are read, so recursive
    /// objects get distinct lists and getter/toJSON mutations cannot alter the
    /// current object's captured key set.
    /// </summary>
    private void EmitJsonDictionaryKeyPool(TypeBuilder typeBuilder)
    {
        if (_jsonTryRentDictionaryKeysMethod is not null)
            return;

        Type stackType = _types.MakeGenericType(typeof(Stack<>), _types.ListOfObject);
        var poolField = typeBuilder.DefineField(
            "_jsonDictionaryKeySnapshots",
            stackType,
            FieldAttributes.Private | FieldAttributes.Static);
        var threadStaticCtor = typeof(ThreadStaticAttribute).GetConstructor(Type.EmptyTypes)!;
        poolField.SetCustomAttribute(threadStaticCtor, CustomAttributeEncoder.EmptyBlob);

        var returnMethod = typeBuilder.DefineMethod(
            "ReturnJsonDictionaryKeys",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Void,
            [_types.ListOfObject]);
        _jsonReturnDictionaryKeysMethod = returnMethod;

        var tryRentMethod = typeBuilder.DefineMethod(
            "TryRentJsonDictionaryKeys",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.DictionaryStringObject]);
        _jsonTryRentDictionaryKeysMethod = tryRentMethod;

        // ReturnJsonDictionaryKeys(list): clear references and make the list
        // available to the next non-overlapping object on this thread.
        {
            var il = returnMethod.GetILGenerator();
            var poolReady = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Clear", Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldsfld, poolField);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, poolReady);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(stackType, Type.EmptyTypes)!);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Stsfld, poolField);
            il.MarkLabel(poolReady);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(stackType, "Push", [_types.ListOfObject])!);
            il.Emit(OpCodes.Ret);
        }

        // TryRentJsonDictionaryKeys(dict): snapshot insertion-ordered keys into
        // a pooled list, but reject canonical array-index keys. Numeric shapes
        // need the general NormalizeOwnPropertyKeys path.
        {
            var il = tryRentMethod.GetILGenerator();
            var poolLocal = il.DeclareLocal(stackType);
            var resultLocal = il.DeclareLocal(_types.ListOfObject);
            var keyLocal = il.DeclareLocal(_types.String);
            var firstCharLocal = il.DeclareLocal(_types.Char);
            var indexLocal = il.DeclareLocal(_types.UInt32);
            var poolReady = il.DefineLabel();
            var allocate = il.DefineLabel();
            var rented = il.DefineLabel();

            il.Emit(OpCodes.Ldsfld, poolField);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, poolReady);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(stackType, Type.EmptyTypes)!);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Stsfld, poolField);
            il.MarkLabel(poolReady);
            il.Emit(OpCodes.Stloc, poolLocal);

            il.Emit(OpCodes.Ldloc, poolLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(stackType, "Count").GetGetMethod()!);
            il.Emit(OpCodes.Brfalse, allocate);
            il.Emit(OpCodes.Ldloc, poolLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(stackType, "Pop", Type.EmptyTypes)!);
            il.Emit(OpCodes.Stloc, resultLocal);
            il.Emit(OpCodes.Br, rented);

            il.MarkLabel(allocate);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryStringObject, "Count").GetGetMethod()!);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, [_types.Int32])!);
            il.Emit(OpCodes.Stloc, resultLocal);

            il.MarkLabel(rented);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryStringObject, "Count").GetGetMethod()!);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "EnsureCapacity", [_types.Int32])!);
            il.Emit(OpCodes.Pop);

            Type keysType = _types.MakeGenericType(
                typeof(Dictionary<,>.KeyCollection).GetGenericTypeDefinition(),
                _types.String, _types.Object);
            Type keysEnumeratorType = _types.MakeGenericType(
                typeof(Dictionary<,>.KeyCollection.Enumerator).GetGenericTypeDefinition(),
                _types.String, _types.Object);
            var enumeratorLocal = il.DeclareLocal(keysEnumeratorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryStringObject, "Keys").GetGetMethod()!);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(keysType, "GetEnumerator", Type.EmptyTypes)!);
            il.Emit(OpCodes.Stloc, enumeratorLocal);

            var loop = il.DefineLabel();
            var loopEnd = il.DefineLabel();
            var addKey = il.DefineLabel();
            var numericKey = il.DefineLabel();
            il.MarkLabel(loop);
            il.Emit(OpCodes.Ldloca, enumeratorLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(keysEnumeratorType, "MoveNext", Type.EmptyTypes)!);
            il.Emit(OpCodes.Brfalse, loopEnd);
            il.Emit(OpCodes.Ldloca, enumeratorLocal);
            il.Emit(OpCodes.Call, _types.GetProperty(keysEnumeratorType, "Current").GetGetMethod()!);
            il.Emit(OpCodes.Stloc, keyLocal);

            // Reject ordinary names before entering UInt32.TryParse. JSON
            // records overwhelmingly use identifier-like keys.
            il.Emit(OpCodes.Ldloc, keyLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
            il.Emit(OpCodes.Brfalse, addKey);
            il.Emit(OpCodes.Ldloc, keyLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32]));
            il.Emit(OpCodes.Stloc, firstCharLocal);
            il.Emit(OpCodes.Ldloc, firstCharLocal);
            il.Emit(OpCodes.Ldc_I4, (int)'0');
            il.Emit(OpCodes.Blt, addKey);
            il.Emit(OpCodes.Ldloc, firstCharLocal);
            il.Emit(OpCodes.Ldc_I4, (int)'9');
            il.Emit(OpCodes.Bgt, addKey);

            il.Emit(OpCodes.Ldloc, keyLocal);
            il.Emit(OpCodes.Ldloca, indexLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(
                _types.UInt32, "TryParse", [_types.String, _types.UInt32.MakeByRefType()])!);
            il.Emit(OpCodes.Brfalse, addKey);
            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Ldc_I4_M1);
            il.Emit(OpCodes.Conv_U4);
            il.Emit(OpCodes.Beq, addKey);
            il.Emit(OpCodes.Ldloca, indexLocal);
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.UInt32, "ToString"));
            il.Emit(OpCodes.Ldloc, keyLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(
                _types.String, "op_Equality", [_types.String, _types.String])!);
            il.Emit(OpCodes.Brtrue, numericKey);

            il.MarkLabel(addKey);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, keyLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object])!);
            il.Emit(OpCodes.Br, loop);

            il.MarkLabel(numericKey);
            il.Emit(OpCodes.Ldloca, enumeratorLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(keysEnumeratorType, "Dispose", Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Call, returnMethod);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(loopEnd);
            il.Emit(OpCodes.Ldloca, enumeratorLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(keysEnumeratorType, "Dispose", Type.EmptyTypes)!);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// Emits a JSON-only thread-local pool for the builders used by recursive
    /// array and object serialization. Builders remain rented across nested
    /// calls, and unusually large buffers are discarded when returned.
    /// </summary>
    private void EmitJsonStringBuilderPool(TypeBuilder typeBuilder)
    {
        if (_jsonRentStringBuilderMethod is not null)
            return;

        Type stackType = _types.MakeGenericType(typeof(Stack<>), _types.StringBuilder);
        var poolField = typeBuilder.DefineField(
            "_jsonStringBuilders",
            stackType,
            FieldAttributes.Private | FieldAttributes.Static);
        var threadStaticCtor = typeof(ThreadStaticAttribute).GetConstructor(Type.EmptyTypes)!;
        poolField.SetCustomAttribute(threadStaticCtor, CustomAttributeEncoder.EmptyBlob);

        var returnMethod = typeBuilder.DefineMethod(
            "ReturnJsonStringBuilder",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Void,
            [_types.StringBuilder]);
        _jsonReturnStringBuilderMethod = returnMethod;

        var rentMethod = typeBuilder.DefineMethod(
            "RentJsonStringBuilder",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.StringBuilder,
            Type.EmptyTypes);
        _jsonRentStringBuilderMethod = rentMethod;

        {
            var il = returnMethod.GetILGenerator();
            var poolReady = il.DefineLabel();
            var discard = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Clear", Type.EmptyTypes)!);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.StringBuilder, "Capacity").GetGetMethod()!);
            il.Emit(OpCodes.Ldc_I4, 1_048_576);
            il.Emit(OpCodes.Bgt, discard);

            il.Emit(OpCodes.Ldsfld, poolField);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, poolReady);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(stackType, Type.EmptyTypes)!);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Stsfld, poolField);
            il.MarkLabel(poolReady);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(stackType, "Push", [_types.StringBuilder])!);
            il.MarkLabel(discard);
            il.Emit(OpCodes.Ret);
        }

        {
            var il = rentMethod.GetILGenerator();
            var poolLocal = il.DeclareLocal(stackType);
            var poolReady = il.DefineLabel();
            var allocate = il.DefineLabel();

            il.Emit(OpCodes.Ldsfld, poolField);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, poolReady);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(stackType, Type.EmptyTypes)!);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Stsfld, poolField);
            il.MarkLabel(poolReady);
            il.Emit(OpCodes.Stloc, poolLocal);

            il.Emit(OpCodes.Ldloc, poolLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(stackType, "Count").GetGetMethod()!);
            il.Emit(OpCodes.Brfalse, allocate);
            il.Emit(OpCodes.Ldloc, poolLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(stackType, "Pop", Type.EmptyTypes)!);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(allocate);
            il.Emit(OpCodes.Ldc_I4, 256);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.StringBuilder, [_types.Int32])!);
            il.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// Reads an existing ordinary dictionary property without entering the
    /// general dynamic-property dispatcher. Descriptor-bearing objects and
    /// misses still take the full path so accessors, prototype lookup, and
    /// mutations between the key snapshot and value read remain observable.
    /// </summary>
    private MethodBuilder EmitJsonGetDictionaryPropertyHelper(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime)
    {
        if (_jsonGetDictionaryPropertyMethod is not null)
            return _jsonGetDictionaryPropertyMethod;

        var method = typeBuilder.DefineMethod(
            "JsonGetDictionaryProperty",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Object,
            [_types.DictionaryStringObject, _types.String]);

        var il = method.GetILGenerator();
        var valueLocal = il.DeclareLocal(_types.Object);
        var fallback = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSHasPropertyDescriptors);
        il.Emit(OpCodes.Brtrue, fallback);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryStringObject,
            "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, fallback);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(fallback);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Ret);

        _jsonGetDictionaryPropertyMethod = method;
        return method;
    }

    /// <summary>
    /// Performs the JSON <c>toJSON</c> lookup for a dictionary receiver while
    /// bypassing unrelated dynamic receiver branches. Custom prototypes and
    /// descriptor-bearing receivers retain the general [[Get]] path.
    /// </summary>
    private MethodBuilder EmitJsonGetDictionaryToJsonHelper(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime)
    {
        if (_jsonGetDictionaryToJsonMethod is not null)
            return _jsonGetDictionaryToJsonMethod;

        var method = typeBuilder.DefineMethod(
            "JsonGetDictionaryToJson",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Object,
            [_types.DictionaryStringObject]);

        var il = method.GetILGenerator();
        var valueLocal = il.DeclareLocal(_types.Object);
        var fullLookup = il.DefineLabel();
        var implicitPrototype = il.DefineLabel();
        var miss = il.DefineLabel();
        var returnValue = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSHasPropertyDescriptors);
        il.Emit(OpCodes.Brtrue, fullLookup);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "toJSON");
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryStringObject,
            "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, implicitPrototype);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(implicitPrototype);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSHasPrototypeEntry);
        il.Emit(OpCodes.Brtrue, fullLookup);
        il.Emit(OpCodes.Call, runtime.ObjectPrototypePopulateMethod);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
        il.Emit(OpCodes.Ldstr, "toJSON");
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryStringObject,
            "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, miss);

        // Object.prototype accessors retain an undefined dictionary
        // placeholder to preserve property order. Only that unusual sentinel
        // case needs a descriptor probe; an absent toJSON remains CWT-free.
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Bne_Un, returnValue);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
        il.Emit(OpCodes.Ldstr, "toJSON");
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Brtrue, fullLookup);

        il.MarkLabel(returnValue);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(miss);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(fullLookup);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "toJSON");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Ret);

        _jsonGetDictionaryToJsonMethod = method;
        return method;
    }

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

        // Most property names and application strings require no JSON
        // escaping. Detect that shape first and let String.Concat allocate the
        // quoted result in one pass; the full well-formed/surrogate path below
        // remains unchanged for every string containing a special character.
        var escapeSlow = il.DefineLabel();
        var probeLoop = il.DefineLabel();
        var probeNext = il.DefineLabel();
        var probeDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(probeLoop);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, probeDone);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32]));
        il.Emit(OpCodes.Stloc, cLocal);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'"');
        il.Emit(OpCodes.Beq, escapeSlow);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'\\');
        il.Emit(OpCodes.Beq, escapeSlow);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Blt, escapeSlow);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, 0xD800);
        il.Emit(OpCodes.Blt, probeNext);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, 0xE000);
        il.Emit(OpCodes.Blt, escapeSlow);
        il.MarkLabel(probeNext);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, probeLoop);
        il.MarkLabel(probeDone);
        il.Emit(OpCodes.Ldstr, "\"");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "\"");
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String,
            "Concat",
            [_types.String, _types.String, _types.String]));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(escapeSlow);

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

    /// <summary>
    /// Appends a quoted JSON string directly into an existing builder. The
    /// common escape-free path avoids creating an intermediate quoted string;
    /// special characters delegate to the complete escaping helper above.
    /// </summary>
    private MethodBuilder EmitAppendEscapedJsonStringHelper(TypeBuilder typeBuilder)
    {
        if (_appendEscapedJsonStringMethod is not null)
            return _appendEscapedJsonStringMethod;

        var method = typeBuilder.DefineMethod(
            "AppendEscapedJsonString",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Void,
            [_types.StringBuilder, _types.String]);
        var il = method.GetILGenerator();
        var indexLocal = il.DeclareLocal(_types.Int32);
        var charLocal = il.DeclareLocal(_types.Char);
        var loop = il.DefineLabel();
        var next = il.DefineLabel();
        var fast = il.DefineLabel();
        var slow = il.DefineLabel();

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Bge, fast);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32]));
        il.Emit(OpCodes.Stloc, charLocal);
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'"');
        il.Emit(OpCodes.Beq, slow);
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'\\');
        il.Emit(OpCodes.Beq, slow);
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Blt, slow);
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Ldc_I4, 0xD800);
        il.Emit(OpCodes.Blt, next);
        il.Emit(OpCodes.Ldloc, charLocal);
        il.Emit(OpCodes.Ldc_I4, 0xE000);
        il.Emit(OpCodes.Blt, slow);
        il.MarkLabel(next);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(fast);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)'"');
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.Char]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)'"');
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.Char]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(slow);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _escapeJsonStringMethod!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);

        _appendEscapedJsonStringMethod = method;
        return method;
    }

    private void EmitJsonStringify(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // First emit the escape helper (needed by stringify)
        EmitEscapeJsonStringHelper(typeBuilder);
        EmitAppendEscapedJsonStringHelper(typeBuilder);
        EmitJsonGetDictionaryPropertyHelper(typeBuilder, runtime);
        EmitJsonGetDictionaryToJsonHelper(typeBuilder, runtime);
        EmitJsonDictionaryKeyPool(typeBuilder);
        EmitJsonStringBuilderPool(typeBuilder);

        // Then emit the main stringify helper
        _ = EmitJsonStringifyHelper(typeBuilder, runtime);
        var appendValue = EmitAppendJsonValueHelper(typeBuilder, runtime);

        var method = typeBuilder.DefineMethod(
            "JsonStringify",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.JsonStringify = method;

        var il = method.GetILGenerator();

        // One root-owned builder spans the complete recursive walk. It remains
        // rented through toJSON/reentrant calls and is returned on every abrupt
        // completion; the bounded pool discards unusually large buffers.
        var builderLocal = il.DeclareLocal(_types.StringBuilder);
        var resultRootLocal = il.DeclareLocal(_types.Object);
        var undefinedRoot = il.DefineLabel();
        var cleanupDone = il.DefineLabel();
        il.Emit(OpCodes.Call, _jsonRentStringBuilderMethod!);
        il.Emit(OpCodes.Stloc, builderLocal);
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, builderLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0); // depth
        il.Emit(OpCodes.Ldstr, ""); // root key
        il.Emit(OpCodes.Ldc_I4_0); // unused array index
        il.Emit(OpCodes.Ldc_I4_0); // key is not an index
        il.Emit(OpCodes.Ldc_I4_0); // no object-property prefix
        il.Emit(OpCodes.Ldc_I4_0); // no comma
        il.Emit(OpCodes.Call, appendValue);
        il.Emit(OpCodes.Brfalse, undefinedRoot);
        il.Emit(OpCodes.Ldloc, builderLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, resultRootLocal);
        il.Emit(OpCodes.Leave, cleanupDone);

        il.MarkLabel(undefinedRoot);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Stloc, resultRootLocal);
        il.Emit(OpCodes.Leave, cleanupDone);

        il.BeginFinallyBlock();
        il.Emit(OpCodes.Ldloc, builderLocal);
        il.Emit(OpCodes.Call, _jsonReturnStringBuilderMethod!);
        il.EndExceptionBlock();

        il.MarkLabel(cleanupDone);
        il.Emit(OpCodes.Ldloc, resultRootLocal);
        il.Emit(OpCodes.Ret);

        EmitJsonStringifyShapedMethod(typeBuilder, runtime, appendValue);
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
        var allowPooledDictionaryKeysLocal = il.DeclareLocal(_types.Boolean);

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
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, allowPooledDictionaryKeysLocal);

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

        // A callable toJSON may return JavaScript null. Handle it before
        // object-only processing (notably BigInt's GetType-based check).
        var afterToJsonNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Brtrue, afterToJsonNullLabel);
        il.Emit(OpCodes.Ldstr, "null");
        il.Emit(OpCodes.Ret);
        il.MarkLabel(afterToJsonNullLabel);

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

        // BigInt rejection occurs after toJSON and boxed-primitive unwrapping.
        EmitBigIntCheck(il, valueLocal, runtime);

        // Proxy materialization (#92): if value is SharpTSProxy, dispatch its
        // [[OwnPropertyKeys]] / [[Get]] traps and substitute a Dictionary so the
        // existing dict path serializes the proxied view.
        var notProxyLabelSimple = il.DefineLabel();
        EmitProxyMaterializeForJson(il, valueLocal, notProxyLabelSimple, runtime);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, allowPooledDictionaryKeysLocal);
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
        EmitStringifyObject(
            il, method, valueLocal, runtime, allowPooledDictionaryKeysLocal);

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
        EmitStringifyObject(il, method, valueLocal, runtime, null);

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

    private void EmitToJsonCheck(ILGenerator il, LocalBuilder valueLocal, EmittedRuntime runtime,
        string? key = null, int? keyArgIndex = null, LocalBuilder? keyLocal = null,
        int? keyIndexArgIndex = null, int? keyIsIndexArgIndex = null)
    {
        var doneLabel = il.DefineLabel();

        // SerializeJSONProperty only performs Get(value, "toJSON") for
        // Objects and BigInt primitives. Other primitive values skip this step.
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Brfalse, doneLabel);
        foreach (var primitiveType in new[] { _types.Boolean, _types.Double, _types.String })
        {
            il.Emit(OpCodes.Ldloc, valueLocal);
            il.Emit(OpCodes.Isinst, primitiveType);
            il.Emit(OpCodes.Brtrue, doneLabel);
        }
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brtrue, doneLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, doneLabel);

        var toJsonLocal = il.DeclareLocal(_types.Object);
        var toJsonDictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var genericToJsonLookup = il.DefineLabel();
        var toJsonLookupDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, toJsonDictLocal);
        il.Emit(OpCodes.Ldloc, toJsonDictLocal);
        il.Emit(OpCodes.Brfalse, genericToJsonLookup);
        il.Emit(OpCodes.Ldloc, toJsonDictLocal);
        il.Emit(OpCodes.Call, _jsonGetDictionaryToJsonMethod!);
        il.Emit(OpCodes.Br, toJsonLookupDone);
        il.MarkLabel(genericToJsonLookup);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldstr, "toJSON");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.MarkLabel(toJsonLookupDone);
        il.Emit(OpCodes.Stloc, toJsonLocal);

        // IsCallable(toJSON)
        il.Emit(OpCodes.Ldloc, toJsonLocal);
        il.Emit(OpCodes.Call, runtime.TypeOf);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, doneLabel);

        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        if (keyIndexArgIndex.HasValue && keyIsIndexArgIndex.HasValue)
        {
            var stringKeyLabel = il.DefineLabel();
            var keyReadyLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg, keyIsIndexArgIndex.Value);
            il.Emit(OpCodes.Brfalse, stringKeyLabel);
            il.Emit(OpCodes.Ldarga, keyIndexArgIndex.Value);
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int32, "ToString"));
            il.Emit(OpCodes.Br, keyReadyLabel);
            il.MarkLabel(stringKeyLabel);
            il.Emit(OpCodes.Ldarg, keyArgIndex!.Value);
            il.MarkLabel(keyReadyLabel);
        }
        else if (keyLocal != null)
            il.Emit(OpCodes.Ldloc, keyLocal);
        else if (keyArgIndex.HasValue)
            il.Emit(OpCodes.Ldarg, keyArgIndex.Value);
        else
            il.Emit(OpCodes.Ldstr, key ?? string.Empty);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Stloc, argsLocal);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldloc, toJsonLocal);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Stloc, valueLocal);

        il.MarkLabel(doneLabel);
    }

    private void EmitToJsonCheckLegacy(ILGenerator il, LocalBuilder valueLocal, EmittedRuntime runtime, string? key = null, int? keyArgIndex = null)
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

        // Use ordinary Get so compact JSON records observe a mutable
        // prototype's inherited toJSON hook. Class instances retain their
        // existing IHasFields behavior inside the shared GetProperty helper.
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldstr, "toJSON");
        il.Emit(OpCodes.Call, runtime.GetProperty);
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
        var returnValueLocal = il.DeclareLocal(_types.String);

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

        // Rent the buffer for the whole array walk. Recursive containers rent
        // distinct builders until their parent finishes.
        il.Emit(OpCodes.Call, _jsonRentStringBuilderMethod!);
        il.Emit(OpCodes.Stloc, sbLocal);
        var cleanupDone = il.DefineLabel();
        il.BeginExceptionBlock();

        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "[");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

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

    private void EmitStringifyObject(
        ILGenerator il,
        MethodBuilder stringifyMethod,
        LocalBuilder valueLocal,
        EmittedRuntime runtime,
        LocalBuilder? allowPooledDictionaryKeysLocal)
    {
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var keysLocal = il.DeclareLocal(_types.ListOfObject);
        var keyLocal = il.DeclareLocal(_types.String);
        var iLocal = il.DeclareLocal(_types.Int32);
        var firstLocal = il.DeclareLocal(_types.Boolean);
        var rentedKeysLocal = il.DeclareLocal(_types.Boolean);
        var returnValueLocal = il.DeclareLocal(_types.String);
        var dictValueLocal = il.DeclareLocal(_types.Object);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // Descriptor-free ordinary dictionaries with no canonical index keys
        // can snapshot into a thread-local list. Exotic shapes retain GetKeys'
        // descriptor filtering and OrdinaryOwnPropertyKeys normalization.
        var fallbackSnapshot = il.DefineLabel();
        var snapshotReady = il.DefineLabel();
        if (allowPooledDictionaryKeysLocal is not null)
        {
            il.Emit(OpCodes.Ldloc, allowPooledDictionaryKeysLocal);
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
            il.Emit(OpCodes.Stloc, rentedKeysLocal);
            il.Emit(OpCodes.Br, snapshotReady);
        }

        il.MarkLabel(fallbackSnapshot);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Call, runtime.GetKeys);
        il.Emit(OpCodes.Stloc, keysLocal);
        il.MarkLabel(snapshotReady);

        // The pooled list must remain rented across every value read, including
        // recursive calls, and must be returned after abrupt completion.
        var cleanupDone = il.DefineLabel();
        il.BeginExceptionBlock();

        // if (keys.Count == 0) return "{}";
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        var notEmpty = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, notEmpty);
        il.Emit(OpCodes.Ldstr, "{}");
        il.Emit(OpCodes.Stloc, returnValueLocal);
        il.Emit(OpCodes.Leave, cleanupDone);

        il.MarkLabel(notEmpty);

        il.Emit(OpCodes.Call, _jsonRentStringBuilderMethod!);
        il.Emit(OpCodes.Stloc, sbLocal);
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldstr, "{");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", [_types.String]));
        il.Emit(OpCodes.Pop);

        // bool first = true;
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, firstLocal);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEnd);

        il.Emit(OpCodes.Ldloc, keysLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", [_types.Int32]));
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, keyLocal);

        // Advance before any continue branch.
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);

        // strResult = StringifyValue(currentValue, indent, depth + 1, currentKey)
        // Compute first; if null, the value was undefined → skip entry per
        // ECMA-262 25.5.2.1 SerializeJSONObject step 7.b.
        // ECMA-262 25.5.2.5 step 6.a: the recursive key is the property name
        // so toJSON can branch on it.
        var dictValStrLocal = il.DeclareLocal(_types.String);
        var generalRead = il.DefineLabel();
        var valueReady = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, rentedKeysLocal);
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
        il.Emit(OpCodes.Ldloc, dictValueLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, keyLocal);
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
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Call, _appendEscapedJsonStringMethod!);

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
        il.Emit(OpCodes.Ldloc, keysLocal);
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

    /// <summary>
    /// ECMA-262 §25.5.2.3 step 4 / §25.5.2.1 step 5: coerce a boxed
    /// Number/String/Boolean/BigInt wrapper held in <paramref name="valueLocal"/> to the
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
        var readTsObjectTag = il.DefineLabel();
        var tagRead = il.DefineLabel();
        var done = il.DefineLabel();
        var boxedDict = il.DeclareLocal(_types.DictionaryStringObject);
        var tagValue = il.DeclareLocal(_types.Object);

        // The wrapper brand is represented by an own internal marker. Read it
        // directly: inherited/accessor properties are not [[PrimitiveData]].
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, readTsObjectTag);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, boxedDict);
        il.Emit(OpCodes.Ldloc, boxedDict);
        il.Emit(OpCodes.Brfalse, notBoxed);
        il.Emit(OpCodes.Ldloc, boxedDict);
        il.Emit(OpCodes.Ldstr, "__primitiveType");
        il.Emit(OpCodes.Ldloca, tagValue);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryStringObject,
            "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, notBoxed);
        il.Emit(OpCodes.Br, tagRead);

        il.MarkLabel(readTsObjectTag);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldstr, "__primitiveType");
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetProperty);
        il.Emit(OpCodes.Stloc, tagValue);

        il.MarkLabel(tagRead);

        // The marker must carry a recognized string tag (#565).
        var tag = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, tagValue);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, tag);
        il.Emit(OpCodes.Ldloc, tag);
        il.Emit(OpCodes.Brfalse, notBoxed);

        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);
        var numberCase = il.DefineLabel();
        var booleanCase = il.DefineLabel();
        var stringCase = il.DefineLabel();
        var bigintCase = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, tag);
        il.Emit(OpCodes.Ldstr, "Number");
        il.Emit(OpCodes.Call, strEq);
        il.Emit(OpCodes.Brtrue, numberCase);
        il.Emit(OpCodes.Ldloc, tag);
        il.Emit(OpCodes.Ldstr, "Boolean");
        il.Emit(OpCodes.Call, strEq);
        il.Emit(OpCodes.Brtrue, booleanCase);
        il.Emit(OpCodes.Ldloc, tag);
        il.Emit(OpCodes.Ldstr, "String");
        il.Emit(OpCodes.Call, strEq);
        il.Emit(OpCodes.Brtrue, stringCase);
        il.Emit(OpCodes.Ldloc, tag);
        il.Emit(OpCodes.Ldstr, "BigInt");
        il.Emit(OpCodes.Call, strEq);
        il.Emit(OpCodes.Brtrue, bigintCase);
        il.Emit(OpCodes.Br, notBoxed);

        // String tag → ToString (string-hint ToPrimitive → toString first).
        il.MarkLabel(stringCase);
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
        il.Emit(OpCodes.Br, done);

        // BigInt wrapper → [[BigIntData]]. The caller performs the required
        // TypeError check after all user hooks have run.
        il.MarkLabel(bigintCase);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldstr, "__primitiveValue");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, valueLocal);

        il.MarkLabel(notBoxed);
        il.MarkLabel(done);
    }

}

