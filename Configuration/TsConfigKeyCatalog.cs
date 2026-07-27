namespace SharpTS.Configuration;

/// <summary>
/// Classifies <c>compilerOptions</c> keys SharpTS did not bind, so a typo produces a
/// "did you mean" instead of silence.
/// </summary>
/// <remarks>
/// Three buckets, because they deserve different volumes:
/// <list type="number">
/// <item>Keys tsc defines that SharpTS deliberately does not implement (emit options, unbuilt
/// strictness flags). These fire on essentially every real-world tsconfig, so they are
/// <b>suppressed by default</b> and surface only under <c>--showConfig</c> or
/// <c>SHARPTS_TSCONFIG_VERBOSE=1</c>. Warning on `target`/`module`/`lib` every single run
/// would just train users to ignore SharpTS warnings.</item>
/// <item>Keys nobody defines — almost always a typo. Always shown, with a suggestion.</item>
/// <item>Unknown top-level keys, same treatment.</item>
/// </list>
/// </remarks>
internal static class TsConfigKeyCatalog
{
    /// <summary>Set <c>SHARPTS_TSCONFIG_VERBOSE=1</c> to also see the "recognized but not implemented" notes.</summary>
    public static bool Verbose =>
        Environment.GetEnvironmentVariable("SHARPTS_TSCONFIG_VERBOSE") is "1" or "true";

    /// <summary>
    /// tsc options SharpTS binds and acts on. Kept here (rather than reflected off the model) so
    /// the "did you mean" suggester can see them even though they never reach UnknownKeys.
    /// </summary>
    private static readonly string[] AppliedCompilerOptions =
    [
        "strict", "strictNullChecks", "strictFunctionTypes", "noImplicitAny", "noImplicitThis",
        "strictPropertyInitialization", "exactOptionalPropertyTypes", "noUncheckedIndexedAccess", "checkJs",
        "preserveConstEnums", "experimentalDecorators", "decorators", "emitDecoratorMetadata",
        "rootDir", "outDir", "allowJs", "moduleResolution", "lib", "noLib", "baseUrl", "paths",
        "typeRoots", "types", "incremental", "composite", "tsBuildInfoFile",
        "declaration", "emitDeclarationOnly", "declarationDir",
    ];

    /// <summary>
    /// Real tsc options that SharpTS knowingly ignores. Split by reason so the message can say
    /// which kind of "not supported" it is.
    /// </summary>
    private static readonly HashSet<string> EmitOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "target", "module", "moduleDetection", "jsx", "jsxFactory",
        "jsxFragmentFactory", "jsxImportSource", "declarationMap",
        "sourceMap", "inlineSourceMap", "inlineSources", "sourceRoot", "mapRoot", "outFile", "out",
        "removeComments", "importHelpers", "downlevelIteration", "isolatedModules",
        "verbatimModuleSyntax", "esModuleInterop", "allowSyntheticDefaultImports",
        "resolveJsonModule", "skipLibCheck", "skipDefaultLibCheck", "noEmit",
        "noEmitOnError", "noEmitHelpers", "newLine", "pretty", "plugins", "noResolve",
        "preserveSymlinks", "forceConsistentCasingInFileNames", "useDefineForClassFields",
        "emitBOM", "charset", "watchOptions", "allowImportingTsExtensions", "allowArbitraryExtensions",
    };

    /// <summary>Strictness-family options SharpTS recognizes but does not yet enforce.</summary>
    private static readonly HashSet<string> UnimplementedChecks = new(StringComparer.OrdinalIgnoreCase)
    {
        "strictBindCallApply", "alwaysStrict",
        "useUnknownInCatchVariables", "noUnusedLocals",
        "noUnusedParameters", "noImplicitReturns", "noImplicitOverride",
        "noFallthroughCasesInSwitch",
        "noPropertyAccessFromIndexSignature", "allowUnreachableCode", "allowUnusedLabels",
    };

    private static readonly string[] AppliedTopLevel =
        ["compilerOptions", "files", "include", "exclude", "extends", "references"];

    private static readonly HashSet<string> KnownTopLevel = new(StringComparer.OrdinalIgnoreCase)
        { "compileOnSave", "typeAcquisition", "ts-node", "watchOptions", "$schema" };

    /// <summary>Formats every note for one file in an extends chain.</summary>
    public static IEnumerable<string> Diagnose(string configPath, TsConfigJson json)
    {
        string file = $"{TsConfigLoader.FileName} ('{configPath}')";

        foreach (var key in json.UnknownKeys?.Keys ?? Enumerable.Empty<string>())
        {
            if (KnownTopLevel.Contains(key))
            {
                if (Verbose)
                    yield return $"Note: {file}: '{key}' is a recognized TypeScript option that SharpTS does not implement; ignored.";
                continue;
            }

            yield return $"Warning: {file}: unknown key '{key}'.{Suggest(key, AppliedTopLevel.Concat(KnownTopLevel))}";
        }

        foreach (var key in json.CompilerOptions?.UnknownKeys?.Keys ?? Enumerable.Empty<string>())
        {
            if (EmitOptions.Contains(key))
            {
                if (Verbose)
                    yield return $"Note: {file}: '{key}' is a TypeScript emit option; SharpTS compiles to .NET IL and ignores it.";
                continue;
            }

            if (UnimplementedChecks.Contains(key))
            {
                if (Verbose)
                    yield return $"Note: {file}: '{key}' is recognized but not yet enforced by SharpTS; ignored.";
                continue;
            }

            yield return $"Warning: {file}: unknown compiler option '{key}'."
                + Suggest(key, AppliedCompilerOptions.Concat(EmitOptions).Concat(UnimplementedChecks));
        }
    }

    /// <summary>Appends " Did you mean 'x'?" when a close-enough known key exists.</summary>
    private static string Suggest(string key, IEnumerable<string> candidates)
    {
        // Longer names tolerate one more slip; beyond that the "suggestion" is noise.
        int budget = key.Length > 12 ? 3 : 2;

        string? best = null;
        int bestDistance = int.MaxValue;
        foreach (var candidate in candidates)
        {
            int distance = Distance(key, candidate, budget);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return bestDistance <= budget && best is not null ? $" Did you mean '{best}'?" : string.Empty;
    }

    /// <summary>Levenshtein distance, abandoning any row that already exceeds <paramref name="budget"/>.</summary>
    private static int Distance(string a, string b, int budget)
    {
        if (Math.Abs(a.Length - b.Length) > budget) return int.MaxValue;

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) previous[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            int rowBest = current[0];

            for (int j = 1; j <= b.Length; j++)
            {
                int cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                rowBest = Math.Min(rowBest, current[j]);
            }

            if (rowBest > budget) return int.MaxValue;
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
