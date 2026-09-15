using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private const string SourceExecutionServiceLateBoundName =
        "SharpTS.Execution.SourceExecutionService, SharpTS";

    private void EmitSourceExecutionMethods(TypeBuilder typeBuilder, EmittedSourceExecutionRuntime sourceExecution, Action<string, MethodBuilder> registerModuleMethod)
    {
        var method = typeBuilder.DefineMethod(
            "SourceExecutionRunJson",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.Object]);
        sourceExecution.RunJson = method;
        registerModuleMethod("runSourceJson", method);

        var il = method.GetILGenerator();
        EmitReflectionCall(
            il,
            SourceExecutionServiceLateBoundName,
            "RunJson",
            3);
        il.Emit(OpCodes.Ret);

        var configureMethod = typeBuilder.DefineMethod(
            "SourceExecutionConfigureUntrustedProcess",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]);
        sourceExecution.ConfigureUntrustedProcess = configureMethod;
        registerModuleMethod("configureUntrustedProcess", configureMethod);

        il = configureMethod.GetILGenerator();
        EmitReflectionCall(
            il,
            SourceExecutionServiceLateBoundName,
            "ConfigureUntrustedProcess",
            1);
        il.Emit(OpCodes.Ret);
    }
}
