using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the raw EC-point helpers backing the $ECDH raw-point rework (#1060).
    /// All pure-BCL; part of the standalone $Runtime type.
    /// </summary>
    private void EmitEcPointHelpers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitEcdhPadTo(typeBuilder, runtime);
        EmitEcdhEncodePoint(typeBuilder, runtime);
    }

    private MethodBuilder _ecdhPadTo = null!;

    /// <summary>byte[] EcdhPadTo(byte[] bytes, int length) — left-pad/trim a big-endian magnitude.</summary>
    private void EmitEcdhPadTo(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "EcdhPadTo",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ByteArray,
            [_types.ByteArray, _types.Int32]);
        _ecdhPadTo = method;

        var il = method.GetILGenerator();
        var lenLocal = il.DeclareLocal(_types.Int32);
        var resultLocal = il.DeclareLocal(_types.ByteArray);

        // int srcLen = bytes.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lenLocal);

        // if (srcLen == length) return bytes
        var notEqualLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Bne_Un, notEqualLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notEqualLabel);
        // if (srcLen > length): return bytes[srcLen-length ..]  (Array.Copy tail)
        var padLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ble, padLabel);

        // result = new byte[length]; Array.Copy(bytes, srcLen-length, result, 0, length)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);                 // src
        il.Emit(OpCodes.Ldloc, lenLocal);         // srcLen
        il.Emit(OpCodes.Ldarg_1);                 // length
        il.Emit(OpCodes.Sub);                     // srcIndex = srcLen - length
        il.Emit(OpCodes.Ldloc, resultLocal);      // dst
        il.Emit(OpCodes.Ldc_I4_0);                // dstIndex
        il.Emit(OpCodes.Ldarg_1);                 // length
        il.Emit(OpCodes.Call, _types.ArrayCopy5);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // pad: result = new byte[length]; Array.Copy(bytes, 0, result, length-srcLen, srcLen)
        il.MarkLabel(padLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldarg_0);                 // src
        il.Emit(OpCodes.Ldc_I4_0);                // srcIndex
        il.Emit(OpCodes.Ldloc, resultLocal);      // dst
        il.Emit(OpCodes.Ldarg_1);                 // length
        il.Emit(OpCodes.Ldloc, lenLocal);         // srcLen
        il.Emit(OpCodes.Sub);                     // dstIndex = length - srcLen
        il.Emit(OpCodes.Ldloc, lenLocal);         // srcLen
        il.Emit(OpCodes.Call, _types.ArrayCopy5);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// byte[] EcdhEncodePoint(byte[] x, byte[] y, int fieldLen, string format) —
    /// builds an uncompressed (04||X||Y), compressed (02/03||X), or hybrid (06/07||X||Y) point.
    /// </summary>
    private void EmitEcdhEncodePoint(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "EcdhEncodePoint",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ByteArray,
            [_types.ByteArray, _types.ByteArray, _types.Int32, _types.String]);
        runtime.EcdhEncodePoint = method;

        var il = method.GetILGenerator();
        var xLocal = il.DeclareLocal(_types.ByteArray);
        var yLocal = il.DeclareLocal(_types.ByteArray);
        var resultLocal = il.DeclareLocal(_types.ByteArray);
        var fmtLocal = il.DeclareLocal(_types.String);

        // x = EcdhPadTo(x, fieldLen); y = EcdhPadTo(y, fieldLen)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _ecdhPadTo);
        il.Emit(OpCodes.Stloc, xLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _ecdhPadTo);
        il.Emit(OpCodes.Stloc, yLocal);

        // fmt = (format ?? "uncompressed").ToLowerInvariant()
        var haveFmtLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brtrue, haveFmtLabel);
        il.Emit(OpCodes.Ldstr, "uncompressed");
        il.Emit(OpCodes.Stloc, fmtLocal);
        var fmtDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, fmtDoneLabel);
        il.MarkLabel(haveFmtLabel);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, fmtLocal);
        il.MarkLabel(fmtDoneLabel);

        var compressedLabel = il.DefineLabel();
        var hybridLabel = il.DefineLabel();
        var invalidLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, fmtLocal);
        il.Emit(OpCodes.Ldstr, "compressed");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, compressedLabel);
        il.Emit(OpCodes.Ldloc, fmtLocal);
        il.Emit(OpCodes.Ldstr, "hybrid");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, hybridLabel);
        il.Emit(OpCodes.Ldloc, fmtLocal);
        il.Emit(OpCodes.Ldstr, "uncompressed");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brfalse, invalidLabel);

        // uncompressed: result = new byte[1 + 2*fieldLen]; result[0]=0x04; copy x @1, y @1+fieldLen
        EmitAllocPointBuffer(il, resultLocal, doubled: true);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4, 0x04);
        il.Emit(OpCodes.Stelem_I1);
        EmitCopyCoord(il, xLocal, resultLocal, offsetConst: 1);
        EmitCopyCoordAtFieldOffset(il, yLocal, resultLocal);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // hybrid: result[0] = (y_last & 1)==0 ? 0x06 : 0x07; copy x,y
        il.MarkLabel(hybridLabel);
        EmitAllocPointBuffer(il, resultLocal, doubled: true);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        EmitYParityPrefix(il, yLocal, evenValue: 0x06, oddValue: 0x07);
        il.Emit(OpCodes.Stelem_I1);
        EmitCopyCoord(il, xLocal, resultLocal, offsetConst: 1);
        EmitCopyCoordAtFieldOffset(il, yLocal, resultLocal);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // compressed: result = new byte[1 + fieldLen]; result[0] = parity prefix; copy x
        il.MarkLabel(compressedLabel);
        EmitAllocPointBuffer(il, resultLocal, doubled: false);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        EmitYParityPrefix(il, yLocal, evenValue: 0x02, oddValue: 0x03);
        il.Emit(OpCodes.Stelem_I1);
        EmitCopyCoord(il, xLocal, resultLocal, offsetConst: 1);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        // invalid format
        il.MarkLabel(invalidLabel);
        il.Emit(OpCodes.Ldstr, "Invalid point format (expected 'uncompressed', 'compressed', or 'hybrid')");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ArgumentException, [_types.String])!);
        il.Emit(OpCodes.Throw);
    }

    // result = new byte[ (doubled ? 1+2*fieldLen : 1+fieldLen) ]
    private void EmitAllocPointBuffer(ILGenerator il, LocalBuilder resultLocal, bool doubled)
    {
        il.Emit(OpCodes.Ldarg_2);      // fieldLen
        if (doubled)
        {
            il.Emit(OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Mul);
        }
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, resultLocal);
    }

    // Array.Copy(coord, 0, result, offsetConst, fieldLen)
    private void EmitCopyCoord(ILGenerator il, LocalBuilder coordLocal, LocalBuilder resultLocal, int offsetConst)
    {
        il.Emit(OpCodes.Ldloc, coordLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4, offsetConst);
        il.Emit(OpCodes.Ldarg_2);              // fieldLen
        il.Emit(OpCodes.Call, _types.ArrayCopy5);
    }

    // Array.Copy(coord, 0, result, 1 + fieldLen, fieldLen)
    private void EmitCopyCoordAtFieldOffset(ILGenerator il, LocalBuilder coordLocal, LocalBuilder resultLocal)
    {
        il.Emit(OpCodes.Ldloc, coordLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Add);                  // 1 + fieldLen
        il.Emit(OpCodes.Ldarg_2);              // fieldLen
        il.Emit(OpCodes.Call, _types.ArrayCopy5);
    }

    // Push (byte)((y[y.Length-1] & 1) == 0 ? evenValue : oddValue)
    private void EmitYParityPrefix(ILGenerator il, LocalBuilder yLocal, int evenValue, int oddValue)
    {
        var oddLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, yLocal);
        il.Emit(OpCodes.Ldloc, yLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brtrue, oddLabel);
        il.Emit(OpCodes.Ldc_I4, evenValue);
        il.Emit(OpCodes.Br, doneLabel);
        il.MarkLabel(oddLabel);
        il.Emit(OpCodes.Ldc_I4, oddValue);
        il.MarkLabel(doneLabel);
    }
}
