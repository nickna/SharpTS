using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Trusted-host bridge for executing a source string through SharpTS's public embedding facade.
/// </summary>
public static class SourceExecutionModuleInterpreter
{
    public static Dictionary<string, object?> GetExports() => new()
    {
        ["runSourceJson"] = BuiltInMethod.CreateV2("runSourceJson", 3, 3, RunSourceJson),
        ["configureUntrustedProcess"] = BuiltInMethod.CreateV2(
            "configureUntrustedProcess", 1, 1, ConfigureUntrustedProcess)
    };

    private static RuntimeValue ConfigureUntrustedProcess(
        Execution.Interpreter interpreter,
        RuntimeValue receiver,
        ReadOnlySpan<RuntimeValue> args)
    {
        SourceExecutionService.ConfigureUntrustedProcess(args[0].ToObject());
        return RuntimeValue.Undefined;
    }

    private static RuntimeValue RunSourceJson(
        Execution.Interpreter interpreter,
        RuntimeValue receiver,
        ReadOnlySpan<RuntimeValue> args)
    {
        var json = SourceExecutionService.RunJson(
            args[0].ToObject(),
            args[1].ToObject(),
            args[2].ToObject());
        return RuntimeValue.FromString(json);
    }
}
