namespace SharpTS.Parsing;

/// <summary>
/// TypeScript pragma directives recovered from comments by the lexer.
/// Mirrors tsc's documented directive set.
/// </summary>
/// <param name="HasTsCheck">A `// @ts-check` comment appeared at the top of the file (before any code token).</param>
/// <param name="HasTsNoCheck">A `// @ts-nocheck` comment appeared at the top of the file.</param>
/// <param name="IgnoreLines">1-based line numbers where `// @ts-ignore` appeared. Type errors on the next non-comment line are suppressed.</param>
/// <param name="ExpectErrorLines">1-based line numbers where `// @ts-expect-error` appeared. The next non-comment line is *required* to produce a type error; absence becomes a diagnostic of its own.</param>
/// <param name="JsxFactory">Value of an `@jsx` pragma (e.g. `/** @jsx h */`). Forces the classic transform with this factory.</param>
/// <param name="JsxFragmentFactory">Value of an `@jsxFrag` pragma.</param>
/// <param name="JsxImportSource">Value of an `@jsxImportSource` pragma (automatic runtime package).</param>
/// <param name="JsxRuntime">Value of an `@jsxRuntime` pragma: "classic" or "automatic".</param>
public sealed record TypeScriptPragmas(
    bool HasTsCheck,
    bool HasTsNoCheck,
    IReadOnlySet<int> IgnoreLines,
    IReadOnlySet<int> ExpectErrorLines,
    string? JsxFactory = null,
    string? JsxFragmentFactory = null,
    string? JsxImportSource = null,
    string? JsxRuntime = null)
{
    public static TypeScriptPragmas Empty { get; } = new(false, false, new HashSet<int>(), new HashSet<int>());
}
