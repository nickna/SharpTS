using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;

namespace SharpTS.Compilation;

/// <summary>
/// WebCrypto (#1063) $Runtime static helpers — the byte[]-level cores behind
/// $SubtleCrypto (see RuntimeEmitter.WebCrypto.Types.cs). All pure BCL, so the
/// standalone constraint holds. Must stay behaviorally in sync with
/// Runtime/Types/SharpTSSubtleCrypto.cs.
/// </summary>
public partial class RuntimeEmitter
{
    // Cross-file references for the $SubtleCrypto/$WebCrypto type emitters.
    private MethodBuilder _wcHashAlg = null!;        // (string) → HashAlgorithmName
    private MethodBuilder _wcMapHash = null!;        // (object) → string (lowercase)
    private MethodBuilder _wcAlgoName = null!;       // (object) → string (UPPER)
    private MethodBuilder _wcParam = null!;          // (object, string) → object (undefined→null)
    private MethodBuilder _wcIntParam = null!;       // (object, int) → int
    private MethodBuilder _wcToBytes = null!;        // (object) → byte[]
    private MethodBuilder _wcToArrayBuffer = null!;  // (byte[]) → object
    private MethodBuilder _wcResolved = null!;       // (object) → object ($Promise)
    private MethodBuilder _wcRejected = null!;       // (Exception) → object (rejected $Promise)
    private MethodBuilder _wcThrow = null!;          // (string) → object (throws)
    private MethodBuilder _wcDigest = null!;         // (string, byte[]) → byte[]
    private MethodBuilder _wcHmac = null!;           // (string, byte[], byte[]) → byte[]
    private MethodBuilder _wcDigestLen = null!;      // (string) → int
    private MethodBuilder _wcAesGcm = null!;         // (byte[], byte[], byte[]?, int, byte[], bool) → byte[]
    private MethodBuilder _wcAesCbc = null!;         // (byte[], byte[], byte[], bool) → byte[]
    private MethodBuilder _wcRsaOaep = null!;        // (byte[], bool, string, byte[], bool) → byte[]
    private MethodBuilder _wcRsaSignVerify = null!;  // (byte[], bool, string, bool, byte[], byte[]?) → object
    private MethodBuilder _wcEcdsaSignVerify = null!;// (byte[], bool, string, byte[], byte[]?) → object
    private MethodBuilder _wcPbkdf2 = null!;         // (byte[], byte[], int, string, int) → byte[]
    private MethodBuilder _wcHkdf = null!;           // (string, byte[], int, byte[], byte[]) → byte[]
    private MethodBuilder _wcEcdhDerive = null!;     // (byte[], byte[], int) → byte[]
    private MethodBuilder _wcGenRsa = null!;         // (int) → object[] { spki, pkcs8 }
    private MethodBuilder _wcGenEc = null!;          // (string) → object[] { spki, pkcs8 }
    private MethodBuilder _wcCurve = null!;          // (string canonical) → ECCurve
    private MethodBuilder _wcCanonicalCurve = null!; // (object) → string ("P-256"...)
    private MethodBuilder _wcEcRawToSpki = null!;    // (byte[], string) → byte[]
    private MethodBuilder _wcEcSpkiToRaw = null!;    // (byte[]) → byte[]
    private MethodBuilder _wcImportRsaCheck = null!; // (byte[], bool) → int (KeySize)
    private MethodBuilder _wcImportEcCheck = null!;  // (byte[], bool) → void
    private MethodBuilder _wcBase64Url = null!;      // (byte[]) → string

    private static readonly (string Lower, string Web)[] _wcHashes =
        [("sha1", "SHA-1"), ("sha256", "SHA-256"), ("sha384", "SHA-384"), ("sha512", "SHA-512")];

    private MethodInfo WcSpanFromBytes => _types.GetMethod(_types.ReadOnlySpanOfByte, "op_Implicit", [typeof(byte[])])!;

    /// <summary>Emits all WebCrypto byte-level helpers onto $Runtime.</summary>
    private void EmitWebCryptoRuntimeHelpers(TypeBuilder tb, EmittedRuntime runtime)
    {
        EmitWcThrow(tb);
        EmitWcHashAlg(tb);
        EmitWcDigestLen(tb);
        EmitWcMapHash(tb, runtime);
        EmitWcAlgoName(tb, runtime);
        EmitWcParam(tb, runtime);
        EmitWcIntParam(tb);
        EmitWcToBytes(tb, runtime);
        EmitWcToArrayBuffer(tb, runtime);
        EmitWcResolved(tb, runtime);
        EmitWcDigest(tb);
        EmitWcHmac(tb);
        EmitWcAesGcm(tb);
        EmitWcAesCbc(tb);
        EmitWcRsaOaep(tb);
        EmitWcRsaSignVerify(tb);
        EmitWcEcdsaSignVerify(tb);
        EmitWcPbkdf2(tb);
        EmitWcHkdf(tb);
        EmitWcEcdhDerive(tb);
        EmitWcCurve(tb);
        EmitWcCanonicalCurve(tb, runtime);
        EmitWcGenRsa(tb);
        EmitWcGenEc(tb);
        EmitWcEcRawToSpki(tb);
        EmitWcEcSpkiToRaw(tb);
        EmitWcImportRsaCheck(tb);
        EmitWcImportEcCheck(tb);
        EmitWcBase64Url(tb);
    }

    private MethodBuilder WcDefine(TypeBuilder tb, string name, Type returnType, Type[] args)
        => tb.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, returnType, args);

    /// <summary>Emits: object WcThrow(string message) — throws ArgumentException.</summary>
    private void EmitWcThrow(TypeBuilder tb)
    {
        _wcThrow = WcDefine(tb, "WcThrow", _types.Object, [_types.String]);
        var il = _wcThrow.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>Emits: HashAlgorithmName WcHashAlg(string lower).</summary>
    private void EmitWcHashAlg(TypeBuilder tb)
    {
        _wcHashAlg = WcDefine(tb, "WcHashAlg", typeof(HashAlgorithmName), [_types.String]);
        var il = _wcHashAlg.GetILGenerator();
        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);

        foreach (var (lower, _) in _wcHashes)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, lower);
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Call, typeof(HashAlgorithmName).GetProperty(lower.ToUpperInvariant())!.GetGetMethod()!);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(next);
        }
        il.Emit(OpCodes.Ldstr, "crypto.subtle: unsupported hash '");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "'");
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>Emits: int WcDigestLen(string lower).</summary>
    private void EmitWcDigestLen(TypeBuilder tb)
    {
        _wcDigestLen = WcDefine(tb, "WcDigestLen", _types.Int32, [_types.String]);
        var il = _wcDigestLen.GetILGenerator();
        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);
        (string, int)[] lens = [("sha1", 20), ("sha256", 32), ("sha384", 48), ("sha512", 64)];
        foreach (var (lower, len) in lens)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, lower);
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldc_I4, len);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(next);
        }
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: string WcMapHash(object hashOrName) — 'SHA-256' or { name: 'SHA-256' } → "sha256".
    /// </summary>
    private void EmitWcMapHash(TypeBuilder tb, EmittedRuntime runtime)
    {
        _wcMapHash = WcDefine(tb, "WcMapHash", _types.String, [_types.Object]);
        var il = _wcMapHash.GetILGenerator();
        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);

        var nameLocal = il.DeclareLocal(_types.String);
        var isStringLabel = il.DefineLabel();
        var haveNameLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        // null → throw
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, throwLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, isStringLabel);

        // object form: GetProperty(obj, "name")
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, nameLocal);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Brfalse, throwLabel);
        il.Emit(OpCodes.Br, haveNameLabel);

        il.MarkLabel(isStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, nameLocal);

        il.MarkLabel(haveNameLabel);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToUpperInvariant")!);
        il.Emit(OpCodes.Stloc, nameLocal);

        foreach (var (lower, web) in _wcHashes)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, nameLocal);
            il.Emit(OpCodes.Ldstr, web);
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldstr, lower);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(next);
        }

        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "crypto.subtle: a hash of SHA-1, SHA-256, SHA-384, or SHA-512 is required");
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>Emits: string WcAlgoName(object) — string or { name } → UPPER name.</summary>
    private void EmitWcAlgoName(TypeBuilder tb, EmittedRuntime runtime)
    {
        _wcAlgoName = WcDefine(tb, "WcAlgoName", _types.String, [_types.Object]);
        var il = _wcAlgoName.GetILGenerator();

        var nameLocal = il.DeclareLocal(_types.String);
        var isStringLabel = il.DefineLabel();
        var haveNameLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, throwLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, isStringLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, nameLocal);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Brfalse, throwLabel);
        il.Emit(OpCodes.Br, haveNameLabel);

        il.MarkLabel(isStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Stloc, nameLocal);

        il.MarkLabel(haveNameLabel);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToUpperInvariant")!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "crypto.subtle: algorithm must be a string or { name } object");
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>Emits: object WcParam(object algo, string name) — property read, undefined → null.</summary>
    private void EmitWcParam(TypeBuilder tb, EmittedRuntime runtime)
    {
        _wcParam = WcDefine(tb, "WcParam", _types.Object, [_types.Object, _types.String]);
        var il = _wcParam.GetILGenerator();

        var nullLabel = il.DefineLabel();
        var valueLocal = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Stloc, valueLocal);

        // undefined sentinel → null
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Beq, nullLabel);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emits: int WcIntParam(object boxed, int defaultValue).</summary>
    private void EmitWcIntParam(TypeBuilder tb)
    {
        _wcIntParam = WcDefine(tb, "WcIntParam", _types.Int32, [_types.Object, _types.Int32]);
        var il = _wcIntParam.GetILGenerator();

        var defaultLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, defaultLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, defaultLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(defaultLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: byte[] WcToBytes(object) — string (UTF-8) / $Buffer / $TypedArray view /
    /// $ArrayBuffer / raw byte[].
    /// </summary>
    private void EmitWcToBytes(TypeBuilder tb, EmittedRuntime runtime)
    {
        _wcToBytes = WcDefine(tb, "WcToBytes", _types.ByteArray, [_types.Object]);
        var il = _wcToBytes.GetILGenerator();

        var throwLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, throwLabel);

        // byte[] passthrough
        {
            var notBytes = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, _types.ByteArray);
            il.Emit(OpCodes.Brfalse, notBytes);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.ByteArray);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notBytes);
        }

        // string → UTF-8
        {
            var notString = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, _types.String);
            il.Emit(OpCodes.Brfalse, notString);
            il.Emit(OpCodes.Call, _types.GetProperty(_types.Encoding, "UTF8")!.GetGetMethod()!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.String);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Encoding, "GetBytes", [_types.String])!);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notString);
        }

        // $Buffer → Data
        if (runtime.TSBufferType != null)
        {
            var notBuffer = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSBufferType);
            il.Emit(OpCodes.Brfalse, notBuffer);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSBufferType);
            il.Emit(OpCodes.Call, runtime.TSBufferGetData);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notBuffer);
        }

        // $TypedArray → copy of the view window
        if (runtime.TypedArrayBaseType != null)
        {
            var notTyped = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TypedArrayBaseType);
            il.Emit(OpCodes.Brfalse, notTyped);

            var typedLocal = il.DeclareLocal(runtime.TypedArrayBaseType);
            var lenLocal = il.DeclareLocal(_types.Int32);
            var resultLocal = il.DeclareLocal(_types.ByteArray);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TypedArrayBaseType);
            il.Emit(OpCodes.Stloc, typedLocal);

            il.Emit(OpCodes.Ldloc, typedLocal);
            il.Emit(OpCodes.Callvirt, runtime.TypedArrayByteLengthGetter);
            il.Emit(OpCodes.Stloc, lenLocal);

            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Newarr, _types.Byte);
            il.Emit(OpCodes.Stloc, resultLocal);

            // Array.Copy(src, srcOffset, dst, 0, len)
            il.Emit(OpCodes.Ldloc, typedLocal);
            il.Emit(OpCodes.Callvirt, runtime.TypedArrayGetBuffer);
            il.Emit(OpCodes.Ldloc, typedLocal);
            il.Emit(OpCodes.Callvirt, runtime.TypedArrayByteOffsetGetter);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Call, _types.ArrayCopy5);

            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notTyped);
        }

        // $ArrayBuffer → clone of the backing array
        if (runtime.ArrayBufferType != null)
        {
            var notAb = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.ArrayBufferType);
            il.Emit(OpCodes.Brfalse, notAb);

            var srcLocal = il.DeclareLocal(_types.ByteArray);
            var cloneLocal = il.DeclareLocal(_types.ByteArray);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.ArrayBufferType);
            il.Emit(OpCodes.Callvirt, runtime.ArrayBufferGetBuffer);
            il.Emit(OpCodes.Stloc, srcLocal);

            il.Emit(OpCodes.Ldloc, srcLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Newarr, _types.Byte);
            il.Emit(OpCodes.Stloc, cloneLocal);

            il.Emit(OpCodes.Ldloc, srcLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, cloneLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, srcLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Call, _types.ArrayCopy5);

            il.Emit(OpCodes.Ldloc, cloneLocal);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notAb);
        }

        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "crypto.subtle: expected an ArrayBuffer, TypedArray, Buffer, or string");
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>Emits: object WcToArrayBuffer(byte[]) — new $ArrayBuffer with copied contents.</summary>
    private void EmitWcToArrayBuffer(TypeBuilder tb, EmittedRuntime runtime)
    {
        _wcToArrayBuffer = WcDefine(tb, "WcToArrayBuffer", _types.Object, [_types.ByteArray]);
        var il = _wcToArrayBuffer.GetILGenerator();

        var abLocal = il.DeclareLocal(_types.Object);

        // var ab = new $ArrayBuffer(bytes.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newobj, runtime.ArrayBufferCtor);
        il.Emit(OpCodes.Stloc, abLocal);

        // Array.Copy(bytes, 0, ab.GetBuffer(), 0, bytes.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, abLocal);
        il.Emit(OpCodes.Castclass, runtime.ArrayBufferType!);
        il.Emit(OpCodes.Callvirt, runtime.ArrayBufferGetBuffer);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, _types.ArrayCopy5);

        il.Emit(OpCodes.Ldloc, abLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emits: object WcResolved(object value) — new $Promise(Task.FromResult(value)).</summary>
    private void EmitWcResolved(TypeBuilder tb, EmittedRuntime runtime)
    {
        _wcResolved = WcDefine(tb, "WcResolved", _types.Object, [_types.Object]);
        var il = _wcResolved.GetILGenerator();

        var fromResult = EmitGenerics.MakeGenericMethod(typeof(System.Threading.Tasks.Task)
            .GetMethod("FromResult")!, _types.Object);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Newobj, runtime.TSPromiseCtor);
        il.Emit(OpCodes.Ret);

        // object WcRejected(Exception ex) — new $Promise(Task.FromException(ex)).
        // WebCrypto methods reject rather than throw, which also keeps guest
        // try/catch-around-await working in compiled async bodies.
        _wcRejected = WcDefine(tb, "WcRejected", _types.Object, [typeof(Exception)]);
        var ril = _wcRejected.GetILGenerator();
        var fromException = EmitGenerics.MakeGenericMethod(typeof(System.Threading.Tasks.Task)
            .GetMethod("FromException", 1, [typeof(Exception)])!, _types.Object);
        ril.Emit(OpCodes.Ldarg_0);
        ril.Emit(OpCodes.Call, fromException);
        ril.Emit(OpCodes.Newobj, runtime.TSPromiseCtor);
        ril.Emit(OpCodes.Ret);
    }

    /// <summary>Emits: byte[] WcDigest(string lower, byte[] data).</summary>
    private void EmitWcDigest(TypeBuilder tb)
    {
        _wcDigest = WcDefine(tb, "WcDigest", _types.ByteArray, [_types.String, _types.ByteArray]);
        var il = _wcDigest.GetILGenerator();
        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);

        (string Lower, Type Impl)[] impls =
            [("sha1", typeof(SHA1)), ("sha256", typeof(SHA256)), ("sha384", typeof(SHA384)), ("sha512", typeof(SHA512))];
        foreach (var (lower, impl) in impls)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, lower);
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, impl.GetMethod("HashData", [typeof(byte[])])!);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(next);
        }
        il.Emit(OpCodes.Ldstr, "crypto.subtle.digest: unsupported hash algorithm");
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>Emits: byte[] WcHmac(string lower, byte[] key, byte[] data).</summary>
    private void EmitWcHmac(TypeBuilder tb)
    {
        _wcHmac = WcDefine(tb, "WcHmac", _types.ByteArray, [_types.String, _types.ByteArray, _types.ByteArray]);
        var il = _wcHmac.GetILGenerator();
        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);

        (string Lower, Type Impl)[] impls =
            [("sha1", typeof(HMACSHA1)), ("sha256", typeof(HMACSHA256)), ("sha384", typeof(HMACSHA384)), ("sha512", typeof(HMACSHA512))];
        foreach (var (lower, impl) in impls)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, lower);
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, impl.GetMethod("HashData", [typeof(byte[]), typeof(byte[])])!);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(next);
        }
        il.Emit(OpCodes.Ldstr, "crypto.subtle: unsupported HMAC hash algorithm");
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits: byte[] WcAesGcm(byte[] key, byte[] iv, byte[]? aad, int tagBits, byte[] data, bool encrypt).
    /// WebCrypto layout: encrypt output / decrypt input is ciphertext || tag.
    /// </summary>
    private void EmitWcAesGcm(TypeBuilder tb)
    {
        _wcAesGcm = WcDefine(tb, "WcAesGcm", _types.ByteArray,
            [_types.ByteArray, _types.ByteArray, _types.ByteArray, _types.Int32, _types.ByteArray, _types.Boolean]);
        var il = _wcAesGcm.GetILGenerator();

        var tagLenLocal = il.DeclareLocal(_types.Int32);
        var gcmLocal = il.DeclareLocal(typeof(AesGcm));
        var okLabel = il.DefineLabel();

        // if (tagBits >= 96 && tagBits <= 128 && tagBits % 8 == 0) ok
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldc_I4, 96);
        il.Emit(OpCodes.Blt, DefineThrowTag(il, out var throwTagLabel));
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldc_I4, 128);
        il.Emit(OpCodes.Bgt, throwTagLabel);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Rem);
        il.Emit(OpCodes.Brtrue, throwTagLabel);
        il.Emit(OpCodes.Br, okLabel);

        il.MarkLabel(throwTagLabel);
        il.Emit(OpCodes.Ldstr, "crypto.subtle: tagLength is not supported on this runtime (.NET AesGcm supports 96-128 bits in steps of 8)");
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(okLabel);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Stloc, tagLenLocal);

        // gcm = new AesGcm(key, tagLen)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, tagLenLocal);
        il.Emit(OpCodes.Newobj, typeof(AesGcm).GetConstructor([typeof(byte[]), typeof(int)])!);
        il.Emit(OpCodes.Stloc, gcmLocal);

        var encryptMethod = typeof(AesGcm).GetMethod("Encrypt",
            [typeof(byte[]), typeof(byte[]), typeof(byte[]), typeof(byte[]), typeof(byte[])])!;
        var decryptMethod = typeof(AesGcm).GetMethod("Decrypt",
            [typeof(byte[]), typeof(byte[]), typeof(byte[]), typeof(byte[]), typeof(byte[])])!;

        var decryptLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, 5);
        il.Emit(OpCodes.Brfalse, decryptLabel);

        // --- encrypt ---
        {
            var ctLocal = il.DeclareLocal(_types.ByteArray);
            var tagLocal = il.DeclareLocal(_types.ByteArray);
            var resultLocal = il.DeclareLocal(_types.ByteArray);

            il.Emit(OpCodes.Ldarg, 4);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Newarr, _types.Byte);
            il.Emit(OpCodes.Stloc, ctLocal);

            il.Emit(OpCodes.Ldloc, tagLenLocal);
            il.Emit(OpCodes.Newarr, _types.Byte);
            il.Emit(OpCodes.Stloc, tagLocal);

            // gcm.Encrypt(iv, data, ct, tag, aad)
            il.Emit(OpCodes.Ldloc, gcmLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg, 4);
            il.Emit(OpCodes.Ldloc, ctLocal);
            il.Emit(OpCodes.Ldloc, tagLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, encryptMethod);

            // result = ct || tag
            il.Emit(OpCodes.Ldloc, ctLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldloc, tagLenLocal);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Newarr, _types.Byte);
            il.Emit(OpCodes.Stloc, resultLocal);

            il.Emit(OpCodes.Ldloc, ctLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, ctLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Call, _types.ArrayCopy5);

            il.Emit(OpCodes.Ldloc, tagLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldloc, ctLocal);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldloc, tagLenLocal);
            il.Emit(OpCodes.Call, _types.ArrayCopy5);

            // gcm.Dispose(); return result
            il.Emit(OpCodes.Ldloc, gcmLocal);
            il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
        }

        // --- decrypt ---
        il.MarkLabel(decryptLabel);
        {
            var ctLenLocal = il.DeclareLocal(_types.Int32);
            var ctLocal = il.DeclareLocal(_types.ByteArray);
            var tagLocal = il.DeclareLocal(_types.ByteArray);
            var ptLocal = il.DeclareLocal(_types.ByteArray);
            var lenOkLabel = il.DefineLabel();

            // ctLen = data.Length - tagLen; if (ctLen < 0) throw
            il.Emit(OpCodes.Ldarg, 4);
            il.Emit(OpCodes.Ldlen);
            il.Emit(OpCodes.Conv_I4);
            il.Emit(OpCodes.Ldloc, tagLenLocal);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Stloc, ctLenLocal);
            il.Emit(OpCodes.Ldloc, ctLenLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Bge, lenOkLabel);
            il.Emit(OpCodes.Ldstr, "crypto.subtle.decrypt: ciphertext too short");
            il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
            il.Emit(OpCodes.Throw);

            il.MarkLabel(lenOkLabel);
            il.Emit(OpCodes.Ldloc, ctLenLocal);
            il.Emit(OpCodes.Newarr, _types.Byte);
            il.Emit(OpCodes.Stloc, ctLocal);
            il.Emit(OpCodes.Ldloc, tagLenLocal);
            il.Emit(OpCodes.Newarr, _types.Byte);
            il.Emit(OpCodes.Stloc, tagLocal);
            il.Emit(OpCodes.Ldloc, ctLenLocal);
            il.Emit(OpCodes.Newarr, _types.Byte);
            il.Emit(OpCodes.Stloc, ptLocal);

            // split data → ct, tag
            il.Emit(OpCodes.Ldarg, 4);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, ctLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, ctLenLocal);
            il.Emit(OpCodes.Call, _types.ArrayCopy5);

            il.Emit(OpCodes.Ldarg, 4);
            il.Emit(OpCodes.Ldloc, ctLenLocal);
            il.Emit(OpCodes.Ldloc, tagLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, tagLenLocal);
            il.Emit(OpCodes.Call, _types.ArrayCopy5);

            // gcm.Decrypt(iv, ct, tag, pt, aad)
            il.Emit(OpCodes.Ldloc, gcmLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, ctLocal);
            il.Emit(OpCodes.Ldloc, tagLocal);
            il.Emit(OpCodes.Ldloc, ptLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, decryptMethod);

            il.Emit(OpCodes.Ldloc, gcmLocal);
            il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
            il.Emit(OpCodes.Ldloc, ptLocal);
            il.Emit(OpCodes.Ret);
        }
    }

    private static Label DefineThrowTag(ILGenerator il, out Label label)
    {
        label = il.DefineLabel();
        return label;
    }

    /// <summary>Emits: byte[] WcAesCbc(byte[] key, byte[] iv, byte[] data, bool encrypt) — PKCS7.</summary>
    private void EmitWcAesCbc(TypeBuilder tb)
    {
        _wcAesCbc = WcDefine(tb, "WcAesCbc", _types.ByteArray,
            [_types.ByteArray, _types.ByteArray, _types.ByteArray, _types.Boolean]);
        var il = _wcAesCbc.GetILGenerator();

        var aesLocal = il.DeclareLocal(typeof(Aes));
        var resultLocal = il.DeclareLocal(_types.ByteArray);

        il.Emit(OpCodes.Call, typeof(Aes).GetMethod("Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, aesLocal);

        il.Emit(OpCodes.Ldloc, aesLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, typeof(SymmetricAlgorithm).GetProperty("Key")!.GetSetMethod()!);

        var decryptLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brfalse, decryptLabel);

        il.Emit(OpCodes.Ldloc, aesLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_2); // PaddingMode.PKCS7
        il.Emit(OpCodes.Callvirt, typeof(SymmetricAlgorithm).GetMethod("EncryptCbc", [typeof(byte[]), typeof(byte[]), typeof(PaddingMode)])!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(decryptLabel);
        il.Emit(OpCodes.Ldloc, aesLocal);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_2); // PaddingMode.PKCS7
        il.Emit(OpCodes.Callvirt, typeof(SymmetricAlgorithm).GetMethod("DecryptCbc", [typeof(byte[]), typeof(byte[]), typeof(PaddingMode)])!);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ldloc, aesLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emits IL that imports pkcs8/spki DER (byte[] on stack via args) into the algorithm local.</summary>
    private void EmitWcImportInto(ILGenerator il, LocalBuilder algLocal, Type algType, int derArgIndex, int isPrivateArgIndex)
    {
        var outLocal = il.DeclareLocal(_types.Int32);
        var publicLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg, isPrivateArgIndex);
        il.Emit(OpCodes.Brfalse, publicLabel);

        il.Emit(OpCodes.Ldloc, algLocal);
        il.Emit(OpCodes.Ldarg, derArgIndex);
        il.Emit(OpCodes.Call, WcSpanFromBytes);
        il.Emit(OpCodes.Ldloca, outLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(algType, "ImportPkcs8PrivateKey", [_types.ReadOnlySpanOfByte, typeof(int).MakeByRefType()])!);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(publicLabel);
        il.Emit(OpCodes.Ldloc, algLocal);
        il.Emit(OpCodes.Ldarg, derArgIndex);
        il.Emit(OpCodes.Call, WcSpanFromBytes);
        il.Emit(OpCodes.Ldloca, outLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(algType, "ImportSubjectPublicKeyInfo", [_types.ReadOnlySpanOfByte, typeof(int).MakeByRefType()])!);

        il.MarkLabel(doneLabel);
    }

    /// <summary>Emits: byte[] WcRsaOaep(byte[] der, bool isPrivate, string hashLower, byte[] data, bool encrypt).</summary>
    private void EmitWcRsaOaep(TypeBuilder tb)
    {
        _wcRsaOaep = WcDefine(tb, "WcRsaOaep", _types.ByteArray,
            [_types.ByteArray, _types.Boolean, _types.String, _types.ByteArray, _types.Boolean]);
        var il = _wcRsaOaep.GetILGenerator();
        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);

        var rsaLocal = il.DeclareLocal(typeof(RSA));
        var paddingLocal = il.DeclareLocal(typeof(RSAEncryptionPadding));
        var resultLocal = il.DeclareLocal(_types.ByteArray);

        il.Emit(OpCodes.Call, typeof(RSA).GetMethod("Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, rsaLocal);
        EmitWcImportInto(il, rsaLocal, typeof(RSA), derArgIndex: 0, isPrivateArgIndex: 1);

        // padding switch
        var paddingDone = il.DefineLabel();
        (string Lower, string Prop)[] paddings =
            [("sha1", "OaepSHA1"), ("sha256", "OaepSHA256"), ("sha384", "OaepSHA384"), ("sha512", "OaepSHA512")];
        foreach (var (lower, prop) in paddings)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldstr, lower);
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Call, typeof(RSAEncryptionPadding).GetProperty(prop)!.GetGetMethod()!);
            il.Emit(OpCodes.Stloc, paddingLocal);
            il.Emit(OpCodes.Br, paddingDone);
            il.MarkLabel(next);
        }
        il.Emit(OpCodes.Ldstr, "crypto.subtle: unsupported OAEP hash");
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(paddingDone);

        var decryptLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Brfalse, decryptLabel);

        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldloc, paddingLocal);
        il.Emit(OpCodes.Callvirt, typeof(RSA).GetMethod("Encrypt", [typeof(byte[]), typeof(RSAEncryptionPadding)])!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(decryptLabel);
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldloc, paddingLocal);
        il.Emit(OpCodes.Callvirt, typeof(RSA).GetMethod("Decrypt", [typeof(byte[]), typeof(RSAEncryptionPadding)])!);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: object WcRsaSignVerify(byte[] der, bool isPrivate, string hashLower, bool pss, byte[] data, byte[]? sig).
    /// sig == null → sign (returns byte[]); otherwise verify (returns boxed bool).
    /// </summary>
    private void EmitWcRsaSignVerify(TypeBuilder tb)
    {
        _wcRsaSignVerify = WcDefine(tb, "WcRsaSignVerify", _types.Object,
            [_types.ByteArray, _types.Boolean, _types.String, _types.Boolean, _types.ByteArray, _types.ByteArray]);
        var il = _wcRsaSignVerify.GetILGenerator();

        var rsaLocal = il.DeclareLocal(typeof(RSA));
        var hashLocal = il.DeclareLocal(typeof(HashAlgorithmName));
        var paddingLocal = il.DeclareLocal(typeof(RSASignaturePadding));
        var resultLocal = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Call, typeof(RSA).GetMethod("Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, rsaLocal);
        EmitWcImportInto(il, rsaLocal, typeof(RSA), derArgIndex: 0, isPrivateArgIndex: 1);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _wcHashAlg);
        il.Emit(OpCodes.Stloc, hashLocal);

        var pkcs1Label = il.DefineLabel();
        var paddingDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Brfalse, pkcs1Label);
        il.Emit(OpCodes.Call, typeof(RSASignaturePadding).GetProperty("Pss")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, paddingLocal);
        il.Emit(OpCodes.Br, paddingDone);
        il.MarkLabel(pkcs1Label);
        il.Emit(OpCodes.Call, typeof(RSASignaturePadding).GetProperty("Pkcs1")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, paddingLocal);
        il.MarkLabel(paddingDone);

        var verifyLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, 5);
        il.Emit(OpCodes.Brtrue, verifyLabel);

        // sign
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Ldloc, hashLocal);
        il.Emit(OpCodes.Ldloc, paddingLocal);
        il.Emit(OpCodes.Callvirt, typeof(RSA).GetMethod("SignData", [typeof(byte[]), typeof(HashAlgorithmName), typeof(RSASignaturePadding)])!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, doneLabel);

        // verify
        il.MarkLabel(verifyLabel);
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Ldarg, 5);
        il.Emit(OpCodes.Ldloc, hashLocal);
        il.Emit(OpCodes.Ldloc, paddingLocal);
        il.Emit(OpCodes.Callvirt, typeof(RSA).GetMethod("VerifyData", [typeof(byte[]), typeof(byte[]), typeof(HashAlgorithmName), typeof(RSASignaturePadding)])!);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: object WcEcdsaSignVerify(byte[] der, bool isPrivate, string hashLower, byte[] data, byte[]? sig).
    /// WebCrypto ECDSA signatures are IEEE P1363 (raw r||s).
    /// </summary>
    private void EmitWcEcdsaSignVerify(TypeBuilder tb)
    {
        _wcEcdsaSignVerify = WcDefine(tb, "WcEcdsaSignVerify", _types.Object,
            [_types.ByteArray, _types.Boolean, _types.String, _types.ByteArray, _types.ByteArray]);
        var il = _wcEcdsaSignVerify.GetILGenerator();

        var ecLocal = il.DeclareLocal(typeof(ECDsa));
        var hashLocal = il.DeclareLocal(typeof(HashAlgorithmName));
        var resultLocal = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Call, typeof(ECDsa).GetMethod("Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, ecLocal);
        EmitWcImportInto(il, ecLocal, typeof(ECDsa), derArgIndex: 0, isPrivateArgIndex: 1);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _wcHashAlg);
        il.Emit(OpCodes.Stloc, hashLocal);

        var verifyLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Brtrue, verifyLabel);

        // sign: ec.SignData(data, hash, IeeeP1363FixedFieldConcatenation)
        il.Emit(OpCodes.Ldloc, ecLocal);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldloc, hashLocal);
        il.Emit(OpCodes.Ldc_I4_0); // DSASignatureFormat.IeeeP1363FixedFieldConcatenation
        il.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod("SignData", [typeof(byte[]), typeof(HashAlgorithmName), typeof(DSASignatureFormat)])!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(verifyLabel);
        il.Emit(OpCodes.Ldloc, ecLocal);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Ldloc, hashLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod("VerifyData", [typeof(byte[]), typeof(byte[]), typeof(HashAlgorithmName), typeof(DSASignatureFormat)])!);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.MarkLabel(doneLabel);
        il.Emit(OpCodes.Ldloc, ecLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emits: byte[] WcPbkdf2(byte[] pw, byte[] salt, int iterations, string hashLower, int lenBytes).</summary>
    private void EmitWcPbkdf2(TypeBuilder tb)
    {
        _wcPbkdf2 = WcDefine(tb, "WcPbkdf2", _types.ByteArray,
            [_types.ByteArray, _types.ByteArray, _types.Int32, _types.String, _types.Int32]);
        var il = _wcPbkdf2.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, _wcHashAlg);
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Call, typeof(Rfc2898DeriveBytes).GetMethod("Pbkdf2",
            [typeof(byte[]), typeof(byte[]), typeof(int), typeof(HashAlgorithmName), typeof(int)])!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emits: byte[] WcHkdf(string hashLower, byte[] ikm, int lenBytes, byte[] salt, byte[] info).</summary>
    private void EmitWcHkdf(TypeBuilder tb)
    {
        _wcHkdf = WcDefine(tb, "WcHkdf", _types.ByteArray,
            [_types.String, _types.ByteArray, _types.Int32, _types.ByteArray, _types.ByteArray]);
        var il = _wcHkdf.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _wcHashAlg);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.HKDF, "DeriveKey",
            [typeof(HashAlgorithmName), typeof(byte[]), typeof(int), typeof(byte[]), typeof(byte[])])!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emits: byte[] WcEcdhDerive(byte[] privPkcs8, byte[] pubSpki, int lenBytes).</summary>
    private void EmitWcEcdhDerive(TypeBuilder tb)
    {
        _wcEcdhDerive = WcDefine(tb, "WcEcdhDerive", _types.ByteArray,
            [_types.ByteArray, _types.ByteArray, _types.Int32]);
        var il = _wcEcdhDerive.GetILGenerator();

        var privLocal = il.DeclareLocal(typeof(ECDiffieHellman));
        var pubLocal = il.DeclareLocal(typeof(ECDiffieHellman));
        var secretLocal = il.DeclareLocal(_types.ByteArray);
        var truncatedLocal = il.DeclareLocal(_types.ByteArray);
        var outLocal = il.DeclareLocal(_types.Int32);

        il.Emit(OpCodes.Call, typeof(ECDiffieHellman).GetMethod("Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, privLocal);
        il.Emit(OpCodes.Ldloc, privLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, WcSpanFromBytes);
        il.Emit(OpCodes.Ldloca, outLocal);
        il.Emit(OpCodes.Callvirt, typeof(ECDiffieHellman).GetMethod("ImportPkcs8PrivateKey", [_types.ReadOnlySpanOfByte, typeof(int).MakeByRefType()])!);

        il.Emit(OpCodes.Call, typeof(ECDiffieHellman).GetMethod("Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, pubLocal);
        il.Emit(OpCodes.Ldloc, pubLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, WcSpanFromBytes);
        il.Emit(OpCodes.Ldloca, outLocal);
        il.Emit(OpCodes.Callvirt, typeof(ECDiffieHellman).GetMethod("ImportSubjectPublicKeyInfo", [_types.ReadOnlySpanOfByte, typeof(int).MakeByRefType()])!);

        // secret = priv.DeriveRawSecretAgreement(pub.PublicKey)
        il.Emit(OpCodes.Ldloc, privLocal);
        il.Emit(OpCodes.Ldloc, pubLocal);
        il.Emit(OpCodes.Callvirt, typeof(ECDiffieHellman).GetProperty("PublicKey")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(ECDiffieHellman).GetMethod("DeriveRawSecretAgreement", [typeof(ECDiffieHellmanPublicKey)])!);
        il.Emit(OpCodes.Stloc, secretLocal);

        // dispose both
        il.Emit(OpCodes.Ldloc, privLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
        il.Emit(OpCodes.Ldloc, pubLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);

        // length checks / truncation
        var fitsLabel = il.DefineLabel();
        var exactLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, secretLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ble, fitsLabel);
        il.Emit(OpCodes.Ldstr, "crypto.subtle.deriveBits: requested more bits than the ECDH secret provides");
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(fitsLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldloc, secretLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Beq, exactLabel);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, truncatedLocal);
        il.Emit(OpCodes.Ldloc, secretLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, truncatedLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _types.ArrayCopy5);
        il.Emit(OpCodes.Ldloc, truncatedLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(exactLabel);
        il.Emit(OpCodes.Ldloc, secretLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emits: ECCurve WcCurve(string canonical) — "P-256"/"P-384"/"P-521" → named curve.</summary>
    private void EmitWcCurve(TypeBuilder tb)
    {
        _wcCurve = WcDefine(tb, "WcCurve", typeof(ECCurve), [_types.String]);
        var il = _wcCurve.GetILGenerator();
        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);

        (string Name, string Prop)[] curves = [("P-256", "nistP256"), ("P-384", "nistP384"), ("P-521", "nistP521")];
        foreach (var (name, prop) in curves)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Call, typeof(ECCurve.NamedCurves).GetProperty(prop)!.GetGetMethod()!);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(next);
        }
        il.Emit(OpCodes.Ldstr, "crypto.subtle: unsupported namedCurve (supported: P-256, P-384, P-521)");
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>Emits: string WcCanonicalCurve(object) — curve name/aliases → "P-256" form.</summary>
    private void EmitWcCanonicalCurve(TypeBuilder tb, EmittedRuntime runtime)
    {
        _wcCanonicalCurve = WcDefine(tb, "WcCanonicalCurve", _types.String, [_types.Object]);
        var il = _wcCanonicalCurve.GetILGenerator();
        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);

        var lowerLocal = il.DeclareLocal(_types.String);
        var throwLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, lowerLocal);
        il.Emit(OpCodes.Ldloc, lowerLocal);
        il.Emit(OpCodes.Brfalse, throwLabel);
        il.Emit(OpCodes.Ldloc, lowerLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, lowerLocal);

        (string[] Aliases, string Canonical)[] table =
        [
            (["p-256", "prime256v1", "secp256r1"], "P-256"),
            (["p-384", "secp384r1"], "P-384"),
            (["p-521", "secp521r1"], "P-521"),
        ];
        foreach (var (aliases, canonical) in table)
        {
            var matched = il.DefineLabel();
            var next = il.DefineLabel();
            foreach (var alias in aliases)
            {
                il.Emit(OpCodes.Ldloc, lowerLocal);
                il.Emit(OpCodes.Ldstr, alias);
                il.Emit(OpCodes.Call, strEq);
                il.Emit(OpCodes.Brtrue, matched);
            }
            il.Emit(OpCodes.Br, next);
            il.MarkLabel(matched);
            il.Emit(OpCodes.Ldstr, canonical);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(next);
        }

        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "crypto.subtle: unsupported namedCurve (supported: P-256, P-384, P-521)");
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>Emits: object[] WcGenRsa(int modulusLength) — [spki, pkcs8].</summary>
    private void EmitWcGenRsa(TypeBuilder tb)
    {
        _wcGenRsa = WcDefine(tb, "WcGenRsa", typeof(object[]), [_types.Int32]);
        var il = _wcGenRsa.GetILGenerator();

        var rsaLocal = il.DeclareLocal(typeof(RSA));
        var arrLocal = il.DeclareLocal(typeof(object[]));

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RSA).GetMethod("Create", [typeof(int)])!);
        il.Emit(OpCodes.Stloc, rsaLocal);

        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, arrLocal);

        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Callvirt, typeof(RSA).GetMethod("ExportSubjectPublicKeyInfo", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Callvirt, typeof(RSA).GetMethod("ExportPkcs8PrivateKey", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emits: object[] WcGenEc(string canonical) — [spki, pkcs8].</summary>
    private void EmitWcGenEc(TypeBuilder tb)
    {
        _wcGenEc = WcDefine(tb, "WcGenEc", typeof(object[]), [_types.String]);
        var il = _wcGenEc.GetILGenerator();

        var ecLocal = il.DeclareLocal(typeof(ECDsa));
        var arrLocal = il.DeclareLocal(typeof(object[]));

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _wcCurve);
        il.Emit(OpCodes.Call, typeof(ECDsa).GetMethod("Create", [typeof(ECCurve)])!);
        il.Emit(OpCodes.Stloc, ecLocal);

        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, arrLocal);

        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, ecLocal);
        il.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod("ExportSubjectPublicKeyInfo", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, ecLocal);
        il.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod("ExportPkcs8PrivateKey", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ldloc, ecLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
        il.Emit(OpCodes.Ldloc, arrLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: byte[] WcEcRawToSpki(byte[] raw, string canonical) — uncompressed point 04||X||Y → SPKI DER.
    /// Compressed points (02/03) are a documented compiled-mode ceiling.
    /// </summary>
    private void EmitWcEcRawToSpki(TypeBuilder tb)
    {
        _wcEcRawToSpki = WcDefine(tb, "WcEcRawToSpki", _types.ByteArray, [_types.ByteArray, _types.String]);
        var il = _wcEcRawToSpki.GetILGenerator();

        var fieldLenLocal = il.DeclareLocal(_types.Int32);
        var xLocal = il.DeclareLocal(_types.ByteArray);
        var yLocal = il.DeclareLocal(_types.ByteArray);
        var paramsLocal = il.DeclareLocal(typeof(ECParameters));
        var ecLocal = il.DeclareLocal(typeof(ECDsa));
        var resultLocal = il.DeclareLocal(_types.ByteArray);
        var okLabel = il.DefineLabel();

        // if (raw.Length >= 3 && raw[0] == 4) ok
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Blt, DefineThrowTag(il, out var badPointLabel));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Beq, okLabel);

        il.MarkLabel(badPointLabel);
        il.Emit(OpCodes.Ldstr, "crypto.subtle.importKey: only uncompressed EC points (04||X||Y) are supported in compiled mode");
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(okLabel);
        // fieldLen = (raw.Length - 1) / 2
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Stloc, fieldLenLocal);

        // x = raw[1 .. 1+fieldLen]; y = raw[1+fieldLen ..]
        il.Emit(OpCodes.Ldloc, fieldLenLocal);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, xLocal);
        il.Emit(OpCodes.Ldloc, fieldLenLocal);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, yLocal);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, xLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, fieldLenLocal);
        il.Emit(OpCodes.Call, _types.ArrayCopy5);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, fieldLenLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, yLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, fieldLenLocal);
        il.Emit(OpCodes.Call, _types.ArrayCopy5);

        // params = default; params.Curve = WcCurve(canonical); params.Q.X = x; params.Q.Y = y
        il.Emit(OpCodes.Ldloca, paramsLocal);
        il.Emit(OpCodes.Initobj, typeof(ECParameters));

        il.Emit(OpCodes.Ldloca, paramsLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _wcCurve);
        il.Emit(OpCodes.Stfld, typeof(ECParameters).GetField("Curve")!);

        il.Emit(OpCodes.Ldloca, paramsLocal);
        il.Emit(OpCodes.Ldflda, typeof(ECParameters).GetField("Q")!);
        il.Emit(OpCodes.Ldloc, xLocal);
        il.Emit(OpCodes.Stfld, typeof(ECPoint).GetField("X")!);

        il.Emit(OpCodes.Ldloca, paramsLocal);
        il.Emit(OpCodes.Ldflda, typeof(ECParameters).GetField("Q")!);
        il.Emit(OpCodes.Ldloc, yLocal);
        il.Emit(OpCodes.Stfld, typeof(ECPoint).GetField("Y")!);

        // ec = ECDsa.Create(); ec.ImportParameters(params); result = ec.ExportSubjectPublicKeyInfo()
        il.Emit(OpCodes.Call, typeof(ECDsa).GetMethod("Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, ecLocal);
        il.Emit(OpCodes.Ldloc, ecLocal);
        il.Emit(OpCodes.Ldloc, paramsLocal);
        il.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod("ImportParameters", [typeof(ECParameters)])!);
        il.Emit(OpCodes.Ldloc, ecLocal);
        il.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod("ExportSubjectPublicKeyInfo", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Ldloc, ecLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emits: byte[] WcEcSpkiToRaw(byte[] spki) — SPKI DER → uncompressed point 04||X||Y.</summary>
    private void EmitWcEcSpkiToRaw(TypeBuilder tb)
    {
        _wcEcSpkiToRaw = WcDefine(tb, "WcEcSpkiToRaw", _types.ByteArray, [_types.ByteArray]);
        var il = _wcEcSpkiToRaw.GetILGenerator();

        var ecLocal = il.DeclareLocal(typeof(ECDsa));
        var paramsLocal = il.DeclareLocal(typeof(ECParameters));
        var xLocal = il.DeclareLocal(_types.ByteArray);
        var yLocal = il.DeclareLocal(_types.ByteArray);
        var resultLocal = il.DeclareLocal(_types.ByteArray);
        var outLocal = il.DeclareLocal(_types.Int32);

        il.Emit(OpCodes.Call, typeof(ECDsa).GetMethod("Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, ecLocal);
        il.Emit(OpCodes.Ldloc, ecLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, WcSpanFromBytes);
        il.Emit(OpCodes.Ldloca, outLocal);
        il.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod("ImportSubjectPublicKeyInfo", [_types.ReadOnlySpanOfByte, typeof(int).MakeByRefType()])!);

        il.Emit(OpCodes.Ldloc, ecLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, typeof(ECDsa).GetMethod("ExportParameters", [typeof(bool)])!);
        il.Emit(OpCodes.Stloc, paramsLocal);

        il.Emit(OpCodes.Ldloca, paramsLocal);
        il.Emit(OpCodes.Ldflda, typeof(ECParameters).GetField("Q")!);
        il.Emit(OpCodes.Ldfld, typeof(ECPoint).GetField("X")!);
        il.Emit(OpCodes.Stloc, xLocal);

        il.Emit(OpCodes.Ldloca, paramsLocal);
        il.Emit(OpCodes.Ldflda, typeof(ECParameters).GetField("Q")!);
        il.Emit(OpCodes.Ldfld, typeof(ECPoint).GetField("Y")!);
        il.Emit(OpCodes.Stloc, yLocal);

        // result = new byte[1 + x.Length + y.Length]; result[0] = 4
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, xLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, yLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Newarr, _types.Byte);
        il.Emit(OpCodes.Stloc, resultLocal);

        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Stelem_I1);

        il.Emit(OpCodes.Ldloc, xLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, xLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, _types.ArrayCopy5);

        il.Emit(OpCodes.Ldloc, yLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, xLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, yLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Call, _types.ArrayCopy5);

        il.Emit(OpCodes.Ldloc, ecLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emits: int WcImportRsaCheck(byte[] der, bool isPrivate) — validates, returns KeySize.</summary>
    private void EmitWcImportRsaCheck(TypeBuilder tb)
    {
        _wcImportRsaCheck = WcDefine(tb, "WcImportRsaCheck", _types.Int32, [_types.ByteArray, _types.Boolean]);
        var il = _wcImportRsaCheck.GetILGenerator();

        var rsaLocal = il.DeclareLocal(typeof(RSA));
        var sizeLocal = il.DeclareLocal(_types.Int32);

        il.Emit(OpCodes.Call, typeof(RSA).GetMethod("Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, rsaLocal);
        EmitWcImportInto(il, rsaLocal, typeof(RSA), derArgIndex: 0, isPrivateArgIndex: 1);

        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Callvirt, typeof(AsymmetricAlgorithm).GetProperty("KeySize")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, sizeLocal);
        il.Emit(OpCodes.Ldloc, rsaLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
        il.Emit(OpCodes.Ldloc, sizeLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emits: void WcImportEcCheck(byte[] der, bool isPrivate) — validates the DER imports.</summary>
    private void EmitWcImportEcCheck(TypeBuilder tb)
    {
        _wcImportEcCheck = WcDefine(tb, "WcImportEcCheck", typeof(void), [_types.ByteArray, _types.Boolean]);
        var il = _wcImportEcCheck.GetILGenerator();

        var ecLocal = il.DeclareLocal(typeof(ECDsa));
        il.Emit(OpCodes.Call, typeof(ECDsa).GetMethod("Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, ecLocal);
        EmitWcImportInto(il, ecLocal, typeof(ECDsa), derArgIndex: 0, isPrivateArgIndex: 1);
        il.Emit(OpCodes.Ldloc, ecLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emits: string WcBase64Url(byte[]).</summary>
    private void EmitWcBase64Url(TypeBuilder tb)
    {
        _wcBase64Url = WcDefine(tb, "WcBase64Url", _types.String, [_types.ByteArray]);
        var il = _wcBase64Url.GetILGenerator();

        // Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_')
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.ConvertToBase64String);

        // TrimEnd(params char[]) with a 1-element array
        var charsLocal = il.DeclareLocal(typeof(char[]));
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, typeof(char));
        il.Emit(OpCodes.Stloc, charsLocal);
        il.Emit(OpCodes.Ldloc, charsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4, (int)'=');
        il.Emit(OpCodes.Stelem_I2);
        il.Emit(OpCodes.Ldloc, charsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "TrimEnd", [typeof(char[])])!);

        il.Emit(OpCodes.Ldc_I4, (int)'+');
        il.Emit(OpCodes.Ldc_I4, (int)'-');
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Replace", [typeof(char), typeof(char)])!);
        il.Emit(OpCodes.Ldc_I4, (int)'/');
        il.Emit(OpCodes.Ldc_I4, (int)'_');
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Replace", [typeof(char), typeof(char)])!);
        il.Emit(OpCodes.Ret);
    }
}
