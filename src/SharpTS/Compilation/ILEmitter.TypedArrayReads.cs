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
        LocalBuilder? originalReceiver = null;
        bool provenCarrier = read.Object is Expr.Variable known
            && _ctx.TypeMap.IsStableTypedArrayBackingReceiver(known);
        if (direct is null && provenCarrier)
        {
            EmitTypedArrayReceiver(read.Object, concreteType);
            receiver = IL.DeclareLocal(concreteType);
            IL.Emit(OpCodes.Stloc, receiver);
        }
        else if (direct is null)
        {
            originalReceiver = SpillBoxed(read.Object);
            receiver = IL.DeclareLocal(concreteType);
            if (read.Object is Expr.Variable variable && _ctx.TryGetHoistedTypedArray(variable.Name.Lexeme) is { } hoisted)
                IL.Emit(OpCodes.Ldloc, hoisted.TypedLocal);
            else
            {
                IL.Emit(OpCodes.Ldloc, originalReceiver);
                IL.Emit(OpCodes.Isinst, concreteType);
            }
            IL.Emit(OpCodes.Stloc, receiver);
        }

        EmitExpressionAsDouble(read.Index);
        var numericIndex = IL.DeclareLocal(_ctx.Types.Double);
        var physicalIndex = IL.DeclareLocal(_ctx.Types.Int32);
        IL.Emit(OpCodes.Stloc, numericIndex);
        var invalid = IL.DefineLabel();
        var done = IL.DefineLabel();
        var ordinary = IL.DefineLabel();
        if (originalReceiver is not null)
        {
            IL.Emit(OpCodes.Ldloc, receiver!);
            IL.Emit(OpCodes.Brfalse, ordinary);
        }
        EmitTypedArrayNumericIndexGuard(numericIndex, physicalIndex, invalid, () =>
        {
            if (direct is { } lengthBacking) IL.Emit(OpCodes.Ldloc, lengthBacking.LengthLocal);
            else
            {
                IL.Emit(OpCodes.Ldloc, receiver!);
                IL.Emit(OpCodes.Callvirt, arrays.LengthGetter);
            }
        });

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
        if (originalReceiver is not null)
        {
            IL.Emit(OpCodes.Br, done);
            IL.MarkLabel(ordinary);
            IL.Emit(OpCodes.Ldloc, numericIndex);
            IL.Emit(OpCodes.Box, _ctx.Types.Double);
            var boxedIndex = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, boxedIndex);
            EmitThrowIfUndefinedIndexReceiver(originalReceiver, boxedIndex, isWrite: false);
            IL.Emit(OpCodes.Ldloc, originalReceiver);
            IL.Emit(OpCodes.Ldloc, boxedIndex);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.ObjectRead.Index);
            if (numericConsumer) IL.Emit(OpCodes.Call, _ctx.Runtime.NumericCoercion.ToNumber);
        }
        IL.MarkLabel(done);
        SetStackType(numericConsumer ? StackType.Double : StackType.Unknown);
        return true;
    }

    // Empty stack at entry and at both exits. Validate the original Number
    // before narrowing; callers supply only the intrinsic physical length.
    private void EmitTypedArrayNumericIndexGuard(LocalBuilder numericIndex,
        LocalBuilder physicalIndex, Label invalid, Action loadLength)
    {
        IL.Emit(OpCodes.Ldloc, numericIndex);
        IL.Emit(OpCodes.Ldc_R8, 0d);
        IL.Emit(OpCodes.Blt_Un, invalid);
        IL.Emit(OpCodes.Ldloc, numericIndex);
        loadLength();
        IL.Emit(OpCodes.Conv_R8);
        IL.Emit(OpCodes.Bge_Un, invalid);
        IL.Emit(OpCodes.Ldloc, numericIndex);
        IL.Emit(OpCodes.Conv_I4);
        IL.Emit(OpCodes.Stloc, physicalIndex);
        IL.Emit(OpCodes.Ldloc, physicalIndex);
        IL.Emit(OpCodes.Conv_R8);
        IL.Emit(OpCodes.Ldloc, numericIndex);
        IL.Emit(OpCodes.Bne_Un, invalid);
    }
}
