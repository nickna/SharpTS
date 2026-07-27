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
}
