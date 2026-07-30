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
            il.Emit(OpCodes.Call, kvpType.GetProperty("Key")!.GetGetMethod()!);
            il.Emit(OpCodes.Ldloca, kvpLocal);
            il.Emit(OpCodes.Call, kvpType.GetProperty("Value")!.GetGetMethod()!);
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
        il.Emit(OpCodes.Call, fieldsEnumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, fieldsLoopEnd);
        var fkvLocal = il.DeclareLocal(_types.KeyValuePairStringObject);
        il.Emit(OpCodes.Ldloca, fieldsEnumLocal);
        il.Emit(OpCodes.Call, fieldsEnumeratorType.GetProperty("Current")!.GetGetMethod()!);
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
        il.Emit(OpCodes.Call, fieldsEnumeratorType.GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, gLoopEnd);
        var gkvLocal = il.DeclareLocal(_types.KeyValuePairStringObject);
        il.Emit(OpCodes.Ldloca, gettersEnumLocal);
        il.Emit(OpCodes.Call, fieldsEnumeratorType.GetProperty("Current")!.GetGetMethod()!);
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
    /// (List&lt;object&gt; receivers only — non-list receivers throw TypeError),
    /// throws TypeError on non-Number elements (rejects BigInt, Strings, etc.),
    /// and returns the sum. Special cases:
    ///   • empty list → -0
    ///   • +Infinity AND -Infinity present → NaN
    ///   • any NaN → NaN
    ///   • all -0 → -0; mix of +0 and -0 → +0
    /// Naive accumulator (not Shewchuk); precision matches double arithmetic.
    /// </summary>
    private void EmitMathSumPrecise(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "MathSumPrecise",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.MathSumPrecise = method;

        var il = method.GetILGenerator();

        // Pre-declare every label so emit order doesn't matter.
        var hasListLabel = il.DefineLabel();
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var isDoubleLabel = il.DefineLabel();
        var notNanLabel = il.DefineLabel();
        var notPosInfLabel = il.DefineLabel();
        var notNegInfLabel = il.DefineLabel();
        var notNegZeroLabel = il.DefineLabel();
        var sumAddLabel = il.DefineLabel();
        var advanceLabel = il.DefineLabel();
        var nonEmptyLabel = il.DefineLabel();
        var notBothInfLabel = il.DefineLabel();
        var notNanResultLabel = il.DefineLabel();
        var notPosInfResultLabel = il.DefineLabel();
        var notNegInfResultLabel = il.DefineLabel();
        var notAllNegZeroLabel = il.DefineLabel();

        // Step 1: receiver must be List<object>; otherwise TypeError.
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Brtrue, hasListLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Math.sumPrecise requires an iterable");
        il.MarkLabel(hasListLabel);

        var sumLocal = il.DeclareLocal(_types.Double);
        var sawPosInfLocal = il.DeclareLocal(_types.Boolean);
        var sawNegInfLocal = il.DeclareLocal(_types.Boolean);
        var sawNaNLocal = il.DeclareLocal(_types.Boolean);
        var allNegZeroLocal = il.DeclareLocal(_types.Boolean);
        var anyElementLocal = il.DeclareLocal(_types.Boolean);
        var idxLocal = il.DeclareLocal(_types.Int32);
        var lenLocal = il.DeclareLocal(_types.Int32);
        var elemLocal = il.DeclareLocal(_types.Object);
        var dLocal = il.DeclareLocal(_types.Double);

        il.Emit(OpCodes.Ldc_R8, 0.0); il.Emit(OpCodes.Stloc, sumLocal);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, sawPosInfLocal);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, sawNegInfLocal);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, sawNaNLocal);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stloc, allNegZeroLocal);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, anyElementLocal);

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, idxLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, elemLocal);

        // Type-check: must be Double-boxed.
        il.Emit(OpCodes.Ldloc, elemLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, isDoubleLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Math.sumPrecise: every element must be a Number");
        il.MarkLabel(isDoubleLabel);

        il.Emit(OpCodes.Ldloc, elemLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, dLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, anyElementLocal);

        // NaN check.
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNaN", _types.Double));
        il.Emit(OpCodes.Brfalse, notNanLabel);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stloc, sawNaNLocal);
        il.Emit(OpCodes.Br, advanceLabel);
        il.MarkLabel(notNanLabel);

        // +Inf check.
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsPositiveInfinity", _types.Double));
        il.Emit(OpCodes.Brfalse, notPosInfLabel);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stloc, sawPosInfLocal);
        il.Emit(OpCodes.Br, advanceLabel);
        il.MarkLabel(notPosInfLabel);

        // -Inf check.
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNegativeInfinity", _types.Double));
        il.Emit(OpCodes.Brfalse, notNegInfLabel);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stloc, sawNegInfLocal);
        il.Emit(OpCodes.Br, advanceLabel);
        il.MarkLabel(notNegInfLabel);

        // allNegZero tracking: if d != 0 → not all -0.
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Beq, notNegZeroLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, allNegZeroLocal);
        il.Emit(OpCodes.Br, sumAddLabel);
        il.MarkLabel(notNegZeroLabel);
        // d == 0: if d is +0 (not negative) → not all -0.
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Double, "IsNegative", _types.Double));
        il.Emit(OpCodes.Brtrue, sumAddLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, allNegZeroLocal);

        il.MarkLabel(sumAddLabel);
        il.Emit(OpCodes.Ldloc, sumLocal);
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, sumLocal);

        il.MarkLabel(advanceLabel);
        il.Emit(OpCodes.Ldloc, idxLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, idxLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Empty list → -0.
        il.Emit(OpCodes.Ldloc, anyElementLocal);
        il.Emit(OpCodes.Brtrue, nonEmptyLabel);
        il.Emit(OpCodes.Ldc_R8, -0.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(nonEmptyLabel);

        // sawPosInf && sawNegInf → NaN.
        il.Emit(OpCodes.Ldloc, sawPosInfLocal);
        il.Emit(OpCodes.Ldloc, sawNegInfLocal);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brfalse, notBothInfLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBothInfLabel);

        // sawNaN → NaN.
        il.Emit(OpCodes.Ldloc, sawNaNLocal);
        il.Emit(OpCodes.Brfalse, notNanResultLabel);
        il.Emit(OpCodes.Ldc_R8, double.NaN);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notNanResultLabel);

        // sawPosInf → +Inf.
        il.Emit(OpCodes.Ldloc, sawPosInfLocal);
        il.Emit(OpCodes.Brfalse, notPosInfResultLabel);
        il.Emit(OpCodes.Ldc_R8, double.PositiveInfinity);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notPosInfResultLabel);

        // sawNegInf → -Inf.
        il.Emit(OpCodes.Ldloc, sawNegInfLocal);
        il.Emit(OpCodes.Brfalse, notNegInfResultLabel);
        il.Emit(OpCodes.Ldc_R8, double.NegativeInfinity);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notNegInfResultLabel);

        // allNegZero → -0.
        il.Emit(OpCodes.Ldloc, allNegZeroLocal);
        il.Emit(OpCodes.Brfalse, notAllNegZeroLabel);
        il.Emit(OpCodes.Ldc_R8, -0.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notAllNegZeroLabel);

        // Otherwise: return accumulator.
        il.Emit(OpCodes.Ldloc, sumLocal);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
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
