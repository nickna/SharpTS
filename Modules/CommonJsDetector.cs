using SharpTS.Configuration;

namespace SharpTS.Modules;

/// <summary>
/// Determines whether a source file should be loaded as CommonJS or ES Module.
/// </summary>
/// <remarks>
/// Resolution rules (in order):
/// <list type="number">
/// <item>Extension: <c>.cjs</c>/<c>.cts</c> → CJS; <c>.mjs</c>/<c>.mts</c>/<c>.ts</c>/<c>.tsx</c> → ESM.</item>
/// <item><c>.js</c>/<c>.jsx</c>: walk up to nearest <c>package.json</c> (stopping at
/// <see cref="Configuration.FileDiscovery"/>'s ambient ceilings — temp root, user profile).
/// <c>"type":"module"</c> → ESM; <c>"type":"commonjs"</c> or no field → CJS (Node default).</item>
/// <item>Fallback heuristic for <c>.js</c> with no reachable <c>package.json</c>:
/// content scan for <c>require(</c>/<c>module.exports</c>/<c>exports.</c>
/// without <c>import</c>/<c>export</c> tokens.</item>
/// </list>
/// </remarks>
public static class CommonJsDetector
{
    /// <summary>
    /// Result of CJS detection.
    /// </summary>
    public enum ModuleKind
    {
        /// <summary>ES module — uses import/export syntax.</summary>
        EsModule,
        /// <summary>CommonJS module — uses require/module.exports.</summary>
        CommonJs
    }

    /// <summary>
    /// Determines the module kind of a file.
    /// </summary>
    /// <param name="filePath">Absolute path to the source file.</param>
    /// <returns>The detected module kind.</returns>
    public static ModuleKind Detect(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        // Step 1: extension-based decision
        switch (ext)
        {
            case ".cjs":
            case ".cts":
                return ModuleKind.CommonJs;
            case ".mjs":
            case ".mts":
            case ".ts":
            case ".tsx":
                return ModuleKind.EsModule;
        }

        // Step 2: .js/.jsx — walk up to nearest package.json
        if (ext == ".js" || ext == ".jsx")
        {
            var pkgType = FindNearestPackageJsonType(filePath);
            if (pkgType == "module")
                return ModuleKind.EsModule;
            if (pkgType == "commonjs")
                return ModuleKind.CommonJs;

            // Step 2a: package.json found but no "type" field → Node default is CommonJS
            if (pkgType == "")
                return ModuleKind.CommonJs;

            // Step 3: no reachable package.json — fall back to content heuristic
            return DetectFromContent(filePath);
        }

        // Unknown extension — assume ESM (TS-first project default)
        return ModuleKind.EsModule;
    }

    /// <summary>
    /// Walks up from <paramref name="filePath"/> looking for a package.json.
    /// Returns the value of the "type" field, an empty string if found but no type, or null if no package.json reachable.
    /// </summary>
    private static string? FindNearestPackageJsonType(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        while (!string.IsNullOrEmpty(dir))
        {
            var pkgPath = Path.Combine(dir, "package.json");
            if (File.Exists(pkgPath))
            {
                var pkg = ModulePackageJson.TryLoad(pkgPath);
                if (pkg != null)
                {
                    return pkg.Type ?? "";
                }
            }
            dir = FileDiscovery.AmbientParent(dir);
        }
        return null;
    }

    /// <summary>
    /// Heuristic content scan: classifies a file as CJS if it uses require/module.exports/exports
    /// without import/export syntax. Used only when no package.json is reachable.
    /// </summary>
    private static ModuleKind DetectFromContent(string filePath)
    {
        try
        {
            var source = File.ReadAllText(filePath);

            // Cheap token scan — not parsing, just looking for obvious markers
            bool hasEsm =
                ContainsKeywordToken(source, "import") ||
                ContainsKeywordToken(source, "export");

            if (hasEsm)
                return ModuleKind.EsModule;

            bool hasCjs =
                source.Contains("require(") ||
                source.Contains("module.exports") ||
                source.Contains("exports.");

            return hasCjs ? ModuleKind.CommonJs : ModuleKind.EsModule;
        }
        catch
        {
            return ModuleKind.EsModule;
        }
    }

    /// <summary>
    /// True if <paramref name="source"/> contains <paramref name="keyword"/> as a standalone token
    /// (not as part of another identifier and not inside a single-line string literal).
    /// Naive scanner — adequate for the heuristic, not for production parsing.
    /// </summary>
    private static bool ContainsKeywordToken(string source, string keyword)
    {
        int idx = 0;
        while ((idx = source.IndexOf(keyword, idx, StringComparison.Ordinal)) >= 0)
        {
            bool leftOk = idx == 0 || !IsIdentChar(source[idx - 1]);
            int after = idx + keyword.Length;
            bool rightOk = after >= source.Length || !IsIdentChar(source[after]);
            if (leftOk && rightOk)
                return true;
            idx = after;
        }
        return false;
    }

    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';
}
