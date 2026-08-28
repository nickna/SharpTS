using System.Collections.Frozen;
using SharpTS.Configuration;
using System.Text.Json;
using SharpTS.Modules.Stdlib;
using SharpTS.Modules.Stdlib.Providers;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.Runtime.BuiltIns.Modules;
using SharpTS.TypeSystem;

namespace SharpTS.Modules;

/// <summary>
/// Whether a resolution request originates from an ESM <c>import</c> or a CJS <c>require()</c>.
/// Determines which conditions are passed to <see cref="ExportsResolver"/>. See
/// <see cref="ExportsResolver.EsmConditions"/> and <see cref="ExportsResolver.CjsConditions"/>.
/// </summary>
public enum ResolutionKind
{
    /// <summary>ESM import — matches <c>"import"</c> condition, not <c>"require"</c>.</summary>
    Esm,
    /// <summary>CJS require — matches <c>"require"</c> condition, not <c>"import"</c>.</summary>
    Cjs,
}

/// <summary>
/// Resolves module paths and manages module loading with circular dependency detection.
/// </summary>
/// <remarks>
/// Handles relative paths (./foo, ../bar), bare specifiers (lodash), and .ts extension
/// inference. Detects circular dependencies during loading and provides modules in
/// dependency order for type checking and execution.
/// Also handles triple-slash path references for script files.
/// </remarks>
public class ModuleResolver
{
    private const string AmbientModulePrefix = "ambient:";
    private readonly string _basePath;
    private readonly ModuleResolutionOptions _resolutionOptions;
    private readonly Dictionary<string, ParsedModule> _moduleCache = [];
    private readonly HashSet<string> _loadingModules = [];  // For circular detection
    private readonly HashSet<string> _loadingScriptRefs = [];  // For circular script reference detection
    private readonly Dictionary<string, ModulePackageJson?> _packageJsonCache = [];
    private readonly Dictionary<string, string> _ambientModulePaths = new(StringComparer.Ordinal);
    private readonly StdlibProviderChain _stdlibChain;
    private readonly TypeScriptProgramOptions _programOptions;
    /// <summary>
    /// Optional in-memory virtual file system. When non-null, all file existence checks and
    /// reads consult this map instead of touching the disk. Tests use this to bypass the
    /// kernel-serialized Windows file system (Defender real-time scan in particular makes
    /// concurrent <c>File.WriteAllText</c>/<c>File.ReadAllText</c> calls largely sequential —
    /// measured 1.4× speedup at 12 threads vs ideal 12×). Keys are normalized via
    /// <see cref="NormalizePath"/> (full path + OS-appropriate casing).
    /// </summary>
    private readonly Dictionary<string, string>? _virtualFiles;
    private readonly bool _virtualFilesFallBackToDisk;

    /// <summary>
    /// Creates a new module resolver rooted at the given path.
    /// </summary>
    /// <param name="basePath">Entry point file path or base directory</param>
    public ModuleResolver(string basePath)
        : this(basePath, ModuleResolutionOptions.Default, virtualFiles: null, TypeScriptProgramOptions.Disabled) { }

    public ModuleResolver(string basePath, TypeScriptProgramOptions programOptions)
        : this(basePath, ModuleResolutionOptions.Default, virtualFiles: null, programOptions) { }

    /// <summary>
    /// Creates a new module resolver with an optional in-memory virtual file system.
    /// When <paramref name="virtualFiles"/> is non-null, the resolver bypasses the disk
    /// entirely — all file existence checks and content reads consult the map. Tests use
    /// this to avoid the per-file Windows kernel/AV serialization that bottlenecks
    /// parallel test execution.
    /// </summary>
    /// <param name="basePath">Entry point file path or base directory</param>
    /// <param name="virtualFiles">If non-null, an in-memory file system. Keys must be
    /// absolute paths; normalization happens internally.</param>
    public ModuleResolver(string basePath, IReadOnlyDictionary<string, string>? virtualFiles)
        : this(basePath, ModuleResolutionOptions.Default, virtualFiles, TypeScriptProgramOptions.Disabled) { }

    /// <summary>
    /// Creates a resolver whose in-memory files overlay the real file system. Overlay content wins
    /// for open/dirty documents while unopened dependencies continue resolving from disk.
    /// </summary>
    public ModuleResolver(
        string basePath,
        IReadOnlyDictionary<string, string> overlayFiles,
        bool fallBackToFileSystem)
        : this(
            basePath,
            ModuleResolutionOptions.Default,
            overlayFiles,
            TypeScriptProgramOptions.Disabled,
            fallBackToFileSystem) { }

    public ModuleResolver(
        string basePath,
        IReadOnlyDictionary<string, string>? virtualFiles,
        TypeScriptProgramOptions programOptions)
        : this(basePath, ModuleResolutionOptions.Default, virtualFiles, programOptions) { }

    /// <summary>Creates a resolver using project-selected module resolution behavior.</summary>
    public ModuleResolver(
        string basePath,
        ModuleResolutionOptions resolutionOptions,
        IReadOnlyDictionary<string, string>? virtualFiles = null)
        : this(basePath, resolutionOptions, virtualFiles, TypeScriptProgramOptions.Disabled) { }

    /// <summary>
    /// Creates a resolver using project-selected module resolution and declaration-program behavior.
    /// </summary>
    public ModuleResolver(
        string basePath,
        ModuleResolutionOptions resolutionOptions,
        IReadOnlyDictionary<string, string>? virtualFiles,
        TypeScriptProgramOptions programOptions,
        bool virtualFilesFallBackToDisk = false)
    {
        _basePath = Path.GetDirectoryName(Path.GetFullPath(basePath)) ?? ".";
        _resolutionOptions = resolutionOptions;
        _programOptions = programOptions;
        _virtualFilesFallBackToDisk = virtualFilesFallBackToDisk;
        _stdlibChain = new StdlibProviderChain(
        [
            new PrimitiveProvider(),
            new EmbeddedStdlibProvider(),
            new BuiltInCSharpProvider(),
        ]);
        if (virtualFiles is not null)
        {
            _virtualFiles = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in virtualFiles)
                _virtualFiles[NormalizePath(k)] = v;
        }
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path);

    private bool ResolverFileExists(string path)
    {
        if (_virtualFiles is null) return File.Exists(path);
        return _virtualFiles.ContainsKey(NormalizePath(path)) ||
               (_virtualFilesFallBackToDisk && File.Exists(path));
    }

    private bool ResolverDirectoryExists(string path)
    {
        if (_virtualFiles is null) return Directory.Exists(path);
        var canonical = NormalizePath(path);
        var prefix = canonical + Path.DirectorySeparatorChar;
        foreach (var k in _virtualFiles.Keys)
            if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        return _virtualFilesFallBackToDisk && Directory.Exists(path);
    }

    private string ResolverReadAllText(string path)
    {
        if (_virtualFiles is not null && _virtualFiles.TryGetValue(NormalizePath(path), out var src))
            return src;
        return File.ReadAllText(path);
    }

    /// <summary>
    /// The stdlib provider chain. Exposed for diagnostics; not intended for mutation.
    /// </summary>
    internal StdlibProviderChain StdlibChain => _stdlibChain;

    // npm-fallback shims (react family). Deliberately NOT part of _stdlibChain: node
    // builtins are stdlib-first, but these answer only after node_modules resolution
    // misses, so a real installed package always beats the embedded shim.
    private readonly EmbeddedNpmFallbackProvider _npmFallback = new();

    /// <summary>
    /// Resolved JSX settings applied to every .tsx/.jsx source this resolver parses.
    /// Jsx config is per-project (unlike the historical per-call decorator mode), so it is a
    /// property rather than a LoadModule parameter. Null falls back to
    /// <see cref="JsxParseOptions.Default"/> — .tsx files always parse in the TSX dialect.
    /// </summary>
    public JsxParseOptions? JsxOptions { get; set; }

    /// <summary>
    /// Retains the parser's partial AST and syntax diagnostics instead of throwing on
    /// the first recovered parse error. Intended for diagnostic-oriented callers such
    /// as language conformance runners; execution and compilation remain fail-fast.
    /// </summary>
    public bool RecoverParseErrors { get; set; }

    private static bool IsJsxSourcePath(string path) =>
        path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves a module specifier to an absolute file path.
    /// </summary>
    /// <param name="specifier">The import specifier (e.g., './foo', '../bar', 'lodash')</param>
    /// <param name="currentModulePath">The path of the module containing the import</param>
    /// <param name="kind">Whether this is an ESM import or a CJS require (controls exports
    /// conditions). Defaults to <see cref="ResolutionKind.Esm"/> — call sites that represent a
    /// <c>require()</c> or literal CJS specifier should pass <see cref="ResolutionKind.Cjs"/>
    /// so dual-export packages route to the correct entry.</param>
    /// <returns>Absolute path to the resolved module</returns>
    /// <exception cref="Exception">If the module cannot be found</exception>
    public string ResolveModulePath(string specifier, string currentModulePath, ResolutionKind kind = ResolutionKind.Esm)
        => ResolveModulePathCore(
            specifier, currentModulePath, kind, _programOptions.PreferDeclarationFiles);

    /// <summary>
    /// Resolves the executable target for an import. Unlike <see cref="ResolveModulePath"/>,
    /// package <c>types</c>/<c>typings</c> entries and <c>types</c> export conditions are
    /// ignored.
    /// </summary>
    public string ResolveRuntimeModulePath(
        string specifier,
        string currentModulePath,
        ResolutionKind kind = ResolutionKind.Esm)
        => ResolveModulePathCore(specifier, currentModulePath, kind, preferDeclarations: false);

    private string ResolveModulePathCore(
        string specifier,
        string currentModulePath,
        ResolutionKind kind,
        bool preferDeclarations)
    {
        string currentDir = Path.GetDirectoryName(currentModulePath) ?? _basePath;

        // dotnet: scheme — .NET interop imports resolve via reflection, not the file system.
        // The specifier itself is the virtual module path (and cache key).
        if (DotNetImports.IsDotNetSpecifier(specifier) ||
            DotNetExtensionImports.IsSpecifier(specifier))
        {
            if (kind == ResolutionKind.Cjs)
            {
                throw new Exception(
                    $"Module Error: '{specifier}' is not available via require(). " +
                    "Use a named ESM import instead: import { TypeName } from \"" + specifier + "\".");
            }
            return specifier;
        }

        // Imports inside an embedded npm fallback are package-internal and must stay on
        // that fallback graph. A program-level ambient declaration may shadow the package
        // for user imports, but must not retarget the shim's own dependencies midway
        // through checking.
        if (currentModulePath.StartsWith(EmbeddedStdlibProvider.VirtualPathPrefix, StringComparison.Ordinal) &&
            _npmFallback.TryResolve(specifier, out var npmIntraShim) && npmIntraShim is not null)
        {
            return npmIntraShim.VirtualPath;
        }

        if (preferDeclarations &&
            _ambientModulePaths.TryGetValue(specifier, out string? ambientPath))
            return ambientPath;

        if (specifier.StartsWith("./") || specifier.StartsWith("../") ||
            specifier.StartsWith(".\\") || specifier.StartsWith("..\\"))
        {
            // Relative path
            string resolved = Path.GetFullPath(Path.Combine(currentDir, specifier));
            return AddExtensionIfNeeded(resolved);
        }
        else if (Path.IsPathRooted(specifier))
        {
            // Absolute path
            return AddExtensionIfNeeded(specifier);
        }
        else if (TryResolvePaths(specifier, out string? mapped))
        {
            return mapped!;
        }
        else if (specifier.StartsWith('#'))
        {
            if (!_resolutionOptions.UsesPackageMaps)
                throw new Exception(
                    $"Module Error: '{specifier}' requires node16, nodenext, or bundler module resolution.");
            // Subpath imports (#-prefixed) — resolve through nearest package.json "imports" field
            string? result = TryResolveSubpathImport(
                specifier, currentDir, kind, preferDeclarations);
            if (result != null)
                return result;
            throw new Exception($"Module Error: Cannot resolve subpath import '{specifier}'. " +
                                "No matching entry found in the nearest package.json \"imports\" field.");
        }
        else
        {
            // Strip 'node:' prefix (e.g., 'node:fs' -> 'fs')
            var bareSpecifier = specifier.StartsWith("node:") ? specifier[5..] : specifier;

            // Origin-gate primitive:* specifiers. The narrow C# interop surface that
            // stdlib TS modules rely on is intentionally hidden from user code —
            // only modules already loaded from stdlib: virtual paths may reach it.
            // Leaking it would couple user programs to an unstable internal API.
            if (PrimitiveRegistry.IsPrimitive(bareSpecifier) &&
                !currentModulePath.StartsWith(EmbeddedStdlibProvider.VirtualPathPrefix, StringComparison.Ordinal))
            {
                throw new Exception(
                    $"Module Error: Cannot import '{specifier}'. The primitive: namespace " +
                    "is reserved for SharpTS stdlib modules and is not available to user code.");
            }

            // Consult the stdlib provider chain. In the current phase, only the
            // BuiltInCSharpProvider claims anything, so this is behaviorally identical
            // to the legacy IsBuiltIn check. Once TypeScript stdlib modules ship as
            // embedded resources, the EmbeddedStdlibProvider answers first and returns
            // a "stdlib:node/<name>.ts" virtual path, causing LoadModule to compile
            // the TS source in place of the C# built-in dispatch.
            if (_stdlibChain.TryResolve(bareSpecifier, out var stdlibModule) && stdlibModule is not null)
            {
                return stdlibModule.VirtualPath;
            }

            // Imports issued from inside stdlib virtual modules never have a real directory
            // to probe node_modules from, so the npm-fallback shim answers immediately
            // (e.g. the react shim's own `react/jsx-runtime` re-exports).
            if (currentModulePath.StartsWith(EmbeddedStdlibProvider.VirtualPathPrefix, StringComparison.Ordinal) &&
                _npmFallback.TryResolve(bareSpecifier, out var npmIntraPackage) && npmIntraPackage is not null)
            {
                return npmIntraPackage.VirtualPath;
            }

            if (_resolutionOptions.BaseUrl is not null)
            {
                string? baseUrlResult = TryAddExtension(
                    Path.GetFullPath(Path.Combine(_resolutionOptions.BaseUrl, specifier)));
                if (baseUrlResult is not null)
                    return baseUrlResult;
            }

            if (_resolutionOptions.Mode == ModuleResolutionMode.Classic)
            {
                throw new Exception(
                    $"Module Error: Cannot resolve bare specifier '{specifier}' with classic module resolution.");
            }

            // Try self-referencing: if nearest package.json has "name" matching the specifier
            string? selfRef = _resolutionOptions.UsesPackageMaps
                ? TryResolveSelfReference(specifier, currentDir, kind, preferDeclarations)
                : null;
            if (selfRef != null)
                return selfRef;

            // Bare specifier (e.g., 'lodash')
            // Look in node_modules directories
            string? resolvedPath = TryResolveNodeModule(
                specifier, currentDir, kind, preferDeclarations);
            if (resolvedPath != null)
            {
                return resolvedPath;
            }

            // npm-fallback shims (react family): consulted strictly AFTER node_modules so a
            // real installed package always wins over the embedded shim.
            if (_npmFallback.TryResolve(bareSpecifier, out var npmFallbackModule) && npmFallbackModule is not null)
            {
                return npmFallbackModule.VirtualPath;
            }

            throw new Exception($"Module Error: Cannot resolve bare specifier '{specifier}'. " +
                                "Bare imports require a node_modules directory with the package installed.");
        }
    }

    private bool TryResolvePaths(string specifier, out string? resolved)
    {
        resolved = null;
        if (_resolutionOptions.Paths.Count == 0)
            return false;

        var matches = new List<(int PrefixLength, string? Capture, IReadOnlyList<string> Targets)>();
        foreach (var (pattern, targets) in _resolutionOptions.Paths)
        {
            int star = pattern.IndexOf('*');
            if (star < 0)
            {
                if (string.Equals(pattern, specifier, StringComparison.Ordinal))
                    matches.Add((int.MaxValue, null, targets));
                continue;
            }

            string prefix = pattern[..star];
            string suffix = pattern[(star + 1)..];
            if (specifier.StartsWith(prefix, StringComparison.Ordinal) &&
                specifier.EndsWith(suffix, StringComparison.Ordinal) &&
                specifier.Length >= prefix.Length + suffix.Length)
            {
                string capture = specifier.Substring(
                    prefix.Length, specifier.Length - prefix.Length - suffix.Length);
                matches.Add((prefix.Length, capture, targets));
            }
        }

        foreach (var match in matches.OrderByDescending(m => m.PrefixLength))
        {
            foreach (string target in match.Targets)
            {
                string candidate = match.Capture is null
                    ? target
                    : target.Replace("*", match.Capture, StringComparison.Ordinal);
                resolved = TryAddExtension(candidate);
                if (resolved is not null)
                    return true;
            }
        }

        resolved = null;
        return false;
    }

    /// <summary>
    /// Tries to resolve a bare specifier by looking in node_modules directories.
    /// Supports package.json "exports" field, "main"/"types" fallback, and legacy index.ts.
    /// </summary>
    private string? TryResolveNodeModule(
        string specifier,
        string startDir,
        ResolutionKind kind,
        bool preferDeclarations)
    {
        var (packageName, subpath) = ParsePackageSpecifier(specifier);
        string? currentDir = startDir;

        while (currentDir != null)
        {
            string packageDir = Path.Combine(currentDir, "node_modules", packageName);

            if (ResolverDirectoryExists(packageDir))
            {
                var result = TryResolveInPackageDir(
                    packageDir, subpath, kind, preferDeclarations);
                if (result != null)
                    return result;
            }

            // Also try as a direct file (e.g., node_modules/foo.ts, node_modules/foo.js)
            if (subpath == ".")
            {
                foreach (var ext in SourceExtensions)
                {
                    string directPath = Path.Combine(currentDir, "node_modules", packageName + ext);
                    if (ResolverFileExists(directPath))
                        return directPath;
                }
            }

            // Move up one directory, stopping at ambient-walk ceilings (temp root, user profile)
            currentDir = FileDiscovery.AmbientParent(currentDir);
        }

        return null;
    }

    /// <summary>
    /// Attempts to resolve a subpath within a specific package directory.
    /// </summary>
    private string? TryResolveInPackageDir(
        string packageDir,
        string subpath,
        ResolutionKind kind,
        bool preferDeclarations)
    {
        string packageJsonPath = Path.Combine(packageDir, "package.json");
        var pkg = LoadPackageJson(packageJsonPath);

        if (_resolutionOptions.UsesPackageMaps && pkg?.Exports != null)
        {
            // Use exports field
            var resolved = ExportsResolver.ResolvePackageExports(
                pkg.Exports.Value, subpath, ConditionsFor(kind, preferDeclarations));
            if (resolved != null)
                return ResolveExportsPath(resolved, packageDir);
            // Exports field exists but no match — per spec, this blocks resolution
            return null;
        }

        if (pkg != null && subpath == ".")
        {
            // Type checking follows types/typings; execution follows the JavaScript entry.
            string? entryPath = preferDeclarations
                ? pkg.Types ?? pkg.Typings ?? pkg.Main ?? pkg.Module
                : pkg.Main ?? pkg.Module;
            if (entryPath != null)
            {
                var mapped = ResolveExportsPath(
                    entryPath.StartsWith("./") ? entryPath : "./" + entryPath, packageDir);
                if (mapped != null)
                    return mapped;
            }
        }

        if (subpath != ".")
        {
            // No exports — resolve subpath directly against package dir
            string subFile = Path.Combine(packageDir, subpath.TrimStart('.', '/'));
            return TryAddExtension(subFile);
        }

        // Legacy fallback: try index.* (any known source extension)
        foreach (var ext in SourceExtensions)
        {
            string indexPath = Path.Combine(packageDir, "index" + ext);
            if (ResolverFileExists(indexPath))
                return indexPath;
        }

        return null;
    }

    /// <summary>
    /// Resolves a path from the exports algorithm, applying extension mapping (.js → .ts, etc.).
    /// </summary>
    private string? ResolveExportsPath(string resolvedRelative, string packageDir)
    {
        // Strip leading "./" and combine with package dir
        string relPath = resolvedRelative.StartsWith("./") ? resolvedRelative[2..] : resolvedRelative;
        string fullPath = Path.GetFullPath(Path.Combine(packageDir, relPath));
        return TryAddExtension(fullPath);
    }

    /// <summary>
    /// Tries to add a file extension to a path, returning null if nothing resolves.
    /// </summary>
    private string? TryAddExtension(string path)
    {
        path = Path.GetFullPath(path);
        if (ResolverFileExists(path))
            return path;

        foreach (var ext in SourceExtensions)
        {
            string candidate = path + ext;
            if (ResolverFileExists(candidate)) return candidate;
        }

        // TypeScript source substitution for JavaScript-flavoured specifiers.
        string[] substitutions = path.ToLowerInvariant() switch
        {
            var value when value.EndsWith(".mjs") => [".mts", ".d.mts"],
            var value when value.EndsWith(".cjs") => [".cts", ".d.cts"],
            var value when value.EndsWith(".jsx") => [".tsx", ".ts", ".d.ts"],
            var value when value.EndsWith(".js") => [".ts", ".tsx", ".d.ts"],
            _ => [],
        };
        if (substitutions.Length > 0)
        {
            int extensionLength = Path.GetExtension(path).Length;
            string stem = path[..^extensionLength];
            foreach (string extension in substitutions)
            {
                string candidate = stem + extension;
                if (ResolverFileExists(candidate))
                    return candidate;
            }
        }

        if (ResolverDirectoryExists(path))
        {
            foreach (var ext in SourceExtensions)
            {
                string indexPath = Path.Combine(path, "index" + ext);
                if (ResolverFileExists(indexPath)) return indexPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses a bare specifier into (packageName, subpath).
    /// </summary>
    public static (string packageName, string subpath) ParsePackageSpecifier(string specifier)
    {
        if (specifier.StartsWith('@'))
        {
            // Scoped package: @scope/pkg or @scope/pkg/utils
            int firstSlash = specifier.IndexOf('/');
            if (firstSlash < 0)
                return (specifier, ".");

            int secondSlash = specifier.IndexOf('/', firstSlash + 1);
            if (secondSlash < 0)
                return (specifier, ".");

            return (specifier[..secondSlash], "./" + specifier[(secondSlash + 1)..]);
        }
        else
        {
            // Unscoped package: lodash or lodash/fp
            int firstSlash = specifier.IndexOf('/');
            if (firstSlash < 0)
                return (specifier, ".");

            return (specifier[..firstSlash], "./" + specifier[(firstSlash + 1)..]);
        }
    }

    /// <summary>
    /// Resolves #-prefixed subpath imports through the nearest package.json "imports" field.
    /// </summary>
    private string? TryResolveSubpathImport(
        string specifier,
        string startDir,
        ResolutionKind kind,
        bool preferDeclarations)
    {
        string? dir = startDir;
        while (dir != null)
        {
            string pkgPath = Path.Combine(dir, "package.json");
            var pkg = LoadPackageJson(pkgPath);
            if (pkg != null)
            {
                if (pkg.Imports != null)
                {
                    var resolved = ExportsResolver.ResolvePackageImports(
                        pkg.Imports.Value, specifier, ConditionsFor(kind, preferDeclarations));
                    if (resolved != null)
                        return ResolveExportsPath(resolved, dir);
                }
                // Found a package.json but no matching import — stop walking
                return null;
            }
            dir = FileDiscovery.AmbientParent(dir);
        }
        return null;
    }

    /// <summary>
    /// Resolves self-referencing imports (when a package imports itself by name through its own exports).
    /// </summary>
    private string? TryResolveSelfReference(
        string specifier,
        string startDir,
        ResolutionKind kind,
        bool preferDeclarations)
    {
        var (packageName, subpath) = ParsePackageSpecifier(specifier);

        string? dir = startDir;
        while (dir != null)
        {
            string pkgPath = Path.Combine(dir, "package.json");
            var pkg = LoadPackageJson(pkgPath);
            if (pkg?.Name == packageName && pkg.Exports != null)
            {
                var resolved = ExportsResolver.ResolvePackageExports(
                    pkg.Exports.Value, subpath, ConditionsFor(kind, preferDeclarations));
                if (resolved != null)
                    return ResolveExportsPath(resolved, dir);
                return null;
            }
            dir = FileDiscovery.AmbientParent(dir);
        }
        return null;
    }

    private string[] ConditionsFor(ResolutionKind kind, bool preferDeclarations)
    {
        if (_resolutionOptions.Mode == ModuleResolutionMode.Bundler)
        {
            if (preferDeclarations)
                return kind == ResolutionKind.Cjs
                    ? ["types", "require", "default"]
                    : ["types", "import", "default"];
            return kind == ResolutionKind.Cjs ? ["require", "default"] : ["import", "default"];
        }

        return (kind, preferDeclarations) switch
        {
            (ResolutionKind.Cjs, true) => ExportsResolver.TypeCjsConditions,
            (_, true) => ExportsResolver.TypeEsmConditions,
            (ResolutionKind.Cjs, false) => ExportsResolver.CjsConditions,
            _ => ExportsResolver.EsmConditions,
        };
    }

    /// <summary>
    /// Loads a package.json with caching.
    /// </summary>
    private ModulePackageJson? LoadPackageJson(string path)
    {
        if (_packageJsonCache.TryGetValue(path, out var cached))
            return cached;

        // Route through ResolverFileExists/ResolverReadAllText so test mode (virtual FS)
        // can serve package.json from the in-memory map.
        ModulePackageJson? pkg = null;
        if (ResolverFileExists(path))
        {
            try { pkg = ModulePackageJson.TryLoadFromContent(ResolverReadAllText(path)); }
            catch { pkg = null; }
        }
        _packageJsonCache[path] = pkg;
        return pkg;
    }

    private static readonly string[] SourceExtensions =
        [".ts", ".tsx", ".d.ts", ".cts", ".d.cts", ".mts", ".d.mts", ".js", ".jsx", ".cjs", ".mjs"];

    private string AddExtensionIfNeeded(string path)
    {
        return TryAddExtension(path)
            ?? throw new Exception($"Module Error: Cannot resolve module '{path}'. File not found.");
    }

    /// <summary>
    /// Resolves a triple-slash reference path to an absolute file path.
    /// </summary>
    /// <param name="refPath">The path specified in the reference directive.</param>
    /// <param name="containingFilePath">The absolute path of the file containing the directive.</param>
    /// <returns>Absolute path to the referenced file.</returns>
    private string ResolveReferencePath(string refPath, string containingFilePath)
    {
        string directory = Path.GetDirectoryName(containingFilePath)!;
        string resolved = Path.GetFullPath(Path.Combine(directory, refPath));

        // Add .ts extension if needed
        if (!ResolverFileExists(resolved) && !resolved.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
        {
            resolved += ".ts";
        }

        if (!ResolverFileExists(resolved))
        {
            throw new Exception($"Type Error: Referenced file not found: '{refPath}' (resolved to '{resolved}')");
        }

        return resolved;
    }

    /// <summary>
    /// Loads a script file referenced via triple-slash directive.
    /// Uses separate circular detection from module imports.
    /// </summary>
    /// <param name="absolutePath">Absolute path to the script file.</param>
    /// <param name="decoratorMode">Decorator mode for parsing.</param>
    /// <param name="referencingFile">The file that contains the reference (for error messages).</param>
    /// <returns>The parsed script module.</returns>
    private ParsedModule LoadScriptReference(string absolutePath, DecoratorMode decoratorMode, string referencingFile)
    {
        absolutePath = Path.GetFullPath(absolutePath);

        // Return cached module if already loaded
        if (_moduleCache.TryGetValue(absolutePath, out var cached))
        {
            return cached;
        }

        // Check for circular reference
        if (_loadingScriptRefs.Contains(absolutePath))
        {
            throw new Exception($"Type Error: Circular triple-slash reference detected: '{absolutePath}' is referenced while still being processed.");
        }

        _loadingScriptRefs.Add(absolutePath);

        try
        {
            // Load the script using the normal LoadModule path
            // This will also process any nested path references
            return LoadModule(absolutePath, decoratorMode);
        }
        finally
        {
            _loadingScriptRefs.Remove(absolutePath);
        }
    }

    /// <summary>
    /// Loads a module and all its dependencies, detecting circular dependencies.
    /// </summary>
    /// <param name="absolutePath">Absolute path to the module file</param>
    /// <param name="decoratorMode">Decorator mode to use for parsing</param>
    /// <returns>The parsed module with dependencies populated</returns>
    /// <exception cref="Exception">If a circular dependency is detected</exception>
    public ParsedModule LoadModule(string absolutePath, DecoratorMode decoratorMode = DecoratorMode.None)
    {
        if (absolutePath.StartsWith(AmbientModulePrefix, StringComparison.Ordinal))
        {
            return _moduleCache.TryGetValue(absolutePath, out var ambient)
                ? ambient
                : throw new Exception($"Module Error: Unknown ambient module '{absolutePath}'.");
        }

        if (absolutePath.StartsWith(TypeScriptLibProvider.VirtualPathPrefix, StringComparison.Ordinal))
            return LoadTypeScriptLibModule(absolutePath, decoratorMode);

        // Embedded stdlib TypeScript module — compile from resource instead of filesystem.
        if (absolutePath.StartsWith(EmbeddedStdlibProvider.VirtualPathPrefix, StringComparison.Ordinal))
        {
            return LoadStdlibModule(absolutePath, decoratorMode);
        }

        // dotnet: interop module — synthesized placeholder; exports are added per importing
        // statement by DotNetImports.EnsureImports (see the import loop below).
        if (DotNetImports.IsDotNetSpecifier(absolutePath))
        {
            if (!_moduleCache.TryGetValue(absolutePath, out var dotnetModule))
            {
                dotnetModule = DotNetImports.CreateModule(absolutePath);
                _moduleCache[absolutePath] = dotnetModule;
            }
            return dotnetModule;
        }

        if (DotNetExtensionImports.IsSpecifier(absolutePath))
        {
            if (!_moduleCache.TryGetValue(absolutePath, out var extensionModule))
            {
                extensionModule = DotNetExtensionImports.CreateModule(absolutePath);
                _moduleCache[absolutePath] = extensionModule;
            }
            return extensionModule;
        }

        // Primitive C# module — materialize a placeholder ParsedModule with types.
        // Origin-gating in ResolveModulePath has already ensured only stdlib-origin
        // modules resolve here, so no per-caller check is needed.
        if (absolutePath.StartsWith(PrimitiveRegistry.Prefix, StringComparison.Ordinal))
        {
            if (!_moduleCache.TryGetValue(absolutePath, out var primitiveModule))
            {
                var primitiveName = PrimitiveRegistry.GetPrimitiveName(absolutePath)!;
                primitiveModule = new ParsedModule(absolutePath, []) { IsBuiltIn = true, IsTypeChecked = true };
                var primitiveTypes = BuiltInModuleTypes.GetPrimitiveTypes(primitiveName);
                if (primitiveTypes != null)
                {
                    foreach (var (name, type) in primitiveTypes)
                    {
                        primitiveModule.ExportedTypes[name] = type;
                    }
                    primitiveModule.DefaultExportType = new TypeInfo.Record(
                        primitiveModule.ExportedTypes.ToFrozenDictionary());
                }
                _moduleCache[absolutePath] = primitiveModule;
            }
            return primitiveModule;
        }

        // Skip built-in modules - they don't need to be loaded from files
        if (absolutePath.StartsWith(BuiltInModuleRegistry.BuiltInPrefix))
        {
            // Return a placeholder module for built-in modules
            var moduleName = BuiltInModuleRegistry.GetModuleName(absolutePath) ?? "builtin";
            if (!_moduleCache.TryGetValue(absolutePath, out var builtinModule))
            {
                builtinModule = new ParsedModule(absolutePath, []) { IsBuiltIn = true, IsTypeChecked = true };
                // Populate the exported types from the built-in module type definitions
                var moduleTypes = BuiltInModuleTypes.GetModuleTypes(moduleName);
                if (moduleTypes != null)
                {
                    foreach (var (name, type) in moduleTypes)
                    {
                        builtinModule.ExportedTypes[name] = type;
                    }

                    // Set default export to a record of all exports, enabling: import fs from 'fs'
                    builtinModule.DefaultExportType = new TypeInfo.Record(
                        builtinModule.ExportedTypes.ToFrozenDictionary()
                    );
                }
                _moduleCache[absolutePath] = builtinModule;
            }
            return builtinModule;
        }

        absolutePath = Path.GetFullPath(absolutePath);

        // Return cached module if already loaded
        if (_moduleCache.TryGetValue(absolutePath, out var cached))
        {
            return cached;
        }

        // Check for circular dependency
        if (_loadingModules.Contains(absolutePath))
        {
            throw new Exception($"Module Error: Circular dependency detected involving '{absolutePath}'.");
        }

        _loadingModules.Add(absolutePath);

        try
        {
            string source = ResolverReadAllText(absolutePath);

            bool isJsxSource = IsJsxSourcePath(absolutePath);
            var lexer = new Lexer(source) { JsxTolerant = isJsxSource };
            var tokens = lexer.ScanTokens();
            // Parse into a document so the module keeps its text, checksum and statement spans —
            // what debug symbols and editor navigation both resolve positions against.
            var document = new SourceDocument(absolutePath, source);
            var parser = new Parser(tokens, decoratorMode)
                .WithSourceDocument(document)
                .AsDeclarationFile(IsDeclarationFilePath(absolutePath))
                .WithFilePath(absolutePath)
                .WithMaxErrors(RecoverParseErrors ? 1000 : 10);
            if (isJsxSource)
                parser.WithJsx(source, (JsxOptions ?? JsxParseOptions.Default).ApplyPragmas(lexer.Pragmas));
            var parseResult = parser.Parse();

            // Product loading remains fail-fast. Diagnostic-oriented callers can keep
            // the recovered statement list and compare the parser diagnostics instead.
            if (!parseResult.IsSuccess && !RecoverParseErrors)
            {
                string? parentName = Path.GetFileName(Path.GetDirectoryName(absolutePath));
                string displayName = string.IsNullOrEmpty(parentName)
                    ? Path.GetFileName(absolutePath)
                    : $"{parentName}/{Path.GetFileName(absolutePath)}";
                throw new Exception(
                    $"{parseResult.Diagnostics.First()} in '{displayName}' ({absolutePath})");
            }

            var statements = parseResult.Statements;
            var module = new ParsedModule(absolutePath, statements)
            {
                IsDeclarationFile = IsDeclarationFilePath(absolutePath),
                Document = document,
            };
            module.ParseDiagnostics.AddRange(parseResult.Diagnostics);

            // Determine if this is a script or module file
            module.IsScript = ScriptDetector.IsScriptFile(statements);

            // Determine if this is a CommonJS module
            module.IsCommonJs = CommonJsDetector.Detect(absolutePath) == CommonJsDetector.ModuleKind.CommonJs;

            // CommonJS files are modules, not scripts — they have isolated scope and their own
            // synthetic require/module/exports bindings. Override the no-import/export heuristic.
            if (module.IsCommonJs)
            {
                module.IsScript = false;
            }

            // Declaration graphs are routinely cyclic (including @types/node).
            // Publish the parsed shell before following references/imports so a
            // back-edge can reuse it. Runtime modules retain the historical
            // circular-dependency diagnostic.
            if (module.IsDeclarationFile)
                _moduleCache[absolutePath] = module;

            // Process triple-slash path references. TypeScript permits a module to
            // reference a script or declaration file; the referenced file contributes
            // its global declarations to the program without changing the referrer's
            // module scope.
            // NOTE: Process BEFORE caching to properly detect circular references
            var directives = lexer.TripleSlashDirectives;
            var pathRefs = directives.Where(d => d.Type == TripleSlashReferenceType.Path).ToList();
            module.NoDefaultLib = directives.Any(d => d.Type == TripleSlashReferenceType.NoDefaultLib);

            if (pathRefs.Count > 0)
            {
                // Load referenced scripts
                foreach (var pathRef in pathRefs)
                {
                    string refPath = ResolveReferencePath(pathRef.Value, absolutePath);
                    var refModule = LoadScriptReference(refPath, decoratorMode, absolutePath);

                    // A referenced declaration file can introduce ambient external modules
                    // consumed later in this file (the TypeScript JSX corpus commonly
                    // references react.d.ts and then imports `react`). Register those names
                    // before resolving this module's import statements so a real declaration
                    // wins over an executable fallback package.
                    if (refModule.IsDeclarationFile)
                        RegisterAmbientModuleDeclarations([refModule]);

                    if (!refModule.IsScript && !refModule.IsDeclarationFile)
                    {
                        throw new Exception($"Type Error: /// <reference path=\"{pathRef.Value}\"> cannot reference a module file. Referenced file '{refPath}' contains import/export statements.");
                    }

                    module.PathReferences.Add(pathRef);
                    AddTypeDependency(module, refModule);
                }
            }

            // Runtime scripts are cached only after path-reference processing so
            // their existing circular triple-slash diagnostic remains intact.
            if (!module.IsDeclarationFile)
                _moduleCache[absolutePath] = module;

            foreach (var libRef in directives.Where(d => d.Type == TripleSlashReferenceType.Lib))
            {
                var library = LoadTypeScriptLibModule(
                    TypeScriptLibProvider.GetVirtualPath(libRef.Value), decoratorMode);
                if (!module.ReferencedScripts.Contains(library))
                    module.ReferencedScripts.Add(library);
            }

            foreach (var typeRef in directives.Where(d => d.Type == TripleSlashReferenceType.Types))
            {
                string declarationPath = ResolveTypeReferenceDirective(typeRef.Value, absolutePath)
                    ?? throw new Exception(
                        $"Type Error: Cannot find type definition file for '{typeRef.Value}'.");
                var declarationModule = LoadModule(declarationPath, decoratorMode);
                RegisterAmbientModuleDeclarations([declarationModule]);
                AddTypeDependency(module, declarationModule);
            }

            // CommonJS modules use runtime require() instead of static imports. We still walk
            // their bodies for literal `require('./literal')` calls so the compiler can pre-load
            // the dependency graph (interpreter mode tolerates lazy discovery, but the AOT
            // compiler needs every CJS module to be present in the same assembly).
            if (module.IsCommonJs)
            {
                CollectCjsRequireDependencies(module, statements, absolutePath, decoratorMode);
                return module;
            }

            // Recursively load imported modules
            foreach (var stmt in statements)
            {
                if (stmt is Stmt.Import import)
                {
                    if (module.IsDeclarationFile
                        && IsRuntimeFacadeSpecifier(import.ModulePath))
                    {
                        // A declaration package's `node:*` imports refer to its
                        // ambient module declarations, not SharpTS's executable
                        // stdlib facade with the same specifier.
                        continue;
                    }

                    ParsedModule importedModule;
                    try
                    {
                        string importedPath = ResolveModulePath(import.ModulePath, absolutePath);
                        importedModule = LoadModule(importedPath, decoratorMode);
                    }
                    catch (Exception ex) when (
                        _programOptions.PreferDeclarationFiles
                        && ex.Message.StartsWith("Module Error: Cannot resolve", StringComparison.Ordinal))
                    {
                        // Program loading must keep the rest of the graph alive
                        // so the checker can report canonical TS2307 at the
                        // import site. Runtime-only resolution remains eager.
                        continue;
                    }
                    // dotnet: modules resolve their export surface from the importing
                    // statements — each named import is resolved (and validated) here.
                    if (importedModule.IsDotNetModule)
                    {
                        DotNetImports.EnsureImports(importedModule, import);
                    }
                    else if (importedModule.IsDotNetExtensionModule)
                    {
                        DotNetExtensionImports.EnsureSideEffectImport(
                            module, importedModule, import);
                    }
                    // Files loaded via import are always modules, even if they have no exports
                    // (e.g., side-effect imports like `import './polyfill'`)
                    importedModule.IsScript = false;
                    if (!module.Dependencies.Contains(importedModule))
                    {
                        module.Dependencies.Add(importedModule);
                    }
                    if (!module.IsDeclarationFile &&
                        ImportHasRuntimeBinding(import) &&
                        importedModule.IsDeclarationFile)
                    {
                        TryAddRuntimeDependency(
                            module, import.ModulePath, absolutePath,
                            ResolutionKind.Esm, decoratorMode);
                    }
                }
                else if (stmt is Stmt.Export export && export.FromModulePath != null)
                {
                    if (module.IsDeclarationFile
                        && IsRuntimeFacadeSpecifier(export.FromModulePath))
                    {
                        continue;
                    }

                    // Re-export: export { x } from './foo' or export * from './foo'
                    string reexportPath;
                    try
                    {
                        reexportPath = ResolveModulePath(export.FromModulePath, absolutePath);
                    }
                    catch (Exception ex) when (
                        _programOptions.PreferDeclarationFiles
                        && ex.Message.StartsWith("Module Error: Cannot resolve", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (DotNetImports.IsDotNetSpecifier(reexportPath))
                    {
                        throw new Exception(
                            $"Module Error: re-exporting from '{export.FromModulePath}' is not supported. " +
                            "Import the type and re-export the local binding instead.");
                    }
                    var reexportedModule = LoadModule(reexportPath, decoratorMode);
                    // Re-exported files are always modules
                    reexportedModule.IsScript = false;
                    if (!module.Dependencies.Contains(reexportedModule))
                    {
                        module.Dependencies.Add(reexportedModule);
                    }
                    if (!module.IsDeclarationFile &&
                        ExportHasRuntimeBinding(export) &&
                        reexportedModule.IsDeclarationFile)
                    {
                        TryAddRuntimeDependency(
                            module, export.FromModulePath, absolutePath,
                            ResolutionKind.Esm, decoratorMode);
                    }
                }
                else if (stmt is Stmt.ImportRequire importReq)
                {
                    if (module.IsDeclarationFile
                        && IsRuntimeFacadeSpecifier(importReq.ModulePath))
                    {
                        continue;
                    }

                    // CommonJS-style import: import x = require('./foo')
                    // Skip built-in modules (fs, path, etc.)
                    if (BuiltInModuleRegistry.GetModuleName(importReq.ModulePath) != null)
                    {
                        continue;
                    }

                    if (DotNetImports.IsDotNetSpecifier(importReq.ModulePath))
                    {
                        throw new Exception(
                            $"Module Error: '{importReq.ModulePath}' is not available via import-require. " +
                            "Use a named ESM import instead: import { TypeName } from \"" + importReq.ModulePath + "\".");
                    }

                    string importedPath = ResolveModulePath(importReq.ModulePath, absolutePath);
                    var importedModule = LoadModule(importedPath, decoratorMode);
                    // Files loaded via require are always modules
                    importedModule.IsScript = false;
                    if (!module.Dependencies.Contains(importedModule))
                    {
                        module.Dependencies.Add(importedModule);
                    }
                    if (!module.IsDeclarationFile && importedModule.IsDeclarationFile)
                    {
                        TryAddRuntimeDependency(
                            module, importReq.ModulePath, absolutePath,
                            ResolutionKind.Cjs, decoratorMode);
                    }
                }
            }

            // ESM modules (and scripts) can also reach modules via bare require() — it's a
            // global in SharpTS, mirroring Node's createRequire interop. Walk the body for
            // literal require() specifiers so they get bundled into compiled output. This
            // covers both the global require interop (#1217) and the canonical
            // `const require = createRequire(import.meta.url)` pattern from node:module.
            // Compiled mode is strict-AOT, so every literal target must be present in the
            // static graph; the interpreter still resolves the same call lazily.
            CollectCjsRequireDependencies(module, statements, absolutePath, decoratorMode);

            return module;
        }
        finally
        {
            _loadingModules.Remove(absolutePath);
        }
    }

    private bool IsRuntimeFacadeSpecifier(string specifier)
    {
        // A node:* specifier in a declaration file always refers to an ambient module
        // declaration or the stdlib facade — never to a loadable source file. @types/node
        // imports modules SharpTS's stdlib doesn't provide (e.g. node:console); those must
        // bind ambiently rather than fall through to bare-specifier file resolution.
        if (specifier.StartsWith("node:", StringComparison.Ordinal))
            return true;
        return _stdlibChain.TryResolve(specifier, out var resolved)
            && resolved is not null;
    }

    private static bool ImportHasRuntimeBinding(Stmt.Import import)
    {
        if (import.IsTypeOnly)
            return false;
        if (import.DefaultImport is not null || import.NamespaceImport is not null)
            return true;
        if (import.NamedImports is null)
            return true; // Side-effect import.
        return import.NamedImports.Any(specifier => !specifier.IsTypeOnly);
    }

    private static bool ExportHasRuntimeBinding(Stmt.Export export)
    {
        if (export.IsTypeOnly)
            return false;
        return export.NamedExports is null ||
               export.NamedExports.Any(specifier => !specifier.IsTypeOnly);
    }

    private void TryAddRuntimeDependency(
        ParsedModule owner,
        string specifier,
        string containingPath,
        ResolutionKind kind,
        DecoratorMode decoratorMode)
    {
        try
        {
            string runtimePath = ResolveRuntimeModulePath(specifier, containingPath, kind);
            var runtimeModule = LoadModule(runtimePath, decoratorMode);
            runtimeModule.IsScript = false;
            if (!owner.RuntimeDependencies.Contains(runtimeModule))
                owner.RuntimeDependencies.Add(runtimeModule);
        }
        catch (Exception ex) when (
            _programOptions.PreferDeclarationFiles &&
            ex.Message.StartsWith("Module Error: Cannot resolve", StringComparison.Ordinal))
        {
            // A declaration-only package remains valid for type-only consumers. A value
            // consumer receives the normal runtime module-not-found diagnostic if executed.
        }
    }

    /// <summary>
    /// Loads a stdlib TypeScript module from its embedded resource. The virtual
    /// path (e.g. "stdlib:node/querystring.ts") is used as both the module ID
    /// and cache key — there is no real filesystem location.
    /// </summary>
    private ParsedModule LoadStdlibModule(string virtualPath, DecoratorMode decoratorMode)
    {
        if (_moduleCache.TryGetValue(virtualPath, out var cached))
            return cached;

        if (_loadingModules.Contains(virtualPath))
            throw new Exception($"Module Error: Circular dependency detected involving '{virtualPath}'.");

        StdlibModule? stdlibModule;
        var npmSpecifier = EmbeddedNpmFallbackProvider.TryExtractSpecifier(virtualPath);
        if (npmSpecifier is not null)
        {
            if (!_npmFallback.TryResolve(npmSpecifier, out stdlibModule) || stdlibModule is null)
                throw new Exception($"Module Error: No npm-fallback provider resolved '{npmSpecifier}'.");
        }
        else
        {
            var specifier = EmbeddedStdlibProvider.TryExtractSpecifier(virtualPath);
            if (specifier is null)
                throw new Exception($"Module Error: Malformed stdlib virtual path '{virtualPath}'.");

            if (!_stdlibChain.TryResolve(specifier, out stdlibModule) || stdlibModule is null)
                throw new Exception($"Module Error: No stdlib provider resolved '{specifier}'.");
        }

        if (stdlibModule.Source is not TypeScriptSource tsSource)
            throw new Exception($"Module Error: Stdlib module '{stdlibModule.Specifier}' is not TypeScript source.");

        _loadingModules.Add(virtualPath);
        try
        {
            var lexer = new Lexer(tsSource.Text);
            var tokens = lexer.ScanTokens();
            // Marked virtual: the stdlib is embedded in the compiler, so there is no file on disk a
            // debugger could open — its text has to travel with the symbols instead.
            var document = new SourceDocument(virtualPath, tsSource.Text, isVirtual: true);
            var parser = new Parser(tokens, decoratorMode).WithSourceDocument(document);
            var parseResult = parser.Parse();
            if (!parseResult.IsSuccess)
                throw new Exception(parseResult.Diagnostics.First().ToString());

            var module = new ParsedModule(virtualPath, parseResult.Statements)
            {
                IsScript = false,
                IsCommonJs = false,
                Document = document,
            };

            _moduleCache[virtualPath] = module;

            // Recursively resolve imports. Stdlib modules should only import from
            // other stdlib specifiers or C# built-ins; the resolver chain handles
            // both transparently.
            foreach (var stmt in parseResult.Statements)
            {
                if (stmt is Stmt.Import import)
                {
                    var importedPath = ResolveModulePath(import.ModulePath, virtualPath);
                    var importedModule = LoadModule(importedPath, decoratorMode);
                    importedModule.IsScript = false;
                    if (!module.Dependencies.Contains(importedModule))
                        module.Dependencies.Add(importedModule);
                }
                else if (stmt is Stmt.Export export && export.FromModulePath != null)
                {
                    var reexportPath = ResolveModulePath(export.FromModulePath, virtualPath);
                    var reexportedModule = LoadModule(reexportPath, decoratorMode);
                    reexportedModule.IsScript = false;
                    if (!module.Dependencies.Contains(reexportedModule))
                        module.Dependencies.Add(reexportedModule);
                }
            }

            return module;
        }
        finally
        {
            _loadingModules.Remove(virtualPath);
        }
    }

    /// <summary>
    /// Walks a module's body for literal `require('./literal')` calls and recursively
    /// loads each target. Adds resolved targets to <see cref="ParsedModule.Dependencies"/>.
    /// Non-literal specifiers are ignored here — the IL compiler will reject them later.
    /// Unresolvable specifiers are also ignored — they'll either resolve via the optional-dep
    /// runtime throw path or surface as a compile error from the IL emitter.
    /// </summary>
    /// <param name="stdlibOnly">When true, only specifiers that resolve to embedded stdlib
    /// modules are loaded. Current callers include all literal targets for strict AOT.</param>
    private void CollectCjsRequireDependencies(
        ParsedModule module,
        List<Stmt> statements,
        string absolutePath,
        DecoratorMode decoratorMode,
        bool stdlibOnly = false)
    {
        var walker = new CjsRequireWalker();
        foreach (var stmt in statements)
        {
            walker.Visit(stmt);
        }

        foreach (var specifier in walker.Specifiers)
        {
            // Built-in modules: skip — they're not loaded via the file system.
            if (BuiltInModuleRegistry.IsBuiltIn(specifier.StartsWith("node:") ? specifier[5..] : specifier))
                continue;

            string requiredPath;
            try
            {
                // Literal require() — pass Cjs so dual-export packages route to the
                // "require" entry, not "import" (matches Node semantics).
                requiredPath = ResolveRuntimeModulePath(
                    specifier, absolutePath, ResolutionKind.Cjs);
            }
            catch
            {
                // Optional dep / will be handled at runtime by the optional-dep throw path.
                continue;
            }

            if (stdlibOnly &&
                !requiredPath.StartsWith(EmbeddedStdlibProvider.VirtualPathPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            ParsedModule requiredModule;
            try
            {
                requiredModule = LoadModule(requiredPath, decoratorMode);
            }
            catch
            {
                continue;
            }

            if (!module.Dependencies.Contains(requiredModule))
            {
                module.Dependencies.Add(requiredModule);
            }
        }
    }

    /// <summary>
    /// AST walker that collects literal-specifier require() call arguments from a CJS body.
    /// </summary>
    private sealed class CjsRequireWalker : AstVisitorBase
    {
        public List<string> Specifiers { get; } = [];

        protected override void VisitCall(Expr.Call expr)
        {
            if (expr.Callee is Expr.Variable v &&
                v.Name.Lexeme == "require" &&
                expr.Arguments.Count == 1 &&
                expr.Arguments[0] is Expr.Literal lit &&
                lit.Value is string specifier)
            {
                Specifiers.Add(specifier);
            }
            base.VisitCall(expr);
        }
    }

    /// <summary>
    /// Returns all loaded modules in dependency order (topological sort).
    /// Dependencies and script references come before the modules that depend on them.
    /// </summary>
    /// <param name="entryPoint">The entry point module</param>
    /// <returns>List of modules in dependency order</returns>
    public List<ParsedModule> GetModulesInOrder(ParsedModule entryPoint)
        => GetModulesInOrder([entryPoint]);

    /// <summary>
    /// Returns executable modules in dependency order, substituting package JavaScript
    /// entries for the declaration entries used by the type-check graph.
    /// </summary>
    public List<ParsedModule> GetRuntimeModulesInOrder(ParsedModule entryPoint)
        => GetRuntimeModulesInOrder([entryPoint]);

    /// <summary>Returns the union of executable root graphs in dependency order.</summary>
    public List<ParsedModule> GetRuntimeModulesInOrder(IEnumerable<ParsedModule> entryPoints)
    {
        List<ParsedModule> result = [];
        HashSet<string> visited = [];

        void Visit(ParsedModule module)
        {
            if (!visited.Add(module.Path))
                return;

            foreach (var dependency in module.Dependencies.Where(
                         dependency => !dependency.IsDeclarationFile))
                Visit(dependency);
            foreach (var dependency in module.RuntimeDependencies)
                Visit(dependency);

            if (!module.IsDeclarationFile)
                result.Add(module);
        }

        foreach (ParsedModule entryPoint in entryPoints)
            Visit(entryPoint);
        return result;
    }

    /// <summary>
    /// Registers <c>declare module "name"</c> blocks from declaration roots as synthetic
    /// type-only modules, allowing imports to resolve without a runtime package.
    /// </summary>
    public void RegisterAmbientModuleDeclarations(IEnumerable<ParsedModule> declarationModules)
    {
        foreach (var declarationFile in declarationModules)
        {
            foreach (var statement in declarationFile.Statements)
            {
                if (statement is not Stmt.DeclareModule declaration ||
                    declaration.ModulePath.StartsWith("./", StringComparison.Ordinal) ||
                    declaration.ModulePath.StartsWith("../", StringComparison.Ordinal))
                {
                    continue;
                }

                string virtualPath = AmbientModulePrefix + declaration.ModulePath;
                if (!_moduleCache.TryGetValue(virtualPath, out var module))
                {
                    module = new ParsedModule(virtualPath, [])
                    {
                        IsScript = false,
                        IsAmbientModule = true,
                        // Synthetic ambient modules originate in declaration files and should
                        // receive the same trusted-input/skip-lib-check treatment as their owner.
                        IsDeclarationFile = true,
                        // A bodyless ambient module is TypeScript's wildcard declaration for an
                        // untyped package. CJS-style `any` imports model that contract exactly.
                        IsCommonJs = declaration.Members.Count == 0,
                    };
                    _moduleCache[virtualPath] = module;
                    _ambientModulePaths[declaration.ModulePath] = virtualPath;
                }
                foreach (Stmt member in declaration.Members)
                {
                    if (!module.Statements.Any(existing => ReferenceEquals(existing, member)))
                        module.Statements.Add(member);
                }
                if (!declarationFile.Dependencies.Contains(module))
                    declarationFile.Dependencies.Add(module);
            }
        }
    }

    /// <summary>Returns the union of several root graphs in dependency order.</summary>
    public List<ParsedModule> GetModulesInOrder(IEnumerable<ParsedModule> entryPoints)
    {
        List<ParsedModule> result = [];
        HashSet<string> visited = [];

        void Visit(ParsedModule module)
        {
            if (visited.Contains(module.Path))
            {
                return;
            }
            visited.Add(module.Path);

            // Visit script references first (they merge into global scope)
            foreach (var refScript in module.ReferencedScripts)
            {
                Visit(refScript);
            }

            // Visit module dependencies
            foreach (var dep in module.Dependencies)
            {
                Visit(dep);
            }

            // Then add this module
            result.Add(module);
        }

        foreach (var entryPoint in entryPoints)
            Visit(entryPoint);
        return result;
    }

    /// <summary>Real source files currently present in the resolver cache.</summary>
    public IReadOnlyList<string> LoadedFilePaths =>
        _moduleCache.Keys
            .Where(File.Exists)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Gets a cached module by its absolute path.
    /// </summary>
    public ParsedModule? GetCachedModule(string absolutePath)
    {
        // Don't normalize virtual paths (builtin: sentinels, stdlib: TS sources, dotnet:
        // interop modules, primitive: C# interop modules — none resolve to a real filesystem path).
        if (!absolutePath.StartsWith(BuiltInModuleRegistry.BuiltInPrefix)
            && !absolutePath.StartsWith(EmbeddedStdlibProvider.VirtualPathPrefix, StringComparison.Ordinal)
            && !absolutePath.StartsWith(AmbientModulePrefix, StringComparison.Ordinal)
            && !DotNetImports.IsDotNetSpecifier(absolutePath)
            && !absolutePath.StartsWith(TypeScriptLibProvider.VirtualPathPrefix, StringComparison.Ordinal)
            && !DotNetExtensionImports.IsSpecifier(absolutePath)
            && !absolutePath.StartsWith(PrimitiveRegistry.Prefix, StringComparison.Ordinal))
        {
            absolutePath = Path.GetFullPath(absolutePath);
        }
        return _moduleCache.GetValueOrDefault(absolutePath);
    }

    /// <summary>
    /// Loads a complete TypeScript program: the entry graph plus standard declaration
    /// libraries selected by <see cref="TypeScriptProgramOptions"/>.
    /// </summary>
    public ParsedModule LoadProgram(string absolutePath, DecoratorMode decoratorMode = DecoratorMode.None)
    {
        var entry = LoadModule(absolutePath, decoratorMode);
        if (!_programOptions.NoLib && !entry.NoDefaultLib)
        {
            IReadOnlyList<string> requested = _programOptions.Lib is not null
                ? _programOptions.Lib
                : _programOptions.LoadDefaultLib ? ["lib.d.ts"] : [];

            foreach (string name in requested)
            {
                ParsedModule library;
                if (TypeScriptLibProvider.TryGetParsed(name, out _))
                {
                    library = LoadTypeScriptLibModule(
                        TypeScriptLibProvider.GetVirtualPath(name), decoratorMode);
                }
                else if (_virtualFiles is null)
                {
                    // Preserve support for project-local custom library files while
                    // using embedded declarations for the standard TypeScript set.
                    string physicalPath =
                        TsConfigDeclarationResolver.ResolveLibReference(absolutePath, name);
                    library = LoadModule(physicalPath, decoratorMode);
                }
                else
                {
                    // Produce the same actionable embedded-library diagnostic in VFS
                    // and conformance runs, where physical fallback is unavailable.
                    library = LoadTypeScriptLibModule(
                        TypeScriptLibProvider.GetVirtualPath(name), decoratorMode);
                }
                AddTypeDependency(entry, library);
            }
        }

        foreach (string typeName in GetAutomaticTypeDirectiveNames(absolutePath))
        {
            string? declarationPath = ResolveTypeReferenceDirective(typeName, absolutePath);
            if (declarationPath is null)
            {
                if (_programOptions.Types is not null)
                    throw new Exception(
                        $"Type Error: Cannot find type definition file for '{typeName}'.");
            }
            else
            {
                var declaration = LoadModule(declarationPath, decoratorMode);
                RegisterAmbientModuleDeclarations([declaration]);
                AddTypeDependency(entry, declaration);
            }
        }

        return entry;
    }

    private static void AddTypeDependency(ParsedModule owner, ParsedModule declaration)
    {
        if (declaration.IsScript)
        {
            if (!owner.ReferencedScripts.Contains(declaration))
                owner.ReferencedScripts.Add(declaration);
        }
        else if (!owner.Dependencies.Contains(declaration))
        {
            owner.Dependencies.Add(declaration);
        }
    }

    private IEnumerable<string> GetAutomaticTypeDirectiveNames(string containingFile)
    {
        if (_programOptions.Types is not null)
            return _programOptions.Types.Distinct(StringComparer.OrdinalIgnoreCase);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in GetEffectiveTypeRoots(containingFile))
        {
            if (_virtualFiles is not null)
            {
                string prefix = NormalizePath(root).TrimEnd(Path.DirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                foreach (string path in _virtualFiles.Keys)
                {
                    if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string relative = path[prefix.Length..];
                    int separator = relative.IndexOf(Path.DirectorySeparatorChar);
                    if (separator > 0)
                    {
                        string package = relative[..separator];
                        names.Add(package.Contains("__", StringComparison.Ordinal)
                            ? "@" + package.Replace("__", "/", StringComparison.Ordinal)
                            : package);
                    }
                }
                continue;
            }

            if (!Directory.Exists(root))
                continue;
            try
            {
                foreach (string directory in Directory.EnumerateDirectories(root))
                {
                    string package = Path.GetFileName(directory);
                    names.Add(package.Contains("__", StringComparison.Ordinal)
                        ? "@" + package.Replace("__", "/", StringComparison.Ordinal)
                        : package);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return names;
    }

    private string? ResolveTypeReferenceDirective(string typeName, string containingFile)
    {
        string packageDirectoryName = typeName.StartsWith('@')
            ? typeName[1..].Replace("/", "__", StringComparison.Ordinal)
            : typeName;

        foreach (string root in GetEffectiveTypeRoots(containingFile))
        {
            string packageDir = Path.Combine(root, packageDirectoryName);
            if (!ResolverDirectoryExists(packageDir))
                continue;

            var package = LoadPackageJson(Path.Combine(packageDir, "package.json"));
            string? entry = package?.Types ?? package?.Typings;
            string defaultEntry = string.IsNullOrWhiteSpace(entry) ? "index.d.ts" : entry;
            string? versionedEntry = TryResolveTypesVersionEntry(
                package, packageDir, defaultEntry);
            if (versionedEntry is not null)
                return versionedEntry;

            if (!string.IsNullOrWhiteSpace(entry))
            {
                string? resolved = TryAddExtension(Path.GetFullPath(Path.Combine(packageDir, entry)));
                if (resolved is not null)
                    return resolved;
            }

            string index = Path.Combine(packageDir, "index.d.ts");
            if (ResolverFileExists(index))
                return index;
        }
        return null;
    }

    private string? TryResolveTypesVersionEntry(
        ModulePackageJson? package,
        string packageDir,
        string requestedPath)
    {
        if (package?.TypesVersions is not { ValueKind: JsonValueKind.Object } versions)
            return null;

        foreach (var rangeProperty in versions.EnumerateObject())
        {
            if (!MatchesCompilerVersionRange(rangeProperty.Name)
                || rangeProperty.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string request = requestedPath
                .Replace('\\', '/')
                .TrimStart('.', '/');
            foreach (var mapping in rangeProperty.Value.EnumerateObject())
            {
                if (!TryMatchTypesVersionPattern(
                        mapping.Name, request, out string wildcard))
                {
                    continue;
                }

                if (mapping.Value.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var target in mapping.Value.EnumerateArray())
                {
                    if (target.ValueKind != JsonValueKind.String)
                        continue;
                    string mapped = target.GetString()!.Replace(
                        "*", wildcard, StringComparison.Ordinal);
                    string? resolved = TryAddExtension(
                        Path.GetFullPath(Path.Combine(
                            packageDir, mapped.Replace('/', Path.DirectorySeparatorChar))));
                    if (resolved is not null)
                        return resolved;
                }
            }

            // TypeScript selects the first matching version range.
            return null;
        }

        return null;
    }

    private static bool TryMatchTypesVersionPattern(
        string pattern,
        string request,
        out string wildcard)
    {
        int star = pattern.IndexOf('*');
        if (star < 0)
        {
            wildcard = "";
            return string.Equals(pattern, request, StringComparison.Ordinal);
        }

        string prefix = pattern[..star];
        string suffix = pattern[(star + 1)..];
        if (!request.StartsWith(prefix, StringComparison.Ordinal)
            || !request.EndsWith(suffix, StringComparison.Ordinal)
            || request.Length < prefix.Length + suffix.Length)
        {
            wildcard = "";
            return false;
        }

        wildcard = request[prefix.Length..(request.Length - suffix.Length)];
        return true;
    }

    private static bool MatchesCompilerVersionRange(string range)
    {
        foreach (string alternative in range.Split(
                     "||", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            bool matches = true;
            foreach (string clause in alternative.Split(
                         ' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                string op = clause.StartsWith("<=", StringComparison.Ordinal) ? "<="
                    : clause.StartsWith(">=", StringComparison.Ordinal) ? ">="
                    : clause.StartsWith('<') ? "<"
                    : clause.StartsWith('>') ? ">"
                    : clause.StartsWith('=') ? "="
                    : "";
                string versionText = clause[op.Length..];
                if (!Version.TryParse(versionText, out Version? version))
                {
                    matches = false;
                    break;
                }

                int comparison = TypeScriptLibProvider.CompilerVersion.CompareTo(version);
                matches &= op switch
                {
                    "<=" => comparison <= 0,
                    ">=" => comparison >= 0,
                    "<" => comparison < 0,
                    ">" => comparison > 0,
                    _ => comparison == 0,
                };
                if (!matches)
                    break;
            }
            if (matches)
                return true;
        }
        return false;
    }

    private IEnumerable<string> GetEffectiveTypeRoots(string containingFile)
    {
        if (_programOptions.TypeRoots is not null)
            return _programOptions.TypeRoots.Select(Path.GetFullPath);
        if (_resolutionOptions.TypeRoots is { Count: > 0 })
            return _resolutionOptions.TypeRoots.Select(Path.GetFullPath);

        var roots = new List<string>();
        string? directory = Path.GetDirectoryName(Path.GetFullPath(containingFile));
        while (directory is not null)
        {
            roots.Add(Path.Combine(directory, "node_modules", "@types"));
            directory = FileDiscovery.AmbientParent(directory);
        }
        return roots;
    }

    private ParsedModule LoadTypeScriptLibModule(string virtualPath, DecoratorMode decoratorMode)
    {
        virtualPath = TypeScriptLibProvider.GetVirtualPath(virtualPath);
        if (_moduleCache.TryGetValue(virtualPath, out var cached))
            return cached;

        if (!TypeScriptLibProvider.TryGetParsed(virtualPath, out var parsedLibrary)
            || parsedLibrary is null)
        {
            string available = string.Join(", ", TypeScriptLibProvider.AvailableLibraries
                .Select(TypeScriptLibProvider.GetDisplayName));
            throw new Exception(
                $"Type Error: Cannot resolve library '{virtualPath[TypeScriptLibProvider.VirtualPathPrefix.Length..]}'. " +
                $"Available libraries: {available}");
        }

        var module = new ParsedModule(virtualPath, parsedLibrary.Statements)
        {
            IsScript = true,
            IsDeclarationFile = true,
            IsDefaultLibrary = true,
            NoDefaultLib = true,
        };
        _moduleCache[virtualPath] = module;

        foreach (var directive in parsedLibrary.Directives.Where(
                     d => d.Type == TripleSlashReferenceType.Lib))
        {
            var dependency = LoadTypeScriptLibModule(
                TypeScriptLibProvider.GetVirtualPath(directive.Value), decoratorMode);
            if (!module.ReferencedScripts.Contains(dependency))
                module.ReferencedScripts.Add(dependency);
        }

        return module;
    }

    private static bool IsDeclarationFilePath(string path) =>
        path.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".d.mts", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".d.cts", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Clears all cached modules.
    /// </summary>
    public void ClearCache()
    {
        _moduleCache.Clear();
        _ambientModulePaths.Clear();
    }

    /// <summary>
    /// Loads modules discovered through dynamic import expressions.
    /// These modules may not be in the static dependency graph but should be
    /// compiled to support runtime dynamic imports.
    /// </summary>
    /// <param name="paths">Relative module paths from dynamic import string literals</param>
    /// <param name="basePath">Base path for resolving relative paths (typically entry module path)</param>
    /// <param name="decoratorMode">Decorator mode to use for parsing</param>
    /// <returns>List of newly loaded modules (not previously cached)</returns>
    public List<ParsedModule> LoadDynamicImportModules(
        IEnumerable<string> paths,
        string basePath,
        DecoratorMode decoratorMode = DecoratorMode.None)
    {
        List<ParsedModule> newModules = [];

        foreach (var path in paths)
        {
            try
            {
                string resolvedPath = ResolveRuntimeModulePath(path, basePath);

                // Skip if already loaded
                if (_moduleCache.ContainsKey(resolvedPath))
                {
                    continue;
                }

                HashSet<string> previouslyLoaded = _moduleCache.Keys.ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

                // Load the module (this will also load its dependencies).
                var module = LoadModule(resolvedPath, decoratorMode);
                foreach (ParsedModule loaded in GetRuntimeModulesInOrder(module))
                {
                    if (previouslyLoaded.Contains(loaded.Path))
                        continue;
                    loaded.IsDynamicImportOnly = true;
                    newModules.Add(loaded);
                }
            }
            catch
            {
                // Dynamic imports may reference modules that don't exist yet
                // or are optional - don't fail the compilation
                // The runtime will handle missing modules with rejected promises
            }
        }

        return newModules;
    }
}
