using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits: public static object CryptoCreateHash(string algorithm, object options)
    /// options may carry { outputLength } for the XOF hashes (#1062).
    /// </summary>
    private void EmitCryptoCreateHash(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoCreateHash",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object]);
        runtime.CryptoCreateHash = method;

        var il = method.GetILGenerator();

        // new $Hash(algorithm, GetOptionInt(options, "outputLength", -1))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "outputLength");
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Call, runtime.GetOptionInt);
        il.Emit(OpCodes.Newobj, runtime.TSHashCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoCreateHmac(string algorithm, byte[] key)
    /// </summary>
    private void EmitCryptoCreateHmac(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoCreateHmac",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.MakeArrayType(_types.Byte)]);
        runtime.CryptoCreateHmac = method;

        var il = method.GetILGenerator();

        // new $Hmac(algorithm, key) - use emitted type for standalone compatibility
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, runtime.TSHmacCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoCreateCipheriv(string algorithm, byte[] key, byte[] iv)
    /// </summary>
    private void EmitCryptoCreateCipheriv(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoCreateCipheriv",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte)]);
        runtime.CryptoCreateCipheriv = method;

        var il = method.GetILGenerator();

        // new $Cipher(algorithm, key, iv)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Newobj, runtime.TSCipherCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoCreateDecipheriv(string algorithm, byte[] key, byte[] iv)
    /// </summary>
    private void EmitCryptoCreateDecipheriv(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoCreateDecipheriv",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte)]);
        runtime.CryptoCreateDecipheriv = method;

        var il = method.GetILGenerator();

        // new $Decipher(algorithm, key, iv)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Newobj, runtime.TSDecipherCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoRandomBytes(int size)
    /// Returns a $Buffer containing random bytes.
    /// </summary>
    private void EmitCryptoRandomBytes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoRandomBytes",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Int32]);
        runtime.CryptoRandomBytes = method;

        var il = method.GetILGenerator();

        // var bytes = RandomNumberGenerator.GetBytes(size);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.RandomNumberGeneratorGetBytes);

        // Return new $Buffer(bytes)
        il.Emit(OpCodes.Newobj, runtime.TSBufferCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoRandomFillSync(object buffer, int offset, int size)
    /// Fills the buffer with random bytes in-place and returns the buffer.
    /// If size is -1, fills from offset to end of buffer.
    /// </summary>
    private void EmitCryptoRandomFillSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoRandomFillSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Int32, _types.Int32]);
        runtime.CryptoRandomFillSync = method;

        var il = method.GetILGenerator();

        // Local for buffer's byte[] data
        var dataLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        var sizeLocal = il.DeclareLocal(_types.Int32);
        var randomBytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));

        // Get buffer.Data (assume arg0 is $Buffer with Data property)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSBufferType);
        il.Emit(OpCodes.Call, runtime.TSBufferGetData);
        il.Emit(OpCodes.Stloc, dataLocal);

        // Calculate actual size: if size == -1, use data.Length - offset
        var sizeCalculatedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2); // size
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Bne_Un, sizeCalculatedLabel);

        // size = data.Length - offset
        il.Emit(OpCodes.Ldloc, dataLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_1); // offset
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, sizeLocal);
        var continueLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, continueLabel);

        il.MarkLabel(sizeCalculatedLabel);
        il.Emit(OpCodes.Ldarg_2); // size
        il.Emit(OpCodes.Stloc, sizeLocal);

        il.MarkLabel(continueLabel);

        // Generate random bytes: RandomNumberGenerator.GetBytes(size)
        il.Emit(OpCodes.Ldloc, sizeLocal);
        il.Emit(OpCodes.Call, _types.RandomNumberGeneratorGetBytes);
        il.Emit(OpCodes.Stloc, randomBytesLocal);

        // Array.Copy(randomBytes, 0, data, offset, size)
        il.Emit(OpCodes.Ldloc, randomBytesLocal); // sourceArray
        il.Emit(OpCodes.Ldc_I4_0);                // sourceIndex
        il.Emit(OpCodes.Ldloc, dataLocal);        // destinationArray
        il.Emit(OpCodes.Ldarg_1);                 // destinationIndex (offset)
        il.Emit(OpCodes.Ldloc, sizeLocal);        // length
        il.Emit(OpCodes.Call, _types.ArrayCopy5);

        // Return the buffer
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoTimingSafeEqual(byte[] a, byte[] b)
    /// Returns a boxed boolean indicating whether the buffers are equal using constant-time comparison.
    /// Throws if the buffers have different lengths (Node.js behavior).
    /// </summary>
    private void EmitCryptoTimingSafeEqual(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoTimingSafeEqual",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.MakeArrayType(_types.Byte), _types.MakeArrayType(_types.Byte)]);
        runtime.CryptoTimingSafeEqual = method;

        var il = method.GetILGenerator();

        // Check if lengths are equal
        var lengthsMatchLabel = il.DefineLabel();
        var aLenLocal = il.DeclareLocal(_types.Int32);
        var bLenLocal = il.DeclareLocal(_types.Int32);

        // Get length of a
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, aLenLocal);

        // Get length of b
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, bLenLocal);

        // if (a.Length == b.Length) goto lengthsMatch
        il.Emit(OpCodes.Ldloc, aLenLocal);
        il.Emit(OpCodes.Ldloc, bLenLocal);
        il.Emit(OpCodes.Beq, lengthsMatchLabel);

        // Throw exception with Node.js-style message
        // "Input buffers must have the same byte length. Received {aLen} and {bLen}"
        il.Emit(OpCodes.Ldstr, "crypto.timingSafeEqual: Input buffers must have the same byte length. Received ");
        il.Emit(OpCodes.Ldloca, aLenLocal);
        il.Emit(OpCodes.Call, _types.Int32.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldstr, " and ");
        il.Emit(OpCodes.Ldloca, bLenLocal);
        il.Emit(OpCodes.Call, _types.Int32.GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string), typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Newobj, _types.ArgumentExceptionCtorString);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(lengthsMatchLabel);

        // Convert byte[] to ReadOnlySpan<byte> and call CryptographicOperations.FixedTimeEquals
        // The implicit conversion from byte[] to ReadOnlySpan<byte> is done via op_Implicit
        var opImplicit = _types.ReadOnlySpanOfByte.GetMethod("op_Implicit", [typeof(byte[])])!;
        var fixedTimeEquals = _types.CryptographicOperations.GetMethod("FixedTimeEquals",
            [_types.ReadOnlySpanOfByte, _types.ReadOnlySpanOfByte])!;

        // Convert first byte[] to ReadOnlySpan<byte>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, opImplicit);

        // Convert second byte[] to ReadOnlySpan<byte>
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, opImplicit);

        // Call CryptographicOperations.FixedTimeEquals(span1, span2)
        il.Emit(OpCodes.Call, fixedTimeEquals);

        // Box the result and return
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoCreateSign(string algorithm)
    /// </summary>
    private void EmitCryptoCreateSign(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoCreateSign",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String]);
        runtime.CryptoCreateSign = method;

        var il = method.GetILGenerator();

        // new $Sign(algorithm) - use emitted type for standalone compatibility
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.TSSignCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoCreateVerify(string algorithm)
    /// </summary>
    private void EmitCryptoCreateVerify(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoCreateVerify",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String]);
        runtime.CryptoCreateVerify = method;

        var il = method.GetILGenerator();

        // new $Verify(algorithm) - use emitted type for standalone compatibility
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.TSVerifyCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoGetHashes()
    /// Returns an array of supported hash algorithm names.
    /// </summary>
    private void EmitCryptoGetHashes(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoGetHashes",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes);
        runtime.CryptoGetHashes = method;

        var il = method.GetILGenerator();

        // new List<object?>()
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);

        // Static family, then the platform-gated SHA-3/SHAKE members (#1062).
        // Mirrors CryptoAlgorithms.SupportedHashNames.
        foreach (var (hash, impl, guarded, _) in _hashTable)
        {
            var skipLabel = il.DefineLabel();
            if (guarded)
            {
                il.Emit(OpCodes.Call, impl.GetProperty("IsSupported")!.GetGetMethod()!);
                il.Emit(OpCodes.Brfalse, skipLabel);
            }
            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Ldstr, hash);
            il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add", [_types.Object])!);
            il.MarkLabel(skipLabel);
        }

        // return new $Array(list)
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoGetCiphers()
    /// Returns an array of supported cipher algorithm names.
    /// </summary>
    private void EmitCryptoGetCiphers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoGetCiphers",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes);
        runtime.CryptoGetCiphers = method;

        var il = method.GetILGenerator();

        // Create List<object?> with cipher names
        string[] ciphers = ["aes-128-cbc", "aes-192-cbc", "aes-256-cbc", "aes-128-gcm", "aes-192-gcm", "aes-256-gcm"];

        // new List<object?>()
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);

        // Add each cipher name to the list
        foreach (var cipher in ciphers)
        {
            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Ldstr, cipher);
            il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add", [_types.Object])!);
        }

        // return new $Array(list)
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
    }
}
