using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    internal const string NativeReflectiveCallableMessage = "Reflective Invoke/Call callable fallback is not supported in native guest output. Use --target dll or --target exe.";

    private void EmitReflectiveCallableRejection(ILGenerator il, Module destination, MethodInfo rejection)
    {
        if (rejection.Module.ModuleVersionId != destination.ModuleVersionId || !rejection.IsStatic ||
            rejection.IsGenericMethod || rejection.ReturnType != _types.Object || rejection.GetParameters().Length != 0)
            throw new ArgumentException("Native callable refusal requires this output's exact static object() helper.", nameof(rejection));
        il.Emit(OpCodes.Call, rejection);
        il.Emit(OpCodes.Ret);
    }
}
