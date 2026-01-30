namespace SharpTS.Parsing.Visitors;

/// <summary>
/// Unit type for void-returning visitors that must implement IStmtVisitor{T}.
/// Used when a visitor needs to implement IStmtVisitor but doesn't produce meaningful values.
/// </summary>
/// <remarks>
/// Since C# doesn't have a built-in unit/void type that can be used as a generic argument,
/// this struct provides a lightweight alternative. All instances are equivalent, and the
/// default value (<see cref="Instance"/>) should be used for returns.
/// </remarks>
public readonly struct VoidResult
{
    /// <summary>
    /// The singleton instance to use for all void returns.
    /// </summary>
    public static readonly VoidResult Instance = default;
}
