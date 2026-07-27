using SharpTS.Cli;
using SharpTS.Configuration;
using SharpTS.Declaration;
using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.References;
using SharpTS.TypeSystem;

namespace SharpTS.Projects;

/// <summary>Runs project checks, build graphs, incremental checks, and watch sessions.</summary>
public static class ProjectCommandRunner
{
    public static int Run(
        IReadOnlyList<string> rootConfigPaths,
        GlobalOptions cliOptions,
        bool buildMode,
        bool force = false)
    {
        try
        {
            var graph = ProjectGraph.Load(rootConfigPaths);
            foreach (var project in graph.Projects)
            {
                string optionsKey = BuildOptionsKey(cliOptions);
                bool useIncremental = buildMode || cliOptions.Incremental ||
                    project.Incremental == true || project.Composite == true;
                if (useIncremental && !force && ProjectBuildState.IsUpToDate(project, optionsKey))
                {
                    Console.WriteLine($"{project.ConfigPath}: up to date");
                    continue;
                }

                if (!CheckProject(project, cliOptions, out var inputs, out var outputs))
                {
                    return 1;
                }

                if (useIncremental)
                    ProjectBuildState.Write(project, optionsKey, inputs, outputs);
                string emitted = outputs.Count == 0
                    ? ""
                    : $"; emitted {outputs.Count} declaration file(s)";
                Console.WriteLine($"{project.ConfigPath}: checked {project.RootFiles.Count} root file(s){emitted}");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return 1;
        }
    }

    public static int Watch(
        IReadOnlyList<string> rootConfigPaths,
        GlobalOptions options,
        bool buildMode)
    {
        int exitCode = Run(rootConfigPaths, options, buildMode, force: options.Force);
        var graph = ProjectGraph.Load(rootConfigPaths);
        var directories = CollapseWatchDirectories(graph.Projects.SelectMany(WatchDirectories));

        using var changed = new AutoResetEvent(false);
        using var cancelled = new ManualResetEvent(false);
        var watchers = new List<FileSystemWatcher>();
        ConsoleCancelEventHandler cancelHandler = (_, args) =>
        {
            args.Cancel = true;
            cancelled.Set();
            changed.Set();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            foreach (string directory in directories)
            {
                var watcher = new FileSystemWatcher(directory)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite |
                                   NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true,
                };
                FileSystemEventHandler onChange = (_, args) =>
                {
                    if (IsProjectInput(args.FullPath))
                        changed.Set();
                };
                RenamedEventHandler onRename = (_, args) =>
                {
                    if (IsProjectInput(args.FullPath) || IsProjectInput(args.OldFullPath))
                        changed.Set();
                };
                watcher.Changed += onChange;
                watcher.Created += onChange;
                watcher.Deleted += onChange;
                watcher.Renamed += onRename;
                watchers.Add(watcher);
            }

            Console.WriteLine("Watching for project changes. Press Ctrl+C to stop.");
            while (true)
            {
                changed.WaitOne();
                if (cancelled.WaitOne(0))
                    break;

                // Coalesce the burst of write/rename notifications produced by one save.
                Thread.Sleep(100);
                while (changed.WaitOne(0)) { }
                Console.WriteLine($"[{DateTime.Now:T}] Change detected. Rechecking...");
                exitCode = Run(rootConfigPaths, options, buildMode);
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            foreach (var watcher in watchers)
                watcher.Dispose();
        }

        return exitCode;
    }

    private static bool CheckProject(
        TsConfigResult project,
        GlobalOptions cliOptions,
        out IReadOnlyList<string> inputs,
        out IReadOnlyList<string> outputs)
    {
        inputs = [];
        outputs = [];
        foreach (string warning in project.Warnings)
            Console.WriteLine(warning);

        if (project.RootFiles.Count == 0 && project.DeclarationFiles.Count == 0)
        {
            Console.WriteLine(
                $"Error: tsconfig.json ('{project.ConfigPath}'): no inputs were found. " +
                "Check 'files', 'include', and 'exclude'.");
            return false;
        }

        string projectDirectory = Path.GetDirectoryName(project.ConfigPath)!;
        DotNetReferences.Load(projectDirectory, cliOptions.References);

        var options = MergeOptions(cliOptions, project);
        var resolver = new ModuleResolver(project.ConfigPath, project.ModuleResolution);
        var declarationRoots = project.DeclarationFiles
            .Select(path => resolver.LoadModule(path, options.DecoratorMode))
            .ToArray();
        resolver.RegisterAmbientModuleDeclarations(declarationRoots);

        var sourceRoots = project.RootFiles
            .Where(path => !project.DeclarationFiles.Contains(path, PathComparer))
            .Select(path => resolver.LoadModule(path, options.DecoratorMode))
            .ToArray();
        var modules = resolver.GetModulesInOrder(declarationRoots.Concat(sourceRoots));

        var checker = new TypeChecker(options.TypeCheckerOptions);
        checker.SetDecoratorMode(options.DecoratorMode);
        TypeMap typeMap = checker.CheckModules(modules, resolver);

        var errors = checker.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        foreach (var diagnostic in errors)
            Console.WriteLine($"Error: {diagnostic}");

        inputs = resolver.LoadedFilePaths;
        if (errors.Length > 0)
            return false;

        if (options.Declaration && !options.NoEmit)
        {
            var declarations = SourceDeclarationEmitter.EmitModules(
                modules,
                typeMap,
                modules
                    .Select(module => module.Path)
                    .Where(IsProjectDeclarationSource)
                    .Where(path => !IsNodeModulesPath(path)),
                project.RootDir,
                options.DeclarationDir,
                project.OutDir);
            SourceDeclarationEmitter.WriteAll(declarations);
            outputs = declarations.Select(output => output.OutputPath).ToArray();
        }
        return true;
    }

    private static GlobalOptions MergeOptions(GlobalOptions cli, TsConfigResult project) =>
        cli with
        {
            Strictness = new StrictnessOptions
            {
                Strict = cli.Strictness.Strict ?? project.Strictness.Strict,
                StrictNullChecks = cli.Strictness.StrictNullChecks ?? project.Strictness.StrictNullChecks,
                StrictFunctionTypes = cli.Strictness.StrictFunctionTypes ?? project.Strictness.StrictFunctionTypes,
                NoImplicitAny = cli.Strictness.NoImplicitAny ?? project.Strictness.NoImplicitAny,
            },
            CheckJs = cli.CheckJs || project.CheckJs == true,
            EmitDecoratorMetadata = cli.EmitDecoratorMetadata || project.EmitDecoratorMetadata == true,
            Declaration = cli.Declaration || project.Declaration == true ||
                project.EmitDeclarationOnly == true || project.Composite == true,
            EmitDeclarationOnly = cli.EmitDeclarationOnly || project.EmitDeclarationOnly == true,
            DeclarationDir = cli.DeclarationDir is not null
                ? Path.GetFullPath(cli.DeclarationDir)
                : project.DeclarationDir,
            DecoratorMode = cli.DecoratorMode == DecoratorMode.Stage3 && project.DecoratorMode is { } configured
                ? configured
                : cli.DecoratorMode,
        };

    private static IReadOnlyList<string> CollapseWatchDirectories(IEnumerable<string> directories)
    {
        var comparer = PathComparer;
        var ordered = directories
            .Select(Path.GetFullPath)
            .Distinct(comparer)
            .OrderBy(path => path.Length)
            .ToList();
        var result = new List<string>();
        foreach (string candidate in ordered)
        {
            if (!result.Any(parent => IsUnder(candidate, parent)))
                result.Add(candidate);
        }
        return result;
    }

    private static bool IsProjectInput(string path)
    {
        string fileName = Path.GetFileName(path);
        return fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".mts", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".cts", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".cjs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProjectDeclarationSource(string path) =>
        !path.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase) &&
        !path.EndsWith(".d.mts", StringComparison.OrdinalIgnoreCase) &&
        !path.EndsWith(".d.cts", StringComparison.OrdinalIgnoreCase) &&
        (path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".mts", StringComparison.OrdinalIgnoreCase) ||
         path.EndsWith(".cts", StringComparison.OrdinalIgnoreCase));

    private static bool IsNodeModulesPath(string path) =>
        Path.GetFullPath(path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part.Equals("node_modules", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> WatchDirectories(TsConfigResult project)
    {
        yield return Path.GetDirectoryName(project.ConfigPath)!;

        foreach (string file in project.RootFiles.Concat(project.DeclarationFiles))
            yield return Path.GetDirectoryName(file)!;

        if (project.ModuleResolution.BaseUrl is { } baseUrl && Directory.Exists(baseUrl))
            yield return baseUrl;

        foreach (string target in project.ModuleResolution.Paths.Values.SelectMany(value => value))
        {
            int wildcard = target.IndexOfAny(['*', '?']);
            string prefix = wildcard < 0 ? target : target[..wildcard];
            string? directory = Directory.Exists(prefix)
                ? prefix
                : Path.GetDirectoryName(prefix.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (directory is not null && Directory.Exists(directory))
                yield return directory;
        }
    }

    private static string BuildOptionsKey(GlobalOptions options)
    {
        string[] values =
        [
            options.Strictness.Strict?.ToString() ?? "",
            options.Strictness.StrictNullChecks?.ToString() ?? "",
            options.Strictness.StrictFunctionTypes?.ToString() ?? "",
            options.Strictness.NoImplicitAny?.ToString() ?? "",
            options.CheckJs.ToString(),
            options.DecoratorMode.ToString(),
            options.EmitDecoratorMetadata.ToString(),
            options.Declaration.ToString(),
            options.EmitDeclarationOnly.ToString(),
            options.DeclarationDir is null ? "" : Path.GetFullPath(options.DeclarationDir),
            .. options.References
                .Select(Path.GetFullPath)
                .OrderBy(path => path, PathComparer),
        ];
        return string.Join("|", values);
    }

    private static bool IsUnder(string candidate, string parent)
    {
        string relative = Path.GetRelativePath(parent, candidate);
        return relative == "." ||
               (!relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                relative != ".." &&
                !Path.IsPathRooted(relative));
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
