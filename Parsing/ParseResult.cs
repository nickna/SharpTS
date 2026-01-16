namespace SharpTS.Parsing;

/// <summary>
/// Result of parsing that includes both successfully parsed statements and any errors encountered.
/// </summary>
public record ParseResult(List<Stmt> Statements, List<ParseError> Errors)
{
    /// <summary>
    /// Returns true if no errors were encountered during parsing.
    /// </summary>
    public bool IsSuccess => Errors.Count == 0;

    /// <summary>
    /// Indicates whether parsing stopped due to reaching the maximum error limit.
    /// </summary>
    public bool HitErrorLimit { get; init; } = false;
}
