namespace SharpTS.TypeScriptConformance;

/// <summary>One ordered type observation extracted from a TypeScript <c>*.types</c> baseline.</summary>
public sealed record TypesBaselineObservation(
    string VirtualFileName,
    int SourceLine,
    string SourceLineText,
    string SourceText,
    int OccurrenceOrdinal,
    string ExpectedTypeText,
    string? Underline);

/// <summary>One virtual-file section from a TypeScript <c>*.types</c> baseline.</summary>
public sealed record TypesBaselineFile(
    string VirtualFileName,
    IReadOnlyList<TypesBaselineObservation> Observations);

/// <summary>A parsed TypeScript <c>*.types</c> baseline in committed file order.</summary>
public sealed record TypesBaselineDocument(IReadOnlyList<TypesBaselineFile> Files)
{
    public IReadOnlyList<TypesBaselineObservation> Observations =>
        Files.SelectMany(file => file.Observations).ToArray();
}

/// <summary>A malformed or source-inconsistent TypeScript <c>*.types</c> baseline.</summary>
public sealed class TypesBaselineParseException : FormatException
{
    public TypesBaselineParseException(string message, int baselineLine)
        : base($"Baseline line {baselineLine}: {message}")
    {
        BaselineLine = baselineLine;
    }

    public int BaselineLine { get; }
}
