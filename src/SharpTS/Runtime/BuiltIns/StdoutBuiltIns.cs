using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides implementations for process.stdout members.
/// Delegates to SharpTSStdout.GetMember for all stream methods and properties.
/// </summary>
public static class StdoutBuiltIns
{
    public static object? GetMember(SharpTSStdout stdout, string name)
    {
        return stdout.GetMember(name);
    }
}
