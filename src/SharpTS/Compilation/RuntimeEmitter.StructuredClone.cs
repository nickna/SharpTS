using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    // Scoped inputs for peer families that have not yet acquired their own metadata components.
    // These readonly values are never retained on the emitter or the emitted runtime.
    private readonly record struct StructuredCloneObjectInputs(Type Type, ConstructorInfo Ctor, MethodInfo FieldsGetter);

    private readonly record struct StructuredCloneErrorInputs(
        Type Type, Type InstanceFieldsInterface, MethodInfo NameGetter, MethodInfo MessageGetter,
        MethodInfo StackGetter, MethodInfo StackSetter, ConstructorInfo ErrorCtor, ConstructorInfo TypeErrorCtor,
        ConstructorInfo RangeErrorCtor, ConstructorInfo ReferenceErrorCtor, ConstructorInfo SyntaxErrorCtor,
        ConstructorInfo URIErrorCtor, ConstructorInfo EvalErrorCtor);

    private readonly record struct StructuredCloneDateInputs(Type Type, ConstructorInfo Ctor, MethodInfo GetTime);

    private readonly record struct StructuredCloneRegExpInputs(
        Type Type, ConstructorInfo Ctor, MethodInfo SourceGetter, MethodInfo FlagsGetter);

    private readonly record struct StructuredCloneBinaryInputs(
        EmittedArrayBufferRuntime ArrayBuffer, EmittedSharedArrayBufferRuntime SharedArrayBuffer,
        EmittedTypedArrayImplementation TypedArrays);

    /// <summary>
    /// Emits StructuredClone helper.
    /// Preserves the compiled wrapper signature; its second argument is currently ignored.
    /// </summary>
    private void EmitStructuredCloneHelper(
        TypeBuilder runtimeType, EmittedStructuredCloneRuntime clone, EmittedArrayStorageRuntime arrays, Type undefinedType,
        StructuredCloneObjectInputs objects, StructuredCloneErrorInputs errors, StructuredCloneBinaryInputs? binary,
        StructuredCloneDateInputs? date, StructuredCloneRegExpInputs? regExp, EmittedBufferRuntime? buffer)
    {
        var cloneCore = runtimeType.DefineMethod(
            "StructuredCloneCore",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );

        var coreIl = cloneCore.GetILGenerator();
        var sourceListLocal = coreIl.DeclareLocal(_types.ListOfObject);
        var clonedListLocal = coreIl.DeclareLocal(_types.ListOfObject);
        var sourceDictStringLocal = coreIl.DeclareLocal(_types.DictionaryStringObject);
        var clonedDictStringLocal = coreIl.DeclareLocal(_types.DictionaryStringObject);
        var sourceDictObjectLocal = coreIl.DeclareLocal(_types.DictionaryObjectObject);
        var clonedDictObjectLocal = coreIl.DeclareLocal(_types.DictionaryObjectObject);
        var sourceSetLocal = coreIl.DeclareLocal(_types.HashSetOfObject);
        var clonedSetLocal = coreIl.DeclareLocal(_types.HashSetOfObject);
        var indexLocal = coreIl.DeclareLocal(_types.Int32);
        var valueLocal = coreIl.DeclareLocal(_types.Object);
        var keyLocal = coreIl.DeclareLocal(_types.Object);
        var dictEnumLocal = coreIl.DeclareLocal(_types.IDictionaryEnumerator);
        var setEnumLocal = coreIl.DeclareLocal(_types.IEnumerator);
        var currentLocal = coreIl.DeclareLocal(_types.Object);

        // $Object (plain object literal, possibly with getters/setters) — clone via its
        // Fields dictionary (#1255).
        var objLiteralSourceLocal = coreIl.DeclareLocal(objects.Type);
        var objLiteralClonedFieldsLocal = coreIl.DeclareLocal(_types.DictionaryStringObject);

        // $Error hierarchy — clone via name-based reconstruction + Stack preservation (#1255).
        var errSourceLocal = coreIl.DeclareLocal(errors.Type);
        var errNameLocal = coreIl.DeclareLocal(_types.String);
        var errMsgLocal = coreIl.DeclareLocal(_types.String);
        var clonedErrLocal = coreIl.DeclareLocal(errors.Type);

        var nextCheckNull = coreIl.DefineLabel();
        var checkSharedArrayBuffer = coreIl.DefineLabel();
        var checkArrayBuffer = coreIl.DefineLabel();
        var checkTypedArray = coreIl.DefineLabel();
        var checkList = coreIl.DefineLabel();
        var checkStringDict = coreIl.DefineLabel();
        var checkObjectLiteral = coreIl.DefineLabel();
        var checkObjectDict = coreIl.DefineLabel();
        var checkSet = coreIl.DefineLabel();
        var checkDate = coreIl.DefineLabel();
        var checkRegExp = coreIl.DefineLabel();
        var checkBuffer = coreIl.DefineLabel();
        var checkError = coreIl.DefineLabel();
        var fallbackReturn = coreIl.DefineLabel();
        var returnClonedList = coreIl.DefineLabel();
        var returnClonedStringDict = coreIl.DefineLabel();
        var returnClonedObjectDict = coreIl.DefineLabel();
        var returnClonedSet = coreIl.DefineLabel();
        var listLoopCheck = coreIl.DefineLabel();
        var listLoopBody = coreIl.DefineLabel();
        var stringDictLoopCheck = coreIl.DefineLabel();
        var stringDictLoopBody = coreIl.DefineLabel();
        var objectDictLoopCheck = coreIl.DefineLabel();
        var objectDictLoopBody = coreIl.DefineLabel();
        var setLoopCheck = coreIl.DefineLabel();
        var setLoopBody = coreIl.DefineLabel();

        // if (value == null) return null;
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Brtrue, nextCheckNull);
        coreIl.Emit(OpCodes.Ldnull);
        coreIl.Emit(OpCodes.Ret);
        coreIl.MarkLabel(nextCheckNull);

        // Primitives (number/string/boolean/bigint) and the `undefined` singleton are
        // immutable/shared — return as-is, no cloning needed. Matches the interpreter's
        // early `value is double or string or bool or BigInteger` return in CloneInternal
        // (#1255) — without this, throwing-on-fallback below would wrongly reject every
        // clone of a container holding a boxed number/string/bool/bigint/undefined value.
        var checkPrimitiveString = coreIl.DefineLabel();
        var checkPrimitiveBool = coreIl.DefineLabel();
        var checkPrimitiveBigInt = coreIl.DefineLabel();
        var checkPrimitiveUndefined = coreIl.DefineLabel();

        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Isinst, _types.Double);
        coreIl.Emit(OpCodes.Brfalse, checkPrimitiveString);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Ret);

        coreIl.MarkLabel(checkPrimitiveString);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Isinst, _types.String);
        coreIl.Emit(OpCodes.Brfalse, checkPrimitiveBool);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Ret);

        coreIl.MarkLabel(checkPrimitiveBool);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Isinst, _types.Boolean);
        coreIl.Emit(OpCodes.Brfalse, checkPrimitiveBigInt);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Ret);

        coreIl.MarkLabel(checkPrimitiveBigInt);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Isinst, _types.BigInteger);
        coreIl.Emit(OpCodes.Brfalse, checkPrimitiveUndefined);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Ret);

        coreIl.MarkLabel(checkPrimitiveUndefined);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Isinst, undefinedType);
        coreIl.Emit(OpCodes.Brfalse, checkSharedArrayBuffer);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Ret);

        // SharedArrayBuffer is transferred by reference. Skip when typed
        // arrays aren't emitted — no SharedArrayBuffer values can exist.
        coreIl.MarkLabel(checkSharedArrayBuffer);
        if (binary.HasValue)
        {
            coreIl.Emit(OpCodes.Ldarg_0);
            coreIl.Emit(OpCodes.Isinst, binary.Value.SharedArrayBuffer.Type);
            coreIl.Emit(OpCodes.Brfalse, checkArrayBuffer);
            coreIl.Emit(OpCodes.Ldarg_0);
            coreIl.Emit(OpCodes.Ret);
        }
        else
        {
            // Always fall through to the next check.
            coreIl.Emit(OpCodes.Br, checkArrayBuffer);
        }

        // $ArrayBuffer / $TypedArray — deep clone (#1255). A non-shared buffer is copied
        // independently; a TypedArray view backed by a SharedArrayBuffer clones to a NEW
        // VIEW over the SAME buffer (sharing is the entire point of SharedArrayBuffer,
        // matching the interpreter's CreateTypedArrayView for the shared case).
        coreIl.MarkLabel(checkArrayBuffer);
        if (binary.HasValue)
        {
            var abLocal = coreIl.DeclareLocal(binary.Value.ArrayBuffer.Type);
            coreIl.Emit(OpCodes.Ldarg_0);
            coreIl.Emit(OpCodes.Isinst, binary.Value.ArrayBuffer.Type);
            coreIl.Emit(OpCodes.Stloc, abLocal);
            coreIl.Emit(OpCodes.Ldloc, abLocal);
            coreIl.Emit(OpCodes.Brfalse, checkTypedArray);

            // return source.Slice(0, source.ByteLength)
            coreIl.Emit(OpCodes.Ldloc, abLocal);
            coreIl.Emit(OpCodes.Ldc_I4_0);
            coreIl.Emit(OpCodes.Ldloc, abLocal);
            coreIl.Emit(OpCodes.Callvirt, binary.Value.ArrayBuffer.ByteLengthGetter);
            coreIl.Emit(OpCodes.Callvirt, binary.Value.ArrayBuffer.Slice);
            coreIl.Emit(OpCodes.Ret);

            coreIl.MarkLabel(checkTypedArray);
            var taLocal = coreIl.DeclareLocal(binary.Value.TypedArrays.BaseType);
            var taSharedLabel = coreIl.DefineLabel();
            coreIl.Emit(OpCodes.Ldarg_0);
            coreIl.Emit(OpCodes.Isinst, binary.Value.TypedArrays.BaseType);
            coreIl.Emit(OpCodes.Stloc, taLocal);
            coreIl.Emit(OpCodes.Ldloc, taLocal);
            coreIl.Emit(OpCodes.Brfalse, checkList);

            coreIl.Emit(OpCodes.Ldloc, taLocal);
            coreIl.Emit(OpCodes.Callvirt, binary.Value.TypedArrays.BufferGetter);
            coreIl.Emit(OpCodes.Isinst, binary.Value.SharedArrayBuffer.Type);
            coreIl.Emit(OpCodes.Brtrue, taSharedLabel);

            // return source.Slice(0, source.Length) — independent copy
            coreIl.Emit(OpCodes.Ldloc, taLocal);
            coreIl.Emit(OpCodes.Ldc_I4_0);
            coreIl.Emit(OpCodes.Ldloc, taLocal);
            coreIl.Emit(OpCodes.Callvirt, binary.Value.TypedArrays.LengthGetter);
            coreIl.Emit(OpCodes.Callvirt, binary.Value.TypedArrays.Slice);
            coreIl.Emit(OpCodes.Ret);

            // return source.Subarray(0, source.Length) — shares the SharedArrayBuffer
            coreIl.MarkLabel(taSharedLabel);
            coreIl.Emit(OpCodes.Ldloc, taLocal);
            coreIl.Emit(OpCodes.Ldc_I4_0);
            coreIl.Emit(OpCodes.Ldloc, taLocal);
            coreIl.Emit(OpCodes.Callvirt, binary.Value.TypedArrays.LengthGetter);
            coreIl.Emit(OpCodes.Callvirt, binary.Value.TypedArrays.Subarray);
            coreIl.Emit(OpCodes.Ret);
        }
        else
        {
            coreIl.Emit(OpCodes.Br, checkList);
        }

        // List<object> deep clone.
        coreIl.MarkLabel(checkList);
        // number[] unboxing: materialize a numeric-mode $Array before deep-cloning its base list.
        EmitDeoptArgIfNumericArrayStorage(coreIl, arrays, 0);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Isinst, _types.ListOfObject);
        coreIl.Emit(OpCodes.Stloc, sourceListLocal);
        coreIl.Emit(OpCodes.Ldloc, sourceListLocal);
        coreIl.Emit(OpCodes.Brfalse, checkStringDict);

        coreIl.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        coreIl.Emit(OpCodes.Stloc, clonedListLocal);
        coreIl.Emit(OpCodes.Ldc_I4_0);
        coreIl.Emit(OpCodes.Stloc, indexLocal);
        coreIl.Emit(OpCodes.Br, listLoopCheck);

        coreIl.MarkLabel(listLoopBody);
        coreIl.Emit(OpCodes.Ldloc, sourceListLocal);
        coreIl.Emit(OpCodes.Ldloc, indexLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        coreIl.Emit(OpCodes.Call, cloneCore);
        coreIl.Emit(OpCodes.Stloc, valueLocal);
        coreIl.Emit(OpCodes.Ldloc, clonedListLocal);
        coreIl.Emit(OpCodes.Ldloc, valueLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        coreIl.Emit(OpCodes.Ldloc, indexLocal);
        coreIl.Emit(OpCodes.Ldc_I4_1);
        coreIl.Emit(OpCodes.Add);
        coreIl.Emit(OpCodes.Stloc, indexLocal);

        coreIl.MarkLabel(listLoopCheck);
        coreIl.Emit(OpCodes.Ldloc, indexLocal);
        coreIl.Emit(OpCodes.Ldloc, sourceListLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Count"));
        coreIl.Emit(OpCodes.Blt, listLoopBody);
        coreIl.Emit(OpCodes.Br, returnClonedList);

        // Dictionary<string, object> deep clone.
        coreIl.MarkLabel(checkStringDict);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        coreIl.Emit(OpCodes.Stloc, sourceDictStringLocal);
        coreIl.Emit(OpCodes.Ldloc, sourceDictStringLocal);
        coreIl.Emit(OpCodes.Brfalse, checkObjectLiteral);

        coreIl.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        coreIl.Emit(OpCodes.Stloc, clonedDictStringLocal);
        coreIl.Emit(OpCodes.Ldloc, sourceDictStringLocal);
        coreIl.Emit(OpCodes.Castclass, _types.IDictionary);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "GetEnumerator"));
        coreIl.Emit(OpCodes.Stloc, dictEnumLocal);
        coreIl.Emit(OpCodes.Br, stringDictLoopCheck);

        coreIl.MarkLabel(stringDictLoopBody);
        coreIl.Emit(OpCodes.Ldloc, dictEnumLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionaryEnumerator, "get_Key"));
        coreIl.Emit(OpCodes.Stloc, keyLocal);
        coreIl.Emit(OpCodes.Ldloc, dictEnumLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionaryEnumerator, "get_Value"));
        coreIl.Emit(OpCodes.Call, cloneCore);
        coreIl.Emit(OpCodes.Stloc, valueLocal);

        coreIl.Emit(OpCodes.Ldloc, clonedDictStringLocal);
        coreIl.Emit(OpCodes.Ldloc, keyLocal);
        coreIl.Emit(OpCodes.Castclass, _types.String);
        coreIl.Emit(OpCodes.Ldloc, valueLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);

        coreIl.MarkLabel(stringDictLoopCheck);
        coreIl.Emit(OpCodes.Ldloc, dictEnumLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IEnumerator, "MoveNext"));
        coreIl.Emit(OpCodes.Brtrue, stringDictLoopBody);
        coreIl.Emit(OpCodes.Br, returnClonedStringDict);

        // $Object (object literal, possibly with getters/setters not present on a plain
        // Dictionary<string,object?>) — clone its Fields dict via a recursive cloneCore
        // call (reuses the Dictionary<string,object?> branch above), then rebuild (#1255).
        // Getters/setters are not preserved — matches the interpreter's CloneObject, which
        // only copies data fields.
        coreIl.MarkLabel(checkObjectLiteral);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Isinst, objects.Type);
        coreIl.Emit(OpCodes.Stloc, objLiteralSourceLocal);
        coreIl.Emit(OpCodes.Ldloc, objLiteralSourceLocal);
        coreIl.Emit(OpCodes.Brfalse, checkObjectDict);

        coreIl.Emit(OpCodes.Ldloc, objLiteralSourceLocal);
        coreIl.Emit(OpCodes.Callvirt, objects.FieldsGetter);
        coreIl.Emit(OpCodes.Call, cloneCore);
        coreIl.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        coreIl.Emit(OpCodes.Stloc, objLiteralClonedFieldsLocal);
        coreIl.Emit(OpCodes.Ldloc, objLiteralClonedFieldsLocal);
        coreIl.Emit(OpCodes.Newobj, objects.Ctor);
        coreIl.Emit(OpCodes.Ret);

        // Dictionary<object, object> deep clone (Map backing store).
        coreIl.MarkLabel(checkObjectDict);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Isinst, _types.DictionaryObjectObject);
        coreIl.Emit(OpCodes.Stloc, sourceDictObjectLocal);
        coreIl.Emit(OpCodes.Ldloc, sourceDictObjectLocal);
        coreIl.Emit(OpCodes.Brfalse, checkSet);

        coreIl.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryObjectObject));
        coreIl.Emit(OpCodes.Stloc, clonedDictObjectLocal);
        coreIl.Emit(OpCodes.Ldloc, sourceDictObjectLocal);
        coreIl.Emit(OpCodes.Castclass, _types.IDictionary);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "GetEnumerator"));
        coreIl.Emit(OpCodes.Stloc, dictEnumLocal);
        coreIl.Emit(OpCodes.Br, objectDictLoopCheck);

        coreIl.MarkLabel(objectDictLoopBody);
        coreIl.Emit(OpCodes.Ldloc, dictEnumLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionaryEnumerator, "get_Key"));
        coreIl.Emit(OpCodes.Call, cloneCore);
        coreIl.Emit(OpCodes.Stloc, keyLocal);
        coreIl.Emit(OpCodes.Ldloc, dictEnumLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionaryEnumerator, "get_Value"));
        coreIl.Emit(OpCodes.Call, cloneCore);
        coreIl.Emit(OpCodes.Stloc, valueLocal);

        coreIl.Emit(OpCodes.Ldloc, clonedDictObjectLocal);
        coreIl.Emit(OpCodes.Ldloc, keyLocal);
        coreIl.Emit(OpCodes.Ldloc, valueLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.DictionaryObjectObjectSetItem);

        coreIl.MarkLabel(objectDictLoopCheck);
        coreIl.Emit(OpCodes.Ldloc, dictEnumLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IEnumerator, "MoveNext"));
        coreIl.Emit(OpCodes.Brtrue, objectDictLoopBody);
        coreIl.Emit(OpCodes.Br, returnClonedObjectDict);

        // HashSet<object> deep clone.
        coreIl.MarkLabel(checkSet);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Isinst, _types.HashSetOfObject);
        coreIl.Emit(OpCodes.Stloc, sourceSetLocal);
        coreIl.Emit(OpCodes.Ldloc, sourceSetLocal);
        coreIl.Emit(OpCodes.Brfalse, checkDate);

        coreIl.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.HashSetOfObject));
        coreIl.Emit(OpCodes.Stloc, clonedSetLocal);
        coreIl.Emit(OpCodes.Ldloc, sourceSetLocal);
        coreIl.Emit(OpCodes.Castclass, _types.IEnumerable);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IEnumerable, "GetEnumerator"));
        coreIl.Emit(OpCodes.Stloc, setEnumLocal);
        coreIl.Emit(OpCodes.Br, setLoopCheck);

        coreIl.MarkLabel(setLoopBody);
        coreIl.Emit(OpCodes.Ldloc, setEnumLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IEnumerator, "get_Current"));
        coreIl.Emit(OpCodes.Call, cloneCore);
        coreIl.Emit(OpCodes.Stloc, currentLocal);
        coreIl.Emit(OpCodes.Ldloc, clonedSetLocal);
        coreIl.Emit(OpCodes.Ldloc, currentLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.HashSetOfObject, "Add", _types.Object));
        coreIl.Emit(OpCodes.Pop);

        coreIl.MarkLabel(setLoopCheck);
        coreIl.Emit(OpCodes.Ldloc, setEnumLocal);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IEnumerator, "MoveNext"));
        coreIl.Emit(OpCodes.Brtrue, setLoopBody);
        coreIl.Emit(OpCodes.Br, returnClonedSet);

        // $TSDate — deep clone via epoch milliseconds (#1255).
        coreIl.MarkLabel(checkDate);
        if (date.HasValue)
        {
            var dateLocal = coreIl.DeclareLocal(date.Value.Type);
            coreIl.Emit(OpCodes.Ldarg_0);
            coreIl.Emit(OpCodes.Isinst, date.Value.Type);
            coreIl.Emit(OpCodes.Stloc, dateLocal);
            coreIl.Emit(OpCodes.Ldloc, dateLocal);
            coreIl.Emit(OpCodes.Brfalse, checkRegExp);

            coreIl.Emit(OpCodes.Ldloc, dateLocal);
            coreIl.Emit(OpCodes.Call, date.Value.GetTime);
            coreIl.Emit(OpCodes.Newobj, date.Value.Ctor);
            coreIl.Emit(OpCodes.Ret);
        }
        else
        {
            coreIl.Emit(OpCodes.Br, checkRegExp);
        }

        // $RegExp — clone pattern + flags (#1255). Matches the interpreter's CloneInternal,
        // which likewise rebuilds from Source/Flags only (lastIndex is not preserved).
        coreIl.MarkLabel(checkRegExp);
        if (regExp.HasValue)
        {
            var regexpLocal = coreIl.DeclareLocal(regExp.Value.Type);
            coreIl.Emit(OpCodes.Ldarg_0);
            coreIl.Emit(OpCodes.Isinst, regExp.Value.Type);
            coreIl.Emit(OpCodes.Stloc, regexpLocal);
            coreIl.Emit(OpCodes.Ldloc, regexpLocal);
            coreIl.Emit(OpCodes.Brfalse, checkBuffer);

            coreIl.Emit(OpCodes.Ldloc, regexpLocal);
            coreIl.Emit(OpCodes.Callvirt, regExp.Value.SourceGetter);
            coreIl.Emit(OpCodes.Ldloc, regexpLocal);
            coreIl.Emit(OpCodes.Callvirt, regExp.Value.FlagsGetter);
            coreIl.Emit(OpCodes.Newobj, regExp.Value.Ctor);
            coreIl.Emit(OpCodes.Ret);
        }
        else
        {
            coreIl.Emit(OpCodes.Br, checkBuffer);
        }

        // $Buffer — deep-copy bytes via the existing Buffer.from(buffer) factory (#1255).
        coreIl.MarkLabel(checkBuffer);
        if (buffer is not null)
        {
            var bufferLocal = coreIl.DeclareLocal(buffer.Type);
            coreIl.Emit(OpCodes.Ldarg_0);
            coreIl.Emit(OpCodes.Isinst, buffer.Type);
            coreIl.Emit(OpCodes.Stloc, bufferLocal);
            coreIl.Emit(OpCodes.Ldloc, bufferLocal);
            coreIl.Emit(OpCodes.Brfalse, checkError);

            coreIl.Emit(OpCodes.Ldloc, bufferLocal);
            coreIl.Emit(OpCodes.Call, buffer.FromBuffer);
            coreIl.Emit(OpCodes.Ret);
        }
        else
        {
            coreIl.Emit(OpCodes.Br, checkError);
        }

        // $Error hierarchy — clone via name-based reconstruction + Stack preservation
        // (#1255), mirroring the interpreter's CloneError. A user-defined class extending
        // Error (`class Foo extends Error`) compiles with $Error as its real CLR base
        // (ILCompiler.Classes.cs) but — like every user class — ALSO implements
        // $IHasFields, which built-in $Error/$TypeError/etc. instances never do. Excluding
        // that case here routes it to the generic uncloneable fallback below, matching the
        // interpreter's narrower CloneError (only the fixed Error/TypeError/RangeError/...
        // hierarchy — a SharpTSInstance user subclass takes a different, field-copying path
        // the compiled side does not replicate).
        coreIl.MarkLabel(checkError);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Isinst, errors.Type);
        coreIl.Emit(OpCodes.Stloc, errSourceLocal);
        coreIl.Emit(OpCodes.Ldloc, errSourceLocal);
        coreIl.Emit(OpCodes.Brfalse, fallbackReturn);

        coreIl.Emit(OpCodes.Ldloc, errSourceLocal);
        coreIl.Emit(OpCodes.Isinst, errors.InstanceFieldsInterface);
        coreIl.Emit(OpCodes.Brtrue, fallbackReturn);

        coreIl.Emit(OpCodes.Ldloc, errSourceLocal);
        coreIl.Emit(OpCodes.Callvirt, errors.NameGetter);
        coreIl.Emit(OpCodes.Stloc, errNameLocal);
        coreIl.Emit(OpCodes.Ldloc, errSourceLocal);
        coreIl.Emit(OpCodes.Callvirt, errors.MessageGetter);
        coreIl.Emit(OpCodes.Stloc, errMsgLocal);

        var strEquals = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);
        var isTypeErrorLabel = coreIl.DefineLabel();
        var isRangeErrorLabel = coreIl.DefineLabel();
        var isReferenceErrorLabel = coreIl.DefineLabel();
        var isSyntaxErrorLabel = coreIl.DefineLabel();
        var isURIErrorLabel = coreIl.DefineLabel();
        var isEvalErrorLabel = coreIl.DefineLabel();
        var defaultErrorLabel = coreIl.DefineLabel();
        var haveClonedErrLabel = coreIl.DefineLabel();

        coreIl.Emit(OpCodes.Ldloc, errNameLocal);
        coreIl.Emit(OpCodes.Ldstr, "TypeError");
        coreIl.Emit(OpCodes.Call, strEquals);
        coreIl.Emit(OpCodes.Brtrue, isTypeErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errNameLocal);
        coreIl.Emit(OpCodes.Ldstr, "RangeError");
        coreIl.Emit(OpCodes.Call, strEquals);
        coreIl.Emit(OpCodes.Brtrue, isRangeErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errNameLocal);
        coreIl.Emit(OpCodes.Ldstr, "ReferenceError");
        coreIl.Emit(OpCodes.Call, strEquals);
        coreIl.Emit(OpCodes.Brtrue, isReferenceErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errNameLocal);
        coreIl.Emit(OpCodes.Ldstr, "SyntaxError");
        coreIl.Emit(OpCodes.Call, strEquals);
        coreIl.Emit(OpCodes.Brtrue, isSyntaxErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errNameLocal);
        coreIl.Emit(OpCodes.Ldstr, "URIError");
        coreIl.Emit(OpCodes.Call, strEquals);
        coreIl.Emit(OpCodes.Brtrue, isURIErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errNameLocal);
        coreIl.Emit(OpCodes.Ldstr, "EvalError");
        coreIl.Emit(OpCodes.Call, strEquals);
        coreIl.Emit(OpCodes.Brtrue, isEvalErrorLabel);
        coreIl.Emit(OpCodes.Br, defaultErrorLabel);

        coreIl.MarkLabel(isTypeErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errMsgLocal);
        coreIl.Emit(OpCodes.Newobj, errors.TypeErrorCtor);
        coreIl.Emit(OpCodes.Stloc, clonedErrLocal);
        coreIl.Emit(OpCodes.Br, haveClonedErrLabel);

        coreIl.MarkLabel(isRangeErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errMsgLocal);
        coreIl.Emit(OpCodes.Newobj, errors.RangeErrorCtor);
        coreIl.Emit(OpCodes.Stloc, clonedErrLocal);
        coreIl.Emit(OpCodes.Br, haveClonedErrLabel);

        coreIl.MarkLabel(isReferenceErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errMsgLocal);
        coreIl.Emit(OpCodes.Newobj, errors.ReferenceErrorCtor);
        coreIl.Emit(OpCodes.Stloc, clonedErrLocal);
        coreIl.Emit(OpCodes.Br, haveClonedErrLabel);

        coreIl.MarkLabel(isSyntaxErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errMsgLocal);
        coreIl.Emit(OpCodes.Newobj, errors.SyntaxErrorCtor);
        coreIl.Emit(OpCodes.Stloc, clonedErrLocal);
        coreIl.Emit(OpCodes.Br, haveClonedErrLabel);

        coreIl.MarkLabel(isURIErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errMsgLocal);
        coreIl.Emit(OpCodes.Newobj, errors.URIErrorCtor);
        coreIl.Emit(OpCodes.Stloc, clonedErrLocal);
        coreIl.Emit(OpCodes.Br, haveClonedErrLabel);

        coreIl.MarkLabel(isEvalErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errMsgLocal);
        coreIl.Emit(OpCodes.Newobj, errors.EvalErrorCtor);
        coreIl.Emit(OpCodes.Stloc, clonedErrLocal);
        coreIl.Emit(OpCodes.Br, haveClonedErrLabel);

        coreIl.MarkLabel(defaultErrorLabel);
        coreIl.Emit(OpCodes.Ldloc, errMsgLocal);
        coreIl.Emit(OpCodes.Newobj, errors.ErrorCtor);
        coreIl.Emit(OpCodes.Stloc, clonedErrLocal);

        coreIl.MarkLabel(haveClonedErrLabel);
        coreIl.Emit(OpCodes.Ldloc, clonedErrLocal);
        coreIl.Emit(OpCodes.Ldloc, errSourceLocal);
        coreIl.Emit(OpCodes.Callvirt, errors.StackGetter);
        coreIl.Emit(OpCodes.Callvirt, errors.StackSetter);
        coreIl.Emit(OpCodes.Ldloc, clonedErrLocal);
        coreIl.Emit(OpCodes.Ret);

        // Everything else — functions/closures, Symbols, class instances, Promises,
        // WeakMap/WeakSet, iterators/generators, EventEmitters, etc. — cannot be
        // structured-cloned (#1255). Throw rather than alias by reference, matching the
        // interpreter's exhaustive switch (default arm: "Cannot clone value of type X").
        coreIl.MarkLabel(fallbackReturn);

        // A value whose CLR type is NOT defined in this dynamically-emitted assembly is
        // foreign — most commonly a SharpTS.dll interpreter runtime value (SharpTSObject/
        // SharpTSArray/etc.) crossing in via CompiledMessagePortBridge, which already
        // structured-clones on the interpreter side before handing off to this (compiled)
        // port's PostMessage (see CompiledMessagePortBridge.PostMessage's "pass-through
        // no-op for interpreter-shaped values" comment). Pass it through unchanged rather
        // than throwing — only OUR OWN uncloneable constructs ($TSFunction, $TSSymbol,
        // class instances, $Promise, etc., all defined in this assembly) should throw.
        var sameAssemblyLabel = coreIl.DefineLabel();
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        coreIl.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "Assembly").GetGetMethod()!);
        coreIl.Emit(OpCodes.Ldtoken, runtimeType);
        coreIl.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        coreIl.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "Assembly").GetGetMethod()!);
        coreIl.Emit(OpCodes.Ceq);
        coreIl.Emit(OpCodes.Brtrue, sameAssemblyLabel);
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Ret);

        coreIl.MarkLabel(sameAssemblyLabel);
        coreIl.Emit(OpCodes.Ldstr, "Cannot clone value of type ");
        coreIl.Emit(OpCodes.Ldarg_0);
        coreIl.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        coreIl.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "Name").GetGetMethod()!);
        coreIl.Emit(OpCodes.Call, _types.StringConcat2);
        coreIl.Emit(OpCodes.Newobj, clone.ErrorCtor);
        coreIl.Emit(OpCodes.Throw);

        coreIl.MarkLabel(returnClonedList);
        coreIl.Emit(OpCodes.Ldloc, clonedListLocal);
        coreIl.Emit(OpCodes.Ret);

        coreIl.MarkLabel(returnClonedStringDict);
        coreIl.Emit(OpCodes.Ldloc, clonedDictStringLocal);
        coreIl.Emit(OpCodes.Ret);

        coreIl.MarkLabel(returnClonedObjectDict);
        coreIl.Emit(OpCodes.Ldloc, clonedDictObjectLocal);
        coreIl.Emit(OpCodes.Ret);

        coreIl.MarkLabel(returnClonedSet);
        coreIl.Emit(OpCodes.Ldloc, clonedSetLocal);
        coreIl.Emit(OpCodes.Ret);

        var method = runtimeType.DefineMethod(
            "StructuredClone",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, cloneCore);
        il.Emit(OpCodes.Ret);

        clone.Clone = method;
    }
}
