namespace SharpTS.TypeSystem;

/// <summary>
/// Represents a type checking error with location and type information.
/// </summary>
public record TypeCheckError(string Message, int? Line = null, int? Column = null,
    TypeInfo? Expected = null, TypeInfo? Actual = null)
{
    public override string ToString() => Line.HasValue
        ? $"Type Error at line {Line}: {Message}"
        : $"Type Error: {Message}";
}
