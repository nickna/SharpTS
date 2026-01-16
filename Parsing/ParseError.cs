namespace SharpTS.Parsing;

/// <summary>
/// Represents a parsing error with location information.
/// </summary>
public record ParseError(string Message, int Line, int? Column = null, string? TokenLexeme = null)
{
    public override string ToString() => Column.HasValue
        ? $"Parse Error at line {Line}, column {Column}: {Message}"
        : $"Parse Error at line {Line}: {Message}";
}
