using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits IL that reads option <c>name</c> (arg1) from options (arg0), which may be an
    /// emitted $Object or a Dictionary&lt;string,object?&gt; literal, into <paramref name="valueLocal"/>.
    /// Branches to <paramref name="notFoundLabel"/> if options is neither / the key is absent.
    /// </summary>
    private void EmitReadObjectOption(ILGenerator il, EmittedRuntime runtime, LocalBuilder valueLocal, System.Reflection.Emit.Label notFoundLabel)
    {
        var tryDictLabel = il.DefineLabel();
        var haveValueLabel = il.DefineLabel();

        // $Object path
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, tryDictLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetProperty);
        il.Emit(OpCodes.Stloc, valueLocal);
        il.Emit(OpCodes.Br, haveValueLabel);

        // Dictionary<string,object?> path
        il.MarkLabel(tryDictLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, notFoundLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue", [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, notFoundLabel);

        il.MarkLabel(haveValueLabel);
    }

    /// <summary>
    /// Registers the compiled wrappers completing KeyObject/DH/FIPS parity (#1059/#1060):
    /// generateKey/generateKeySync (secret KeyObjects), getFips/setFips.
    /// Called at the end of EmitCryptoMethods.
    /// </summary>
    private void EmitCryptoCompletionWrappers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var genSync = EmitCryptoGenerateSecretKeyCore(typeBuilder, runtime);

        // generateKeySync(type, options) -> $TSKeyObject
        EmitCryptoMethodWrapper(typeBuilder, runtime, "generateKeySync", 2, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, genSync);
        });

        // generateKey(type, options, callback) -> null (sync compute + callback)
        EmitCryptoAsyncWrapper(typeBuilder, runtime, "generateKey", 3, (il, rt) =>
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, genSync);
        });

        // getFips() -> 0.0
        EmitCryptoMethodWrapper(typeBuilder, runtime, "getFips", 0, il =>
        {
            il.Emit(OpCodes.Ldc_R8, 0.0);
            il.Emit(OpCodes.Box, _types.Double);
        });

        // setFips(enabled) -> null (throws when enabling FIPS in a non-FIPS build)
        EmitCryptoMethodWrapper(typeBuilder, runtime, "setFips", 1, il =>
        {
            var okLabel = il.DefineLabel();
            // if (arg0 truthy) throw
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.IsTruthy);
            il.Emit(OpCodes.Brfalse, okLabel);
            il.Emit(OpCodes.Ldstr, "Cannot set FIPS mode in a non-FIPS build.");
            il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([_types.String])!);
            il.Emit(OpCodes.Throw);
            il.MarkLabel(okLabel);
            il.Emit(OpCodes.Ldnull);
        });
    }

    /// <summary>
    /// Emits: static object GenerateSecretKeyCore(object type, object options) — validates
    /// 'hmac'/'aes' + { length } bits and returns a secret $TSKeyObject. Mirrors the interp
    /// CryptoModuleInterpreter.GenerateSecretKey error messages exactly.
    /// </summary>
    private MethodBuilder EmitCryptoGenerateSecretKeyCore(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GenerateSecretKeyCore",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);

        var il = method.GetILGenerator();

        var typeLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        EmitObjectToString(il);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, typeLocal);

        // int length = GetOptionInt(options, "length", -1)
        var lengthLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Call, runtime.GetOptionInt);
        il.Emit(OpCodes.Stloc, lengthLocal);

        var aesLabel = il.DefineLabel();
        var hmacLabel = il.DefineLabel();
        var okLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, "aes");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, aesLabel);
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, "hmac");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, hmacLabel);

        // else throw
        il.Emit(OpCodes.Ldstr, "crypto.generateKeySync: type must be 'hmac' or 'aes'");
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        // aes: length in {128,192,256}
        il.MarkLabel(aesLabel);
        var aesBadLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Ldc_I4, 128);
        il.Emit(OpCodes.Beq, okLabel);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Ldc_I4, 192);
        il.Emit(OpCodes.Beq, okLabel);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Ldc_I4, 256);
        il.Emit(OpCodes.Beq, okLabel);
        il.MarkLabel(aesBadLabel);
        il.Emit(OpCodes.Ldstr, "crypto.generateKeySync: AES key length must be 128, 192, or 256 bits");
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        // hmac: length >= 8 && length % 8 == 0
        il.MarkLabel(hmacLabel);
        var hmacBadLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Blt, hmacBadLabel);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Rem);
        il.Emit(OpCodes.Brfalse, okLabel);
        il.MarkLabel(hmacBadLabel);
        il.Emit(OpCodes.Ldstr, "crypto.generateKeySync: HMAC key length must be a positive multiple of 8 bits");
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        // ok: bytes = RandomNumberGenerator.GetBytes(length/8); return new $TSKeyObject(bytes)
        il.MarkLabel(okLabel);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Call, _types.RandomNumberGeneratorGetBytes);
        il.Emit(OpCodes.Newobj, runtime.TSKeyObjectCtorSecret);
        il.Emit(OpCodes.Ret);

        return method;
    }
}
