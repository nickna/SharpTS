namespace SharpTS.Parsing;

/// <summary>
/// A parse failure that carries a canonical TypeScript diagnostic code (e.g. "TS17004").
/// Parser internals historically throw plain <see cref="Exception"/>; throw this instead when
/// a tsc analogue exists so <c>Parser.Parse()</c> can surface the code on the recorded
/// <see cref="Diagnostics.Diagnostic"/> (the conformance harness matches on it).
/// </summary>
internal sealed class ParseError(string message, string? tsCode = null) : Exception(message)
{
    public string? TsCode { get; } = tsCode;
}
