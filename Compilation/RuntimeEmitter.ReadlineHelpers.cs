using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits readline module helper methods.
    /// </summary>
    private void EmitReadlineMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitReadlineQuestionSync(typeBuilder, runtime);
        EmitReadlineCreateInterface(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits: public static string ReadlineQuestionSync(string query)
    /// </summary>
    private void EmitReadlineQuestionSync(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReadlineQuestionSync",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String]);
        runtime.ReadlineQuestionSync = method;

        var il = method.GetILGenerator();

        // Console.Write(query)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Console, "Write", _types.String));

        // return Console.ReadLine() ?? ""
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Console, "ReadLine"));

        var notNull = il.DefineLabel();
        var end = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brtrue, notNull);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Br, end);
        il.MarkLabel(notNull);
        il.MarkLabel(end);

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object ReadlineCreateInterface(object options)
    /// </summary>
    private void EmitReadlineCreateInterface(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ReadlineCreateInterface",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        runtime.ReadlineCreateInterface = method;

        var il = method.GetILGenerator();

        // Return new SharpTSReadlineInterface()
        var interfaceType = typeof(Runtime.Types.SharpTSReadlineInterface);
        var ctor = interfaceType.GetConstructor(Type.EmptyTypes)!;

        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Ret);
    }
}
