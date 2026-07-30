using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;

namespace SharpTS.Compilation;

/// <summary>
/// Emits generatePrime(Sync)/checkPrime(Sync) as pure-IL Miller-Rabin over
/// System.Numerics.BigInteger (#1062). Standalone — no SharpTS.dll reference.
/// </summary>
/// <remarks>
/// NOTE: Must stay in sync with Runtime/Types/CryptoPrimes.cs. The heavy math is
/// emitted onto the $Runtime type: CryptoIsProbablyPrime(BigInteger, int) → bool
/// and CryptoGeneratePrimeCore(int, bool) → BigInteger. The wrappers convert to
/// $Buffer/bigint and bridge callbacks.
/// </remarks>
public partial class RuntimeEmitter
{
    private static readonly int[] _smallPrimes =
        [2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71];

    private void EmitCryptoPrimeHelpers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitCryptoIsProbablyPrime(typeBuilder, runtime);
        EmitCryptoGeneratePrimeCore(typeBuilder, runtime);
        EmitCryptoGeneratePrimeSyncObj(typeBuilder, runtime);
        EmitCryptoCheckPrimeSyncObj(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits: public static object CryptoGeneratePrimeSyncObj(int bits, object options)
    /// → $Buffer (big-endian) or boxed BigInteger when { bigint: true } (#1062).
    /// </summary>
    private void EmitCryptoGeneratePrimeSyncObj(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoGeneratePrimeSyncObj",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Int32, _types.Object]);
        runtime.CryptoGeneratePrimeSyncObj = method;

        var il = method.GetILGenerator();
        var biType = _types.BigInteger;
        var primeLoc = il.DeclareLocal(biType);

        // safe = GetOptionBool(options, "safe"); (reuse a small inline $Object probe)
        var safeLoc = il.DeclareLocal(_types.Boolean);
        EmitReadBoolOption(il, runtime, "safe", safeLoc);
        var bigintLoc = il.DeclareLocal(_types.Boolean);
        EmitReadBoolOption(il, runtime, "bigint", bigintLoc);

        // prime = CryptoGeneratePrimeCore(bits, safe)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, safeLoc);
        il.Emit(OpCodes.Call, runtime.CryptoGeneratePrimeCore);
        il.Emit(OpCodes.Stloc, primeLoc);

        // if (bigint) return (object)prime (boxed BigInteger)
        var notBigintLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, bigintLoc);
        il.Emit(OpCodes.Brfalse, notBigintLabel);
        il.Emit(OpCodes.Ldloc, primeLoc);
        il.Emit(OpCodes.Box, biType);
        il.Emit(OpCodes.Ret);

        // else return new $Buffer(prime.ToByteArray(true, true))
        il.MarkLabel(notBigintLabel);
        il.Emit(OpCodes.Ldloca, primeLoc);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, biType.GetMethod("ToByteArray", [typeof(bool), typeof(bool)])!);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoCheckPrimeSyncObj(object candidate, object options) → boxed bool.
    /// candidate may be a boxed BigInteger (bigint) or a $Buffer (big-endian magnitude).
    /// </summary>
    private void EmitCryptoCheckPrimeSyncObj(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoCheckPrimeSyncObj",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        runtime.CryptoCheckPrimeSyncObj = method;

        var il = method.GetILGenerator();
        var biType = _types.BigInteger;
        var candLoc = il.DeclareLocal(biType);
        var checksLoc = il.DeclareLocal(_types.Int32);

        // checks = GetOptionInt(options, "checks", 0)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "checks");
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, runtime.GetOptionInt);
        il.Emit(OpCodes.Stloc, checksLoc);

        // if (candidate is BigInteger) cand = (BigInteger)candidate
        var notBigLabel = il.DefineLabel();
        var haveCandLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, biType);
        il.Emit(OpCodes.Brfalse, notBigLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, biType);
        il.Emit(OpCodes.Stloc, candLoc);
        il.Emit(OpCodes.Br, haveCandLabel);

        // else if $Buffer: cand = new BigInteger(buffer.Data, isUnsigned:true, isBigEndian:true)
        il.MarkLabel(notBigLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSBufferType);
        var throwLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, throwLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSBufferType);
        il.Emit(OpCodes.Call, runtime.TSBufferGetData);
        // byte[] → ReadOnlySpan<byte> (implicit), then BigInteger(ROS<byte>, isUnsigned:true, isBigEndian:true)
        il.Emit(OpCodes.Call, _types.GetMethod(_types.ReadOnlySpanOfByte, "op_Implicit", [typeof(byte[])])!);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newobj, biType.GetConstructor([_types.ReadOnlySpanOfByte, typeof(bool), typeof(bool)])!);
        il.Emit(OpCodes.Stloc, candLoc);
        il.Emit(OpCodes.Br, haveCandLabel);

        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "crypto.checkPrimeSync: candidate must be a bigint or Buffer");
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(haveCandLabel);
        il.Emit(OpCodes.Ldloc, candLoc);
        il.Emit(OpCodes.Ldloc, checksLoc);
        il.Emit(OpCodes.Call, runtime.CryptoIsProbablyPrime);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Inline: read a boolean $Object option into a local (false if absent).</summary>
    private void EmitReadBoolOption(ILGenerator il, EmittedRuntime runtime, string key, LocalBuilder dest)
    {
        var doneLabel = il.DefineLabel();
        var valLoc = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, dest);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, doneLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldstr, key);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetProperty);
        il.Emit(OpCodes.Stloc, valLoc);
        il.Emit(OpCodes.Ldloc, valLoc);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, doneLabel);
        il.Emit(OpCodes.Ldloc, valLoc);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Stloc, dest);
        il.MarkLabel(doneLabel);
    }

    private static MethodInfo BI(string name, params Type[] args) =>
        typeof(BigInteger).GetMethod(name, args)!;

    private static MethodInfo BIOp(string op, params Type[] args) =>
        typeof(BigInteger).GetMethod(op, BindingFlags.Public | BindingFlags.Static, args)!;

    /// <summary>Emits: public static bool CryptoIsProbablyPrime(BigInteger n, int checks)</summary>
    private void EmitCryptoIsProbablyPrime(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoIsProbablyPrime",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.BigInteger, _types.Int32]);
        runtime.CryptoIsProbablyPrime = method;

        var il = method.GetILGenerator();
        var biType = _types.BigInteger;
        var nLoc = il.DeclareLocal(biType);
        var dLoc = il.DeclareLocal(biType);
        var rLoc = il.DeclareLocal(_types.Int32);
        var xLoc = il.DeclareLocal(biType);
        var aLoc = il.DeclareLocal(biType);
        var iLoc = il.DeclareLocal(_types.Int32);
        var jLoc = il.DeclareLocal(_types.Int32);
        var checksLoc = il.DeclareLocal(_types.Int32);
        var byteLenLoc = il.DeclareLocal(_types.Int32);
        var tmpBytesLoc = il.DeclareLocal(_types.ByteArray);

        var opLt = BIOp("op_LessThan", biType, biType);
        var opGt = BIOp("op_GreaterThan", biType, biType);
        var opEq = BIOp("op_Equality", biType, biType);
        var opMod = BIOp("op_Modulus", biType, biType);
        var opDiv = BIOp("op_Division", biType, biType);
        var opSub = BIOp("op_Subtraction", biType, biType);
        var opAdd = BIOp("op_Addition", biType, biType);
        var opImplicitInt = biType.GetMethod("op_Implicit", [typeof(int)])!;
        var modPow = BI("ModPow", biType, biType, biType);
        var isEven = biType.GetProperty("IsEven")!.GetGetMethod()!;

        // n = arg0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, nLoc);

        // checks = arg1 <= 0 ? 20 : arg1
        var checksOkLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bgt, checksOkLabel);
        il.Emit(OpCodes.Ldc_I4, 20);
        il.Emit(OpCodes.Stloc, checksLoc);
        var afterChecksLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, afterChecksLabel);
        il.MarkLabel(checksOkLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, checksLoc);
        il.MarkLabel(afterChecksLabel);

        // if (n < 2) return false
        var notLtTwoLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, nLoc);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Call, opImplicitInt);
        il.Emit(OpCodes.Call, opLt);
        il.Emit(OpCodes.Brfalse, notLtTwoLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notLtTwoLabel);

        // foreach small prime sp: if (n % sp == 0) return n == sp
        foreach (var sp in _smallPrimes)
        {
            var notDivLabel = il.DefineLabel();
            // n % sp
            il.Emit(OpCodes.Ldloc, nLoc);
            il.Emit(OpCodes.Ldc_I4, sp);
            il.Emit(OpCodes.Call, opImplicitInt);
            il.Emit(OpCodes.Call, opMod);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, opImplicitInt);
            il.Emit(OpCodes.Call, opEq);
            il.Emit(OpCodes.Brfalse, notDivLabel);
            // return n == sp
            il.Emit(OpCodes.Ldloc, nLoc);
            il.Emit(OpCodes.Ldc_I4, sp);
            il.Emit(OpCodes.Call, opImplicitInt);
            il.Emit(OpCodes.Call, opEq);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notDivLabel);
        }

        // d = n - 1; r = 0; while (d.IsEven) { d /= 2; r++; }
        il.Emit(OpCodes.Ldloc, nLoc);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, opImplicitInt);
        il.Emit(OpCodes.Call, opSub);
        il.Emit(OpCodes.Stloc, dLoc);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, rLoc);

        var evenLoopLabel = il.DefineLabel();
        var evenDoneLabel = il.DefineLabel();
        il.MarkLabel(evenLoopLabel);
        il.Emit(OpCodes.Ldloca, dLoc);
        il.Emit(OpCodes.Call, isEven);
        il.Emit(OpCodes.Brfalse, evenDoneLabel);
        il.Emit(OpCodes.Ldloc, dLoc);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Call, opImplicitInt);
        il.Emit(OpCodes.Call, opDiv);
        il.Emit(OpCodes.Stloc, dLoc);
        il.Emit(OpCodes.Ldloc, rLoc);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, rLoc);
        il.Emit(OpCodes.Br, evenLoopLabel);
        il.MarkLabel(evenDoneLabel);

        // byteLen = n.GetByteCount(false)
        il.Emit(OpCodes.Ldloca, nLoc);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, biType.GetMethod("GetByteCount", [typeof(bool)])!);
        il.Emit(OpCodes.Stloc, byteLenLoc);

        // for (i = 0; i < checks; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLoc);
        var outerCondLabel = il.DefineLabel();
        var outerBodyLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, outerCondLabel);
        il.MarkLabel(outerBodyLabel);

        // bytes = RandomNumberGenerator.GetBytes(byteLen); bytes[^1] &= 0x7f
        // (RandomNumberGenerator has no Fill(byte[]) overload — only Fill(Span<byte>))
        il.Emit(OpCodes.Ldloc, byteLenLoc);
        il.Emit(OpCodes.Call, _types.RandomNumberGeneratorGetBytes);
        il.Emit(OpCodes.Stloc, tmpBytesLoc);
        // bytes[len-1] &= 0x7f
        il.Emit(OpCodes.Ldloc, tmpBytesLoc);
        il.Emit(OpCodes.Ldloc, byteLenLoc);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldloc, tmpBytesLoc);
        il.Emit(OpCodes.Ldloc, byteLenLoc);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4, 0x7f);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Stelem_I1);

        // a = new BigInteger(bytes) % (n - 3) + 2
        il.Emit(OpCodes.Ldloc, tmpBytesLoc);
        il.Emit(OpCodes.Newobj, biType.GetConstructor([typeof(byte[])])!);
        il.Emit(OpCodes.Ldloc, nLoc);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Call, opImplicitInt);
        il.Emit(OpCodes.Call, opSub);
        il.Emit(OpCodes.Call, opMod);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Call, opImplicitInt);
        il.Emit(OpCodes.Call, opAdd);
        il.Emit(OpCodes.Stloc, aLoc);

        // x = ModPow(a, d, n)
        il.Emit(OpCodes.Ldloc, aLoc);
        il.Emit(OpCodes.Ldloc, dLoc);
        il.Emit(OpCodes.Ldloc, nLoc);
        il.Emit(OpCodes.Call, modPow);
        il.Emit(OpCodes.Stloc, xLoc);

        // if (x == 1 || x == n-1) continue
        var continueLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, xLoc);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, opImplicitInt);
        il.Emit(OpCodes.Call, opEq);
        il.Emit(OpCodes.Brtrue, continueLabel);
        il.Emit(OpCodes.Ldloc, xLoc);
        il.Emit(OpCodes.Ldloc, nLoc);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, opImplicitInt);
        il.Emit(OpCodes.Call, opSub);
        il.Emit(OpCodes.Call, opEq);
        il.Emit(OpCodes.Brtrue, continueLabel);

        // inner: for (j = 0; j < r-1; j++) { x = ModPow(x,2,n); if (x == n-1) goto continue; }
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, jLoc);
        var innerCondLabel = il.DefineLabel();
        var innerBodyLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, innerCondLabel);
        il.MarkLabel(innerBodyLabel);
        il.Emit(OpCodes.Ldloc, xLoc);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Call, opImplicitInt);
        il.Emit(OpCodes.Ldloc, nLoc);
        il.Emit(OpCodes.Call, modPow);
        il.Emit(OpCodes.Stloc, xLoc);
        il.Emit(OpCodes.Ldloc, xLoc);
        il.Emit(OpCodes.Ldloc, nLoc);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, opImplicitInt);
        il.Emit(OpCodes.Call, opSub);
        il.Emit(OpCodes.Call, opEq);
        il.Emit(OpCodes.Brtrue, continueLabel);
        il.Emit(OpCodes.Ldloc, jLoc);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, jLoc);
        il.MarkLabel(innerCondLabel);
        il.Emit(OpCodes.Ldloc, jLoc);
        il.Emit(OpCodes.Ldloc, rLoc);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Blt, innerBodyLabel);

        // fell through inner loop without hitting n-1 → composite → return false
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(continueLabel);
        il.Emit(OpCodes.Ldloc, iLoc);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLoc);
        il.MarkLabel(outerCondLabel);
        il.Emit(OpCodes.Ldloc, iLoc);
        il.Emit(OpCodes.Ldloc, checksLoc);
        il.Emit(OpCodes.Blt, outerBodyLabel);

        // all witnesses passed → probably prime
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emits: public static BigInteger CryptoGeneratePrimeCore(int bits, bool safe)</summary>
    private void EmitCryptoGeneratePrimeCore(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoGeneratePrimeCore",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.BigInteger,
            [_types.Int32, _types.Boolean]);
        runtime.CryptoGeneratePrimeCore = method;

        var il = method.GetILGenerator();
        var biType = _types.BigInteger;
        var byteCountLoc = il.DeclareLocal(_types.Int32);
        var bytesLoc = il.DeclareLocal(_types.ByteArray);
        var candLoc = il.DeclareLocal(biType);
        var topBitLoc = il.DeclareLocal(_types.Int32);
        var topByteLoc = il.DeclareLocal(_types.Int32);

        var opSub = BIOp("op_Subtraction", biType, biType);
        var opDiv = BIOp("op_Division", biType, biType);
        var opImplicitInt = biType.GetMethod("op_Implicit", [typeof(int)])!;

        // if (bits < 2) throw
        var okLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Bge, okLabel);
        il.Emit(OpCodes.Ldstr, "generatePrime: size must be at least 2 bits");
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(okLabel);

        // byteCount = (bits + 7) / 8
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_7);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Stloc, byteCountLoc);

        // topBit = (bits-1) % 8; topByte = byteCount-1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Rem);
        il.Emit(OpCodes.Stloc, topBitLoc);
        il.Emit(OpCodes.Ldloc, byteCountLoc);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, topByteLoc);

        // bytes = new byte[byteCount + 1]
        il.Emit(OpCodes.Ldloc, byteCountLoc);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, bytesLoc);

        var loopLabel = il.DefineLabel();
        il.MarkLabel(loopLabel);

        // Array.Copy(RandomNumberGenerator.GetBytes(byteCount), 0, bytes, 0, byteCount)
        // (AsSpan is generic → not resolvable by concrete arg types, so avoid Fill(Span))
        il.Emit(OpCodes.Ldloc, byteCountLoc);
        il.Emit(OpCodes.Call, _types.RandomNumberGeneratorGetBytes);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, bytesLoc);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, byteCountLoc);
        il.Emit(OpCodes.Call, _types.ArrayCopy5);

        // bytes[byteCount] = 0
        il.Emit(OpCodes.Ldloc, bytesLoc);
        il.Emit(OpCodes.Ldloc, byteCountLoc);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stelem_I1);

        // bytes[0] |= 1 (odd)
        il.Emit(OpCodes.Ldloc, bytesLoc);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, bytesLoc);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Stelem_I1);

        // bytes[topByte] &= (1 << (topBit+1)) - 1
        il.Emit(OpCodes.Ldloc, bytesLoc);
        il.Emit(OpCodes.Ldloc, topByteLoc);
        il.Emit(OpCodes.Ldloc, bytesLoc);
        il.Emit(OpCodes.Ldloc, topByteLoc);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, topBitLoc);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Stelem_I1);

        // bytes[topByte] |= 1 << topBit
        il.Emit(OpCodes.Ldloc, bytesLoc);
        il.Emit(OpCodes.Ldloc, topByteLoc);
        il.Emit(OpCodes.Ldloc, bytesLoc);
        il.Emit(OpCodes.Ldloc, topByteLoc);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, topBitLoc);
        il.Emit(OpCodes.Shl);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Conv_U1);
        il.Emit(OpCodes.Stelem_I1);

        // cand = new BigInteger(bytes)
        il.Emit(OpCodes.Ldloc, bytesLoc);
        il.Emit(OpCodes.Newobj, biType.GetConstructor([typeof(byte[])])!);
        il.Emit(OpCodes.Stloc, candLoc);

        // if (!CryptoIsProbablyPrime(cand, 20)) goto loop
        il.Emit(OpCodes.Ldloc, candLoc);
        il.Emit(OpCodes.Ldc_I4, 20);
        il.Emit(OpCodes.Call, runtime.CryptoIsProbablyPrime);
        il.Emit(OpCodes.Brfalse, loopLabel);

        // if (safe && !CryptoIsProbablyPrime((cand-1)/2, 20)) goto loop
        var acceptLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, acceptLabel);
        il.Emit(OpCodes.Ldloc, candLoc);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, opImplicitInt);
        il.Emit(OpCodes.Call, opSub);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Call, opImplicitInt);
        il.Emit(OpCodes.Call, opDiv);
        il.Emit(OpCodes.Ldc_I4, 20);
        il.Emit(OpCodes.Call, runtime.CryptoIsProbablyPrime);
        il.Emit(OpCodes.Brfalse, loopLabel);

        il.MarkLabel(acceptLabel);
        il.Emit(OpCodes.Ldloc, candLoc);
        il.Emit(OpCodes.Ret);
    }
}
