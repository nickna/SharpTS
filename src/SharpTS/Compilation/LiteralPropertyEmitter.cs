using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

// Only construction producers call this: CreateDataProperty bypasses inherited
// setters and replaces any earlier literal accessor with an ordinary data slot.
internal static class LiteralPropertyEmitter
{
    internal static void Emit(TypeProvider types, ILGenerator il,
        EmittedDescriptorStorageRuntime storage, MethodInfo defineProperty,
        Action loadTarget, Action loadKey, Action loadValue)
    {
        var descriptor = il.DeclareLocal(types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, types.DictionaryStringObjectCtor);
        il.Emit(OpCodes.Stloc, descriptor);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, storage.SetPrototype);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Ldstr, "value");
        loadValue();
        il.Emit(OpCodes.Callvirt, types.DictionaryStringObjectSetItem);
        foreach (var attribute in new[] { "writable", "enumerable", "configurable" })
        {
            il.Emit(OpCodes.Ldloc, descriptor);
            il.Emit(OpCodes.Ldstr, attribute);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Box, types.Boolean);
            il.Emit(OpCodes.Callvirt, types.DictionaryStringObjectSetItem);
        }
        loadTarget();
        loadKey();
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Call, defineProperty);
        il.Emit(OpCodes.Pop);
    }
}
