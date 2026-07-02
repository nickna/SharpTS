using System.Collections.Frozen;
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
    private readonly string _basePath;
    private readonly Dictionary<string, ParsedModule> _moduleCache = [];
    private readonly HashSet<string> _loadingModules = [];  // For circular detection
    private readonly HashSet<string> _loadingScriptRefs = [];  // For circular script reference detection
    private readonly Dictionary<string, ModulePackageJson?> _packageJsonCache = [];
    private readonly StdlibProviderChain _stdlibChain;
    /// <summary>
    /// Optional in-memory virtual file system. When non-null, all file existence checks and
    /// reads consult this map instead of touching the disk. Tests use this to bypass the
    /// kernel-serialized Windows file system (Defender real-time scan in particular makes
    /// concurrent <c>File.WriteAllText</c>/<c>File.ReadAllText</c> calls largely sequential —
    /// measured 1.4× speedup at 12 threads vs ideal 12×). Keys are normalized via
    /// <see cref="NormalizePath"/> (full path + OS-appropriate casing).
    /// </summary>
    private readonly Dictionary<string, string>? _virtualFiles;

    /// <summary>
    /// Creates a new module resolver rooted at the given path.
    /// </summary>
    /// <param name="basePath">Entry point file path or base directory</param>
    public ModuleResolver(string basePath) : this(basePath, virtualFiles: null) { }

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
    {
        _basePath = Path.GetDirectoryName(Path.GetFullPath(basePath)) ?? ".";
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
        return _virtualFiles.ContainsKey(NormalizePath(path));
    }

    private bool ResolverDirectoryExists(string path)
    {
        if (_virtualFiles is null) return Directory.Exists(path);
        var canonical = NormalizePath(path);
        var prefix = canonical + Path.DirectorySeparatorChar;
        foreach (var k in _virtualFiles.Keys)
            if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
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
    {
        string currentDir = Path.GetDirectoryName(currentModulePath) ?? _basePath;

        // dotnet: scheme — .NET interop imports resolve via reflection, not the file system.
        // The specifier itself is the virtual module path (and cache key).
        if (DotNetImports.IsDotNetSpecifier(specifier))
        {
            if (kind == ResolutionKind.Cjs)
            {
                throw new Exception(
                    $"Module Error: '{specifier}' is not available via require(). " +
                    "Use a named ESM import instead: import { TypeName } from \"" + specifier + "\".");
            }
            return specifier;
        }

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
        else if (specifier.StartsWith('#'))
        {
            // Subpath imports (#-prefixed) — resolve through nearest package.json "imports" field
            string? result = TryResolveSubpathImport(specifier, currentDir, kind);
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

            // Try self-referencing: if nearest package.json has "name" matching the specifier
            string? selfRef = TryResolveSelfReference(specifier, currentDir, kind);
            if (selfRef != null)
                return selfRef;

            // Bare specifier (e.g., 'lodash')
            // Look in node_modules directories
            string? resolvedPath = TryResolveNodeModule(specifier, currentDir, kind);
            if (resolvedPath != null)
            {
                return resolvedPath;
            }
            throw new Exception($"Module Error: Cannot resolve bare specifier '{specifier}'. " +
                                "Bare imports require a node_modules directory with the package installed.");
        }
    }

    /// <summary>
    /// Tries to resolve a bare specifier by looking in node_modules directories.
    /// Supports package.json "exports" field, "main"/"types" fallback, and legacy index.ts.
    /// </summary>
    private string? TryResolveNodeModule(string specifier, string startDir, ResolutionKind kind)
    {
        var (packageName, subpath) = ParsePackageSpecifier(specifier);
        string? currentDir = startDir;

        while (currentDir != null)
        {
            string packageDir = Path.Combine(currentDir, "node_modules", packageName);

            if (ResolverDirectoryExists(packageDir))
            {
                var result = TryResolveInPackageDir(packageDir, subpath, kind);
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

            // Move up one directory
            currentDir = Path.GetDirectoryName(currentDir);
        }

        return null;
    }

    /// <summary>
    /// Attempts to resolve a subpath within a specific package directory.
    /// </summary>
    private string? TryResolveInPackageDir(string packageDir, string subpath, ResolutionKind kind)
    {
        string packageJsonPath = Path.Combine(packageDir, "package.json");
        var pkg = LoadPackageJson(packageJsonPath);

        if (pkg?.Exports != null)
        {
            // Use exports field
            var resolved = ExportsResolver.ResolvePackageExports(
                pkg.Exports.Value, subpath, ConditionsFor(kind));
            if (resolved != null)
                return ResolveExportsPath(resolved, packageDir);
            // Exports field exists but no match — per spec, this blocks resolution
            return null;
        }

        if (pkg != null && subpath == ".")
        {
            // No exports field — try types/typings, then main, then module
            string? entryPath = pkg.Types ?? pkg.Typings ?? pkg.Main ?? pkg.Module;
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

        // If path exists as-is, use it
        if (ResolverFileExists(fullPath))
            return fullPath;

        // Extension mapping: .js → .ts, .tsx
        if (fullPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        {
            string tsPath = fullPath[..^3] + ".ts";
            if (ResolverFileExists(tsPath)) return tsPath;
            string tsxPath = fullPath[..^3] + ".tsx";
            if (ResolverFileExists(tsxPath)) return tsxPath;
        }
        else if (fullPath.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase))
        {
            string mtsPath = fullPath[..^4] + ".mts";
            if (ResolverFileExists(mtsPath)) return mtsPath;
        }
        else if (fullPath.EndsWith(".cjs", StringComparison.OrdinalIgnoreCase))
        {
            string ctsPath = fullPath[..^4] + ".cts";
            if (ResolverFileExists(ctsPath)) return ctsPath;
        }

        // Try appending each known extension
        foreach (var ext in SourceExtensions)
        {
            string candidate = fullPath + ext;
            if (ResolverFileExists(candidate)) return candidate;
        }

        // Try as directory with index.* file
        if (ResolverDirectoryExists(fullPath))
        {
            foreach (var ext in SourceExtensions)
            {
                string indexPath = Path.Combine(fullPath, "index" + ext);
                if (ResolverFileExists(indexPath)) return indexPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Tries to add a file extension to a path, returning null if nothing resolves.
    /// </summary>
    private string? TryAddExtension(string path)
    {
        if (ResolverFileExists(path))
            return path;

        foreach (var ext in SourceExtensions)
        {
            string candidate = path + ext;
            if (ResolverFileExists(candidate)) return candidate;
        }

        // .js → .ts (TS-source-for-JS-spec)
        if (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        {
            string tsPath = path[..^3] + ".ts";
            if (ResolverFileExists(tsPath)) return tsPath;
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
    private string? TryResolveSubpathImport(string specifier, string startDir, ResolutionKind kind)
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
                        pkg.Imports.Value, specifier, ConditionsFor(kind));
                    if (resolved != null)
                        return ResolveExportsPath(resolved, dir);
                }
                // Found a package.json but no matching import — stop walking
                return null;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    /// <summary>
    /// Resolves self-referencing imports (when a package imports itself by name through its own exports).
    /// </summary>
    private string? TryResolveSelfReference(string specifier, string startDir, ResolutionKind kind)
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
                    pkg.Exports.Value, subpath, ConditionsFor(kind));
                if (resolved != null)
                    return ResolveExportsPath(resolved, dir);
                return null;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static string[] ConditionsFor(ResolutionKind kind) => kind switch
    {
        ResolutionKind.Cjs => ExportsResolver.CjsConditions,
        _ => ExportsResolver.EsmConditions,
    };

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

    private static readonly string[] SourceExtensions = [".ts", ".tsx", ".cts", ".mts", ".js", ".jsx", ".cjs", ".mjs"];

    private string AddExtensionIfNeeded(string path)
    {
        // If path already has a known extension and exists, use it
        if (HasKnownSourceExtension(path) && ResolverFileExists(path))
        {
            return path;
        }

        // Try each known extension
        foreach (var ext in SourceExtensions)
        {
            string candidate = path + ext;
            if (ResolverFileExists(candidate))
                return candidate;
        }

        // Try path as-is as a directory with index.* file
        if (ResolverDirectoryExists(path))
        {
            foreach (var ext in SourceExtensions)
            {
                string indexPath = Path.Combine(path, "index" + ext);
                if (ResolverFileExists(indexPath))
                    return indexPath;
            }
        }

        // If original path exists (e.g. an unusual extension), use it
        if (ResolverFileExists(path))
        {
            return path;
        }

        throw new Exception($"Module Error: Cannot resolve module '{path}'. File not found.");
    }

    private static bool HasKnownSourceExtension(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return false;
        foreach (var known in SourceExtensions)
        {
            if (string.Equals(ext, known, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
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

            var lexer = new Lexer(source);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens, decoratorMode);
            var parseResult = parser.Parse();

            // For module loading, we throw on parse errors (backward compatible)
            if (!parseResult.IsSuccess)
            {
                throw new Exception(parseResult.Diagnostics.First().ToString());
            }

            var statements = parseResult.Statements;
            var module = new ParsedModule(absolutePath, statements);

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

            // Process triple-slash path references (only valid for scripts)
            // NOTE: Process BEFORE caching to properly detect circular references
            var directives = lexer.TripleSlashDirectives;
            var pathRefs = directives.Where(d => d.Type == TripleSlashReferenceType.Path).ToList();

            if (pathRefs.Count > 0)
            {
                if (!module.IsScript)
                {
                    throw new Exception($"Type Error: /// <reference path=\"...\"> is only valid in script files (files without import/export). File '{absolutePath}' is a module.");
                }

                // Load referenced scripts
                foreach (var pathRef in pathRefs)
                {
                    string refPath = ResolveReferencePath(pathRef.Value, absolutePath);
                    var refModule = LoadScriptReference(refPath, decoratorMode, absolutePath);

                    if (!refModule.IsScript)
                    {
                        throw new Exception($"Type Error: /// <reference path=\"{pathRef.Value}\"> cannot reference a module file. Referenced file '{refPath}' contains import/export statements.");
                    }

                    module.PathReferences.Add(pathRef);
                    module.ReferencedScripts.Add(refModule);
                }
            }

            // Cache AFTER processing path references to properly detect circular references
            _moduleCache[absolutePath] = module;

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
                    string importedPath = ResolveModulePath(import.ModulePath, absolutePath);
                    var importedModule = LoadModule(importedPath, decoratorMode);
                    // dotnet: modules resolve their export surface from the importing
                    // statements — each named import is resolved (and validated) here.
                    if (importedModule.IsDotNetModule)
                    {
                        DotNetImports.EnsureImports(importedModule, import);
                    }
                    // Files loaded via import are always modules, even if they have no exports
                    // (e.g., side-effect imports like `import './polyfill'`)
                    importedModule.IsScript = false;
                    if (!module.Dependencies.Contains(importedModule))
                    {
                        module.Dependencies.Add(importedModule);
                    }
                }
                else if (stmt is Stmt.Export export && export.FromModulePath != null)
                {
                    // Re-export: export { x } from './foo' or export * from './foo'
                    string reexportPath = ResolveModulePath(export.FromModulePath, absolutePath);
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
                }
                else if (stmt is Stmt.ImportRequire importReq)
                {
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
                }
            }

            return module;
        }
        finally
        {
            _loadingModules.Remove(absolutePath);
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

        var specifier = EmbeddedStdlibProvider.TryExtractSpecifier(virtualPath);
        if (specifier is null)
            throw new Exception($"Module Error: Malformed stdlib virtual path '{virtualPath}'.");

        if (!_stdlibChain.TryResolve(specifier, out var stdlibModule) || stdlibModule is null)
            throw new Exception($"Module Error: No stdlib provider resolved '{specifier}'.");

        if (stdlibModule.Source is not TypeScriptSource tsSource)
            throw new Exception($"Module Error: Stdlib module '{specifier}' is not TypeScript source.");

        _loadingModules.Add(virtualPath);
        try
        {
            var lexer = new Lexer(tsSource.Text);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens, decoratorMode);
            var parseResult = parser.Parse();
            if (!parseResult.IsSuccess)
                throw new Exception(parseResult.Diagnostics.First().ToString());

            var module = new ParsedModule(virtualPath, parseResult.Statements)
            {
                IsScript = false,
                IsCommonJs = false,
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
    /// Walks a CommonJS module's body for literal `require('./literal')` calls and recursively
    /// loads each target. Adds resolved targets to <see cref="ParsedModule.Dependencies"/>.
    /// Non-literal specifiers are ignored here — the IL compiler will reject them later.
    /// Unresolvable specifiers are also ignored — they'll either resolve via the optional-dep
    /// runtime throw path or surface as a compile error from the IL emitter.
    /// </summary>
    private void CollectCjsRequireDependencies(
        ParsedModule module,
        List<Stmt> statements,
        string absolutePath,
        DecoratorMode decoratorMode)
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
                // Literal require() in a CJS body — pass Cjs so dual-export packages
                // route to the "require" entry, not "import" (matches Node semantics).
                requiredPath = ResolveModulePath(specifier, absolutePath, ResolutionKind.Cjs);
            }
            catch
            {
                // Optional dep / will be handled at runtime by the optional-dep throw path.
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

        Visit(entryPoint);
        return result;
    }

    /// <summary>
    /// Gets a cached module by its absolute path.
    /// </summary>
    public ParsedModule? GetCachedModule(string absolutePath)
    {
        // Don't normalize virtual paths (builtin: sentinels, stdlib: TS sources, dotnet:
        // interop modules, primitive: C# interop modules — none resolve to a real filesystem path).
        if (!absolutePath.StartsWith(BuiltInModuleRegistry.BuiltInPrefix)
            && !absolutePath.StartsWith(EmbeddedStdlibProvider.VirtualPathPrefix, StringComparison.Ordinal)
            && !DotNetImports.IsDotNetSpecifier(absolutePath)
            && !absolutePath.StartsWith(PrimitiveRegistry.Prefix, StringComparison.Ordinal))
        {
            absolutePath = Path.GetFullPath(absolutePath);
        }
        return _moduleCache.GetValueOrDefault(absolutePath);
    }

    /// <summary>
    /// Clears all cached modules.
    /// </summary>
    public void ClearCache()
    {
        _moduleCache.Clear();
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
                string resolvedPath = ResolveModulePath(path, basePath);

                // Skip if already loaded
                if (_moduleCache.ContainsKey(resolvedPath))
                {
                    continue;
                }

                // Load the module (this will also load its dependencies)
                var module = LoadModule(resolvedPath, decoratorMode);
                newModules.Add(module);
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
