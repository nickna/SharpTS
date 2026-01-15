using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'readline' module.
/// </summary>
public static class ReadlineModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the readline module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["questionSync"] = new BuiltInMethod("questionSync", 1, QuestionSync),
            ["createInterface"] = new BuiltInMethod("createInterface", 0, 1, CreateInterface)
        };
    }

    private static object? QuestionSync(Interp interpreter, object? receiver, List<object?> args)
    {
        var query = args.Count > 0 ? args[0]?.ToString() ?? "" : "";
        Console.Write(query);
        return Console.ReadLine() ?? "";
    }

    private static object? CreateInterface(Interp interpreter, object? receiver, List<object?> args)
    {
        return new SharpTSReadlineInterface();
    }
}
