namespace SharpTS.Runtime.Types;

/// <summary>
/// Singleton representing the Object namespace.
/// Provides static methods like Object.keys, Object.values, etc.
/// </summary>
public class SharpTSObjectNamespace
{
    public static readonly SharpTSObjectNamespace Instance = new();
    private SharpTSObjectNamespace() { }

    public override string ToString() => "function Object() { [native code] }";
}
