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

        // Emit wrapper methods for named imports
        EmitCryptoMethodWrappers(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits wrapper methods for crypto module functions to support named imports.
    /// Each wrapper takes individual object parameters (compatible with TSFunction.Invoke).
    /// </summary>
    private void EmitCryptoMethodWrappers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // createHash(algorithm) -> $Hash
        EmitCryptoMethodWrapper(typeBuilder, runtime, "createHash", 1, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToString(il);
            il.Emit(OpCodes.Call, runtime.CryptoCreateHash);
        });

        // randomBytes(size) -> $Array
        EmitCryptoMethodWrapper(typeBuilder, runtime, "randomBytes", 1, il =>
        {
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToInt32(il);
            il.Emit(OpCodes.Call, runtime.CryptoRandomBytes);
        });

        // randomUUID() -> string
        EmitCryptoMethodWrapper(typeBuilder, runtime, "randomUUID", 0, il =>
        {
            var guidLocal = il.DeclareLocal(_types.Guid);
            il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Guid, "NewGuid"));
            il.Emit(OpCodes.Stloc, guidLocal);
            il.Emit(OpCodes.Ldloca, guidLocal);
            il.Emit(OpCodes.Constrained, _types.Guid);
            il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        });

        // randomInt(min?, max?) -> number
        EmitCryptoMethodWrapper(typeBuilder, runtime, "randomInt", 2, il =>
        {
            // If arg0 is null, return 0
            var hasArg0Label = il.DefineLabel();
            var hasArg1Label = il.DefineLabel();
            var doRandomLabel = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brtrue, hasArg0Label);

            // No args - return 0
            il.Emit(OpCodes.Ldc_R8, 0.0);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(hasArg0Label);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Brtrue, hasArg1Label);

            // One arg - randomInt(max): range [0, max)
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToInt32(il);
            il.Emit(OpCodes.Br, doRandomLabel);

            // Two args - randomInt(min, max): range [min, max)
            il.MarkLabel(hasArg1Label);
            il.Emit(OpCodes.Ldarg_0);
            EmitObjectToInt32(il);
            il.Emit(OpCodes.Ldarg_1);
            EmitObjectToInt32(il);

            il.MarkLabel(doRandomLabel);
            il.Emit(OpCodes.Call, _types.GetMethod(
                _types.RandomNumberGenerator,
                "GetInt32",
                _types.Int32, _types.Int32));
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
        });
    }

    /// <summary>
    /// Emits a wrapper method for a crypto module function.
    /// Takes individual object parameters (compatible with TSFunction.Invoke).
    /// </summary>
    private void EmitCryptoMethodWrapper(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime,
        string methodName,
        int paramCount,
        Action<ILGenerator> emitBody)
    {
        var paramTypes = new Type[paramCount];
        for (int i = 0; i < paramCount; i++)
            paramTypes[i] = _types.Object;

        var method = typeBuilder.DefineMethod(
            "CryptoWrapper_" + methodName,
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            paramTypes);

        var il = method.GetILGenerator();
        emitBody(il);
        il.Emit(OpCodes.Ret);

        // Register the wrapper for named imports
        runtime.RegisterBuiltInModuleMethod("crypto", methodName, method);
    }

    /// <summary>
    /// Emits code to convert an object to string (handles null).
    /// </summary>
    private void EmitObjectToString(ILGenerator il)
    {
        // obj?.ToString() ?? ""
        var isNullLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, isNullLabel);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(isNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");

        il.MarkLabel(endLabel);
    }

    /// <summary>
    /// Emits code to convert an object to int32 (handles null and boxed doubles).
    /// </summary>
    private void EmitObjectToInt32(ILGenerator il)
    {
        // Check for null
        var notNullLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, notNullLabel);

        // Unbox as double first (TypeScript numbers are doubles)
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_0);

        il.MarkLabel(endLabel);
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

        // new $Hash(algorithm) - use emitted type for standalone compatibility
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.TSHashCtor);
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

        // Create List<object?> for $Array
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

        // Return new $Array(list)
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
    }
}
