namespace SharpTS.TypeSystem;

/// <summary>
/// Result of type checking that includes the type map and any errors encountered.
/// </summary>
public record TypeCheckResult(TypeMap TypeMap, List<TypeCheckError> Errors)
{
    /// <summary>
    /// Returns true if no errors were encountered during type checking.
    /// </summary>
    public bool IsSuccess => Errors.Count == 0;

    /// <summary>
    /// Indicates whether type checking stopped due to reaching the maximum error limit.
    /// </summary>
    public bool HitErrorLimit { get; init; } = false;
}
