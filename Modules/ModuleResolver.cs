using System.Collections.Frozen;
using SharpTS.Parsing;
using SharpTS.Runtime.BuiltIns.Modules;
using SharpTS.TypeSystem;

namespace SharpTS.Modules;

/// <summary>
/// Resolves module paths and manages module loading with circular dependency detection.
/// </summary>
/// <remarks>
/// Handles relative paths (./foo, ../bar), bare specifiers (lodash), and .ts extension
/// inference. Detects circular dependencies during loading and provides modules in
/// dependency order for type checking and execution.
/// </remarks>
public class ModuleResolver
{
    private readonly string _basePath;
    private readonly Dictionary<string, ParsedModule> _moduleCache = [];
    private readonly HashSet<string> _loadingModules = [];  // For circular detection

    /// <summary>
    /// Creates a new module resolver rooted at the given path.
    /// </summary>
    /// <param name="basePath">Entry point file path or base directory</param>
    public ModuleResolver(string basePath)
    {
        _basePath = Path.GetDirectoryName(Path.GetFullPath(basePath)) ?? ".";
    }

    /// <summary>
    /// Resolves a module specifier to an absolute file path.
    /// </summary>
    /// <param name="specifier">The import specifier (e.g., './foo', '../bar', 'lodash')</param>
    /// <param name="currentModulePath">The path of the module containing the import</param>
    /// <returns>Absolute path to the resolved module</returns>
    /// <exception cref="Exception">If the module cannot be found</exception>
    public string ResolveModulePath(string specifier, string currentModulePath)
    {
        string currentDir = Path.GetDirectoryName(currentModulePath) ?? _basePath;

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
        else
        {
            // Check for built-in modules first (fs, path, os, etc.)
            if (BuiltInModuleRegistry.IsBuiltIn(specifier))
            {
                return BuiltInModuleRegistry.GetBuiltInPath(specifier);
            }

            // Bare specifier (e.g., 'lodash')
            // Look in node_modules directories
            string? resolvedPath = TryResolveNodeModule(specifier, currentDir);
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
    /// </summary>
    private string? TryResolveNodeModule(string specifier, string startDir)
    {
        string? currentDir = startDir;

        while (currentDir != null)
        {
            string nodeModulesPath = Path.Combine(currentDir, "node_modules", specifier);

            // Try index.ts
            string indexPath = Path.Combine(nodeModulesPath, "index.ts");
            if (File.Exists(indexPath))
            {
                return indexPath;
            }

            // Try package.json main field (simplified - just check for index)
            string directPath = nodeModulesPath + ".ts";
            if (File.Exists(directPath))
            {
                return directPath;
            }

            // Move up one directory
            currentDir = Path.GetDirectoryName(currentDir);
        }

        return null;
    }

    private string AddExtensionIfNeeded(string path)
    {
        // If path already has .ts extension and exists, use it
        if (path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            return path;
        }

        // Try adding .ts extension
        string withTs = path + ".ts";
        if (File.Exists(withTs))
        {
            return withTs;
        }

        // Try path as-is (might be a directory with index.ts)
        string indexPath = Path.Combine(path, "index.ts");
        if (Directory.Exists(path) && File.Exists(indexPath))
        {
            return indexPath;
        }

        // If original path exists (maybe .js or no extension), use it
        if (File.Exists(path))
        {
            return path;
        }

        throw new Exception($"Module Error: Cannot resolve module '{path}'. File not found.");
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
            string source = File.ReadAllText(absolutePath);

            var lexer = new Lexer(source);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens, decoratorMode);
            var statements = parser.Parse();

            var module = new ParsedModule(absolutePath, statements);
            _moduleCache[absolutePath] = module;

            // Recursively load imported modules
            foreach (var stmt in statements)
            {
                if (stmt is Stmt.Import import)
                {
                    string importedPath = ResolveModulePath(import.ModulePath, absolutePath);
                    var importedModule = LoadModule(importedPath, decoratorMode);
                    if (!module.Dependencies.Contains(importedModule))
                    {
                        module.Dependencies.Add(importedModule);
                    }
                }
                else if (stmt is Stmt.Export export && export.FromModulePath != null)
                {
                    // Re-export: export { x } from './foo' or export * from './foo'
                    string reexportPath = ResolveModulePath(export.FromModulePath, absolutePath);
                    var reexportedModule = LoadModule(reexportPath, decoratorMode);
                    if (!module.Dependencies.Contains(reexportedModule))
                    {
                        module.Dependencies.Add(reexportedModule);
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
    /// Returns all loaded modules in dependency order (topological sort).
    /// Dependencies come before the modules that depend on them.
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

            // Visit dependencies first
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
        // Don't normalize built-in module paths
        if (!absolutePath.StartsWith(BuiltInModuleRegistry.BuiltInPrefix))
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
