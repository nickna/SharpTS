namespace SharpTS.Runtime.Types;

/// <summary>
/// Singleton marker for the JavaScript undefined value.
/// </summary>
/// <remarks>
/// Represents the JavaScript undefined type - distinct from null.
/// Used to distinguish between uninitialized values and explicit null assignments.
///
/// Key semantics:
/// - typeof undefined === "undefined"
/// - undefined == null (loose equality) but undefined !== null (strict)
/// - undefined is falsy
/// - undefined coerces to NaN when used as a number (unlike null which coerces to 0)
/// </remarks>
public sealed class SharpTSUndefined
{
    /// <summary>
    /// The singleton instance representing the undefined value.
    /// </summary>
    public static readonly SharpTSUndefined Instance = new();

    private SharpTSUndefined() { }

    public override string ToString() => "undefined";
}
