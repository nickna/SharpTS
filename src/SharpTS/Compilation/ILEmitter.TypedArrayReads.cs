using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    // The numeric consumer requests ToNumber(read), so its invalid arm may stay
    // native NaN. Value consumers must retain the actual undefined sentinel.
    private bool TryEmitGuardedTypedArrayRead(Expr.GetIndex read, bool numericConsumer)
    {
        if (read.Optional || read.Object is not Expr.Variable
            || _ctx.TypeMap?.Get(read.Object) is not TypeInfo.TypedArray arrayType
            || _ctx.Runtime?.TypedArrays.Implementation is not { } arrays
            || arrays.GetNumericType(arrayType.ElementType) is not { } concreteType
            || !arrays.GetUnboxedByElement.TryGetValue(arrayType.ElementType, out var getter)
            || !IsPrimitiveNumericCompoundIndex(read.Index))
            return false;

        numericConsumer |= _ctx.TypeMap.IsPresentTypedArrayRead(read);

        HoistedTypedArrayBacking? direct =
            TryGetDirectTypedArrayBacking(read.Object, arrayType.ElementType, out var backing)
                ? backing : null;
        LocalBuilder? receiver = null;
        if (direct is null)
        {
            EmitTypedArrayReceiver(read.Object, concreteType);
            receiver = IL.DeclareLocal(concreteType);
            IL.Emit(OpCodes.Stloc, receiver);
        }

        EmitExpressionAsDouble(read.Index);
        var numericIndex = IL.DeclareLocal(_ctx.Types.Double);
        var physicalIndex = IL.DeclareLocal(_ctx.Types.Int32);
        IL.Emit(OpCodes.Stloc, numericIndex);
        var invalid = IL.DefineLabel();
        var done = IL.DefineLabel();
        // Guard the original Number before narrowing. Primitive numeric -0 is
        // property key "0"; NaN, fractions, infinities and large values are invalid.
        IL.Emit(OpCodes.Ldloc, numericIndex);
        IL.Emit(OpCodes.Ldc_R8, 0d);
        IL.Emit(OpCodes.Blt_Un, invalid);
        IL.Emit(OpCodes.Ldloc, numericIndex);
        if (direct is { } lengthBacking)
            IL.Emit(OpCodes.Ldloc, lengthBacking.LengthLocal);
        else
        {
            IL.Emit(OpCodes.Ldloc, receiver!);
            IL.Emit(OpCodes.Callvirt, arrays.LengthGetter);
        }
        IL.Emit(OpCodes.Conv_R8);
        IL.Emit(OpCodes.Bge_Un, invalid);
        IL.Emit(OpCodes.Ldloc, numericIndex);
        IL.Emit(OpCodes.Conv_I4);
        IL.Emit(OpCodes.Stloc, physicalIndex);
        IL.Emit(OpCodes.Ldloc, physicalIndex);
        IL.Emit(OpCodes.Conv_R8);
        IL.Emit(OpCodes.Ldloc, numericIndex);
        IL.Emit(OpCodes.Bne_Un, invalid);

        if (direct is { } directRead)
            EmitDirectTypedArrayRead(directRead, physicalIndex);
        else
        {
            IL.Emit(OpCodes.Ldloc, receiver!);
            IL.Emit(OpCodes.Ldloc, physicalIndex);
            IL.Emit(OpCodes.Call, getter);
        }
        if (!numericConsumer) IL.Emit(OpCodes.Box, _ctx.Types.Double);
        IL.Emit(OpCodes.Br, done);

        IL.MarkLabel(invalid);
        if (numericConsumer) IL.Emit(OpCodes.Ldc_R8, double.NaN);
        else IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.Sentinels.UndefinedInstance);
        IL.MarkLabel(done);
        SetStackType(numericConsumer ? StackType.Double : StackType.Unknown);
        return true;
    }
}
