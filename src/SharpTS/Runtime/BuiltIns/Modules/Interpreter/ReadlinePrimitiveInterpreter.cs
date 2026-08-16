using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of <c>primitive:readline</c>. Exposes
/// <c>questionSync(query)</c> and <c>createInterface(options)</c>; the TS
/// facade at <c>stdlib/node/readline.ts</c> wraps the returned Interface
/// instance and forwards method calls dynamically.
/// </summary>
public static class ReadlinePrimitiveInterpreter
{
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["questionSync"] = BuiltInMethod.CreateV2("questionSync", 1, QuestionSync),
            ["createInterface"] = BuiltInMethod.CreateV2("createInterface", 0, 1, CreateInterface)
        };
    }

    private static RuntimeValue QuestionSync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var query = args.Length > 0 ? args[0].ToObject()?.ToString() ?? "" : "";
        Console.Write(query);
        return RuntimeValue.FromString(Console.ReadLine() ?? "");
    }

    private static RuntimeValue CreateInterface(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var options = args.Length > 0 ? args[0].ToObject() as SharpTSObject : null;
        return RuntimeValue.FromObject(new SharpTSReadlineInterface(options));
    }
}
