using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitTypedArrayViewValidation(ILGenerator il, int bytesPerElement,
        LocalBuilder bufferByteLength, LocalBuilder viewLength, EmittedErrorRuntime errors)
    {
        var invalid = il.DefineLabel();
        var valid = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, invalid);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4, bytesPerElement);
        il.Emit(OpCodes.Rem);
        il.Emit(OpCodes.Brtrue, invalid);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, bufferByteLength);
        il.Emit(OpCodes.Bgt, invalid);
        var explicitLength = il.DefineLabel();
        il.Emit(OpCodes.Ldarga, 3);
        il.Emit(OpCodes.Call, typeof(int?).GetProperty("HasValue")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, explicitLength);
        il.Emit(OpCodes.Ldloc, bufferByteLength);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_I4, bytesPerElement);
        il.Emit(OpCodes.Rem);
        il.Emit(OpCodes.Brtrue, invalid);
        il.MarkLabel(explicitLength);
        il.Emit(OpCodes.Ldloc, viewLength);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, invalid);

        // Compare element counts after validating the offset. Multiplying the
        // requested length by element width could overflow and admit a bad view.
        il.Emit(OpCodes.Ldloc, viewLength);
        il.Emit(OpCodes.Ldloc, bufferByteLength);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_I4, bytesPerElement);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Ble, valid);
        il.MarkLabel(invalid);
        GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.RangeErrorConstructor,
            "Invalid typed array buffer view");
        il.MarkLabel(valid);
    }
}
