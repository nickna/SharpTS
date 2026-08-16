namespace SharpTS.Runtime.Types;

/// <summary>
/// Common interface for runtime types that support named property access.
/// Provides a unified API for property get/set operations with string keys.
/// </summary>
/// <remarks>
/// This interface enables polymorphic property access in the interpreter,
/// reducing pattern matching and simplifying the addition of new runtime types.
/// Implemented by <see cref="SharpTSObject"/> and <see cref="SharpTSInstance"/>.
/// Note: <see cref="SharpTSArray"/> is not included as it uses index-based access.
/// </remarks>
public interface ISharpTSPropertyAccessor
{
    /// <summary>
    /// Gets a property value by name.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <returns>The property value, or null if not found.</returns>
    object? GetProperty(string name);

    /// <summary>
    /// Sets a property value by name.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <param name="value">The value to set.</param>
    void SetProperty(string name, object? value);

    /// <summary>
    /// Checks if a property exists.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <returns>True if the property exists.</returns>
    bool HasProperty(string name);

    /// <summary>
    /// Gets all property names for iteration (e.g., for...in, Object.keys).
    /// </summary>
    IEnumerable<string> PropertyNames { get; }
}
