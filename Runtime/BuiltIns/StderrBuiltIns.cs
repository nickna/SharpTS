using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides implementations for process.stderr members.
/// </summary>
/// <remarks>
/// Contains the write() method and isTTY property for stderr stream access.
/// Called by the interpreter when resolving property access on <see cref="SharpTSStderr"/>.
/// </remarks>
public static class StderrBuiltIns
{
    private static readonly BuiltInMethod _write = new("write", 1, Write);

    /// <summary>
    /// Gets a member of the stderr object by name.
    /// </summary>
    public static object? GetMember(SharpTSStderr stderr, string name)
    {
        return name switch
        {
            "write" => _write,
            "isTTY" => stderr.IsTTY,
            _ => null
        };
    }

    /// <summary>
    /// Writes data to stderr without a newline.
    /// </summary>
    private static object? Write(Interpreter i, object? receiver, List<object?> args)
    {
        if (args.Count > 0)
        {
            var data = args[0]?.ToString() ?? "";
            Console.Error.Write(data);
        }
        return true;
    }
}
