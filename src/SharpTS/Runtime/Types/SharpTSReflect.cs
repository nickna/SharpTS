namespace SharpTS.Runtime.Types;

/// <summary>
/// Canonical non-callable value for the JavaScript Reflect namespace.
/// </summary>
/// <remarks>
/// The registry uses this marker for bare and globalThis value reads. Its
/// static methods keep their existing ReflectBuiltIns dispatch, including
/// reads through an alias of the namespace object.
/// </remarks>
public sealed class SharpTSReflect
{
    public static readonly SharpTSReflect Instance = new();

    private SharpTSReflect() { }

    public override string ToString() => "[object Reflect]";
}
