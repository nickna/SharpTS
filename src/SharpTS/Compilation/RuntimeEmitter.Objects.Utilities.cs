using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitCreateObject(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateObject",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.DictionaryStringObject,
            [_types.DictionaryStringObject]
        );
        runtime.CreateObject = method;

        var il = method.GetILGenerator();
        // Just return the dictionary as-is (it's already created)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMergeIntoObject(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MergeIntoObject",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.DictionaryStringObject, _types.Object]
        );
        runtime.MergeIntoObject = method;

        var il = method.GetILGenerator();
        var dictLabel = il.DefineLabel();

        // Check if source is dict
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Not a dict - do nothing
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // Iterate and copy
        // We need the Enumerator type for Dictionary<string, object>
        // Since TypeProvider might not expose nested types directly, we resolve it from the Dictionary type
        var dictType = _types.DictionaryStringObject;
        var enumeratorType = typeof(Dictionary<string, object>.Enumerator);
        var keyValuePairType = _types.KeyValuePairStringObject;

        var enumeratorLocal = il.DeclareLocal(enumeratorType);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(dictType, "GetEnumerator"));
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(enumeratorType, "MoveNext"));
        il.Emit(OpCodes.Brfalse, loopEnd);

        // Get current and add to target
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, enumeratorLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(enumeratorType, "Current")!.GetGetMethod()!);
        var kvpLocal = il.DeclareLocal(keyValuePairType);
        il.Emit(OpCodes.Stloc, kvpLocal);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(keyValuePairType, "Key")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, kvpLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(keyValuePairType, "Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(dictType, "set_Item", _types.String, _types.Object));

        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        il.Emit(OpCodes.Ret);
    }

    private void EmitMergeIntoTSObject(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public static void MergeIntoTSObject($Object target, object? source)
        // Merges properties from source (Dictionary or $Object) into target $Object
        var method = typeBuilder.DefineMethod(
            "MergeIntoTSObject",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [runtime.TSObjectType, _types.Object]
        );
        runtime.MergeIntoTSObject = method;

        var il = method.GetILGenerator();
        var dictLabel = il.DefineLabel();
        var tsObjectLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Check if source is Dictionary<string, object?>
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // Check if source is $IHasFields (covers $Object and class instances)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brtrue, tsObjectLabel);

        // Not a dict or $IHasFields - do nothing
        il.Emit(OpCodes.Ret);

        // Handle Dictionary source
        il.MarkLabel(dictLabel);
        {
            var dictType = _types.DictionaryStringObject;
            var enumeratorType = typeof(Dictionary<string, object>.Enumerator);
            var keyValuePairType = _types.KeyValuePairStringObject;

            var enumeratorLocal = il.DeclareLocal(enumeratorType);
            var kvpLocal = il.DeclareLocal(keyValuePairType);
            var loopStart = il.DefineLabel();
            var loopEnd = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, dictType);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(dictType, "GetEnumerator"));
            il.Emit(OpCodes.Stloc, enumeratorLocal);

            il.MarkLabel(loopStart);
            il.Emit(OpCodes.Ldloca, enumeratorLocal);
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(enumeratorType, "MoveNext"));
            il.Emit(OpCodes.Brfalse, loopEnd);

            // target.SetProperty(key, value)
            il.Emit(OpCodes.Ldarg_0); // target
            il.Emit(OpCodes.Ldloca, enumeratorLocal);
            il.Emit(OpCodes.Call, _types.GetProperty(enumeratorType, "Current")!.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, kvpLocal);
            il.Emit(OpCodes.Ldloca, kvpLocal);
            il.Emit(OpCodes.Call, _types.GetProperty(keyValuePairType, "Key")!.GetGetMethod()!);
            il.Emit(OpCodes.Ldloca, kvpLocal);
            il.Emit(OpCodes.Call, _types.GetProperty(keyValuePairType, "Value")!.GetGetMethod()!);
            il.Emit(OpCodes.Callvirt, runtime.TSObjectSetProperty);

            il.Emit(OpCodes.Br, loopStart);

            il.MarkLabel(loopEnd);
            il.Emit(OpCodes.Br, endLabel);
        }

        // Handle $IHasFields source - iterate Fields dictionary
        il.MarkLabel(tsObjectLabel);
        {
            var fieldsDictLocal = il.DeclareLocal(_types.DictionaryStringObject);
            var dictEnumType = typeof(Dictionary<string, object>.Enumerator);
            var kvpType = _types.KeyValuePairStringObject;
            var enumLocal = il.DeclareLocal(dictEnumType);
            var kvpLocal = il.DeclareLocal(kvpType);
            var loopStart = il.DefineLabel();
            var loopEnd = il.DefineLabel();

            // Get Fields dictionary from source
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
            il.Emit(OpCodes.Callvirt, runtime.IHasFieldsFieldsGetter);
            il.Emit(OpCodes.Stloc, fieldsDictLocal);

            // If null, skip
            il.Emit(OpCodes.Ldloc, fieldsDictLocal);
            il.Emit(OpCodes.Brfalse, endLabel);

            // Iterate dictionary
            il.Emit(OpCodes.Ldloc, fieldsDictLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.DictionaryStringObject, "GetEnumerator"));
            il.Emit(OpCodes.Stloc, enumLocal);

            il.MarkLabel(loopStart);
            il.Emit(OpCodes.Ldloca, enumLocal);
            il.Emit(OpCodes.Call, dictEnumType.GetMethod("MoveNext")!);
            il.Emit(OpCodes.Brfalse, loopEnd);

            il.Emit(OpCodes.Ldloca, enumLocal);
            il.Emit(OpCodes.Call, dictEnumType.GetProperty("Current")!.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, kvpLocal);

            // target.SetProperty(key, value)
            il.Emit(OpCodes.Ldarg_0); // target
            il.Emit(OpCodes.Ldloca, kvpLocal);
            il.Emit(OpCodes.Call, _types.GetProperty(kvpType, "Key")!.GetGetMethod()!);
            il.Emit(OpCodes.Ldloca, kvpLocal);
            il.Emit(OpCodes.Call, _types.GetProperty(kvpType, "Value")!.GetGetMethod()!);
            il.Emit(OpCodes.Callvirt, runtime.TSObjectSetProperty);

            il.Emit(OpCodes.Br, loopStart);

            il.MarkLabel(loopEnd);
        }

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitRandom(TypeBuilder typeBuilder, EmittedRuntime runtime, FieldBuilder randomField)
    {
        var method = typeBuilder.DefineMethod(
            "Random",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            _types.EmptyTypes
        );
        runtime.Random = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, randomField);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Random, "NextDouble"));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits TSObjectMergeEnumerable(object obj) -> Dictionary&lt;string, object&gt;.
    /// For \$Object receivers, returns a fresh dict containing every entry from
    /// _fields PLUS getter-resolved entries from _getters (accessor properties
    /// invoked via InvokeMethodValue with obj as \`this\`). For non-\$Object
    /// receivers (including null and \$IHasFields user classes), returns the
    /// receiver's Fields dict directly. Used by JSON.stringify to honor the
    /// ECMA-262 25.5.2.4 spec rule that EnumerableOwnPropertyNames covers both
    /// data and accessor properties.
    /// </summary>
    private void EmitTSObjectMergeEnumerable(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "TSObjectMergeEnumerable",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.DictionaryStringObject,
            [_types.Object]
        );
        runtime.TSObjectMergeEnumerable = method;

        var il = method.GetILGenerator();
        var fallbackLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // If receiver is not $Object, fall back to the IHasFields path.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, fallbackLabel);

        // result = new Dictionary<string, object>()
        var resultLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, resultLocal);

        // Copy _fields entries.
        var fieldsLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsFieldsGetter);
        il.Emit(OpCodes.Stloc, fieldsLocal);

        var noFieldsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Brfalse, noFieldsLabel);

        var fieldsEnumeratorType = _types.MakeGenericType(typeof(Dictionary<,>.Enumerator).GetGenericTypeDefinition(), _types.String, _types.Object);
        var fieldsEnumLocal = il.DeclareLocal(fieldsEnumeratorType);
        il.Emit(OpCodes.Ldloc, fieldsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "GetEnumerator")!);
        il.Emit(OpCodes.Stloc, fieldsEnumLocal);
        var fieldsLoopStart = il.DefineLabel();
        var fieldsLoopEnd = il.DefineLabel();
        il.MarkLabel(fieldsLoopStart);
        il.Emit(OpCodes.Ldloca, fieldsEnumLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(fieldsEnumeratorType, "MoveNext")!);
        il.Emit(OpCodes.Brfalse, fieldsLoopEnd);
        var fkvLocal = il.DeclareLocal(_types.KeyValuePairStringObject);
        il.Emit(OpCodes.Ldloca, fieldsEnumLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(fieldsEnumeratorType, "Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, fkvLocal);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloca, fkvLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.KeyValuePairStringObject, "Key").GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, fkvLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.KeyValuePairStringObject, "Value").GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));
        il.Emit(OpCodes.Br, fieldsLoopStart);
        il.MarkLabel(fieldsLoopEnd);
        il.MarkLabel(noFieldsLabel);

        // Iterate _getters (if any). For each getter, invoke and store result.
        var gettersLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetGettersDict);
        il.Emit(OpCodes.Stloc, gettersLocal);

        var noGettersLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, gettersLocal);
        il.Emit(OpCodes.Brfalse, noGettersLabel);

        var emptyArgsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, emptyArgsLocal);

        var gettersEnumLocal = il.DeclareLocal(fieldsEnumeratorType);
        il.Emit(OpCodes.Ldloc, gettersLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "GetEnumerator")!);
        il.Emit(OpCodes.Stloc, gettersEnumLocal);
        var gLoopStart = il.DefineLabel();
        var gLoopEnd = il.DefineLabel();
        il.MarkLabel(gLoopStart);
        il.Emit(OpCodes.Ldloca, gettersEnumLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(fieldsEnumeratorType, "MoveNext")!);
        il.Emit(OpCodes.Brfalse, gLoopEnd);
        var gkvLocal = il.DeclareLocal(_types.KeyValuePairStringObject);
        il.Emit(OpCodes.Ldloca, gettersEnumLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(fieldsEnumeratorType, "Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, gkvLocal);
        // result[key] = InvokeMethodValue(obj, getter, [])
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloca, gkvLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.KeyValuePairStringObject, "Key").GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, gkvLocal);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.KeyValuePairStringObject, "Value").GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, emptyArgsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));
        il.Emit(OpCodes.Br, gLoopStart);
        il.MarkLabel(gLoopEnd);
        il.MarkLabel(noGettersLabel);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // Fallback: receiver isn't $Object — return the IHasFields dict directly.
        il.MarkLabel(fallbackLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        var nullReturnLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, nullReturnLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsFieldsGetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(nullReturnLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DefineSymbolAccessor(obj, key, getter, setter) — stores an accessor
    /// descriptor in the object's symbol-dict for computed symbol keys
    /// (e.g. `{ get [Symbol.toPrimitive]() {...} }`). Reuses
    /// $CompiledPropertyDescriptor as the storage shape; readers detect the
    /// descriptor via Isinst and invoke the Getter via InvokeMethodValue.
    /// String keys fall through to $Object.DefineGetter/DefineSetter.
    /// </summary>
    private void EmitDefineSymbolAccessor(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "DefineSymbolAccessor",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object, _types.Object]
        );
        runtime.DefineSymbolAccessor = method;

        var il = method.GetILGenerator();
        var symKeyLabel = il.DefineLabel();
        var stringKeyLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // If key is a Symbol → symbol-dict path. Else → $Object accessor path.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
        il.Emit(OpCodes.Brfalse, stringKeyLabel);

        // Symbol path: build $CompiledPropertyDescriptor and store in symbol-dict.
        il.MarkLabel(symKeyLabel);
        var descLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
        il.Emit(OpCodes.Stloc, descLocal);
        // desc.Getter = arg2
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetSetMethod()!);
        // desc.Setter = arg3
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetSetMethod()!);
        // GetSymbolDict(obj)[key] = desc
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, descLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "set_Item", _types.Object, _types.Object));
        il.Emit(OpCodes.Br, endLabel);

        // String path: stringify key, route to $Object.DefineGetter/DefineSetter.
        // Only valid if obj is a $Object.
        il.MarkLabel(stringKeyLabel);
        var notTSObjLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notTSObjLabel);
        // keyStr = key.ToString()
        var keyStrLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, keyStrLocal);
        // if (getter != null) obj.DefineGetter(keyStr, getter)
        var skipGetterLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brfalse, skipGetterLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldloc, keyStrLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectDefineGetter);
        il.MarkLabel(skipGetterLabel);
        // if (setter != null) obj.DefineSetter(keyStr, setter)
        var skipSetterLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brfalse, skipSetterLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldloc, keyStrLocal);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectDefineSetter);
        il.MarkLabel(skipSetterLabel);
        il.MarkLabel(notTSObjLabel);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits Math.sumPrecise(iterable) — ECMA-262 21.3.2.31. Iterates the input
    /// through indexed arrays, lifted generators, or the observable iterator protocol.
    /// Finite binary64 values are converted to exact integer multiples of 2^-1074,
    /// accumulated as BigInteger, and rounded once. Non-Number elements close custom
    /// iterators before throwing; empty/all-negative-zero and infinity/NaN cases follow
    /// the specification directly.
    /// </summary>
    private void EmitMathSumPrecise(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var toUnits = EmitMathSumPreciseToUnits(typeBuilder);
        var fromUnits = EmitMathSumPreciseFromUnits(typeBuilder);

        var method = typeBuilder.DefineMethod(
            "MathSumPrecise",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.MathSumPrecise = method;

        var il = method.GetILGenerator();
        var list = il.DeclareLocal(_types.ListOfObject);
        var symbolDict = il.DeclareLocal(_types.DictionaryObjectObject);
        var iteratorFunction = il.DeclareLocal(_types.Object);
        var iterator = il.DeclareLocal(_types.Object);
        var iteratorResult = il.DeclareLocal(_types.Object);
        var clrIterator = il.DeclareLocal(_types.IEnumeratorOfObject);
        var returnFunction = il.DeclareLocal(_types.Object);
        var element = il.DeclareLocal(_types.Object);
        var value = il.DeclareLocal(_types.Double);
        var exactUnits = il.DeclareLocal(_types.BigInteger);
        var index = il.DeclareLocal(_types.Int32);
        var length = il.DeclareLocal(_types.Int32);
        var customIterator = il.DeclareLocal(_types.Boolean);
        var clrIteratorActive = il.DeclareLocal(_types.Boolean);
        var any = il.DeclareLocal(_types.Boolean);
        var allNegativeZero = il.DeclareLocal(_types.Boolean);
        var sawNaN = il.DeclareLocal(_types.Boolean);
        var sawPositiveInfinity = il.DeclareLocal(_types.Boolean);
        var sawNegativeInfinity = il.DeclareLocal(_types.Boolean);

        var setupIterator = il.DefineLabel();
        var testList = il.DefineLabel();
        var setupList = il.DefineLabel();
        var loop = il.DefineLabel();
        var listElement = il.DefineLabel();
        var processElement = il.DefineLabel();
        var finiteElement = il.DefineLabel();
        var notNaN = il.DefineLabel();
        var notPositiveInfinity = il.DefineLabel();
        var notNegativeInfinity = il.DefineLabel();
        var advance = il.DefineLabel();
        var advanceList = il.DefineLabel();
        var done = il.DefineLabel();
        var invalidElement = il.DefineLabel();
        var throwInvalidElement = il.DefineLabel();
        var iterableTypeError = il.DefineLabel();
        var nonEmpty = il.DefineLabel();
        var notBothInfinities = il.DefineLabel();
        var notNaNResult = il.DefineLabel();
        var notPositiveInfinityResult = il.DefineLabel();
        var notNegativeInfinityResult = il.DefineLabel();
        var notAllNegativeZero = il.DefineLabel();

        il.Emit(OpCodes.Call, _types.GetProperty(_types.BigInteger, "Zero")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, exactUnits);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, allNegativeZero);

        // Dense arrays/lists use indexed access unless they carry an own
        // Symbol.iterator override. Every other input follows the iterator protocol.
        // Lifted generators already expose IEnumerator<object> directly.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.IEnumeratorOfObject);
        il.Emit(OpCodes.Stloc, clrIterator);
        il.Emit(OpCodes.Ldloc, clrIterator);
        il.Emit(OpCodes.Brfalse, testList);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, customIterator);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, clrIteratorActive);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(testList);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, list);
        il.Emit(OpCodes.Ldloc, list);
        il.Emit(OpCodes.Brfalse, setupIterator);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Stloc, symbolDict);
        il.Emit(OpCodes.Ldloc, symbolDict);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolIterator);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.DictionaryObjectObject, "ContainsKey", _types.Object));
        il.Emit(OpCodes.Brfalse, setupList);

        il.MarkLabel(setupIterator);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.SymbolIterator);
        il.Emit(OpCodes.Call, runtime.GetIteratorFunction);
        il.Emit(OpCodes.Stloc, iteratorFunction);
        il.Emit(OpCodes.Ldloc, iteratorFunction);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, iterableTypeError);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iteratorFunction);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Stloc, iterator);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, customIterator);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(setupList);
        il.Emit(OpCodes.Ldloc, list);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, length);

        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, customIterator);
        il.Emit(OpCodes.Brfalse, listElement);
        var jsIteratorElement = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, clrIteratorActive);
        il.Emit(OpCodes.Brfalse, jsIteratorElement);
        il.Emit(OpCodes.Ldloc, clrIterator);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, clrIterator);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(
            _types.IEnumeratorOfObject, "Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, element);
        il.Emit(OpCodes.Br, processElement);

        il.MarkLabel(jsIteratorElement);
        il.Emit(OpCodes.Ldloc, iterator);
        il.Emit(OpCodes.Call, runtime.InvokeIteratorNext);
        il.Emit(OpCodes.Stloc, iteratorResult);
        il.Emit(OpCodes.Ldloc, iteratorResult);
        il.Emit(OpCodes.Call, runtime.GetIteratorDone);
        il.Emit(OpCodes.Brtrue, done);
        il.Emit(OpCodes.Ldloc, iteratorResult);
        il.Emit(OpCodes.Call, runtime.GetIteratorValue);
        il.Emit(OpCodes.Stloc, element);
        il.Emit(OpCodes.Br, processElement);

        il.MarkLabel(listElement);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, length);
        il.Emit(OpCodes.Bge, done);
        il.Emit(OpCodes.Ldloc, list);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, element);

        il.MarkLabel(processElement);
        il.Emit(OpCodes.Ldloc, element);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, invalidElement);
        il.Emit(OpCodes.Ldloc, element);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, any);

        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", _types.Double));
        il.Emit(OpCodes.Brfalse, notNaN);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, sawNaN);
        il.Emit(OpCodes.Br, advance);
        il.MarkLabel(notNaN);

        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsPositiveInfinity", _types.Double));
        il.Emit(OpCodes.Brfalse, notPositiveInfinity);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, sawPositiveInfinity);
        il.Emit(OpCodes.Br, advance);
        il.MarkLabel(notPositiveInfinity);

        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNegativeInfinity", _types.Double));
        il.Emit(OpCodes.Brfalse, notNegativeInfinity);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, sawNegativeInfinity);
        il.Emit(OpCodes.Br, advance);
        il.MarkLabel(notNegativeInfinity);

        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Bne_Un, finiteElement);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNegative", _types.Double));
        il.Emit(OpCodes.Brtrue, finiteElement);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, allNegativeZero);
        il.Emit(OpCodes.Br, finiteElement);

        il.MarkLabel(finiteElement);
        // Every non-zero finite value makes the all-negative-zero condition false.
        var addUnits = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Beq, addUnits);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, allNegativeZero);
        il.MarkLabel(addUnits);
        il.Emit(OpCodes.Ldloc, exactUnits);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Call, toUnits);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.BigInteger, "op_Addition", _types.BigInteger, _types.BigInteger));
        il.Emit(OpCodes.Stloc, exactUnits);

        il.MarkLabel(advance);
        il.Emit(OpCodes.Ldloc, customIterator);
        il.Emit(OpCodes.Brfalse, advanceList);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(advanceList);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, loop);

        // IteratorClose on abrupt non-Number rejection.
        il.MarkLabel(invalidElement);
        il.Emit(OpCodes.Ldloc, customIterator);
        il.Emit(OpCodes.Brfalse, throwInvalidElement);
        var closeJsIterator = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, clrIteratorActive);
        il.Emit(OpCodes.Brfalse, closeJsIterator);
        il.Emit(OpCodes.Ldloc, clrIterator);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IDisposable, "Dispose"));
        il.Emit(OpCodes.Br, throwInvalidElement);
        il.MarkLabel(closeJsIterator);
        il.Emit(OpCodes.Ldloc, iterator);
        il.Emit(OpCodes.Ldstr, "return");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, returnFunction);
        il.Emit(OpCodes.Ldloc, returnFunction);
        il.Emit(OpCodes.Brfalse, throwInvalidElement);
        il.Emit(OpCodes.Ldloc, returnFunction);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, throwInvalidElement);
        il.Emit(OpCodes.Ldloc, iterator);
        il.Emit(OpCodes.Ldloc, returnFunction);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(throwInvalidElement);
        GuestErrorEmitter.ThrowTypeError(
            il, runtime, "Math.sumPrecise: every element must be a Number");

        il.MarkLabel(iterableTypeError);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Math.sumPrecise requires an iterable");

        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, any);
        il.Emit(OpCodes.Brtrue, nonEmpty);
        il.Emit(OpCodes.Ldc_R8, -0.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(nonEmpty);

        il.Emit(OpCodes.Ldloc, sawPositiveInfinity);
        il.Emit(OpCodes.Ldloc, sawNegativeInfinity);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brfalse, notBothInfinities);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBothInfinities);

        il.Emit(OpCodes.Ldloc, sawNaN);
        il.Emit(OpCodes.Brfalse, notNaNResult);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notNaNResult);

        il.Emit(OpCodes.Ldloc, sawPositiveInfinity);
        il.Emit(OpCodes.Brfalse, notPositiveInfinityResult);
        il.Emit(OpCodes.Ldc_R8, double.PositiveInfinity);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notPositiveInfinityResult);

        il.Emit(OpCodes.Ldloc, sawNegativeInfinity);
        il.Emit(OpCodes.Brfalse, notNegativeInfinityResult);
        il.Emit(OpCodes.Ldc_R8, double.NegativeInfinity);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notNegativeInfinityResult);

        il.Emit(OpCodes.Ldloc, allNegativeZero);
        il.Emit(OpCodes.Brfalse, notAllNegativeZero);
        il.Emit(OpCodes.Ldc_R8, -0.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notAllNegativeZero);

        il.Emit(OpCodes.Ldloc, exactUnits);
        il.Emit(OpCodes.Call, fromUnits);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
    }

    private MethodBuilder EmitMathSumPreciseToUnits(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "MathSumPreciseToUnits",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.BigInteger,
            [_types.Double]);

        var il = method.GetILGenerator();
        var bits = il.DeclareLocal(_types.Int64);
        var exponent = il.DeclareLocal(_types.Int32);
        var significand = il.DeclareLocal(_types.BigInteger);
        var negative = il.DeclareLocal(_types.Boolean);
        var subnormal = il.DefineLabel();
        var applySign = il.DefineLabel();
        var done = il.DefineLabel();
        var ctorLong = _types.GetConstructor(_types.BigInteger, _types.Int64);
        var shiftLeft = _types.GetMethod(
            _types.BigInteger, "op_LeftShift", _types.BigInteger, _types.Int32);
        var negate = _types.GetMethod(
            _types.BigInteger, "op_UnaryNegation", _types.BigInteger);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(
            typeof(BitConverter), "DoubleToInt64Bits", _types.Double));
        il.Emit(OpCodes.Stloc, bits);
        il.Emit(OpCodes.Ldloc, bits);
        il.Emit(OpCodes.Ldc_I8, 0L);
        il.Emit(OpCodes.Clt);
        il.Emit(OpCodes.Stloc, negative);
        il.Emit(OpCodes.Ldloc, bits);
        il.Emit(OpCodes.Ldc_I4, 52);
        il.Emit(OpCodes.Shr);
        il.Emit(OpCodes.Ldc_I8, 0x7ffL);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, exponent);
        il.Emit(OpCodes.Ldloc, exponent);
        il.Emit(OpCodes.Brfalse, subnormal);

        il.Emit(OpCodes.Ldloc, bits);
        il.Emit(OpCodes.Ldc_I8, 0x000f_ffff_ffff_ffffL);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Ldc_I8, 1L << 52);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Newobj, ctorLong);
        il.Emit(OpCodes.Ldloc, exponent);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, shiftLeft);
        il.Emit(OpCodes.Stloc, significand);
        il.Emit(OpCodes.Br, applySign);

        il.MarkLabel(subnormal);
        il.Emit(OpCodes.Ldloc, bits);
        il.Emit(OpCodes.Ldc_I8, 0x000f_ffff_ffff_ffffL);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Newobj, ctorLong);
        il.Emit(OpCodes.Stloc, significand);

        il.MarkLabel(applySign);
        il.Emit(OpCodes.Ldloc, negative);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, significand);
        il.Emit(OpCodes.Call, negate);
        il.Emit(OpCodes.Stloc, significand);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ldloc, significand);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitMathSumPreciseFromUnits(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "MathSumPreciseFromUnits",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Double,
            [_types.BigInteger]);

        var il = method.GetILGenerator();
        var magnitude = il.DeclareLocal(_types.BigInteger);
        var significand = il.DeclareLocal(_types.BigInteger);
        var remainder = il.DeclareLocal(_types.BigInteger);
        var halfway = il.DeclareLocal(_types.BigInteger);
        var shift = il.DeclareLocal(_types.Int32);
        var comparison = il.DeclareLocal(_types.Int32);
        var negative = il.DeclareLocal(_types.Boolean);
        var result = il.DeclareLocal(_types.Double);

        var isZero = _types.GetProperty(_types.BigInteger, "IsZero")!.GetGetMethod()!;
        var sign = _types.GetProperty(_types.BigInteger, "Sign")!.GetGetMethod()!;
        var isEven = _types.GetProperty(_types.BigInteger, "IsEven")!.GetGetMethod()!;
        var one = _types.GetProperty(_types.BigInteger, "One")!.GetGetMethod()!;
        var abs = _types.GetMethod(_types.BigInteger, "Abs", _types.BigInteger);
        var bitLength = _types.GetMethodNoParams(_types.BigInteger, "GetBitLength");
        var shiftRight = _types.GetMethod(
            _types.BigInteger, "op_RightShift", _types.BigInteger, _types.Int32);
        var shiftLeft = _types.GetMethod(
            _types.BigInteger, "op_LeftShift", _types.BigInteger, _types.Int32);
        var subtract = _types.GetMethod(
            _types.BigInteger, "op_Subtraction", _types.BigInteger, _types.BigInteger);
        var increment = _types.GetMethod(
            _types.BigInteger, "op_Increment", _types.BigInteger);
        var compare = _types.GetMethod(
            _types.BigInteger, "Compare", _types.BigInteger, _types.BigInteger);
        var toDouble = _types.GetMethods(_types.BigInteger, BindingFlags.Public | BindingFlags.Static)
            .Single(candidate => candidate.Name == "op_Explicit"
                && candidate.ReturnType == _types.Double
                && candidate.GetParameters() is [{ ParameterType: var parameter }]
                && parameter == _types.BigInteger);

        var nonZero = il.DefineLabel();
        var finish = il.DefineLabel();
        var roundUp = il.DefineLabel();
        var belowHalfway = il.DefineLabel();
        var positive = il.DefineLabel();

        il.Emit(OpCodes.Ldarga_S, 0);
        il.Emit(OpCodes.Call, isZero);
        il.Emit(OpCodes.Brfalse, nonZero);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(nonZero);

        il.Emit(OpCodes.Ldarga_S, 0);
        il.Emit(OpCodes.Call, sign);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Clt);
        il.Emit(OpCodes.Stloc, negative);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, abs);
        il.Emit(OpCodes.Stloc, magnitude);
        il.Emit(OpCodes.Ldloca, magnitude);
        il.Emit(OpCodes.Call, bitLength);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4, 53);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.Math, "Max", _types.Int32, _types.Int32));
        il.Emit(OpCodes.Stloc, shift);
        il.Emit(OpCodes.Ldloc, magnitude);
        il.Emit(OpCodes.Ldloc, shift);
        il.Emit(OpCodes.Call, shiftRight);
        il.Emit(OpCodes.Stloc, significand);
        il.Emit(OpCodes.Ldloc, shift);
        il.Emit(OpCodes.Brfalse, finish);

        il.Emit(OpCodes.Ldloc, magnitude);
        il.Emit(OpCodes.Ldloc, significand);
        il.Emit(OpCodes.Ldloc, shift);
        il.Emit(OpCodes.Call, shiftLeft);
        il.Emit(OpCodes.Call, subtract);
        il.Emit(OpCodes.Stloc, remainder);
        il.Emit(OpCodes.Call, one);
        il.Emit(OpCodes.Ldloc, shift);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, shiftLeft);
        il.Emit(OpCodes.Stloc, halfway);
        il.Emit(OpCodes.Ldloc, remainder);
        il.Emit(OpCodes.Ldloc, halfway);
        il.Emit(OpCodes.Call, compare);
        il.Emit(OpCodes.Stloc, comparison);
        il.Emit(OpCodes.Ldloc, comparison);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, roundUp);
        il.Emit(OpCodes.Ldloc, comparison);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, belowHalfway);
        il.Emit(OpCodes.Ldloca, significand);
        il.Emit(OpCodes.Call, isEven);
        il.Emit(OpCodes.Brtrue, finish);

        il.MarkLabel(roundUp);
        il.Emit(OpCodes.Ldloc, significand);
        il.Emit(OpCodes.Call, increment);
        il.Emit(OpCodes.Stloc, significand);
        il.Emit(OpCodes.Ldloca, significand);
        il.Emit(OpCodes.Call, bitLength);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4, 53);
        il.Emit(OpCodes.Ble, finish);
        il.Emit(OpCodes.Ldloc, significand);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, shiftRight);
        il.Emit(OpCodes.Stloc, significand);
        il.Emit(OpCodes.Ldloc, shift);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, shift);
        il.Emit(OpCodes.Br, finish);

        il.MarkLabel(belowHalfway);
        il.MarkLabel(finish);
        il.Emit(OpCodes.Ldloc, significand);
        il.Emit(OpCodes.Call, toDouble);
        il.Emit(OpCodes.Ldloc, shift);
        il.Emit(OpCodes.Ldc_I4, 1074);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.Math, "ScaleB", _types.Double, _types.Int32));
        il.Emit(OpCodes.Stloc, result);
        il.Emit(OpCodes.Ldloc, negative);
        il.Emit(OpCodes.Brfalse, positive);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Neg);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(positive);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private void EmitGetEnumMemberName(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetEnumMemberName",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.Double, _types.DoubleArray, _types.StringArray]
        );
        runtime.GetEnumMemberName = method;

        var il = method.GetILGenerator();
        // Simple linear search through keys to find matching value
        var indexLocal = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // Check if keys[i] == value
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_R8);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ceq);
        var notMatchLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notMatchLabel);

        // Found - return values[i]
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notMatchLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        // Not found - throw
        il.Emit(OpCodes.Ldstr, "Value not found in enum");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
    }
}
