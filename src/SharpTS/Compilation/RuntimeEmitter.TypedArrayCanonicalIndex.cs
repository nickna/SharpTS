using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    // Callers have already performed ToPropertyKey and dispatched actual Proxies
    // and Symbols. Noncanonical names retain the existing ordinary/legacy route.
    private void EmitTypedArrayCanonicalIndexGuard(ILGenerator il,
        EmittedTypedArrayRuntime typedArrays, MethodInfo toNumber, MethodInfo toJsString,
        LocalBuilder index, Label notCanonical, Label invalid)
    {
        var arrays = typedArrays.RequireImplementation();
        var key = il.DeclareLocal(_types.String);
        var numeric = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typedArrays.IsTypedArray);
        il.Emit(OpCodes.Brfalse, notCanonical);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, key);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Brfalse, notCanonical);

        // CanonicalNumericIndexString preserves the otherwise lost sign of -0.
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Ldstr, "-0");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, invalid);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Call, toNumber);
        il.Emit(OpCodes.Stloc, numeric);
        il.Emit(OpCodes.Ldloc, numeric);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Call, toJsString);
        il.Emit(OpCodes.Ldloc, key);
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brfalse, notCanonical);

        // Check range before narrowing: NaN, infinities, negatives, fractions and
        // out-of-bounds indices never reach physical storage or CLR conversion.
        il.Emit(OpCodes.Ldloc, numeric);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Blt_Un, invalid);
        il.Emit(OpCodes.Ldloc, numeric);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, arrays.BaseType);
        il.Emit(OpCodes.Callvirt, arrays.LengthGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Bge_Un, invalid);
        il.Emit(OpCodes.Ldloc, numeric);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Ldloc, numeric);
        il.Emit(OpCodes.Bne_Un, invalid);
    }

    private void EmitTypedArrayCanonicalIndexGet(ILGenerator il,
        EmittedTypedArrayRuntime typedArrays, MethodInfo toNumber,
        MethodInfo toJsString, FieldInfo undefined)
    {
        if (typedArrays.Implementation is not { } arrays) return;
        var next = il.DefineLabel();
        var invalid = il.DefineLabel();
        var index = il.DeclareLocal(_types.Int32);
        EmitTypedArrayCanonicalIndexGuard(il, typedArrays, toNumber, toJsString, index, next, invalid);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Call, arrays.GetElement);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(invalid);
        il.Emit(OpCodes.Ldsfld, undefined);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(next);
    }

    private void EmitTypedArrayCanonicalIndexSet(ILGenerator il,
        EmittedTypedArrayRuntime typedArrays, MethodInfo toNumber,
        MethodInfo? toBigInt, MethodInfo toJsString)
    {
        if (typedArrays.Implementation is not { } arrays) return;
        var next = il.DefineLabel();
        var invalid = il.DefineLabel();
        var index = il.DeclareLocal(_types.Int32);
        EmitTypedArrayCanonicalIndexGuard(il, typedArrays, toNumber, toJsString, index, next, invalid);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, arrays.SetElement);
        il.Emit(OpCodes.Ret);

        // IntegerIndexedElementSet converts the value even when the canonical
        // numeric index is invalid. The original assignment result stays with
        // the caller; only the converted storage value is discarded here.
        il.MarkLabel(invalid);
        var bigInt = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, arrays.BigInt64ArrayType);
        il.Emit(OpCodes.Brtrue, bigInt);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, arrays.BigUint64ArrayType);
        il.Emit(OpCodes.Brtrue, bigInt);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, toNumber);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(bigInt);
        il.Emit(OpCodes.Ldarg_2);
        if (toBigInt is not null)
            il.Emit(OpCodes.Call, toBigInt);
        // Match the existing concrete BigInt setter's feature-gated conversion.
        il.Emit(OpCodes.Unbox_Any, _types.BigInteger);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(next);
    }

    private void EmitTypedArrayCanonicalIndexStrictSet(ILGenerator il,
        EmittedTypedArrayRuntime typedArrays, MethodInfo toNumber,
        MethodInfo toJsString, MethodInfo setIndex)
    {
        if (typedArrays.Implementation is null) return;
        var next = il.DefineLabel();
        var canonical = il.DefineLabel();
        var index = il.DeclareLocal(_types.Int32);
        EmitTypedArrayCanonicalIndexGuard(il, typedArrays, toNumber, toJsString, index, next, canonical);
        il.MarkLabel(canonical);
        // Reclassification receives an already primitive string, so it cannot
        // replay a user key hook. Canonical indexed writes do not use ordinary
        // extensibility/prototype rejection, including in strict code.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, setIndex);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(next);
    }
}
