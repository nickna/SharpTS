using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct CompactIteratorResultInputs(
        EmittedDescriptorStorageRuntime DescriptorStorage,
        EmittedRecordStorageRuntime Records,
        IReadOnlySet<string> IteratorResultShapes,
        IReadOnlyDictionary<string, JsonSerializationShape.Record> Shapes,
        bool UsesDynamicPropertyDescriptors,
        EmittedStableIteratorResultRuntime? StableResults
    );


    private void EmitCompactIteratorResultRead(ILGenerator il, CompactIteratorResultInputs inputs, string key)
    {
        // Proven numeric next methods can return a value type even when an
        // async consumer drives them through the ordinary iterator protocol.
        // Its internal Value/Done fields do not participate in guest lookup.
        if (inputs.StableResults is { } stable)
        {
            var fallback = il.DefineLabel();
            var exact = il.DeclareLocal(stable.Type);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, stable.Type);
            il.Emit(OpCodes.Brfalse, fallback);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Unbox_Any, stable.Type);
            il.Emit(OpCodes.Stloc, exact);
            il.Emit(OpCodes.Ldloca, exact);
            il.Emit(OpCodes.Ldfld, key == "done" ? stable.Done : stable.Value);
            if (key == "value") il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(fallback);
        }

        foreach (string fingerprint in inputs.IteratorResultShapes.Order(StringComparer.Ordinal))
        {
            if (!inputs.Records.CompactTypes.TryGetValue(fingerprint, out var type))
                continue;
            var shape = inputs.Shapes[fingerprint];
            int index = shape.Fields.Select((field, slot) => (field, slot))
                .Single(pair => pair.field.Key == key).slot;
            var fallback = il.DefineLabel();
            var exact = il.DeclareLocal(type);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, type);
            il.Emit(OpCodes.Stloc, exact);
            il.Emit(OpCodes.Ldloc, exact);
            il.Emit(OpCodes.Brfalse, fallback);
            // Always guard observable records, regardless of other uses of the
            // same shape. Descriptors can overlay a record without materializing.
            il.Emit(OpCodes.Ldloc, exact);
            il.Emit(OpCodes.Call, inputs.Records.CompactIsMaterializedGetters[fingerprint]);
            il.Emit(OpCodes.Brtrue, fallback);
            if (inputs.UsesDynamicPropertyDescriptors)
            {
                il.Emit(OpCodes.Ldloc, exact);
                il.Emit(OpCodes.Call, inputs.DescriptorStorage.HasPropertyDescriptors);
                il.Emit(OpCodes.Brtrue, fallback);
            }
            il.Emit(OpCodes.Ldloc, exact);
            il.Emit(OpCodes.Ldfld, inputs.Records.CompactValueFields[(fingerprint, index)]);
            if (key == "value") il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(fallback);
        }
    }

    private readonly record struct CapturedIteratorInputs(
        Type UndefinedType,
        EmittedSymbolRuntime Symbols,
        EmittedObjectReadRuntime ObjectRead,
        EmittedInvocationRuntime Invocation,
        EmittedErrorRuntime Errors,
        MethodInfo HasIn,
        EmittedFunctionConstructionRuntime Construction,
        EmittedFunctionAttributesRuntime Attributes,
        FieldInfo UndefinedInstance,
        Type? GeneratorType
    );

    private void EmitCapturedIteratorMethods(TypeBuilder typeBuilder, EmittedIteratorRecordRuntime iteratorRecords, CapturedIteratorInputs inputs)
    {
        var require = typeBuilder.DefineMethod("RequireIteratorObject",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.Object]);
        var il = require.GetILGenerator();
        var invalid = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, invalid);
        foreach (var primitive in new Type[] { inputs.UndefinedType, _types.Double,
            _types.Boolean, _types.String, _types.BigInteger, inputs.Symbols.Type })
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, primitive);
            il.Emit(OpCodes.Brtrue, invalid);
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(invalid);
        GuestErrorEmitter.ThrowError(il, inputs.Errors.CreateException, inputs.Errors.TypeErrorConstructor, "Iterator protocol requires an object");

        var nativeNext = EmitCapturedNativeIteratorNext(typeBuilder, inputs);
        var getNext = typeBuilder.DefineMethod("GetIteratorNextMethod",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.Object]);
        iteratorRecords.NextMethod = getNext;
        il = getNext.GetILGenerator();
        var next = il.DeclareLocal(_types.Object);
        var useGuestNext = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, require);
        il.Emit(OpCodes.Ldstr, "next");
        il.Emit(OpCodes.Call, inputs.ObjectRead.Property);
        il.Emit(OpCodes.Stloc, next);
        il.Emit(OpCodes.Ldloc, next);
        il.Emit(OpCodes.Isinst, inputs.UndefinedType);
        il.Emit(OpCodes.Brfalse, useGuestNext);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.IEnumerator);
        il.Emit(OpCodes.Brfalse, useGuestNext);
        if (inputs.GeneratorType is { } generatorType)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, generatorType);
            il.Emit(OpCodes.Brtrue, useGuestNext);
        }
        // An explicit undefined/getterless next claims the key. Only a native
        // adapter with no guest slot receives the captured intrinsic operation.
        il.Emit(OpCodes.Ldstr, "next");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, inputs.HasIn);
        il.Emit(OpCodes.Brtrue, useGuestNext);
        _types.EmitLoadMethodInfo(il, nativeNext);
        il.Emit(OpCodes.Ldstr, "next");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, inputs.Construction.GetOrCreate);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(useGuestNext);
        il.Emit(OpCodes.Ldloc, next);
        il.Emit(OpCodes.Ret);

        iteratorRecords.InvokeNext = EmitCall("InvokeCapturedIteratorNext", false);
        iteratorRecords.InvokeNextWithSent = EmitCall("InvokeCapturedIteratorNextWithSent", true);

        MethodBuilder EmitCall(string name, bool hasSent)
        {
            var method = typeBuilder.DefineMethod(name,
                MethodAttributes.Public | MethodAttributes.Static, _types.Object,
                hasSent ? [_types.Object, _types.Object, _types.Object] : [_types.Object, _types.Object]);
            var callIl = method.GetILGenerator();
            callIl.Emit(OpCodes.Ldarg_0); // iterator receiver
            callIl.Emit(OpCodes.Ldarg_1); // captured next value
            if (hasSent)
            {
                callIl.Emit(OpCodes.Ldc_I4_1);
                callIl.Emit(OpCodes.Newarr, _types.Object);
                callIl.Emit(OpCodes.Dup);
                callIl.Emit(OpCodes.Ldc_I4_0);
                callIl.Emit(OpCodes.Ldarg_2);
                callIl.Emit(OpCodes.Stelem_Ref);
                callIl.Emit(OpCodes.Call, inputs.Invocation.Method);
            }
            else
            {
                callIl.Emit(OpCodes.Call, inputs.Invocation.Method0);
            }
            callIl.Emit(OpCodes.Call, require);
            callIl.Emit(OpCodes.Ret);
            return method;
        }
    }

    private MethodBuilder EmitCapturedNativeIteratorNext(TypeBuilder typeBuilder, CapturedIteratorInputs inputs)
    {
        var method = typeBuilder.DefineMethod("CapturedNativeIteratorNext",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.Object]);
        method.DefineParameter(1, ParameterAttributes.None, "__this");
        method.SetCustomAttribute(inputs.Attributes.ExpectsThisCtor, CustomAttributeEncoder.EmptyBlob);
        method.SetCustomAttribute(inputs.Attributes.NonConstructibleCtor, CustomAttributeEncoder.EmptyBlob);
        var il = method.GetILGenerator();
        var iterator = il.DeclareLocal(_types.IEnumerator);
        var done = il.DeclareLocal(_types.Boolean);
        var result = il.DeclareLocal(_types.DictionaryStringObject);
        var valid = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.IEnumerator);
        il.Emit(OpCodes.Stloc, iterator);
        il.Emit(OpCodes.Ldloc, iterator);
        il.Emit(OpCodes.Brtrue, valid);
        GuestErrorEmitter.ThrowError(il, inputs.Errors.CreateException, inputs.Errors.TypeErrorConstructor,
            "Iterator next called on incompatible receiver");
        il.MarkLabel(valid);
        il.Emit(OpCodes.Ldloc, iterator);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Stloc, done);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, result);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ldstr, "done");
        il.Emit(OpCodes.Ldloc, done);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ldstr, "value");
        var complete = il.DefineLabel();
        var valueReady = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, done);
        il.Emit(OpCodes.Brtrue, complete);
        il.Emit(OpCodes.Ldloc, iterator);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.IEnumerator, "Current"));
        il.Emit(OpCodes.Br, valueReady);
        il.MarkLabel(complete);
        il.Emit(OpCodes.Ldsfld, inputs.UndefinedInstance);
        il.MarkLabel(valueReady);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);
        return method;
    }
}
