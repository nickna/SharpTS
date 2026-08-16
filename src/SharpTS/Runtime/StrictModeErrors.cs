namespace SharpTS.Runtime;

/// <summary>
/// Provides standardized JavaScript-style error exceptions for strict mode violations.
/// These follow the ECMAScript specification for error types.
/// </summary>
public static class StrictModeErrors
{
    /// <summary>
    /// Creates a ReferenceError for invalid variable references in strict mode.
    /// Used for: assignment to undeclared variables.
    /// </summary>
    public static Exception ReferenceError(string message) => new($"ReferenceError: {message}");

    /// <summary>
    /// Creates a SyntaxError for strict mode syntax violations.
    /// Used for: delete on variables, duplicate parameters, eval/arguments assignment,
    /// legacy octal literals, octal escape sequences.
    /// </summary>
    public static Exception SyntaxError(string message) => new($"SyntaxError: {message}");

    /// <summary>
    /// Creates a TypeError for strict mode type violations.
    /// Used for: writing to frozen/sealed objects, writing to getter-only properties,
    /// deleting non-configurable properties.
    /// </summary>
    public static Exception TypeError(string message) => new($"TypeError: {message}");
}
