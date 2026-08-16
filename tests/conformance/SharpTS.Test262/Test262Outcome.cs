namespace SharpTS.Test262;

/// <summary>
/// Buckets a Test262 test can land in after running through SharpTS.
/// SharpTS is deliberately not 100% spec-compliant; bucketing tells us
/// *why* a test failed, which is more actionable than binary pass/fail.
/// </summary>
public enum Test262Outcome
{
    /// <summary>Test body ran to completion without an assertion throwing.</summary>
    Pass,

    /// <summary>Test body threw a <c>Test262Error</c> (assertion failed).</summary>
    Fail,

    /// <summary>Test source (or assembled harness) failed to lex/parse.</summary>
    ParseError,

    /// <summary>Static type checker rejected the source.</summary>
    TypeCheckError,

    /// <summary>Test body threw something other than <c>Test262Error</c>.</summary>
    RuntimeError,

    /// <summary>Execution exceeded the per-test timeout.</summary>
    Timeout,

    /// <summary>Harness code (sta.js / assert.js / includes) threw before the test body ran.</summary>
    HarnessError,

    /// <summary>Intentionally not run (e.g., negative tests, deferred features).</summary>
    Skipped,
}

/// <summary>Result of running one Test262 file.</summary>
public sealed record Test262Result(
    Test262Outcome Outcome,
    string? Message,
    string? SkipReason);
