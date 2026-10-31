using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Recognizes real runtime Type objects without calling foreign virtual metadata.</summary>
internal static class NativeTypeIdentityEmitter
{
    internal static void EmitRuntimeTypeFence(ILGenerator il, TypeProvider types, Action loadActual, Label refused)
    {
        il.Emit(OpCodes.Ldtoken, types.Object);
        il.Emit(OpCodes.Call, types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Call, types.GetMethodNoParams(types.Object, "GetType"));
        loadActual();
        il.Emit(OpCodes.Call, types.GetMethodNoParams(types.Object, "GetType"));
        il.Emit(OpCodes.Bne_Un, refused);
    }
}
