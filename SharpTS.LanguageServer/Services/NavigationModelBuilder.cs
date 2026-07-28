using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpTS.Configuration;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SharpTS.LanguageServer.Services;

internal sealed record CheckedNavigationModel(
    TypeChecker Checker,
    SourceDocument Document,
    NavigationGraphScope Scope);

/// <summary>
/// Describes the bounded root set used to discover reverse navigation edges.
/// </summary>
internal sealed record NavigationGraphScope(
    string? ConfigPath,
    IReadOnlyList<string> RootFiles,
    bool IsComplete);

/// <summary>
/// Builds the semantic module graph shared by definition, references, and later rename support.
/// </summary>
internal static class NavigationModelBuilder
{
    public static CheckedNavigationModel? TryBuild(
        string path,
        string text,
        IReadOnlyDictionary<string, string>? openDocuments)
    {
        try
        {
            string absolutePath = Path.GetFullPath(path);
            var overlay = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (openDocuments is not null)
            {
                foreach (var (documentPath, documentText) in openDocuments)
                    overlay[Path.GetFullPath(documentPath)] = documentText;
            }
            overlay[absolutePath] = text;

            NavigationWorkspace workspace = DiscoverWorkspace(absolutePath);
            var resolver = new ModuleResolver(
                workspace.BasePath,
                workspace.ResolutionOptions,
                overlay,
                workspace.ProgramOptions,
                virtualFilesFallBackToDisk: true)
            {
                JsxOptions = workspace.JsxOptions,
            };

            bool allRootsLoaded = true;
            List<ParsedModule> declarationRoots = [];
            foreach (string declarationPath in workspace.DeclarationFiles)
            {
                try
                {
                    declarationRoots.Add(
                        resolver.LoadModule(declarationPath, workspace.DecoratorMode));
                }
                catch
                {
                    allRootsLoaded = false;
                }
            }
            resolver.RegisterAmbientModuleDeclarations(declarationRoots);

            ParsedModule entry = resolver.LoadProgram(
                absolutePath,
                workspace.DecoratorMode);
            if (entry.Document is not { } document)
                return null;

            // Every configured root is a possible reverse importer. Open roots are retained as
            // the fallback for files without a tsconfig and include dirty/new buffers that the
            // disk-backed config matcher cannot yet enumerate.
            List<ParsedModule> loadedRoots = [entry];
            var rootPaths = workspace.RootFiles
                .Concat(overlay.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase);
            foreach (string rootPath in rootPaths)
            {
                if (string.Equals(rootPath, absolutePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    ParsedModule root = resolver.LoadProgram(
                        rootPath,
                        workspace.DecoratorMode);
                    if (!loadedRoots.Contains(root))
                        loadedRoots.Add(root);
                }
                catch
                {
                    if (workspace.RootFiles.Contains(
                            rootPath,
                            StringComparer.OrdinalIgnoreCase))
                    {
                        allRootsLoaded = false;
                    }
                }
            }

            List<ParsedModule> loadedModules = resolver.GetModulesInOrder(
                declarationRoots.Concat(loadedRoots));
            HashSet<ParsedModule> component = FindConnectedComponent(
                entry,
                loadedModules);
            List<ParsedModule> connectedRoots = loadedRoots
                .Where(component.Contains)
                .ToList();

            var checker = new TypeChecker(workspace.CheckerOptions)
                .WithFilePath(absolutePath);
            checker.SetDecoratorMode(workspace.DecoratorMode);
            checker.CheckModules(
                resolver.GetModulesInOrder(connectedRoots),
                resolver);
            return new CheckedNavigationModel(
                checker,
                document,
                new NavigationGraphScope(
                    workspace.ConfigPath,
                    workspace.RootFiles,
                    workspace.IsConfigured && allRootsLoaded));
        }
        catch
        {
            return null;
        }
    }

    private static HashSet<ParsedModule> FindConnectedComponent(
        ParsedModule entry,
        IReadOnlyList<ParsedModule> modules)
    {
        var reverseEdges = new Dictionary<ParsedModule, List<ParsedModule>>();
        foreach (var module in modules)
        {
            foreach (var dependency in module.Dependencies.Concat(module.ReferencedScripts))
            {
                if (!reverseEdges.TryGetValue(dependency, out var importers))
                {
                    importers = [];
                    reverseEdges[dependency] = importers;
                }
                importers.Add(module);
            }
        }

        HashSet<ParsedModule> component = [entry];
        Queue<ParsedModule> pending = new([entry]);

        while (pending.TryDequeue(out var current))
        {
            foreach (var adjacent in current.Dependencies.Concat(current.ReferencedScripts))
            {
                if (component.Add(adjacent))
                    pending.Enqueue(adjacent);
            }

            if (!reverseEdges.TryGetValue(current, out var importers))
                continue;

            foreach (var importer in importers)
            {
                if (component.Add(importer))
                    pending.Enqueue(importer);
            }
        }

        return component;
    }

    private static NavigationWorkspace DiscoverWorkspace(string absolutePath)
    {
        string directory = Path.GetDirectoryName(absolutePath)
            ?? Directory.GetCurrentDirectory();
        string? configPath = TsConfigLoader.Discover(directory);
        if (configPath is null)
            return NavigationWorkspace.Unconfigured(absolutePath);

        try
        {
            TsConfigResult project = TsConfigLoader.Load(configPath);
            DecoratorMode decoratorMode = project.DecoratorMode ?? DecoratorMode.Stage3;
            return new NavigationWorkspace(
                BasePath: project.ConfigPath,
                ResolutionOptions: project.ModuleResolution,
                ProgramOptions: new TypeScriptProgramOptions
                {
                    LoadDefaultLib = true,
                    NoLib = project.NoLib == true,
                    Lib = project.Lib,
                    Types = project.Types,
                    TypeRoots = project.TypeRoots,
                    PreferDeclarationFiles = true,
                },
                CheckerOptions: StrictnessOptions.Resolve(null, project.Strictness),
                DecoratorMode: decoratorMode,
                JsxOptions: new JsxParseOptions(
                    project.Jsx ?? JsxMode.ReactJsx,
                    project.JsxFactory ?? "React.createElement",
                    project.JsxFragmentFactory ?? "React.Fragment",
                    project.JsxImportSource ?? "react"),
                RootFiles: project.RootFiles,
                DeclarationFiles: project.DeclarationFiles,
                ConfigPath: project.ConfigPath,
                IsConfigured: true);
        }
        catch
        {
            // A broken config must not erase navigation in the requested/open files. The
            // resulting scope is explicitly incomplete, so safe rename cannot use it.
            return NavigationWorkspace.Unconfigured(absolutePath, configPath);
        }
    }

    private sealed record NavigationWorkspace(
        string BasePath,
        ModuleResolutionOptions ResolutionOptions,
        TypeScriptProgramOptions ProgramOptions,
        TypeCheckerOptions CheckerOptions,
        DecoratorMode DecoratorMode,
        JsxParseOptions JsxOptions,
        IReadOnlyList<string> RootFiles,
        IReadOnlyList<string> DeclarationFiles,
        string? ConfigPath,
        bool IsConfigured)
    {
        public static NavigationWorkspace Unconfigured(
            string absolutePath,
            string? configPath = null) =>
            new(
                BasePath: absolutePath,
                ResolutionOptions: ModuleResolutionOptions.Default,
                ProgramOptions: new TypeScriptProgramOptions
                {
                    PreferDeclarationFiles = true,
                },
                CheckerOptions: TypeCheckerOptions.Default,
                DecoratorMode: DecoratorMode.Stage3,
                JsxOptions: JsxParseOptions.Default,
                RootFiles: [],
                DeclarationFiles: [],
                ConfigPath: configPath,
                IsConfigured: false);
    }
}

internal static class NavigationLocations
{
    public static Location From(SourceDocument document, Token token)
    {
        var (startLine, startColumn) = document.Lines.ToPosition(token.Start);
        var (endLine, endColumn) = document.Lines.ToPosition(token.End);
        return new Location
        {
            Uri = DocumentUri.FromFileSystemPath(document.Path),
            Range = new Range(
                startLine - 1,
                startColumn - 1,
                endLine - 1,
                endColumn - 1),
        };
    }
}
