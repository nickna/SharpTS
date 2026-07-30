using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $CryptoPrimitives static class: the shared digest table (incl.
/// SHA-3/SHAKE, #1062), sign/verify hash-name parsing, and byte/encoding
/// conversion helpers used by $Hash/$Sign/$Verify and the crypto wrappers.
/// </summary>
/// <remarks>
/// Emitted BEFORE the crypto value types (RuntimeEmitter.cs UsesCrypto block) so
/// their constructors can reference these MethodBuilders. Everything here is
/// pure-BCL IL — no SharpTS.dll dependency (standalone constraint).
/// NOTE: Must stay in sync with Runtime/Types/CryptoAlgorithms.cs and
/// Runtime/Types/CryptoEncoding.cs.
/// </remarks>
public partial class RuntimeEmitter
{
    /// <summary>Digest table rows: name, one-shot HashData holder type, optional IsSupported gate, XOF default length.</summary>
    private static readonly (string Name, Type Impl, bool Guarded, int XofDefault)[] _hashTable =
    [
        ("md5", typeof(MD5), false, -1),
        ("sha1", typeof(SHA1), false, -1),
        ("sha256", typeof(SHA256), false, -1),
        ("sha384", typeof(SHA384), false, -1),
        ("sha512", typeof(SHA512), false, -1),
        ("sha3-256", typeof(SHA3_256), true, -1),
        ("sha3-384", typeof(SHA3_384), true, -1),
        ("sha3-512", typeof(SHA3_512), true, -1),
        ("shake128", typeof(Shake128), true, 16),
        ("shake256", typeof(Shake256), true, 32),
    ];

    private void EmitCryptoPrimitivesClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$CryptoPrimitives",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object);

        EmitCryptoValidateHashName(typeBuilder, runtime);
        EmitCryptoHashData(typeBuilder, runtime);
        EmitCryptoSignHashName(typeBuilder, runtime);
        EmitCryptoEncodeBytes(typeBuilder, runtime);
        EmitCryptoBytesFromAny(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits: public static string CryptoValidateHashName(string algorithm)
    /// Normalizes and validates a createHash/crypto.hash algorithm name, throwing
    /// for unknown names and platform-unsupported SHA-3/SHAKE.
    /// </summary>
    private void EmitCryptoValidateHashName(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoValidateHashName",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String]);
        runtime.CryptoValidateHashName = method;

        var il = method.GetILGenerator();
        var lowerLocal = il.DeclareLocal(_types.String);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, lowerLocal);

        foreach (var (name, impl, guarded, _) in _hashTable)
        {
            var nextLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, lowerLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
            il.Emit(OpCodes.Brfalse, nextLabel);

            if (guarded)
            {
                var supportedLabel = il.DefineLabel();
                il.Emit(OpCodes.Call, impl.GetProperty("IsSupported")!.GetGetMethod()!);
                il.Emit(OpCodes.Brtrue, supportedLabel);
                il.Emit(OpCodes.Ldstr, $"Unsupported hash algorithm: {name} (not supported on this platform)");
                il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ArgumentException, [_types.String])!);
                il.Emit(OpCodes.Throw);
                il.MarkLabel(supportedLabel);
            }

            il.Emit(OpCodes.Ldloc, lowerLocal);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(nextLabel);
        }

        il.Emit(OpCodes.Ldstr, "Unsupported hash algorithm: ");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ArgumentException, [_types.String])!);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits: public static byte[] CryptoHashData(string algorithm, byte[] data, int outputLength)
    /// One-shot digest over the full algorithm table; outputLength applies to XOFs only.
    /// </summary>
    private void EmitCryptoHashData(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoHashData",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ByteArray,
            [_types.String, _types.ByteArray, _types.Int32]);
        runtime.CryptoHashData = method;

        var il = method.GetILGenerator();
        var lowerLocal = il.DeclareLocal(_types.String);

        // lower = CryptoValidateHashName(algorithm) — also enforces the platform gates
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.CryptoValidateHashName);
        il.Emit(OpCodes.Stloc, lowerLocal);

        foreach (var (name, impl, _, xofDefault) in _hashTable)
        {
            var nextLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, lowerLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
            il.Emit(OpCodes.Brfalse, nextLabel);

            if (xofDefault > 0)
            {
                // len = outputLength > 0 ? outputLength : default; Shake*.HashData(data, len)
                var useDefaultLabel = il.DefineLabel();
                var callLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ble, useDefaultLabel);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Br, callLabel);
                il.MarkLabel(useDefaultLabel);
                il.Emit(OpCodes.Ldc_I4, xofDefault);
                il.MarkLabel(callLabel);
                il.Emit(OpCodes.Call, impl.GetMethod("HashData", [typeof(byte[]), typeof(int)])!);
            }
            else
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, impl.GetMethod("HashData", [typeof(byte[])])!);
            }
            il.Emit(OpCodes.Ret);

            il.MarkLabel(nextLabel);
        }

        // Unreachable (validate already threw), but the verifier needs a terminator.
        il.Emit(OpCodes.Ldstr, "Unsupported hash algorithm: ");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ArgumentException, [_types.String])!);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits: public static HashAlgorithmName CryptoSignHashName(string algorithm)
    /// Strips a leading "rsa-"/"ecdsa-" and parses the digest for Sign/Verify (#1055).
    /// </summary>
    private void EmitCryptoSignHashName(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoSignHashName",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.HashAlgorithmName,
            [_types.String]);
        runtime.CryptoSignHashName = method;

        var il = method.GetILGenerator();
        var lowerLocal = il.DeclareLocal(_types.String);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, lowerLocal);

        // Strip "rsa-" / "ecdsa-" prefixes
        foreach (var (prefix, len) in new[] { ("rsa-", 4), ("ecdsa-", 6) })
        {
            var noPrefixLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, lowerLocal);
            il.Emit(OpCodes.Ldstr, prefix);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "StartsWith", [_types.String])!);
            il.Emit(OpCodes.Brfalse, noPrefixLabel);
            il.Emit(OpCodes.Ldloc, lowerLocal);
            il.Emit(OpCodes.Ldc_I4, len);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", [_types.Int32])!);
            il.Emit(OpCodes.Stloc, lowerLocal);
            il.MarkLabel(noPrefixLabel);
        }

        // Digest table for signing: MD5/SHA-1/2 family + SHA-3 (HashAlgorithmName properties)
        foreach (var (name, prop) in new[]
        {
            ("md5", "MD5"), ("sha1", "SHA1"), ("sha256", "SHA256"), ("sha384", "SHA384"), ("sha512", "SHA512"),
            ("sha3-256", "SHA3_256"), ("sha3-384", "SHA3_384"), ("sha3-512", "SHA3_512"),
        })
        {
            var nextLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, lowerLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
            il.Emit(OpCodes.Brfalse, nextLabel);
            il.Emit(OpCodes.Call, _types.GetProperty(_types.HashAlgorithmName, prop)!.GetGetMethod()!);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(nextLabel);
        }

        il.Emit(OpCodes.Ldstr, "Unsupported signing algorithm: ");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ArgumentException, [_types.String])!);
        il.Emit(OpCodes.Throw);
    }

    /// <summary>
    /// Emits: public static object CryptoEncodeBytes(byte[] bytes, string encoding)
    /// hex/base64/base64url → string; anything else → $Buffer. Mirrors CryptoEncoding.ToBufferOrString.
    /// </summary>
    private void EmitCryptoEncodeBytes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoEncodeBytes",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ByteArray, _types.String]);
        runtime.CryptoEncodeBytes = method;

        var il = method.GetILGenerator();
        var bufferLabel = il.DefineLabel();
        var hexLabel = il.DefineLabel();
        var base64Label = il.DefineLabel();
        var base64UrlLabel = il.DefineLabel();
        var lowerLocal = il.DeclareLocal(_types.String);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, bufferLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, lowerLocal);

        foreach (var (name, label) in new[] { ("hex", hexLabel), ("base64", base64Label), ("base64url", base64UrlLabel) })
        {
            il.Emit(OpCodes.Ldloc, lowerLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", [_types.String, _types.String])!);
            il.Emit(OpCodes.Brtrue, label);
        }
        il.Emit(OpCodes.Br, bufferLabel);

        il.MarkLabel(hexLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.ConvertToHexString);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "ToLowerInvariant")!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(base64Label);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.ConvertToBase64String);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(base64UrlLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.ConvertToBase64String);
        // .TrimEnd('=') — TrimEnd takes a params char[] (there is no char overload)
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, typeof(char));
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldc_I4, (int)'=');
        il.Emit(OpCodes.Stelem_I2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "TrimEnd", [typeof(char[])])!);
        il.Emit(OpCodes.Ldc_I4, (int)'+');
        il.Emit(OpCodes.Ldc_I4, (int)'-');
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Replace", [typeof(char), typeof(char)])!);
        il.Emit(OpCodes.Ldc_I4, (int)'/');
        il.Emit(OpCodes.Ldc_I4, (int)'_');
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Replace", [typeof(char), typeof(char)])!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(bufferLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static byte[] CryptoBytesFromAny(object data)
    /// string → UTF-8; $Buffer → data; byte[] → pass-through; else ToString → UTF-8.
    /// </summary>
    private void EmitCryptoBytesFromAny(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoBytesFromAny",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ByteArray,
            [_types.Object]);
        runtime.CryptoBytesFromAny = method;

        var il = method.GetILGenerator();
        var notStringLabel = il.DefineLabel();
        var notBufferLabel = il.DefineLabel();
        var notArrayLabel = il.DefineLabel();

        // if (data is string) return UTF8.GetBytes((string)data)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notStringLabel);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.Encoding, "UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Encoding, "GetBytes", [_types.String])!);
        il.Emit(OpCodes.Ret);

        // if (data is $Buffer) return buffer.GetData()
        il.MarkLabel(notStringLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSBufferType);
        il.Emit(OpCodes.Brfalse, notBufferLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSBufferType);
        il.Emit(OpCodes.Call, runtime.TSBufferGetData);
        il.Emit(OpCodes.Ret);

        // if (data is byte[]) return it
        il.MarkLabel(notBufferLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ByteArray);
        il.Emit(OpCodes.Brfalse, notArrayLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ByteArray);
        il.Emit(OpCodes.Ret);

        // fallback: UTF8.GetBytes(data.ToString())
        il.MarkLabel(notArrayLabel);
        il.Emit(OpCodes.Call, _types.GetProperty(_types.Encoding, "UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Encoding, "GetBytes", [_types.String])!);
        il.Emit(OpCodes.Ret);
    }
}
