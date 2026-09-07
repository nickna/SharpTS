using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitCompactIteratorResultRead(ILGenerator il, EmittedRuntime runtime, string key)
    {
        foreach (string fingerprint in _features.CompactObjectRecordIteratorResultShapes.Order(StringComparer.Ordinal))
        {
            if (!runtime.CompactObjectRecordTypes.TryGetValue(fingerprint, out var type))
                continue;
            var shape = _features.CompactObjectRecordShapes[fingerprint];
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
            il.Emit(OpCodes.Call, runtime.CompactObjectRecordIsMaterializedGetters[fingerprint]);
            il.Emit(OpCodes.Brtrue, fallback);
            if (_features.UsesDynamicPropertyDescriptors)
            {
                il.Emit(OpCodes.Ldloc, exact);
                il.Emit(OpCodes.Call, runtime.PDSHasPropertyDescriptors);
                il.Emit(OpCodes.Brtrue, fallback);
            }
            il.Emit(OpCodes.Ldloc, exact);
            il.Emit(OpCodes.Ldfld, runtime.CompactObjectRecordValueFields[(fingerprint, index)]);
            if (key == "value") il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(fallback);
        }
    }

    private void EmitCapturedIteratorMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var require = typeBuilder.DefineMethod("RequireIteratorObject",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.Object]);
        runtime.RequireIteratorObject = require;
        var il = require.GetILGenerator();
        var invalid = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, invalid);
        foreach (var primitive in new Type[] { runtime.UndefinedType, _types.Double,
            _types.Boolean, _types.String, _types.BigInteger, runtime.TSSymbolType })
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, primitive);
            il.Emit(OpCodes.Brtrue, invalid);
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(invalid);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Iterator protocol requires an object");

        var getNext = typeBuilder.DefineMethod("GetIteratorNextMethod",
            MethodAttributes.Public | MethodAttributes.Static, _types.Object, [_types.Object]);
        runtime.GetIteratorNextMethod = getNext;
        il = getNext.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, require);
        il.Emit(OpCodes.Ldstr, "next");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Ret);

        runtime.InvokeCapturedIteratorNext = EmitCall("InvokeCapturedIteratorNext", false);
        runtime.InvokeCapturedIteratorNextWithSent = EmitCall("InvokeCapturedIteratorNextWithSent", true);

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
                callIl.Emit(OpCodes.Call, runtime.InvokeMethodValue);
            }
            else
            {
                callIl.Emit(OpCodes.Call, runtime.InvokeMethodValue0);
            }
            callIl.Emit(OpCodes.Call, require);
            callIl.Emit(OpCodes.Ret);
            return method;
        }
    }
}
