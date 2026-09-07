using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// dgram module support for compiled TypeScript: dgram.createSocket().
/// Creates a $DatagramSocket instance (pure IL, no reflection needed).
/// </summary>
public partial class RuntimeEmitter
{
    private void EmitDgramModuleMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var dgram = runtime.RequireDgram();
        EmitDgramCreateSocket(typeBuilder, dgram);
        runtime.RegisterBuiltInModuleMethod("dgram", "createSocket", dgram.CreateSocket);
    }

    /// <summary>
    /// Emits: public static object DgramCreateSocket(object? typeOrOptions, object? callback)
    /// Creates a $DatagramSocket instance using the emitted constructor (pure IL).
    /// </summary>
    private void EmitDgramCreateSocket(TypeBuilder typeBuilder, EmittedDgramRuntime dgram)
    {
        var method = typeBuilder.DefineMethod(
            "DgramCreateSocket",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        dgram.CreateSocket = method;

        var il = method.GetILGenerator();

        // Extract type string from first argument
        // If arg is string, pass directly; default "udp4"
        var typeStringLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldstr, "udp4"); // default
        il.Emit(OpCodes.Stloc, typeStringLocal);

        var isStringLabel = il.DefineLabel();
        var extractedLabel = il.DefineLabel();

        // Check if arg0 is a string
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, isStringLabel);
        il.Emit(OpCodes.Stloc, typeStringLocal);
        il.Emit(OpCodes.Br, extractedLabel);

        il.MarkLabel(isStringLabel);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(extractedLabel);

        // return new $DatagramSocket(typeString)
        il.Emit(OpCodes.Ldloc, typeStringLocal);
        il.Emit(OpCodes.Newobj, dgram.SocketCtor);
        il.Emit(OpCodes.Ret);
    }
}
