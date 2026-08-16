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

    /// <summary>
    /// Converts a PascalCase identifier to camelCase for JavaScript-style property access.
    /// </summary>
    /// <remarks>
    /// Examples:
    /// - "Name" → "name"
    /// - "FirstName" → "firstName"
    /// - "Id" → "id"
    /// - "url" → "url" (already camelCase, unchanged)
    /// - "" → "" (empty string unchanged)
    /// </remarks>
    public static string ToCamelCase(string pascalCase)
    {
        if (string.IsNullOrEmpty(pascalCase))
            return pascalCase;

        // If already starts with lowercase, return as-is
        if (char.IsLower(pascalCase[0]))
            return pascalCase;

        return char.ToLowerInvariant(pascalCase[0]) + pascalCase[1..];
    }
}
