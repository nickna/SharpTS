using SharpTS.TypeSystem;

namespace SharpTS.Configuration;

/// <summary>
/// One layer of strictness configuration — what a single source (the command line, or a
/// tsconfig.json in an <c>extends</c> chain) literally said.
/// </summary>
/// <remarks>
/// Every member is nullable so "the user asked for false" stays distinguishable from "the user
/// said nothing". That distinction is what makes <see cref="Resolve"/>'s precedence correct;
/// a non-nullable <c>bool</c> here would silently turn every absent flag into an explicit
/// <c>false</c> and override the layer below it.
/// </remarks>
public sealed record StrictnessOptions
{
    /// <summary>tsc's <c>strict</c> umbrella: the fallback for every other flag in this record.</summary>
    public bool? Strict { get; init; }

    public bool? StrictNullChecks { get; init; }

    public bool? StrictFunctionTypes { get; init; }

    public bool? NoImplicitAny { get; init; }

    public bool? NoImplicitThis { get; init; }

    public bool? UseUnknownInCatchVariables { get; init; }

    public bool? StrictPropertyInitialization { get; init; }

    public bool? ExactOptionalPropertyTypes { get; init; }

    public bool? NoUncheckedIndexedAccess { get; init; }

    /// <summary>True when this layer said nothing at all.</summary>
    public bool IsEmpty =>
        Strict is null && StrictNullChecks is null && StrictFunctionTypes is null && NoImplicitAny is null
        && NoImplicitThis is null && UseUnknownInCatchVariables is null && StrictPropertyInitialization is null
        && ExactOptionalPropertyTypes is null && NoUncheckedIndexedAccess is null;

    /// <summary>
    /// Folds the layers into the checker's resolved options, following tsc's model:
    /// per-key CLI-over-tsconfig first, then <c>strict</c> as the fallback for any key still
    /// unset, then SharpTS's own default.
    /// </summary>
    /// <remarks>
    /// The SharpTS defaults differ per key (<c>strictNullChecks</c> on, the others off) — that
    /// mix is deliberate and preserves pre-existing behavior for anyone who passes nothing.
    /// See <see cref="TypeCheckerOptions"/>.
    /// </remarks>
    /// <param name="cli">What the command line said. Wins per key.</param>
    /// <param name="tsConfig">
    /// What tsconfig.json said, already folded across any <c>extends</c> chain (base first,
    /// deriving file last).
    /// </param>
    public static TypeCheckerOptions Resolve(StrictnessOptions? cli, StrictnessOptions? tsConfig)
    {
        var d = TypeCheckerOptions.Default;

        // `strict` itself merges per-key like any other option before it acts as a fallback.
        bool? umbrella = cli?.Strict ?? tsConfig?.Strict;
        bool? configuredStrictNullChecks =
            cli?.StrictNullChecks ?? tsConfig?.StrictNullChecks ?? umbrella;
        bool strictNullChecks = configuredStrictNullChecks ?? d.StrictNullChecks;

        return new TypeCheckerOptions
        {
            StrictNullChecks = strictNullChecks,
            StrictFunctionTypes =
                cli?.StrictFunctionTypes ?? tsConfig?.StrictFunctionTypes ?? umbrella ?? d.StrictFunctionTypes,
            NoImplicitAny =
                cli?.NoImplicitAny ?? tsConfig?.NoImplicitAny ?? umbrella ?? d.NoImplicitAny,
            NoImplicitThis =
                cli?.NoImplicitThis ?? tsConfig?.NoImplicitThis ?? umbrella ?? d.NoImplicitThis,
            UseUnknownInCatchVariables =
                cli?.UseUnknownInCatchVariables ?? tsConfig?.UseUnknownInCatchVariables
                ?? umbrella ?? d.UseUnknownInCatchVariables,
            StrictPropertyInitialization =
                cli?.StrictPropertyInitialization ?? tsConfig?.StrictPropertyInitialization
                ?? umbrella ?? d.StrictPropertyInitialization,
            // TS2454 was added after SharpTS's historical strict-null default. Preserve the
            // no-options behavior, but enable it whenever strict null checking is explicitly
            // selected (directly or through the strict umbrella).
            CheckVariableUseBeforeAssignment =
                configuredStrictNullChecks ?? d.CheckVariableUseBeforeAssignment,
            // These flags are not members of the strict umbrella in tsc.
            ExactOptionalPropertyTypes =
                cli?.ExactOptionalPropertyTypes ?? tsConfig?.ExactOptionalPropertyTypes
                ?? d.ExactOptionalPropertyTypes,
            NoUncheckedIndexedAccess =
                cli?.NoUncheckedIndexedAccess ?? tsConfig?.NoUncheckedIndexedAccess
                ?? d.NoUncheckedIndexedAccess,
            MaxErrors = d.MaxErrors,
        };
    }
}
