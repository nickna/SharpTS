using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits process global helper methods (GetEnv, GetArgv).
    /// </summary>
    private void EmitProcessMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitProcessGetEnv(typeBuilder, runtime);
        EmitProcessGetArgv(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits: public static object ProcessGetEnv()
    /// Creates a Dictionary containing environment variables and wraps it as an object.
    /// </summary>
    private void EmitProcessGetEnv(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ProcessGetEnv",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.ProcessGetEnv = method;

        var il = method.GetILGenerator();

        // Create new Dictionary<string, object?>
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        // Get environment variables: Environment.GetEnvironmentVariables()
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Environment, "GetEnvironmentVariables"));
        var envVarsLocal = il.DeclareLocal(_types.IDictionary);
        il.Emit(OpCodes.Stloc, envVarsLocal);

        // Get enumerator
        il.Emit(OpCodes.Ldloc, envVarsLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IDictionary, "GetEnumerator"));
        var enumeratorLocal = il.DeclareLocal(_types.IDictionaryEnumerator);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        // Loop: while (enumerator.MoveNext())
        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, enumeratorLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
        il.Emit(OpCodes.Brfalse, loopEnd);

        // Get current entry key and value
        il.Emit(OpCodes.Ldloc, enumeratorLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.IDictionaryEnumerator, "Key").GetMethod!);
        var keyLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, keyLocal);

        il.Emit(OpCodes.Ldloc, enumeratorLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.IDictionaryEnumerator, "Value").GetMethod!);
        var valueLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, valueLocal);

        // dict[key.ToString()] = value?.ToString()
        il.Emit(OpCodes.Ldloc, dictLocal);

        // key.ToString()
        il.Emit(OpCodes.Ldloc, keyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));

        // value?.ToString() - check if value is null
        var valueNotNull = il.DefineLabel();
        var afterValue = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Brtrue, valueNotNull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, afterValue);
        il.MarkLabel(valueNotNull);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.MarkLabel(afterValue);

        // Set the dictionary entry
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));

        il.Emit(OpCodes.Br, loopStart);
        il.MarkLabel(loopEnd);

        // Wrap in SharpTSObject and return
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Call, runtime.CreateObject);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object ProcessGetArgv()
    /// Creates a SharpTSArray containing command line arguments.
    /// </summary>
    private void EmitProcessGetArgv(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ProcessGetArgv",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes
        );
        runtime.ProcessGetArgv = method;

        var il = method.GetILGenerator();

        // Get command line args: Environment.GetCommandLineArgs()
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Environment, "GetCommandLineArgs"));

        // Create array from string[]
        il.Emit(OpCodes.Call, runtime.CreateArray);

        il.Emit(OpCodes.Ret);
    }
}
