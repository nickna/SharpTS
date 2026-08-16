using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;

namespace SharpTS.Compilation;

/// <summary>
/// Callback-based wrappers for the epic-#1054 crypto additions owned by the
/// primes/randomFill slice (randomFill, generatePrime, checkPrime). These mirror
/// the existing crypto async wrappers: run the work synchronously, then invoke the
/// guest callback with (null, result) or (errorMessage, null). Pure-BCL IL.
/// (generateKey lives in the #1058/#1059 KeyObject slice.)
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits a callback wrapper that computes a value synchronously and invokes
    /// callback(null, result) / callback(errMsg, null). The callback local is
    /// chosen by the caller-provided emitter which also pushes the sync result.
    /// </summary>
    private void EmitCryptoResultCallback(ILGenerator il, EmittedRuntime runtime, LocalBuilder callbackLoc, LocalBuilder resultLoc)
    {
        il.Emit(OpCodes.Ldloc, callbackLoc);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, resultLoc);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
    }

    private void EmitCryptoErrorCallback(ILGenerator il, EmittedRuntime runtime, LocalBuilder callbackLoc, LocalBuilder exLoc)
    {
        il.Emit(OpCodes.Ldloc, callbackLoc);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, exLoc);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(typeof(Exception), "get_Message"));
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Pop);
    }

    /// <summary>Picks the callback ($TSFunction) from the trailing args into callbackLoc.</summary>
    private void EmitPickCallback(ILGenerator il, EmittedRuntime runtime, LocalBuilder callbackLoc, params int[] argIndicesHighToLow)
    {
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, callbackLoc);
        foreach (var argIndex in argIndicesHighToLow)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg, argIndex);
            il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
            il.Emit(OpCodes.Brfalse, skipLabel);
            il.Emit(OpCodes.Ldarg, argIndex);
            il.Emit(OpCodes.Stloc, callbackLoc);
            il.MarkLabel(skipLabel);
        }
    }

    /// <summary>randomFill(buffer, offset?|cb, size?|cb, cb) → callback(null, buffer).</summary>
    private void EmitCryptoRandomFillAsyncWrapper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoWrapper_randomFill",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object, _types.Object]);

        var il = method.GetILGenerator();
        var callbackLoc = il.DeclareLocal(_types.Object);
        var resultLoc = il.DeclareLocal(_types.Object);
        var dataLoc = il.DeclareLocal(_types.ByteArray);
        var offsetLoc = il.DeclareLocal(_types.Int32);
        var sizeLoc = il.DeclareLocal(_types.Int32);
        var endLabel = il.DefineLabel();

        // callback = last $TSFunction among args 3,2,1
        EmitPickCallback(il, runtime, callbackLoc, 3, 2, 1);

        // offset = arg1 is double ? (int)arg1 : 0
        EmitIntArgOrDefault(il, 1, 0, offsetLoc);
        // data = ((buffer as $Buffer).GetData())
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSBufferType);
        il.Emit(OpCodes.Call, runtime.TSBufferGetData);
        il.Emit(OpCodes.Stloc, dataLoc);
        // size = arg2 is double ? (int)arg2 : data.Length - offset
        var haveSizeLabel = il.DefineLabel();
        var sizeSetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, haveSizeLabel);
        il.Emit(OpCodes.Ldloc, dataLoc);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldloc, offsetLoc);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Stloc, sizeLoc);
        il.Emit(OpCodes.Br, sizeSetLabel);
        il.MarkLabel(haveSizeLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, sizeLoc);
        il.MarkLabel(sizeSetLabel);

        il.BeginExceptionBlock();
        // var rnd = RandomNumberGenerator.GetBytes(size); Array.Copy(rnd, 0, data, offset, size)
        var rndLoc = il.DeclareLocal(_types.ByteArray);
        il.Emit(OpCodes.Ldloc, sizeLoc);
        il.Emit(OpCodes.Call, _types.RandomNumberGeneratorGetBytes);
        il.Emit(OpCodes.Stloc, rndLoc);
        il.Emit(OpCodes.Ldloc, rndLoc);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, dataLoc);
        il.Emit(OpCodes.Ldloc, offsetLoc);
        il.Emit(OpCodes.Ldloc, sizeLoc);
        il.Emit(OpCodes.Call, _types.ArrayCopy5);
        // result = buffer
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stloc, resultLoc);
        EmitCryptoResultCallback(il, runtime, callbackLoc, resultLoc);
        il.Emit(OpCodes.Leave, endLabel);
        il.BeginCatchBlock(typeof(Exception));
        var exLoc = il.DeclareLocal(typeof(Exception));
        il.Emit(OpCodes.Stloc, exLoc);
        EmitCryptoErrorCallback(il, runtime, callbackLoc, exLoc);
        il.Emit(OpCodes.Leave, endLabel);
        il.EndExceptionBlock();

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        runtime.RegisterBuiltInModuleMethod("crypto", "randomFill", method);
    }

    /// <summary>generateKey(type, options, callback) → callback(null, keyObject).</summary>
    /// <summary>generatePrime(size, options?|cb, cb) → callback(null, buffer|bigint).</summary>
    private void EmitCryptoGeneratePrimeAsyncWrapper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoWrapper_generatePrime",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]);

        var il = method.GetILGenerator();
        var callbackLoc = il.DeclareLocal(_types.Object);
        var optionsLoc = il.DeclareLocal(_types.Object);
        var resultLoc = il.DeclareLocal(_types.Object);
        var endLabel = il.DefineLabel();

        // callback = arg2 ?? arg1; options = arg2 != null ? arg1 : null
        var arg2CbLabel = il.DefineLabel();
        var afterCbLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brtrue, arg2CbLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, callbackLoc);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, optionsLoc);
        il.Emit(OpCodes.Br, afterCbLabel);
        il.MarkLabel(arg2CbLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, callbackLoc);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, optionsLoc);
        il.MarkLabel(afterCbLabel);

        il.BeginExceptionBlock();
        // bits = (int)(double)arg0 ; result = CryptoGeneratePrimeSyncObj(bits, options)
        il.Emit(OpCodes.Ldarg_0);
        EmitObjectToInt32(il);
        il.Emit(OpCodes.Ldloc, optionsLoc);
        il.Emit(OpCodes.Call, runtime.CryptoGeneratePrimeSyncObj);
        il.Emit(OpCodes.Stloc, resultLoc);
        EmitCryptoResultCallback(il, runtime, callbackLoc, resultLoc);
        il.Emit(OpCodes.Leave, endLabel);
        il.BeginCatchBlock(typeof(Exception));
        var exLoc = il.DeclareLocal(typeof(Exception));
        il.Emit(OpCodes.Stloc, exLoc);
        EmitCryptoErrorCallback(il, runtime, callbackLoc, exLoc);
        il.Emit(OpCodes.Leave, endLabel);
        il.EndExceptionBlock();

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        runtime.RegisterBuiltInModuleMethod("crypto", "generatePrime", method);
    }

    /// <summary>checkPrime(candidate, options?|cb, cb) → callback(null, bool).</summary>
    private void EmitCryptoCheckPrimeAsyncWrapper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoWrapper_checkPrime",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]);

        var il = method.GetILGenerator();
        var callbackLoc = il.DeclareLocal(_types.Object);
        var optionsLoc = il.DeclareLocal(_types.Object);
        var resultLoc = il.DeclareLocal(_types.Object);
        var endLabel = il.DefineLabel();

        var arg2CbLabel = il.DefineLabel();
        var afterCbLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brtrue, arg2CbLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, callbackLoc);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, optionsLoc);
        il.Emit(OpCodes.Br, afterCbLabel);
        il.MarkLabel(arg2CbLabel);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, callbackLoc);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, optionsLoc);
        il.MarkLabel(afterCbLabel);

        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, optionsLoc);
        il.Emit(OpCodes.Call, runtime.CryptoCheckPrimeSyncObj);
        il.Emit(OpCodes.Stloc, resultLoc);
        EmitCryptoResultCallback(il, runtime, callbackLoc, resultLoc);
        il.Emit(OpCodes.Leave, endLabel);
        il.BeginCatchBlock(typeof(Exception));
        var exLoc = il.DeclareLocal(typeof(Exception));
        il.Emit(OpCodes.Stloc, exLoc);
        EmitCryptoErrorCallback(il, runtime, callbackLoc, exLoc);
        il.Emit(OpCodes.Leave, endLabel);
        il.EndExceptionBlock();

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        runtime.RegisterBuiltInModuleMethod("crypto", "checkPrime", method);
    }

    /// <summary>Inline: dest = argIndex is double ? (int)argIndex : def.</summary>
    private void EmitIntArgOrDefault(ILGenerator il, int argIndex, int def, LocalBuilder dest)
    {
        var haveLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, haveLabel);
        il.Emit(OpCodes.Ldc_I4, def);
        il.Emit(OpCodes.Stloc, dest);
        il.Emit(OpCodes.Br, doneLabel);
        il.MarkLabel(haveLabel);
        il.Emit(OpCodes.Ldarg, argIndex);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, dest);
        il.MarkLabel(doneLabel);
    }
}
