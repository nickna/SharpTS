namespace SharpTS.Parsing;

/// <summary>
/// JSX transform mode - determines how JSX elements are lowered and which runtime they target.
/// Mirrors tsc's <c>jsx</c> compiler option. <c>preserve</c>/<c>react-native</c> are rejected at
/// config load: SharpTS executes TypeScript directly and cannot emit .jsx output.
/// </summary>
public enum JsxMode
{
    /// <summary>JSX is a syntax error (tsc behavior for an unset --jsx flag, TS17004). Opt in via --jsx none.</summary>
    None,
    /// <summary>Classic transform: lower to jsxFactory calls (default React.createElement).</summary>
    React,
    /// <summary>Automatic runtime: lower to jsx/jsxs imported from "&lt;jsxImportSource&gt;/jsx-runtime".</summary>
    ReactJsx,
    /// <summary>Automatic dev runtime: lower to jsxDEV imported from "&lt;jsxImportSource&gt;/jsx-dev-runtime".</summary>
    ReactJsxDev,
}

/// <summary>
/// Parser-facing JSX settings, resolved from tsconfig/CLI (and later per-file pragmas).
/// Attached to a <see cref="Parser"/> via <c>WithJsx</c> only for .tsx/.jsx sources;
/// its presence is what switches the parser into the TSX dialect.
/// </summary>
/// <param name="Mode">The transform mode.</param>
/// <param name="Factory">Classic-mode factory expression (dotted name allowed).</param>
/// <param name="FragmentFactory">Classic-mode fragment expression (dotted name allowed).</param>
/// <param name="ImportSource">Automatic-mode package to import the runtime from.</param>
public sealed record JsxParseOptions(
    JsxMode Mode,
    string Factory = "React.createElement",
    string FragmentFactory = "React.Fragment",
    string ImportSource = "react")
{
    /// <summary>
    /// SharpTS default: automatic runtime. Deliberate deviation from tsc (which errors with
    /// TS17004 when --jsx is unset) so bare .tsx files run out of the box against the stdlib
    /// shim. Strict tsc parity is available via <c>--jsx none</c>.
    /// </summary>
    public static JsxParseOptions Default { get; } = new(JsxMode.ReactJsx);

    /// <summary>True when Factory came from an inline <c>@jsx</c> pragma (gates TS17017).</summary>
    public bool FactoryFromPragma { get; init; } = false;

    /// <summary>True when FragmentFactory came from an inline <c>@jsxFrag</c> pragma.</summary>
    public bool FragmentFactoryFromPragma { get; init; } = false;

    /// <summary>
    /// Applies per-file pragmas on top of the project settings, tsc semantics:
    /// <c>@jsxRuntime classic|automatic</c> switches the mode; <c>@jsx</c> sets the factory
    /// AND forces classic (an inline factory implies the classic transform); <c>@jsxFrag</c>
    /// sets the fragment factory; <c>@jsxImportSource</c> sets the runtime package.
    /// <c>--jsx none</c> stays none — pragmas configure the transform, they cannot enable JSX
    /// (matching tsc, where no pragma cures a missing --jsx).
    /// </summary>
    public JsxParseOptions ApplyPragmas(TypeScriptPragmas pragmas)
    {
        var options = this;
        if (string.Equals(pragmas.JsxRuntime, "classic", StringComparison.Ordinal))
            options = options with { Mode = JsxMode.React };
        else if (string.Equals(pragmas.JsxRuntime, "automatic", StringComparison.Ordinal))
            options = options with { Mode = JsxMode.ReactJsx };
        if (pragmas.JsxFactory is not null)
            options = options with { Factory = pragmas.JsxFactory, Mode = JsxMode.React, FactoryFromPragma = true };
        if (pragmas.JsxFragmentFactory is not null)
            options = options with { FragmentFactory = pragmas.JsxFragmentFactory, FragmentFactoryFromPragma = true };
        if (pragmas.JsxImportSource is not null)
            options = options with { ImportSource = pragmas.JsxImportSource };
        if (Mode == JsxMode.None)
            options = options with { Mode = JsxMode.None };
        return options;
    }
}
