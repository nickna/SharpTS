using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides implementations for process.stdout members.
/// </summary>
/// <remarks>
/// Contains the write() method and isTTY property for stdout stream access.
/// Called by the interpreter when resolving property access on <see cref="SharpTSStdout"/>.
/// </remarks>
public static class StdoutBuiltIns
{
    private static readonly BuiltInMethod _write = new("write", 1, Write);

    /// <summary>
    /// Gets a member of the stdout object by name.
    /// </summary>
    public static object? GetMember(SharpTSStdout stdout, string name)
    {
        return name switch
        {
            "write" => _write,
            "isTTY" => stdout.IsTTY,
            _ => null
        };
    }

    /// <summary>
    /// Writes data to stdout without a newline.
    /// </summary>
    private static object? Write(Interpreter i, object? receiver, List<object?> args)
    {
        if (args.Count > 0)
        {
            var data = args[0]?.ToString() ?? "";
            Console.Write(data);
        }
        return true;
    }
}
