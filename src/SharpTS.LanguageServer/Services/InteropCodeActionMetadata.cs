namespace SharpTS.LanguageServer.Services;

/// <summary>
/// Stable structured metadata attached to actionable interop diagnostics. Keeping the edit
/// description separate from the human-readable message means clients never need to parse error
/// prose to construct a quick fix.
/// </summary>
internal static class InteropCodeActionMetadata
{
    internal const string VersionKey = "sharpts.codeAction.version";
    internal const string KindKey = "sharpts.codeAction.kind";
    internal const string TitleKey = "sharpts.codeAction.title";
    internal const string NewTextKey = "sharpts.codeAction.newText";
    internal const int CurrentVersion = 1;
    internal const string ReplaceDiagnosticRange = "replaceDiagnosticRange";

    internal static IReadOnlyDictionary<string, object> Replacement(
        string title,
        string newText) =>
        new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [VersionKey] = CurrentVersion,
            [KindKey] = ReplaceDiagnosticRange,
            [TitleKey] = title,
            [NewTextKey] = newText,
        };
}
