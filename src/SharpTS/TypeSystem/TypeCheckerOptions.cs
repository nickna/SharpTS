namespace SharpTS.TypeSystem;

/// <summary>
/// Resolved strictness configuration for one <see cref="TypeChecker"/> instance.
/// Produced by the CLI/tsconfig resolution layer and handed to the checker at
/// construction.
/// </summary>
/// <remarks>
/// <para>Every default here IS the historical product default: an options object built
/// with no initializers must be behaviourally identical to the pre-options
/// <c>new TypeChecker()</c>. <c>StrictNullChecks: true</c> + <c>StrictFunctionTypes: false</c>
/// is deliberately neither of tsc's presets — it is what SharpTS has always done.</para>
/// <para><b>Instance-scoped and immutable.</b> A TypeChecker's assignability caches
/// (<c>_compatibilityCache</c> / <c>_identityCompatibilityCache</c>) are keyed only by type
/// pair, not by strictness. They are correct today only because these flags cannot change
/// during an instance's lifetime. Any future per-file or per-scope strictness MUST be added
/// to the <c>uncacheable</c> guard in <c>TypeChecker.Compatibility.cs</c> or it will serve
/// verdicts computed under the other setting.</para>
/// </remarks>
public sealed record TypeCheckerOptions
{
    /// <summary>Product defaults — every knob at its historical value.</summary>
    public static readonly TypeCheckerOptions Default = new();

    /// <summary>
    /// tsc's <c>strictNullChecks</c>. When false, <c>null</c>/<c>undefined</c> are assignable
    /// to every type except <c>never</c>. SharpTS default: <c>true</c>.
    /// </summary>
    public bool StrictNullChecks { get; init; } = true;

    /// <summary>
    /// tsc's <c>strictFunctionTypes</c>. When true, function-type parameters compare
    /// contravariantly; members declared with method syntax keep the bivariant comparison
    /// (tsc's exemption). SharpTS default: <c>false</c>.
    /// </summary>
    public bool StrictFunctionTypes { get; init; } = false;

    /// <summary>
    /// tsc's <c>noImplicitAny</c>. Gates TS7006/TS7019 on unannotated parameters of declared
    /// functions, methods and constructors. SharpTS default: <c>false</c>.
    /// </summary>
    public bool NoImplicitAny { get; init; } = false;

    public bool NoImplicitThis { get; init; } = false;

    public bool StrictPropertyInitialization { get; init; } = false;

    /// <summary>
    /// Enables TS2454 flow diagnostics for typed variables read before assignment.
    /// The direct checker default remains false for backwards compatibility with
    /// execution-oriented callers; CLI/tsconfig resolution enables it when strict null
    /// checking is explicitly selected, either directly or through the strict umbrella.
    /// </summary>
    public bool CheckVariableUseBeforeAssignment { get; init; } = false;

    public bool ExactOptionalPropertyTypes { get; init; } = false;

    public bool NoUncheckedIndexedAccess { get; init; } = false;

    /// <summary>
    /// Diagnostics collected before <c>CheckWithRecovery</c> stops. Default 10 — an ergonomics
    /// knob rather than a strictness one, and pinned by the user-visible "Too many errors,
    /// stopping." behavior. The conformance runner raises it to 1000.
    /// </summary>
    public int MaxErrors { get; init; } = 10;

    /// <summary>
    /// The project's jsx mode. The JSX checking pipeline is otherwise mode-agnostic (each
    /// lowered call's <c>JsxCallInfo</c> is self-describing), so this exists for diagnostics
    /// that mention the mode. It never influences assignability verdicts, so the
    /// compatibility caches need no <c>uncacheable</c> guard for it.
    /// </summary>
    public Parsing.JsxMode Jsx { get; init; } = Parsing.JsxMode.None;

    /// <summary>
    /// The <c>--strict</c> umbrella. Individual flags override it by layering <c>with</c> on
    /// top, e.g. <c>TypeCheckerOptions.Strict with { NoImplicitAny = false }</c> for
    /// <c>--strict --noImplicitAny=false</c>.
    /// </summary>
    public static readonly TypeCheckerOptions Strict = new()
    {
        StrictNullChecks = true,
        StrictFunctionTypes = true,
        NoImplicitAny = true,
        NoImplicitThis = true,
        StrictPropertyInitialization = true,
        CheckVariableUseBeforeAssignment = true,
    };
}
