namespace SharpTS.Modules;

/// <summary>
/// TypeScript program inputs that sit above ordinary module resolution:
/// standard declaration libraries and visible <c>@types</c> packages.
/// </summary>
public sealed record TypeScriptProgramOptions
{
    /// <summary>Preserves the legacy resolver behavior for embedding/tests.</summary>
    public static readonly TypeScriptProgramOptions Disabled = new();

    /// <summary>TypeScript-compatible CLI defaults (target ES5's default lib set).</summary>
    public static readonly TypeScriptProgramOptions Default = new()
    {
        LoadDefaultLib = true,
        PreferDeclarationFiles = true,
    };

    public bool LoadDefaultLib { get; init; }
    public bool NoLib { get; init; }
    public IReadOnlyList<string>? Lib { get; init; }
    public IReadOnlyList<string>? Types { get; init; }
    public IReadOnlyList<string>? TypeRoots { get; init; }
    public bool PreferDeclarationFiles { get; init; }
}
