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
    private MethodInfo WcSpanFromBytes => _types.GetMethod(_types.ReadOnlySpanOfByte, "op_Implicit", [typeof(byte[])])!;

    /// <summary>Emits all WebCrypto byte-level helpers onto $Runtime.</summary>
    private void EmitWebCryptoRuntimeHelpers(TypeBuilder tb, EmittedRuntime runtime)
    {
        var webCrypto = runtime.WebCrypto.RequireImplementation();
        EmitWcThrow(webCrypto, tb);
        EmitWcHashAlg(webCrypto, tb);
        EmitWcDigestLen(webCrypto, tb);
        EmitWcMapHash(tb, runtime);
        EmitWcAlgoName(tb, runtime);
        EmitWcParam(tb, runtime);
        EmitWcIntParam(webCrypto, tb);
        EmitWcToBytes(tb, runtime);
        EmitWcToArrayBuffer(tb, runtime);
        EmitWcResolved(tb, runtime);
        EmitWcDigest(webCrypto, tb);
        EmitWcHmac(webCrypto, tb);
        EmitWcAesGcm(webCrypto, tb);
        EmitWcAesCbc(webCrypto, tb);
        EmitWcRsaOaep(webCrypto, tb);
        EmitWcRsaSignVerify(webCrypto, tb);
        EmitWcEcdsaSignVerify(webCrypto, tb);
        EmitWcPbkdf2(webCrypto, tb);
        EmitWcHkdf(webCrypto, tb);
        EmitWcEcdhDerive(webCrypto, tb);
        EmitWcCurve(webCrypto, tb);
        EmitWcCanonicalCurve(tb, webCrypto);
        EmitWcGenRsa(webCrypto, tb);
        EmitWcGenEc(webCrypto, tb);
        EmitWcEcRawToSpki(webCrypto, tb);
        EmitWcEcSpkiToRaw(webCrypto, tb);
        EmitWcImportRsaCheck(webCrypto, tb);
        EmitWcImportEcCheck(webCrypto, tb);
        EmitWcBase64Url(webCrypto, tb);
    }

    private MethodBuilder WcDefine(TypeBuilder tb, string name, Type returnType, Type[] args)
        => tb.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, returnType, args);

    /// <summary>Emits: object WcThrow(string message) — throws ArgumentException.</summary>
    private void EmitWcThrow(EmittedWebCryptoImplementation webCrypto, TypeBuilder tb)
    {
        webCrypto.Throw = WcDefine(tb, "WcThrow", _types.Object, [_types.String]);
        var il = webCrypto.Throw.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>Emits: HashAlgorithmName WcHashAlg(string lower).</summary>
    private void EmitWcHashAlg(EmittedWebCryptoImplementation webCrypto, TypeBuilder tb)
    {
        webCrypto.HashAlgorithm = WcDefine(tb, "WcHashAlg", typeof(HashAlgorithmName), [_types.String]);
        var il = webCrypto.HashAlgorithm.GetILGenerator();
        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);

        foreach (var (lower, _) in WebCryptoEmitConstants.HashNames)
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
    private void EmitWcDigestLen(EmittedWebCryptoImplementation webCrypto, TypeBuilder tb)
    {
        webCrypto.DigestLen = WcDefine(tb, "WcDigestLen", _types.Int32, [_types.String]);
        var il = webCrypto.DigestLen.GetILGenerator();
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
        var webCrypto = runtime.WebCrypto.RequireImplementation();
        webCrypto.MapHash = WcDefine(tb, "WcMapHash", _types.String, [_types.Object]);
        var il = webCrypto.MapHash.GetILGenerator();
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
        il.Emit(OpCodes.Call, runtime.ObjectRead.Property);
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

        foreach (var (lower, web) in WebCryptoEmitConstants.HashNames)
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
        var webCrypto = runtime.WebCrypto.RequireImplementation();
        webCrypto.AlgorithmName = WcDefine(tb, "WcAlgoName", _types.String, [_types.Object]);
        var il = webCrypto.AlgorithmName.GetILGenerator();

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
        il.Emit(OpCodes.Call, runtime.ObjectRead.Property);
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
        var webCrypto = runtime.WebCrypto.RequireImplementation();
        webCrypto.Parameter = WcDefine(tb, "WcParam", _types.Object, [_types.Object, _types.String]);
        var il = webCrypto.Parameter.GetILGenerator();

        var nullLabel = il.DefineLabel();
        var valueLocal = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ObjectRead.Property);
        il.Emit(OpCodes.Stloc, valueLocal);

        // undefined sentinel → null
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance);
        il.Emit(OpCodes.Beq, nullLabel);

        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emits: int WcIntParam(object boxed, int defaultValue).</summary>
    private void EmitWcIntParam(EmittedWebCryptoImplementation webCrypto, TypeBuilder tb)
    {
        webCrypto.IntParameter = WcDefine(tb, "WcIntParam", _types.Int32, [_types.Object, _types.Int32]);
        var il = webCrypto.IntParameter.GetILGenerator();

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
        var webCrypto = runtime.WebCrypto.RequireImplementation();
        webCrypto.ToBytes = WcDefine(tb, "WcToBytes", _types.ByteArray, [_types.Object]);
        var il = webCrypto.ToBytes.GetILGenerator();

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
        if (runtime.Buffer is not null)
        {
            var notBuffer = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.RequireBuffer().Type);
            il.Emit(OpCodes.Brfalse, notBuffer);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.RequireBuffer().Type);
            il.Emit(OpCodes.Call, runtime.RequireBuffer().GetData);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notBuffer);
        }

        // $TypedArray → copy of the view window
        if (runtime.TypedArrays.Implementation is not null)
        {
            var notTyped = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TypedArrays.RequireImplementation().BaseType);
            il.Emit(OpCodes.Brfalse, notTyped);

            var typedLocal = il.DeclareLocal(runtime.TypedArrays.RequireImplementation().BaseType);
            var lenLocal = il.DeclareLocal(_types.Int32);
            var resultLocal = il.DeclareLocal(_types.ByteArray);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TypedArrays.RequireImplementation().BaseType);
            il.Emit(OpCodes.Stloc, typedLocal);

            il.Emit(OpCodes.Ldloc, typedLocal);
            il.Emit(OpCodes.Callvirt, runtime.TypedArrays.RequireImplementation().ByteLengthGetter);
            il.Emit(OpCodes.Stloc, lenLocal);

            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Newarr, _types.Byte);
            il.Emit(OpCodes.Stloc, resultLocal);

            // Array.Copy(src, srcOffset, dst, 0, len)
            il.Emit(OpCodes.Ldloc, typedLocal);
            il.Emit(OpCodes.Callvirt, runtime.TypedArrays.RequireImplementation().GetBuffer);
            il.Emit(OpCodes.Ldloc, typedLocal);
            il.Emit(OpCodes.Callvirt, runtime.TypedArrays.RequireImplementation().ByteOffsetGetter);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, lenLocal);
            il.Emit(OpCodes.Call, _types.ArrayCopy5);

            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notTyped);
        }

        // $ArrayBuffer → clone of the backing array
        if (runtime.ArrayBuffer is not null)
        {
            var notAb = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.RequireArrayBuffer().Type);
            il.Emit(OpCodes.Brfalse, notAb);

            var srcLocal = il.DeclareLocal(_types.ByteArray);
            var cloneLocal = il.DeclareLocal(_types.ByteArray);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.RequireArrayBuffer().Type);
            il.Emit(OpCodes.Callvirt, runtime.RequireArrayBuffer().GetBuffer);
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
        var webCrypto = runtime.WebCrypto.RequireImplementation();
        webCrypto.ToArrayBuffer = WcDefine(tb, "WcToArrayBuffer", _types.Object, [_types.ByteArray]);
        var il = webCrypto.ToArrayBuffer.GetILGenerator();

        var abLocal = il.DeclareLocal(_types.Object);

        // var ab = new $ArrayBuffer(bytes.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newobj, runtime.RequireArrayBuffer().Ctor);
        il.Emit(OpCodes.Stloc, abLocal);

        // Array.Copy(bytes, 0, ab.GetBuffer(), 0, bytes.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, abLocal);
        il.Emit(OpCodes.Castclass, runtime.RequireArrayBuffer().Type);
        il.Emit(OpCodes.Callvirt, runtime.RequireArrayBuffer().GetBuffer);
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
        var webCrypto = runtime.WebCrypto.RequireImplementation();
        webCrypto.Resolved = WcDefine(tb, "WcResolved", _types.Object, [_types.Object]);
        var il = webCrypto.Resolved.GetILGenerator();

        var fromResult = EmitGenerics.MakeGenericMethod(typeof(System.Threading.Tasks.Task)
            .GetMethod("FromResult")!, _types.Object);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, fromResult);
        il.Emit(OpCodes.Newobj, runtime.RequirePromise().Ctor);
        il.Emit(OpCodes.Ret);

        // object WcRejected(Exception ex) — new $Promise(Task.FromException(ex)).
        // WebCrypto methods reject rather than throw, which also keeps guest
        // try/catch-around-await working in compiled async bodies.
        webCrypto.Rejected = WcDefine(tb, "WcRejected", _types.Object, [typeof(Exception)]);
        var ril = webCrypto.Rejected.GetILGenerator();
        var fromException = EmitGenerics.MakeGenericMethod(typeof(System.Threading.Tasks.Task)
            .GetMethod("FromException", 1, [typeof(Exception)])!, _types.Object);
        ril.Emit(OpCodes.Ldarg_0);
        ril.Emit(OpCodes.Call, fromException);
        ril.Emit(OpCodes.Newobj, runtime.RequirePromise().Ctor);
        ril.Emit(OpCodes.Ret);
    }

    /// <summary>Emits: byte[] WcDigest(string lower, byte[] data).</summary>
    private void EmitWcDigest(EmittedWebCryptoImplementation webCrypto, TypeBuilder tb)
    {
        webCrypto.Digest = WcDefine(tb, "WcDigest", _types.ByteArray, [_types.String, _types.ByteArray]);
        var il = webCrypto.Digest.GetILGenerator();
        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);

        (string Lower, MethodInfo HashData)[] impls =
        [
            ("sha1", ((Func<byte[], byte[]>)SHA1.HashData).Method),
            ("sha256", ((Func<byte[], byte[]>)SHA256.HashData).Method),
            ("sha384", ((Func<byte[], byte[]>)SHA384.HashData).Method),
            ("sha512", ((Func<byte[], byte[]>)SHA512.HashData).Method),
        ];
        foreach (var (lower, hashData) in impls)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, lower);
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, hashData);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(next);
        }
        il.Emit(OpCodes.Ldstr, "crypto.subtle.digest: unsupported hash algorithm");
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>Emits: byte[] WcHmac(string lower, byte[] key, byte[] data).</summary>
    private void EmitWcHmac(EmittedWebCryptoImplementation webCrypto, TypeBuilder tb)
    {
        webCrypto.Hmac = WcDefine(tb, "WcHmac", _types.ByteArray, [_types.String, _types.ByteArray, _types.ByteArray]);
        var il = webCrypto.Hmac.GetILGenerator();
        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);

        (string Lower, MethodInfo HashData)[] impls =
        [
            ("sha1", ((Func<byte[], byte[], byte[]>)HMACSHA1.HashData).Method),
            ("sha256", ((Func<byte[], byte[], byte[]>)HMACSHA256.HashData).Method),
            ("sha384", ((Func<byte[], byte[], byte[]>)HMACSHA384.HashData).Method),
            ("sha512", ((Func<byte[], byte[], byte[]>)HMACSHA512.HashData).Method),
        ];
        foreach (var (lower, hashData) in impls)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, lower);
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, hashData);
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
    private void EmitWcAesGcm(EmittedWebCryptoImplementation webCrypto, TypeBuilder tb)
    {
        webCrypto.AesGcm = WcDefine(tb, "WcAesGcm", _types.ByteArray,
            [_types.ByteArray, _types.ByteArray, _types.ByteArray, _types.Int32, _types.ByteArray, _types.Boolean]);
        var il = webCrypto.AesGcm.GetILGenerator();

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
    private void EmitWcAesCbc(EmittedWebCryptoImplementation webCrypto, TypeBuilder tb)
    {
        webCrypto.AesCbc = WcDefine(tb, "WcAesCbc", _types.ByteArray,
            [_types.ByteArray, _types.ByteArray, _types.ByteArray, _types.Boolean]);
        var il = webCrypto.AesCbc.GetILGenerator();

        var aesLocal = il.DeclareLocal(typeof(Aes));
        var resultLocal = il.DeclareLocal(_types.ByteArray);

        il.Emit(OpCodes.Call, _types.GetMethod(typeof(Aes), "Create", Type.EmptyTypes)!);
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
    private void EmitWcRsaOaep(EmittedWebCryptoImplementation webCrypto, TypeBuilder tb)
    {
        webCrypto.RsaOaep = WcDefine(tb, "WcRsaOaep", _types.ByteArray,
            [_types.ByteArray, _types.Boolean, _types.String, _types.ByteArray, _types.Boolean]);
        var il = webCrypto.RsaOaep.GetILGenerator();
        var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);

        var rsaLocal = il.DeclareLocal(typeof(RSA));
        var paddingLocal = il.DeclareLocal(typeof(RSAEncryptionPadding));
        var resultLocal = il.DeclareLocal(_types.ByteArray);

        il.Emit(OpCodes.Call, _types.GetMethod(typeof(RSA), "Create", Type.EmptyTypes)!);
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
    private void EmitWcRsaSignVerify(EmittedWebCryptoImplementation webCrypto, TypeBuilder tb)
    {
        webCrypto.RsaSignVerify = WcDefine(tb, "WcRsaSignVerify", _types.Object,
            [_types.ByteArray, _types.Boolean, _types.String, _types.Boolean, _types.ByteArray, _types.ByteArray]);
        var il = webCrypto.RsaSignVerify.GetILGenerator();

        var rsaLocal = il.DeclareLocal(typeof(RSA));
        var hashLocal = il.DeclareLocal(typeof(HashAlgorithmName));
        var paddingLocal = il.DeclareLocal(typeof(RSASignaturePadding));
        var resultLocal = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Call, _types.GetMethod(typeof(RSA), "Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, rsaLocal);
        EmitWcImportInto(il, rsaLocal, typeof(RSA), derArgIndex: 0, isPrivateArgIndex: 1);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, webCrypto.HashAlgorithm);
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
    private void EmitWcEcdsaSignVerify(EmittedWebCryptoImplementation webCrypto, TypeBuilder tb)
    {
        webCrypto.EcdsaSignVerify = WcDefine(tb, "WcEcdsaSignVerify", _types.Object,
            [_types.ByteArray, _types.Boolean, _types.String, _types.ByteArray, _types.ByteArray]);
        var il = webCrypto.EcdsaSignVerify.GetILGenerator();

        var ecLocal = il.DeclareLocal(typeof(ECDsa));
        var hashLocal = il.DeclareLocal(typeof(HashAlgorithmName));
        var resultLocal = il.DeclareLocal(_types.Object);

        il.Emit(OpCodes.Call, _types.GetMethod(typeof(ECDsa), "Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, ecLocal);
        EmitWcImportInto(il, ecLocal, typeof(ECDsa), derArgIndex: 0, isPrivateArgIndex: 1);

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, webCrypto.HashAlgorithm);
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
    private void EmitWcPbkdf2(EmittedWebCryptoImplementation webCrypto, TypeBuilder tb)
    {
        webCrypto.Pbkdf2 = WcDefine(tb, "WcPbkdf2", _types.ByteArray,
            [_types.ByteArray, _types.ByteArray, _types.Int32, _types.String, _types.Int32]);
        var il = webCrypto.Pbkdf2.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, webCrypto.HashAlgorithm);
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Call, typeof(Rfc2898DeriveBytes).GetMethod("Pbkdf2",
            [typeof(byte[]), typeof(byte[]), typeof(int), typeof(HashAlgorithmName), typeof(int)])!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emits: byte[] WcHkdf(string hashLower, byte[] ikm, int lenBytes, byte[] salt, byte[] info).</summary>
    private void EmitWcHkdf(EmittedWebCryptoImplementation webCrypto, TypeBuilder tb)
    {
        webCrypto.Hkdf = WcDefine(tb, "WcHkdf", _types.ByteArray,
            [_types.String, _types.ByteArray, _types.Int32, _types.ByteArray, _types.ByteArray]);
        var il = webCrypto.Hkdf.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, webCrypto.HashAlgorithm);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.HKDF, "DeriveKey",
            [typeof(HashAlgorithmName), typeof(byte[]), typeof(int), typeof(byte[]), typeof(byte[])])!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emits: byte[] WcEcdhDerive(byte[] privPkcs8, byte[] pubSpki, int lenBytes).</summary>
    private void EmitWcEcdhDerive(EmittedWebCryptoImplementation webCrypto, TypeBuilder tb)
    {
        webCrypto.EcdhDerive = WcDefine(tb, "WcEcdhDerive", _types.ByteArray,
            [_types.ByteArray, _types.ByteArray, _types.Int32]);
        var il = webCrypto.EcdhDerive.GetILGenerator();

        var privLocal = il.DeclareLocal(typeof(ECDiffieHellman));
        var pubLocal = il.DeclareLocal(typeof(ECDiffieHellman));
        var secretLocal = il.DeclareLocal(_types.ByteArray);
        var truncatedLocal = il.DeclareLocal(_types.ByteArray);
        var outLocal = il.DeclareLocal(_types.Int32);

        il.Emit(OpCodes.Call, _types.GetMethod(typeof(ECDiffieHellman), "Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, privLocal);
        il.Emit(OpCodes.Ldloc, privLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, WcSpanFromBytes);
        il.Emit(OpCodes.Ldloca, outLocal);
        il.Emit(OpCodes.Callvirt, typeof(ECDiffieHellman).GetMethod("ImportPkcs8PrivateKey", [_types.ReadOnlySpanOfByte, typeof(int).MakeByRefType()])!);

        il.Emit(OpCodes.Call, _types.GetMethod(typeof(ECDiffieHellman), "Create", Type.EmptyTypes)!);
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
    private void EmitWcCurve(EmittedWebCryptoImplementation webCrypto, TypeBuilder tb)
    {
        webCrypto.Curve = WcDefine(tb, "WcCurve", typeof(ECCurve), [_types.String]);
        var il = webCrypto.Curve.GetILGenerator();
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
    private void EmitWcCanonicalCurve(TypeBuilder tb, EmittedWebCryptoImplementation webCrypto)
    {
        webCrypto.CanonicalCurve = WcDefine(tb, "WcCanonicalCurve", _types.String, [_types.Object]);
        var il = webCrypto.CanonicalCurve.GetILGenerator();
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
    private void EmitWcGenRsa(EmittedWebCryptoImplementation webCrypto, TypeBuilder tb)
    {
        webCrypto.GenRsa = WcDefine(tb, "WcGenRsa", typeof(object[]), [_types.Int32]);
        var il = webCrypto.GenRsa.GetILGenerator();

        var rsaLocal = il.DeclareLocal(typeof(RSA));
        var arrLocal = il.DeclareLocal(typeof(object[]));

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(RSA), "Create", [typeof(int)])!);
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
    private void EmitWcGenEc(EmittedWebCryptoImplementation webCrypto, TypeBuilder tb)
    {
        webCrypto.GenEc = WcDefine(tb, "WcGenEc", typeof(object[]), [_types.String]);
        var il = webCrypto.GenEc.GetILGenerator();

        var ecLocal = il.DeclareLocal(typeof(ECDsa));
        var arrLocal = il.DeclareLocal(typeof(object[]));

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, webCrypto.Curve);
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(ECDsa), "Create", [typeof(ECCurve)])!);
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
    private void EmitWcEcRawToSpki(EmittedWebCryptoImplementation webCrypto, TypeBuilder tb)
    {
        webCrypto.EcRawToSpki = WcDefine(tb, "WcEcRawToSpki", _types.ByteArray, [_types.ByteArray, _types.String]);
        var il = webCrypto.EcRawToSpki.GetILGenerator();

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
        il.Emit(OpCodes.Call, webCrypto.Curve);
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
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(ECDsa), "Create", Type.EmptyTypes)!);
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
    private void EmitWcEcSpkiToRaw(EmittedWebCryptoImplementation webCrypto, TypeBuilder tb)
    {
        webCrypto.EcSpkiToRaw = WcDefine(tb, "WcEcSpkiToRaw", _types.ByteArray, [_types.ByteArray]);
        var il = webCrypto.EcSpkiToRaw.GetILGenerator();

        var ecLocal = il.DeclareLocal(typeof(ECDsa));
        var paramsLocal = il.DeclareLocal(typeof(ECParameters));
        var xLocal = il.DeclareLocal(_types.ByteArray);
        var yLocal = il.DeclareLocal(_types.ByteArray);
        var resultLocal = il.DeclareLocal(_types.ByteArray);
        var outLocal = il.DeclareLocal(_types.Int32);

        il.Emit(OpCodes.Call, _types.GetMethod(typeof(ECDsa), "Create", Type.EmptyTypes)!);
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
    private void EmitWcImportRsaCheck(EmittedWebCryptoImplementation webCrypto, TypeBuilder tb)
    {
        webCrypto.ImportRsaCheck = WcDefine(tb, "WcImportRsaCheck", _types.Int32, [_types.ByteArray, _types.Boolean]);
        var il = webCrypto.ImportRsaCheck.GetILGenerator();

        var rsaLocal = il.DeclareLocal(typeof(RSA));
        var sizeLocal = il.DeclareLocal(_types.Int32);

        il.Emit(OpCodes.Call, _types.GetMethod(typeof(RSA), "Create", Type.EmptyTypes)!);
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
    private void EmitWcImportEcCheck(EmittedWebCryptoImplementation webCrypto, TypeBuilder tb)
    {
        webCrypto.ImportEcCheck = WcDefine(tb, "WcImportEcCheck", typeof(void), [_types.ByteArray, _types.Boolean]);
        var il = webCrypto.ImportEcCheck.GetILGenerator();

        var ecLocal = il.DeclareLocal(typeof(ECDsa));
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(ECDsa), "Create", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, ecLocal);
        EmitWcImportInto(il, ecLocal, typeof(ECDsa), derArgIndex: 0, isPrivateArgIndex: 1);
        il.Emit(OpCodes.Ldloc, ecLocal);
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emits: string WcBase64Url(byte[]).</summary>
    private void EmitWcBase64Url(EmittedWebCryptoImplementation webCrypto, TypeBuilder tb)
    {
        webCrypto.Base64Url = WcDefine(tb, "WcBase64Url", _types.String, [_types.ByteArray]);
        var il = webCrypto.Base64Url.GetILGenerator();

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
