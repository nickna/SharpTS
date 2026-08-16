namespace SharpTS.Compilation;

/// <summary>
/// Marker interface for generated discriminated union types.
/// Enables O(1) type checking via <c>is IUnionType</c> instead of string operations.
/// </summary>
/// <remarks>
/// All generated union types (e.g., <c>Union_NUMBER_STRING</c>) implement this interface.
/// The <see cref="Value"/> property returns the boxed value of the currently active union member.
/// </remarks>
public interface IUnionType
{
    /// <summary>
    /// Gets the boxed value currently held by the union.
    /// </summary>
    object? Value { get; }
}
