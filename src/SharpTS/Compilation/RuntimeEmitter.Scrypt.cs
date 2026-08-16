using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the scrypt key derivation function as pure IL for standalone execution.
/// The scrypt algorithm is memory-hard and uses:
/// - PBKDF2-HMAC-SHA256 for initial key derivation
/// - ROMix for memory-hard mixing (uses BlockMix with Salsa20/8)
/// - PBKDF2-HMAC-SHA256 for final key derivation
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits all scrypt-related methods.
    /// Must be called during runtime class emission.
    /// </summary>
    private void EmitScryptMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Emit helper methods first (order matters due to dependencies)
        // RotateLeft is used by Salsa20Core
        EmitScryptRotateLeft(typeBuilder, runtime);
        // Salsa20Core is used by BlockMix
        EmitScryptSalsa20Core(typeBuilder, runtime);
        // BlockMix is used by ROMix
        EmitScryptBlockMix(typeBuilder, runtime);
        // ROMix is used by DeriveBytes
        EmitScryptROMix(typeBuilder, runtime);
        // DeriveBytes is the main entry point
        EmitScryptDeriveBytesImpl(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits: private static uint RotateLeft(uint value, int count)
    /// Returns (value &lt;&lt; count) | (value >> (32 - count))
    /// </summary>
    private void EmitScryptRotateLeft(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ScryptRotateLeft",
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(uint),
            [typeof(uint), _types.Int32]);
        runtime.ScryptRotateLeft = method;

        var il = method.GetILGenerator();

        // return (value << count) | (value >> (32 - count))
        il.Emit(OpCodes.Ldarg_0);  // value
        il.Emit(OpCodes.Ldarg_1);  // count
        il.Emit(OpCodes.Shl);      // value << count

        il.Emit(OpCodes.Ldarg_0);  // value
        il.Emit(OpCodes.Ldc_I4, 32);
        il.Emit(OpCodes.Ldarg_1);  // count
        il.Emit(OpCodes.Sub);      // 32 - count
        il.Emit(OpCodes.Shr_Un);   // value >> (32 - count)

        il.Emit(OpCodes.Or);       // combine
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: private static void Salsa20Core(byte[] block)
    /// Applies the Salsa20/8 core function in-place on a 64-byte block.
    /// </summary>
    private void EmitScryptSalsa20Core(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ScryptSalsa20Core",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Void,
            [_types.ByteArray]);
        runtime.ScryptSalsa20Core = method;

        var il = method.GetILGenerator();

        // Local variables
        var xLocal = il.DeclareLocal(typeof(uint[]));      // uint[16] working array
        var originalLocal = il.DeclareLocal(typeof(uint[])); // uint[16] original copy
        var iLocal = il.DeclareLocal(_types.Int32);        // loop counter

        // x = new uint[16]
        il.Emit(OpCodes.Ldc_I4, 16);
        il.Emit(OpCodes.Newarr, typeof(uint));
        il.Emit(OpCodes.Stloc, xLocal);

        // Convert bytes to uint32 array (little-endian)
        // for (int i = 0; i < 16; i++) x[i] = BitConverter.ToUInt32(block, i * 4);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var convertLoopStart = il.DefineLabel();
        var convertLoopEnd = il.DefineLabel();
        il.MarkLabel(convertLoopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4, 16);
        il.Emit(OpCodes.Bge, convertLoopEnd);

        // x[i] = BitConverter.ToUInt32(block, i * 4)
        il.Emit(OpCodes.Ldloc, xLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);  // block
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("ToUInt32", [typeof(byte[]), _types.Int32])!);
        il.Emit(OpCodes.Stelem_I4);

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, convertLoopStart);
        il.MarkLabel(convertLoopEnd);

        // original = (uint[])x.Clone()
        il.Emit(OpCodes.Ldloc, xLocal);
        il.Emit(OpCodes.Callvirt, typeof(Array).GetMethod("Clone")!);
        il.Emit(OpCodes.Castclass, typeof(uint[]));
        il.Emit(OpCodes.Stloc, originalLocal);

        // 8 rounds (4 double-rounds)
        // for (int i = 0; i < 4; i++) { ... }
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var roundLoopStart = il.DefineLabel();
        var roundLoopEnd = il.DefineLabel();
        il.MarkLabel(roundLoopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Bge, roundLoopEnd);

        // Column round - emit all 16 operations
        // x[4] ^= RotateLeft(x[0] + x[12], 7);
        EmitSalsa20Op(il, xLocal, runtime, 4, 0, 12, 7);
        // x[8] ^= RotateLeft(x[4] + x[0], 9);
        EmitSalsa20Op(il, xLocal, runtime, 8, 4, 0, 9);
        // x[12] ^= RotateLeft(x[8] + x[4], 13);
        EmitSalsa20Op(il, xLocal, runtime, 12, 8, 4, 13);
        // x[0] ^= RotateLeft(x[12] + x[8], 18);
        EmitSalsa20Op(il, xLocal, runtime, 0, 12, 8, 18);

        // x[9] ^= RotateLeft(x[5] + x[1], 7);
        EmitSalsa20Op(il, xLocal, runtime, 9, 5, 1, 7);
        // x[13] ^= RotateLeft(x[9] + x[5], 9);
        EmitSalsa20Op(il, xLocal, runtime, 13, 9, 5, 9);
        // x[1] ^= RotateLeft(x[13] + x[9], 13);
        EmitSalsa20Op(il, xLocal, runtime, 1, 13, 9, 13);
        // x[5] ^= RotateLeft(x[1] + x[13], 18);
        EmitSalsa20Op(il, xLocal, runtime, 5, 1, 13, 18);

        // x[14] ^= RotateLeft(x[10] + x[6], 7);
        EmitSalsa20Op(il, xLocal, runtime, 14, 10, 6, 7);
        // x[2] ^= RotateLeft(x[14] + x[10], 9);
        EmitSalsa20Op(il, xLocal, runtime, 2, 14, 10, 9);
        // x[6] ^= RotateLeft(x[2] + x[14], 13);
        EmitSalsa20Op(il, xLocal, runtime, 6, 2, 14, 13);
        // x[10] ^= RotateLeft(x[6] + x[2], 18);
        EmitSalsa20Op(il, xLocal, runtime, 10, 6, 2, 18);

        // x[3] ^= RotateLeft(x[15] + x[11], 7);
        EmitSalsa20Op(il, xLocal, runtime, 3, 15, 11, 7);
        // x[7] ^= RotateLeft(x[3] + x[15], 9);
        EmitSalsa20Op(il, xLocal, runtime, 7, 3, 15, 9);
        // x[11] ^= RotateLeft(x[7] + x[3], 13);
        EmitSalsa20Op(il, xLocal, runtime, 11, 7, 3, 13);
        // x[15] ^= RotateLeft(x[11] + x[7], 18);
        EmitSalsa20Op(il, xLocal, runtime, 15, 11, 7, 18);

        // Row round
        // x[1] ^= RotateLeft(x[0] + x[3], 7);
        EmitSalsa20Op(il, xLocal, runtime, 1, 0, 3, 7);
        // x[2] ^= RotateLeft(x[1] + x[0], 9);
        EmitSalsa20Op(il, xLocal, runtime, 2, 1, 0, 9);
        // x[3] ^= RotateLeft(x[2] + x[1], 13);
        EmitSalsa20Op(il, xLocal, runtime, 3, 2, 1, 13);
        // x[0] ^= RotateLeft(x[3] + x[2], 18);
        EmitSalsa20Op(il, xLocal, runtime, 0, 3, 2, 18);

        // x[6] ^= RotateLeft(x[5] + x[4], 7);
        EmitSalsa20Op(il, xLocal, runtime, 6, 5, 4, 7);
        // x[7] ^= RotateLeft(x[6] + x[5], 9);
        EmitSalsa20Op(il, xLocal, runtime, 7, 6, 5, 9);
        // x[4] ^= RotateLeft(x[7] + x[6], 13);
        EmitSalsa20Op(il, xLocal, runtime, 4, 7, 6, 13);
        // x[5] ^= RotateLeft(x[4] + x[7], 18);
        EmitSalsa20Op(il, xLocal, runtime, 5, 4, 7, 18);

        // x[11] ^= RotateLeft(x[10] + x[9], 7);
        EmitSalsa20Op(il, xLocal, runtime, 11, 10, 9, 7);
        // x[8] ^= RotateLeft(x[11] + x[10], 9);
        EmitSalsa20Op(il, xLocal, runtime, 8, 11, 10, 9);
        // x[9] ^= RotateLeft(x[8] + x[11], 13);
        EmitSalsa20Op(il, xLocal, runtime, 9, 8, 11, 13);
        // x[10] ^= RotateLeft(x[9] + x[8], 18);
        EmitSalsa20Op(il, xLocal, runtime, 10, 9, 8, 18);

        // x[12] ^= RotateLeft(x[15] + x[14], 7);
        EmitSalsa20Op(il, xLocal, runtime, 12, 15, 14, 7);
        // x[13] ^= RotateLeft(x[12] + x[15], 9);
        EmitSalsa20Op(il, xLocal, runtime, 13, 12, 15, 9);
        // x[14] ^= RotateLeft(x[13] + x[12], 13);
        EmitSalsa20Op(il, xLocal, runtime, 14, 13, 12, 13);
        // x[15] ^= RotateLeft(x[14] + x[13], 18);
        EmitSalsa20Op(il, xLocal, runtime, 15, 14, 13, 18);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, roundLoopStart);
        il.MarkLabel(roundLoopEnd);

        // Add original to result: for (int i = 0; i < 16; i++) x[i] += original[i];
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var addLoopStart = il.DefineLabel();
        var addLoopEnd = il.DefineLabel();
        il.MarkLabel(addLoopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4, 16);
        il.Emit(OpCodes.Bge, addLoopEnd);

        // x[i] += original[i]
        il.Emit(OpCodes.Ldloc, xLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, xLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_U4);
        il.Emit(OpCodes.Ldloc, originalLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_U4);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stelem_I4);

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, addLoopStart);
        il.MarkLabel(addLoopEnd);

        // Convert back to bytes: for (int i = 0; i < 16; i++) { bytes = BitConverter.GetBytes(x[i]); Array.Copy(...) }
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var copyLoopStart = il.DefineLabel();
        var copyLoopEnd = il.DefineLabel();
        var bytesLocal = il.DeclareLocal(_types.ByteArray);
        il.MarkLabel(copyLoopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4, 16);
        il.Emit(OpCodes.Bge, copyLoopEnd);

        // bytes = BitConverter.GetBytes(x[i])
        il.Emit(OpCodes.Ldloc, xLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_U4);
        il.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("GetBytes", [typeof(uint)])!);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // Array.Copy(bytes, 0, block, i * 4, 4)
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);  // block
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy", [typeof(Array), _types.Int32, typeof(Array), _types.Int32, _types.Int32])!);

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, copyLoopStart);
        il.MarkLabel(copyLoopEnd);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Helper to emit a single Salsa20 quarter-round operation.
    /// Emits: x[target] ^= RotateLeft(x[a] + x[b], shift)
    /// </summary>
    private void EmitSalsa20Op(ILGenerator il, LocalBuilder xLocal, EmittedRuntime runtime, int target, int a, int b, int shift)
    {
        // x[target] ^= RotateLeft(x[a] + x[b], shift)
        il.Emit(OpCodes.Ldloc, xLocal);
        il.Emit(OpCodes.Ldc_I4, target);

        // Load current x[target] for XOR
        il.Emit(OpCodes.Ldloc, xLocal);
        il.Emit(OpCodes.Ldc_I4, target);
        il.Emit(OpCodes.Ldelem_U4);

        // RotateLeft(x[a] + x[b], shift)
        il.Emit(OpCodes.Ldloc, xLocal);
        il.Emit(OpCodes.Ldc_I4, a);
        il.Emit(OpCodes.Ldelem_U4);
        il.Emit(OpCodes.Ldloc, xLocal);
        il.Emit(OpCodes.Ldc_I4, b);
        il.Emit(OpCodes.Ldelem_U4);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4, shift);
        il.Emit(OpCodes.Call, runtime.ScryptRotateLeft);

        // XOR and store
        il.Emit(OpCodes.Xor);
        il.Emit(OpCodes.Stelem_I4);
    }

    /// <summary>
    /// Emits: private static void ScryptBlockMix(byte[] B, int r)
    /// Applies the scrypt BlockMix function in-place.
    /// </summary>
    private void EmitScryptBlockMix(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ScryptBlockMix",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Void,
            [_types.ByteArray, _types.Int32]);
        runtime.ScryptBlockMix = method;

        var il = method.GetILGenerator();

        // int blockSize = 128 * r;
        var blockSizeLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4, 128);
        il.Emit(OpCodes.Ldarg_1);  // r
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Stloc, blockSizeLocal);

        // byte[] X = new byte[64];
        var XLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Ldc_I4, 64);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, XLocal);

        // byte[] Y = new byte[blockSize];
        var YLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Ldloc, blockSizeLocal);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, YLocal);

        // Array.Copy(B, blockSize - 64, X, 0, 64) - copy last 64-byte block to X
        il.Emit(OpCodes.Ldarg_0);  // B
        il.Emit(OpCodes.Ldloc, blockSizeLocal);
        il.Emit(OpCodes.Ldc_I4, 64);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldloc, XLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4, 64);
        il.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy", [typeof(Array), _types.Int32, typeof(Array), _types.Int32, _types.Int32])!);

        // for (int i = 0; i < 2 * r; i++)
        var iLocal = il.DeclareLocal(_types.Int32);
        var jLocal = il.DeclareLocal(_types.Int32);
        var destOffsetLocal = il.DeclareLocal(_types.Int32);
        var twoRLocal = il.DeclareLocal(_types.Int32);

        // twoR = 2 * r
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Stloc, twoRLocal);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, twoRLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // XOR X with current block: for (int j = 0; j < 64; j++) X[j] ^= B[i * 64 + j];
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, jLocal);
        var xorLoopStart = il.DefineLabel();
        var xorLoopEnd = il.DefineLabel();
        il.MarkLabel(xorLoopStart);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I4, 64);
        il.Emit(OpCodes.Bge, xorLoopEnd);

        // X[j] ^= B[i * 64 + j]
        il.Emit(OpCodes.Ldloc, XLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldloc, XLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldarg_0);  // B
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4, 64);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Xor);
        il.Emit(OpCodes.Stelem_I1);

        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, jLocal);
        il.Emit(OpCodes.Br, xorLoopStart);
        il.MarkLabel(xorLoopEnd);

        // Apply Salsa20/8 core
        il.Emit(OpCodes.Ldloc, XLocal);
        il.Emit(OpCodes.Call, runtime.ScryptSalsa20Core);

        // Calculate destination offset: destOffset = (i / 2) * 64 + (i % 2) * r * 64
        // (i / 2) * 64
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Ldc_I4, 64);
        il.Emit(OpCodes.Mul);
        // (i % 2) * r * 64
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Rem);
        il.Emit(OpCodes.Ldarg_1);  // r
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ldc_I4, 64);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, destOffsetLocal);

        // Array.Copy(X, 0, Y, destOffset, 64)
        il.Emit(OpCodes.Ldloc, XLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, YLocal);
        il.Emit(OpCodes.Ldloc, destOffsetLocal);
        il.Emit(OpCodes.Ldc_I4, 64);
        il.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy", [typeof(Array), _types.Int32, typeof(Array), _types.Int32, _types.Int32])!);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);

        // Array.Copy(Y, 0, B, 0, blockSize)
        il.Emit(OpCodes.Ldloc, YLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);  // B
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, blockSizeLocal);
        il.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy", [typeof(Array), _types.Int32, typeof(Array), _types.Int32, _types.Int32])!);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: private static void ScryptROMix(byte[] B, int N, int r)
    /// Applies the scrypt ROMix function in-place.
    /// </summary>
    private void EmitScryptROMix(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ScryptROMix",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Void,
            [_types.ByteArray, _types.Int32, _types.Int32]);
        runtime.ScryptROMix = method;

        var il = method.GetILGenerator();

        // int blockSize = 128 * r;
        var blockSizeLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4, 128);
        il.Emit(OpCodes.Ldarg_2);  // r
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Stloc, blockSizeLocal);

        // byte[][] V = new byte[N][];
        var VLocal = il.DeclareLocal(typeof(byte[][]));
        il.Emit(OpCodes.Ldarg_1);  // N
        il.Emit(OpCodes.Newarr, _types.ByteArray);
        il.Emit(OpCodes.Stloc, VLocal);

        var iLocal = il.DeclareLocal(_types.Int32);
        var jLocal = il.DeclareLocal(typeof(long));
        var kLocal = il.DeclareLocal(_types.Int32);

        // Step 1: Store intermediate values in V
        // for (int i = 0; i < N; i++) { V[i] = (byte[])B.Clone(); ScryptBlockMix(B, r); }
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var loop1Start = il.DefineLabel();
        var loop1End = il.DefineLabel();
        il.MarkLabel(loop1Start);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_1);  // N
        il.Emit(OpCodes.Bge, loop1End);

        // V[i] = (byte[])B.Clone()
        il.Emit(OpCodes.Ldloc, VLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_0);  // B
        il.Emit(OpCodes.Callvirt, typeof(Array).GetMethod("Clone")!);
        il.Emit(OpCodes.Castclass, _types.ByteArray);
        il.Emit(OpCodes.Stelem_Ref);

        // ScryptBlockMix(B, r)
        il.Emit(OpCodes.Ldarg_0);  // B
        il.Emit(OpCodes.Ldarg_2);  // r
        il.Emit(OpCodes.Call, runtime.ScryptBlockMix);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop1Start);
        il.MarkLabel(loop1End);

        // Step 2: Mix with random lookups
        // for (int i = 0; i < N; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var loop2Start = il.DefineLabel();
        var loop2End = il.DefineLabel();
        var afterNegativeCheck = il.DefineLabel();
        il.MarkLabel(loop2Start);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_1);  // N
        il.Emit(OpCodes.Bge, loop2End);

        // j = BitConverter.ToInt64(B, blockSize - 64) & (N - 1);
        il.Emit(OpCodes.Ldarg_0);  // B
        il.Emit(OpCodes.Ldloc, blockSizeLocal);
        il.Emit(OpCodes.Ldc_I4, 64);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, typeof(BitConverter).GetMethod("ToInt64", [typeof(byte[]), _types.Int32])!);
        il.Emit(OpCodes.Ldarg_1);  // N
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Stloc, jLocal);

        // if (j < 0) j += N;
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I8, 0L);
        il.Emit(OpCodes.Bge, afterNegativeCheck);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldarg_1);  // N
        il.Emit(OpCodes.Conv_I8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, jLocal);
        il.MarkLabel(afterNegativeCheck);

        // XOR B with V[j]: for (int k = 0; k < blockSize; k++) B[k] ^= V[j][k];
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, kLocal);
        var xorLoopStart = il.DefineLabel();
        var xorLoopEnd = il.DefineLabel();
        il.MarkLabel(xorLoopStart);
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldloc, blockSizeLocal);
        il.Emit(OpCodes.Bge, xorLoopEnd);

        // B[k] ^= V[j][k]
        il.Emit(OpCodes.Ldarg_0);  // B
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldarg_0);  // B
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldloc, VLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldelem_Ref);  // V[j]
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldelem_U1);   // V[j][k]
        il.Emit(OpCodes.Xor);
        il.Emit(OpCodes.Stelem_I1);

        // k++
        il.Emit(OpCodes.Ldloc, kLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, kLocal);
        il.Emit(OpCodes.Br, xorLoopStart);
        il.MarkLabel(xorLoopEnd);

        // ScryptBlockMix(B, r)
        il.Emit(OpCodes.Ldarg_0);  // B
        il.Emit(OpCodes.Ldarg_2);  // r
        il.Emit(OpCodes.Call, runtime.ScryptBlockMix);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loop2Start);
        il.MarkLabel(loop2End);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static byte[] ScryptDeriveBytes(byte[] password, byte[] salt, int N, int r, int p, int dkLen)
    /// Main scrypt key derivation function.
    /// </summary>
    private void EmitScryptDeriveBytesImpl(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ScryptDeriveBytes",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ByteArray,
            [_types.ByteArray, _types.ByteArray, _types.Int32, _types.Int32, _types.Int32, _types.Int32]);
        runtime.ScryptDeriveBytes = method;

        var il = method.GetILGenerator();

        // Parameter validation
        var nGeq2Label = il.DefineLabel();
        var nPowerOf2Label = il.DefineLabel();
        var rOkLabel = il.DefineLabel();
        var pOkLabel = il.DefineLabel();

        // Validate N >= 2: if (N < 2) throw
        il.Emit(OpCodes.Ldarg_2);  // N
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Bge, nGeq2Label);
        ThrowArgumentException(il, "N must be a power of 2 greater than 1");
        il.MarkLabel(nGeq2Label);

        // Validate N is power of 2: if ((N & (N - 1)) != 0) throw
        il.Emit(OpCodes.Ldarg_2);  // N
        il.Emit(OpCodes.Ldarg_2);  // N
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Brfalse, nPowerOf2Label);
        ThrowArgumentException(il, "N must be a power of 2 greater than 1");
        il.MarkLabel(nPowerOf2Label);

        // Validate r: if (r < 1) throw
        il.Emit(OpCodes.Ldarg_3);  // r
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bge, rOkLabel);
        ThrowArgumentException(il, "r must be at least 1");
        il.MarkLabel(rOkLabel);

        // Validate p: if (p < 1) throw
        il.Emit(OpCodes.Ldarg, 4);  // p
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bge, pOkLabel);
        ThrowArgumentException(il, "p must be at least 1");
        il.MarkLabel(pOkLabel);

        // int blockSize = 128 * r;
        var blockSizeLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4, 128);
        il.Emit(OpCodes.Ldarg_3);  // r
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Stloc, blockSizeLocal);

        // Step 1: byte[] B = Rfc2898DeriveBytes.Pbkdf2(password, salt, 1, HashAlgorithmName.SHA256, p * blockSize);
        var BLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Ldarg_0);  // password
        il.Emit(OpCodes.Ldarg_1);  // salt
        il.Emit(OpCodes.Ldc_I4_1);  // iterations = 1
        il.Emit(OpCodes.Call, typeof(HashAlgorithmName).GetProperty("SHA256")!.GetGetMethod()!);  // HashAlgorithmName.SHA256
        il.Emit(OpCodes.Ldarg, 4);  // p
        il.Emit(OpCodes.Ldloc, blockSizeLocal);
        il.Emit(OpCodes.Mul);  // p * blockSize
        il.Emit(OpCodes.Call, typeof(Rfc2898DeriveBytes).GetMethod("Pbkdf2",
            [typeof(byte[]), typeof(byte[]), _types.Int32, typeof(HashAlgorithmName), _types.Int32])!);
        il.Emit(OpCodes.Stloc, BLocal);

        // Step 2: Apply scryptROMix to each block
        // for (int i = 0; i < p; i++)
        var iLocal = il.DeclareLocal(_types.Int32);
        var blockLocal = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg, 4);  // p
        il.Emit(OpCodes.Bge, loopEnd);

        // byte[] block = new byte[blockSize];
        il.Emit(OpCodes.Ldloc, blockSizeLocal);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, blockLocal);

        // Array.Copy(B, i * blockSize, block, 0, blockSize);
        il.Emit(OpCodes.Ldloc, BLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, blockSizeLocal);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ldloc, blockLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, blockSizeLocal);
        il.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy", [typeof(Array), _types.Int32, typeof(Array), _types.Int32, _types.Int32])!);

        // ScryptROMix(block, N, r);
        il.Emit(OpCodes.Ldloc, blockLocal);
        il.Emit(OpCodes.Ldarg_2);  // N
        il.Emit(OpCodes.Ldarg_3);  // r
        il.Emit(OpCodes.Call, runtime.ScryptROMix);

        // Array.Copy(block, 0, B, i * blockSize, blockSize);
        il.Emit(OpCodes.Ldloc, blockLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, BLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, blockSizeLocal);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ldloc, blockSizeLocal);
        il.Emit(OpCodes.Call, typeof(Array).GetMethod("Copy", [typeof(Array), _types.Int32, typeof(Array), _types.Int32, _types.Int32])!);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);

        // Step 3: return Rfc2898DeriveBytes.Pbkdf2(password, B, 1, HashAlgorithmName.SHA256, dkLen);
        il.Emit(OpCodes.Ldarg_0);  // password
        il.Emit(OpCodes.Ldloc, BLocal);  // B as salt
        il.Emit(OpCodes.Ldc_I4_1);  // iterations = 1
        il.Emit(OpCodes.Call, typeof(HashAlgorithmName).GetProperty("SHA256")!.GetGetMethod()!);  // HashAlgorithmName.SHA256
        il.Emit(OpCodes.Ldarg, 5);  // dkLen
        il.Emit(OpCodes.Call, typeof(Rfc2898DeriveBytes).GetMethod("Pbkdf2",
            [typeof(byte[]), typeof(byte[]), _types.Int32, typeof(HashAlgorithmName), _types.Int32])!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Helper to emit an ArgumentException throw with a message.
    /// </summary>
    private void ThrowArgumentException(ILGenerator il, string message)
    {
        il.Emit(OpCodes.Ldstr, message);
        il.Emit(OpCodes.Newobj, typeof(ArgumentException).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);
    }
}
