using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits crypto module helper methods.
    /// </summary>
    private void EmitCryptoMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitCryptoCreateHash(typeBuilder, runtime);
        EmitCryptoRandomBytes(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits: public static object CryptoCreateHash(string algorithm)
    /// </summary>
    private void EmitCryptoCreateHash(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoCreateHash",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String]);
        runtime.CryptoCreateHash = method;

        var il = method.GetILGenerator();

        // new SharpTSHash(algorithm)
        var hashType = typeof(Runtime.Types.SharpTSHash);
        var ctor = hashType.GetConstructor([typeof(string)])!;

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoRandomBytes(int size)
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
        var bytesLocal = il.DeclareLocal(_types.MakeArrayType(_types.Byte));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RandomNumberGenerator).GetMethod("GetBytes", [typeof(int)])!);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // Create List<object?> for SharpTSArray
        var listType = _types.ListOfObject;
        var listCtor = _types.GetConstructor(listType, _types.Int32);
        var listAdd = _types.GetMethod(listType, "Add", _types.Object);

        var listLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, listCtor);
        il.Emit(OpCodes.Stloc, listLocal);

        // Loop through bytes and add to list
        var indexLocal = il.DeclareLocal(_types.Int32);
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, loopEnd);

        // list.Add((double)bytes[i])
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_U1);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Callvirt, listAdd);

        // i++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);

        // Return new SharpTSArray(list)
        var arrayType = typeof(Runtime.Types.SharpTSArray);
        var arrayCtor = arrayType.GetConstructor([typeof(List<object?>)])!;
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Newobj, arrayCtor);
        il.Emit(OpCodes.Ret);
    }
}
