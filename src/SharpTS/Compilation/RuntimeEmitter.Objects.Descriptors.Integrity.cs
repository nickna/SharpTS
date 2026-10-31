using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private static void EmitExistingDescriptorIntegrity(ILGenerator il,
        EmittedDescriptorStorageRuntime storage, LocalBuilder descriptor)
    {
        // Seal/freeze state is kept on the receiver. Validation and merging must
        // observe its effective attributes even when PDS retains an older slot.
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, storage.IsSealed);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, storage.DescriptorConfigurable.GetSetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, storage.IsFrozen);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldloc, descriptor);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, storage.DescriptorWritable.GetSetMethod()!);
        il.MarkLabel(done);
    }
}
