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

internal sealed record CheckedNavigationWorkspace(
    IReadOnlyList<CheckedNavigationModel> Models,
    IReadOnlyList<string> ConfigPaths,
    bool IsComplete);

/// <summary>
/// Builds the semantic module graph shared by definition, references, and later rename support.
/// </summary>
internal static class NavigationModelBuilder
{
    public static CheckedNavigationModel? TryBuild(
        string path,
        string text,
        IReadOnlyDictionary<string, string>? openDocuments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string absolutePath = Path.GetFullPath(path);
        Dictionary<string, string> overlay = CreateOverlay(
            absolutePath,
            text,
            openDocuments);
        string directory = Path.GetDirectoryName(absolutePath)
            ?? Directory.GetCurrentDirectory();
        string? configPath = TsConfigLoader.Discover(directory);
        if (configPath is not null)
        {
            try
            {
                ProjectBuildResult configured = TryBuildProject(
                    absolutePath,
                    overlay,
                    NavigationWorkspace.FromProject(TsConfigLoader.Load(configPath)),
                    requireMembership: true,
                    cancellationToken);
                if (configured.Model is not null)
                    return configured.Model;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Fall through to an explicitly incomplete open-document model.
            }
        }

        return TryBuildProject(
            absolutePath,
            overlay,
            NavigationWorkspace.Unconfigured(absolutePath, configPath),
            requireMembership: false,
            cancellationToken).Model;
    }

    public static CheckedNavigationWorkspace BuildWorkspace(
        string path,
        string text,
        IReadOnlyDictionary<string, string>? openDocuments,
        IReadOnlyList<string> workspaceRoots,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string absolutePath = Path.GetFullPath(path);
        Dictionary<string, string> overlay = CreateOverlay(
            absolutePath,
            text,
            openDocuments);
        NavigationProjectCatalog catalog =
            NavigationProjectCatalog.Discover(workspaceRoots);
        var models = new List<CheckedNavigationModel>();
        bool isComplete = catalog.IsComplete;

        foreach (TsConfigResult project in catalog.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProjectBuildResult result = TryBuildProject(
                absolutePath,
                overlay,
                NavigationWorkspace.FromProject(project),
                requireMembership: true,
                cancellationToken);
            isComplete &= result.IsComplete;
            if (result.Model is not null)
                models.Add(result.Model);
        }

        return new CheckedNavigationWorkspace(
            models,
            catalog.ConfigPaths,
            isComplete && models.Count > 0);
    }

    private static ProjectBuildResult TryBuildProject(
        string absolutePath,
        IReadOnlyDictionary<string, string> overlay,
        NavigationWorkspace workspace,
        bool requireMembership,
        CancellationToken cancellationToken)
    {
        try
        {
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
                cancellationToken.ThrowIfCancellationRequested();
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

            List<ParsedModule> loadedRoots = [];
            foreach (string rootPath in workspace.RootFiles
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Order(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                    allRootsLoaded = false;
                }
            }

            ParsedModule? entry = resolver.GetCachedModule(absolutePath);
            bool isMember = entry is not null;
            if (entry is null && requireMembership)
                return new ProjectBuildResult(null, allRootsLoaded);

            entry ??= resolver.LoadProgram(absolutePath, workspace.DecoratorMode);
            if (entry.Document is not { } document)
                return new ProjectBuildResult(null, IsComplete: false);
            if (!loadedRoots.Contains(entry))
                loadedRoots.Add(entry);

            // Dirty/new open buffers can be reverse importers even before the disk-backed
            // tsconfig matcher sees them.
            foreach (string openPath in overlay.Keys
                         .Order(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.Equals(
                        openPath,
                        absolutePath,
                        StringComparison.OrdinalIgnoreCase) ||
                    workspace.RootFiles.Contains(
                        openPath,
                        StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    ParsedModule root = resolver.LoadProgram(
                        openPath,
                        workspace.DecoratorMode);
                    if (!loadedRoots.Contains(root))
                        loadedRoots.Add(root);
                }
                catch
                {
                    // Configured roots, not unrelated dirty buffers, define completeness.
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
                .WithFilePath(absolutePath)
                .WithCancellation(cancellationToken);
            checker.SetDecoratorMode(workspace.DecoratorMode);
            checker.CheckModules(
                resolver.GetModulesInOrder(connectedRoots),
                resolver);
            var model = new CheckedNavigationModel(
                checker,
                document,
                new NavigationGraphScope(
                    workspace.ConfigPath,
                    workspace.RootFiles,
                    workspace.IsConfigured && isMember && allRootsLoaded));
            return new ProjectBuildResult(
                model,
                workspace.IsConfigured && allRootsLoaded);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new ProjectBuildResult(null, IsComplete: false);
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

    private static Dictionary<string, string> CreateOverlay(
        string absolutePath,
        string text,
        IReadOnlyDictionary<string, string>? openDocuments)
    {
        var overlay = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        if (openDocuments is not null)
        {
            foreach (var (documentPath, documentText) in openDocuments)
                overlay[Path.GetFullPath(documentPath)] = documentText;
        }
        overlay[absolutePath] = text;
        return overlay;
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
        public static NavigationWorkspace FromProject(TsConfigResult project)
        {
            DecoratorMode decoratorMode =
                project.DecoratorMode ?? DecoratorMode.Stage3;
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

    private sealed record ProjectBuildResult(
        CheckedNavigationModel? Model,
        bool IsComplete);
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
