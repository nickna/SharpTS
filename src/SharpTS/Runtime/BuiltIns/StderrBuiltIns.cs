using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides implementations for process.stderr members.
/// Delegates to SharpTSStderr.GetMember for all stream methods and properties.
/// </summary>
public static class StderrBuiltIns
{
    public static object? GetMember(SharpTSStderr stderr, string name)
    {
        return stderr.GetMember(name);
    }
}
