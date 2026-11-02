using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitTypedArrayPhysicalIndexGuard(ILGenerator il,
        EmittedTypedArrayImplementation arrays, LocalBuilder key, LocalBuilder index, Label absent)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, arrays.BaseType);
        il.Emit(OpCodes.Brfalse, absent);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Ldloca, index);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, absent);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, absent);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, arrays.BaseType);
        il.Emit(OpCodes.Callvirt, arrays.LengthGetter);
        il.Emit(OpCodes.Bge, absent);
        // Match the existing string-index guard's canonical decimal round trip.
        // Named spellings such as "01", "+1" and "-0" retain the old path.
        il.Emit(OpCodes.Ldloca, index);
        il.Emit(OpCodes.Call, _types.GetProperty(typeof(System.Globalization.CultureInfo), "InvariantCulture").GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "ToString", typeof(IFormatProvider)));
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brfalse, absent);
    }

    private void EmitGetOwnDescriptorTypedArrayIndex(ILGenerator il,
        EmittedTypedArrayRuntime? typedArrays, LocalBuilder key, LocalBuilder result, Label end)
    {
        if (typedArrays?.Implementation is not { } arrays) return;
        var next = il.DefineLabel();
        var index = il.DeclareLocal(_types.Int32);
        EmitTypedArrayPhysicalIndexGuard(il, arrays, key, index, next);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, result);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, arrays.BaseType);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt, arrays.ElementGet);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        foreach (string flag in new[] { "writable", "enumerable", "configurable" })
        {
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Ldstr, flag);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Box, _types.Boolean);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        }
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Br, end);
        il.MarkLabel(next);
    }

    private void EmitAppendTypedArrayPhysicalKeys(ILGenerator il,
        EmittedTypedArrayRuntime? typedArrays, LocalBuilder result)
    {
        var arrays = typedArrays?.Implementation;
        if (arrays is null) return;

        var next = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, arrays.BaseType);
        il.Emit(OpCodes.Brfalse, next);

        // The physical element count is independent of a named own `length`
        // descriptor. Enumerating integer keys never reads values or getters.
        var count = il.DeclareLocal(_types.Int32);
        var index = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, arrays.BaseType);
        il.Emit(OpCodes.Callvirt, arrays.LengthGetter);
        il.Emit(OpCodes.Stloc, count);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, index);
        var loop = il.DefineLabel();
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, count);
        il.Emit(OpCodes.Bge, next);
        il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ldloca, index);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Int32, "ToString"));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(next);
    }
}
