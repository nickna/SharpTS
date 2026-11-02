using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    // Empty entry/fallthrough stack. A handled existing physical element returns
    // directly; ordinary names/other receivers retain their existing paths.
    private void EmitDefineTypedArrayPhysicalProperty(ILGenerator il,
        EmittedTypedArrayRuntime? typedArrays, EmittedBooleanRuntime booleans,
        EmittedErrorRuntime errors, LocalBuilder key, LocalBuilder convertedDescriptor,
        bool returnsBoolean)
    {
        if (typedArrays?.Implementation is not { } arrays) return;
        var notPhysical = il.DefineLabel();
        var rejected = il.DefineLabel();
        var index = il.DeclareLocal(_types.Int32);
        EmitTypedArrayPhysicalIndexGuard(il, arrays, key, index, notPhysical);

        var contains = _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String);
        var tryGet = _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
            _types.String, _types.Object.MakeByRefType());
        var value = il.DeclareLocal(_types.Object);
        foreach (var field in new[] { "get", "set" })
        {
            il.Emit(OpCodes.Ldloc, convertedDescriptor);
            il.Emit(OpCodes.Ldstr, field);
            il.Emit(OpCodes.Callvirt, contains);
            il.Emit(OpCodes.Brtrue, rejected);
        }
        foreach (var field in new[] { "writable", "enumerable", "configurable" })
        {
            var omitted = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, convertedDescriptor);
            il.Emit(OpCodes.Ldstr, field);
            il.Emit(OpCodes.Ldloca, value);
            il.Emit(OpCodes.Callvirt, tryGet);
            il.Emit(OpCodes.Brfalse, omitted);
            il.Emit(OpCodes.Ldloc, value);
            il.Emit(OpCodes.Call, booleans.IsTruthy);
            il.Emit(OpCodes.Brfalse, rejected);
            il.MarkLabel(omitted);
        }

        var noValue = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, convertedDescriptor);
        il.Emit(OpCodes.Ldstr, "value");
        il.Emit(OpCodes.Ldloca, value);
        il.Emit(OpCodes.Callvirt, tryGet);
        il.Emit(OpCodes.Brfalse, noValue);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Call, arrays.SetElement);
        il.MarkLabel(noValue);
        if (returnsBoolean) il.Emit(OpCodes.Ldc_I4_1);
        else il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(rejected);
        if (returnsBoolean)
        {
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
        }
        else
        {
            GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor,
                "Cannot redefine typed array element");
        }
        il.MarkLabel(notPhysical);
    }
}
