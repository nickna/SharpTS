namespace SharpTS.TypeSystem;

/// <summary>
/// Interface for runtime objects that cache their type category.
/// Avoids repeated type classification in hot paths like property access.
/// </summary>
public interface ITypeCategorized
{
    /// <summary>
    /// Gets the pre-computed type category for this object.
    /// </summary>
    TypeCategory RuntimeCategory { get; }
}
