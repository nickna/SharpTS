using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides implementations for process.stdin members.
/// Delegates to SharpTSStdin (which extends SharpTSReadable) for stream methods.
/// SharpTSStdin.GetMember handles isTTY and reader thread integration.
/// </summary>
public static class StdinBuiltIns
{
    /// <summary>
    /// Gets a member of the stdin object by name.
    /// </summary>
    public static object? GetMember(SharpTSStdin stdin, string name)
    {
        return stdin.GetMember(name);
    }
}
