using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitRunClassDefinition(TypeBuilder typeBuilder, EmittedClassInitializationRuntime initialization)
    {
        // RunClassDefinition(Type): CLR wraps exceptions escaping a type
        // initializer in TypeInitializationException. JavaScript class
        // evaluation must expose the original guest exception instead, so all
        // definition-site forcing goes through this helper.
        var runClassDefinition = typeBuilder.DefineMethod(
            "RunClassDefinition",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Type]);
        initialization.RunDefinition = runClassDefinition;
        {
            var il = runClassDefinition.GetILGenerator();
            var initializationException = il.DeclareLocal(typeof(TypeInitializationException));
            var returnLabel = il.DefineLabel();
            var throwLabel = il.DefineLabel();

            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt,
                typeof(Type).GetProperty(nameof(Type.TypeHandle))!.GetGetMethod()!);
            il.Emit(OpCodes.Call, _types.RuntimeHelpersRunClassConstructor);
            il.Emit(OpCodes.Leave, returnLabel);

            il.BeginCatchBlock(typeof(TypeInitializationException));
            il.Emit(OpCodes.Stloc, initializationException);
            il.Emit(OpCodes.Ldloc, initializationException);
            il.Emit(OpCodes.Callvirt,
                typeof(Exception).GetProperty(nameof(Exception.InnerException))!.GetGetMethod()!);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, throwLabel);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldloc, initializationException);
            il.MarkLabel(throwLabel);
            il.Emit(OpCodes.Throw);
            il.EndExceptionBlock();

            il.MarkLabel(returnLabel);
            il.Emit(OpCodes.Ret);
        }
        initialization.MarkBodyEmitted();
    }
}
