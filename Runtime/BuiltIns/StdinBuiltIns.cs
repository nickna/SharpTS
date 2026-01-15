using SharpTS.Execution;
using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides implementations for process.stdin members.
/// </summary>
/// <remarks>
/// Contains the read() method and isTTY property for stdin stream access.
/// Called by the interpreter when resolving property access on <see cref="SharpTSStdin"/>.
/// </remarks>
public static class StdinBuiltIns
{
    private static readonly BuiltInMethod _read = new("read", 0, Read);

    /// <summary>
    /// Gets a member of the stdin object by name.
    /// </summary>
    public static object? GetMember(SharpTSStdin stdin, string name)
    {
        return name switch
        {
            "read" => _read,
            "isTTY" => stdin.IsTTY,
            _ => null
        };
    }

    /// <summary>
    /// Reads a line from stdin. Returns null at EOF.
    /// </summary>
    private static object? Read(Interpreter i, object? receiver, List<object?> args)
    {
        if (receiver is SharpTSStdin stdin)
        {
            return stdin.Read();
        }
        return Console.ReadLine();
    }
}
