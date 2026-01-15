using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a Node.js-compatible readline Interface object.
/// </summary>
public class SharpTSReadlineInterface
{
    private bool _closed;

    /// <summary>
    /// Creates a new readline interface.
    /// </summary>
    public SharpTSReadlineInterface()
    {
        _closed = false;
    }

    /// <summary>
    /// Gets a member of this interface object.
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "question" => new BuiltInMethod("question", 2, Question),
            "close" => new BuiltInMethod("close", 0, Close),
            "prompt" => new BuiltInMethod("prompt", 0, Prompt),
            _ => null
        };
    }

    private object? Question(Interpreter interpreter, object? receiver, List<object?> args)
    {
        if (_closed || args.Count < 2)
            return null;

        var query = args[0]?.ToString() ?? "";
        var callback = args[1];

        Console.Write(query);
        var answer = Console.ReadLine() ?? "";

        // Invoke the callback with the answer
        if (callback is ISharpTSCallable callable)
        {
            callable.Call(interpreter, [answer]);
        }

        return null;
    }

    private object? Close(Interpreter interpreter, object? receiver, List<object?> args)
    {
        _closed = true;
        return null;
    }

    private object? Prompt(Interpreter interpreter, object? receiver, List<object?> args)
    {
        if (!_closed)
        {
            Console.Write("> ");
        }
        return null;
    }
}
