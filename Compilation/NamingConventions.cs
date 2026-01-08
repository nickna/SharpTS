namespace SharpTS.Compilation;

/// <summary>
/// Provides naming convention utilities for mapping TypeScript identifiers to .NET conventions.
/// </summary>
public static class NamingConventions
{
    /// <summary>
    /// Converts a camelCase identifier to PascalCase for .NET property naming.
    /// </summary>
    /// <remarks>
    /// Examples:
    /// - "name" → "Name"
    /// - "firstName" → "FirstName"
    /// - "id" → "Id"
    /// - "URL" → "URL" (already PascalCase, unchanged)
    /// - "" → "" (empty string unchanged)
    /// </remarks>
    public static string ToPascalCase(string camelCase)
    {
        if (string.IsNullOrEmpty(camelCase))
            return camelCase;

        // If already starts with uppercase, return as-is
        if (char.IsUpper(camelCase[0]))
            return camelCase;

        return char.ToUpperInvariant(camelCase[0]) + camelCase[1..];
    }
}
