using System.Collections.Frozen;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Centralized primitive type mappings used across TypeMapper,
/// ParameterTypeResolver, ExternalMethodResolver, and ILCompiler.
/// </summary>
/// <remarks>
/// This class provides the single source of truth for TypeScript→.NET type mappings.
/// All type mapping logic should delegate to these static members to ensure consistency.
/// </remarks>
public static class PrimitiveTypeMappings
{
    /// <summary>
    /// Maps TypeScript type annotation strings to .NET CLR types.
    /// </summary>
    public static readonly FrozenDictionary<string, Type> StringToClrType =
        new Dictionary<string, Type>
        {
            ["number"] = typeof(double),
            ["string"] = typeof(string),
            ["boolean"] = typeof(bool),
            ["bigint"] = typeof(System.Numerics.BigInteger),
            ["void"] = typeof(void),
            ["any"] = typeof(object),
            ["unknown"] = typeof(object),
            ["never"] = typeof(void),
            ["null"] = typeof(object),
            ["symbol"] = typeof(string)
        }.ToFrozenDictionary();

    /// <summary>
    /// Maps TokenType values to .NET CLR types for Primitive TypeInfo.
    /// </summary>
    public static readonly FrozenDictionary<TokenType, Type> TokenToClrType =
        new Dictionary<TokenType, Type>
        {
            [TokenType.TYPE_NUMBER] = typeof(double),
            [TokenType.TYPE_STRING] = typeof(string),
            [TokenType.TYPE_BOOLEAN] = typeof(bool)
        }.ToFrozenDictionary();

    /// <summary>
    /// Parses a type annotation string into a TypeInfo record.
    /// </summary>
    /// <param name="annotation">The TypeScript type annotation string.</param>
    /// <returns>The corresponding TypeInfo record.</returns>
    public static TypeInfo ParseAnnotation(string annotation) => annotation.Trim() switch
    {
        "number" => new TypeInfo.Primitive(TokenType.TYPE_NUMBER),
        "string" => new TypeInfo.String(),
        "boolean" => new TypeInfo.Primitive(TokenType.TYPE_BOOLEAN),
        "void" => new TypeInfo.Void(),
        "any" => new TypeInfo.Any(),
        "unknown" => new TypeInfo.Unknown(),
        "never" => new TypeInfo.Never(),
        "null" => new TypeInfo.Null(),
        "bigint" => new TypeInfo.BigInt(),
        "symbol" => new TypeInfo.Symbol(),
        _ => new TypeInfo.Any() // Complex types fallback to any/object
    };

    /// <summary>
    /// Checks if a .NET type is a nullable value type (Nullable&lt;T&gt;).
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>True if the type is Nullable&lt;T&gt;, false otherwise.</returns>
    public static bool IsNullableValueType(Type type) =>
        type.IsValueType && Nullable.GetUnderlyingType(type) != null;

    /// <summary>
    /// Gets the underlying type if nullable, otherwise returns the type itself.
    /// </summary>
    /// <param name="type">The type to unwrap.</param>
    /// <returns>The underlying type for Nullable&lt;T&gt;, or the original type.</returns>
    public static Type GetUnderlyingTypeOrSelf(Type type) =>
        Nullable.GetUnderlyingType(type) ?? type;

    /// <summary>
    /// Creates a Nullable&lt;T&gt; type from a value type.
    /// </summary>
    /// <param name="valueType">The value type to wrap.</param>
    /// <returns>The Nullable&lt;T&gt; type.</returns>
    /// <exception cref="ArgumentException">If the type is not a value type.</exception>
    public static Type MakeNullable(Type valueType)
    {
        if (!valueType.IsValueType)
            throw new ArgumentException($"Cannot make nullable: {valueType} is not a value type", nameof(valueType));

        if (Nullable.GetUnderlyingType(valueType) != null)
            return valueType; // Already nullable

        return typeof(Nullable<>).MakeGenericType(valueType);
    }
}
