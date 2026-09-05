using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    private void EmitQueueGet(LocalBuilder local, ArrayQueueTypeInfo queue, Expr index, bool numeric)
    {
        // A fractional/NaN/out-of-Int32 key cannot name a slot in this private
        // representation. Evaluate it once, preserving any mutation side effects.
        EmitExpressionAsDouble(index);
        var key = IL.DeclareLocal(_ctx.Types.Double);
        var integer = IL.DeclareLocal(_ctx.Types.Int32);
        var missing = IL.DefineLabel();
        var end = IL.DefineLabel();
        IL.Emit(OpCodes.Stloc, key);
        IL.Emit(OpCodes.Ldloc, key);
        IL.Emit(OpCodes.Conv_I4);
        IL.Emit(OpCodes.Stloc, integer);
        IL.Emit(OpCodes.Ldloc, integer);
        IL.Emit(OpCodes.Conv_R8);
        IL.Emit(OpCodes.Ldloc, key);
        IL.Emit(OpCodes.Bne_Un, missing);
        IL.Emit(OpCodes.Ldloc, local);
        IL.Emit(OpCodes.Ldloc, integer);
        IL.Emit(OpCodes.Call, numeric ? queue.GetNumber! : queue.Get);
        IL.Emit(OpCodes.Br, end);
        IL.MarkLabel(missing);
        if (numeric) IL.Emit(OpCodes.Ldc_R8, double.NaN);
        else IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.UndefinedInstance);
        IL.MarkLabel(end);
        if (numeric) SetStackType(StackType.Double);
        else SetStackUnknown();
    }
}
