using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides member dispatch for Headers instances.
/// </summary>
public static class HeadersBuiltIns
{
    public static object? GetMember(SharpTSHeaders receiver, string name)
    {
        return receiver.GetMember(name);
    }
}
