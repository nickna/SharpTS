using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    // The existing array-read classifier, reused before physical writes.
    // Entry/fallthrough stacks are empty; the caller owns the named-key route.
    private void EmitCanonicalArrayStringIndex(ILGenerator il, LocalBuilder key,
        LocalBuilder parsedIndex, LocalBuilder longIndex, Label named)
    {
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Ldloca, parsedIndex);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.UInt32, "TryParse", _types.String, _types.UInt32.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, named);
        il.Emit(OpCodes.Ldloc, parsedIndex);
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Conv_U4);
        il.Emit(OpCodes.Beq, named);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Ldloca, parsedIndex);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.UInt32, "ToString"));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, named);
        il.Emit(OpCodes.Ldloc, parsedIndex);
        il.Emit(OpCodes.Conv_U8);
        il.Emit(OpCodes.Stloc, longIndex);
    }
}
