namespace SharpTS.TypeScriptConformance;

/// <summary>
/// Buckets a TS conformance test can land in. Mirrors the shape of
/// <c>SharpTS.Test262.Test262Outcome</c>; the meanings differ because the
/// TS corpus is type-checker-focused (no execution).
/// </summary>
public enum TypeScriptConformanceOutcome
{
    /// <summary>Diagnostic set matches the <c>*.errors.txt</c> baseline (or both empty).</summary>
    Pass,

    /// <summary>Diagnostic set differs from the baseline.</summary>
    Fail,

    /// <summary>Source failed to lex/parse.</summary>
    ParseError,

    /// <summary>The type checker threw something unrecoverable — distinct from "checker found errors."</summary>
    TypeCheckError,

    /// <summary>Skipped per directive policy (e.g. <c>@experimentalDecorators</c>) or
    /// per the lib-drift filter (we report no errors but tsc expected errors that
    /// look like missing-global / version-conditional surface — see #83).</summary>
    Skipped,

    /// <summary>Setup error (couldn't read test, multi-file resolution failed, baseline parse error, ...).</summary>
    HarnessError,
}

/// <summary>
/// Result of running one TS conformance file. <c>ExpectedDiagnostics</c> and
/// <c>ActualDiagnostics</c> are populated on <c>Fail</c> so callers can show a
/// meaningful diff; on <c>Pass</c> they may also be populated for reporting.
/// </summary>
public sealed record TypeScriptConformanceResult(
    TypeScriptConformanceOutcome Outcome,
    string? Message,
    string? SkipReason,
    IReadOnlyList<BaselineDiagnostic>? ExpectedDiagnostics = null,
    IReadOnlyList<BaselineDiagnostic>? ActualDiagnostics = null);

/// <summary>
/// A single diagnostic entry as it appears in the conformance match key:
/// (line, tsCode). File is implicit per-test for single-file cases.
/// Column is intentionally ignored — TS rewords messages between versions
/// and column drift is endemic, so we tolerate it.
/// </summary>
public sealed record BaselineDiagnostic(int Line, string TsCode);
