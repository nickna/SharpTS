namespace SharpTS.Parsing;

/// <summary>
/// An expected grammar failure, optionally carrying a canonical TypeScript diagnostic code.
/// Recovery catches only this exception so internal defects cannot become source diagnostics.
/// </summary>
internal sealed class ParseError(string message, string? tsCode = null) : Exception(message)
{
    public string? TsCode { get; } = tsCode;
}
