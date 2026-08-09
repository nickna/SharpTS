// =============================================================================
// Program.cs - Entry point for the SharpTS TypeScript interpreter/compiler
// =============================================================================
//
// Orchestrates the compiler pipeline: Lex → Parse → TypeCheck → (Interpret OR Compile)
//
// Usage modes:
//   dotnet run                           - Start REPL (interactive mode)
//   dotnet run -- <file.ts>              - Interpret a TypeScript file
//   dotnet run -- --compile <file.ts>    - Compile to .NET IL assembly
//   dotnet run -- -c <file.ts> -o out.dll - Compile with custom output path
//
// Global flags:
//   -r, --reference <assembly.dll>       - Add .NET assembly reference (repeatable; all modes).
//                                          sharpts.json next to/above the entry script supplies
//                                          project-level references and NuGet packages.
//
// Compilation flags:
//   --ref-asm                            - Emit reference-assembly-compatible output
//   --sdk-path <path>                    - Explicit path to .NET SDK reference assemblies
//   --preserveConstEnums                 - Preserve const enum declarations
//   --verify                             - Verify emitted IL using Microsoft.ILVerification
//   -g, --debug                          - Emit a portable PDB alongside the assembly
//
// Decorator flags:
//   --experimentalDecorators             - Enable Legacy (Stage 2) decorators
//   --decorators                         - Enable TC39 Stage 3 decorators
//   --emitDecoratorMetadata              - Emit design-time type metadata
//
// Pipeline stages:
//   1. Lexer      - Tokenizes source code into Token stream
//   2. Parser     - Builds AST from tokens (with desugaring)
//   3. TypeChecker - Static type validation (runs before execution)
//   4. Interpreter - Tree-walking execution (default)
//      OR
//   4. ILCompiler  - Ahead-of-time compilation to .NET assembly (--compile flag)
//
// See also: Lexer.cs, Parser.cs, TypeChecker.cs, Interpreter.cs, ILCompiler.cs
// =============================================================================

using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using SharpTS.Cli;
using SharpTS.Compilation;
using SharpTS.Configuration;
using PEPacker;
using PEPacker.Bundling;
using SharpTS.Declaration;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Execution;
using SharpTS.Modules;
using SharpTS.Packaging;
using SharpTS.Parsing;
using SharpTS.Projects;
using SharpTS.References;
using SharpTS.Runtime.DotNet;
using SharpTS.TypeSystem;
#pragma warning disable SHARPTS_HOSTING001
using SharpTS.Hosting;

return SharpTSCli.Run(args);

/// <summary>Reusable SharpTS command-line host used by the stock and custom Native AOT executables.</summary>
public static class SharpTSCli
{
public static int Run(string[] args, INativeDotNetCatalog? nativeInteropCatalog = null)
{
if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
{
    NativeDotNetInterop.Configure(nativeInteropCatalog ?? DefaultNativeDotNetCatalog.Instance);
}

// Initialize fork IPC if this process was spawned via child_process.fork()
SharpTS.Runtime.Types.ForkIpcClient.TryInitialize();

// Parse command-line arguments
var parser = new CommandLineParser();
var command = parser.Parse(args);

switch (command)
{
    case ParsedCommand.Help:
        PrintHelp();
        return 0;

    case ParsedCommand.Version:
        Console.WriteLine($"sharpts {GetVersion()}");
        return 0;

    case ParsedCommand.NewAvalonia create:
        try { return GuiApplicationCli.Create(create); }
        catch (Exception exception) { Console.Error.WriteLine($"Error: {exception.Message}"); return 1; }

    case ParsedCommand.Application application:
        try { return GuiApplicationCli.Run(application); }
        catch (Exception exception) { Console.Error.WriteLine($"Error: {exception.Message}"); return 1; }

    case ParsedCommand.Error error:
        Console.WriteLine(error.Message);
        if (error.ShowCompileUsage)
            PrintCompileUsage();
        return error.ExitCode;

    case ParsedCommand.Project project:
        try
        {
            string projectPath = TsConfigLoader.ResolveProjectPath(project.Options.ProjectPath!);
            var projectConfig = TsConfigLoader.Load(projectPath);
            if (project.Options.ShowConfig)
            {
                var (resolvedOptions, _) = ApplyTsConfig(
                    project.Options, Path.GetDirectoryName(projectPath) ?? ".");
                PrintResolvedConfig(resolvedOptions, project.Options.Strictness, projectConfig);
                return Environment.ExitCode;
            }

            Environment.ExitCode = project.Options.Watch
                ? ProjectCommandRunner.Watch([projectPath], project.Options, buildMode: false)
                : ProjectCommandRunner.Run(
                    [projectPath], project.Options, buildMode: false, force: project.Options.Force);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            Environment.ExitCode = 1;
        }
        break;

    case ParsedCommand.Build build:
        try
        {
            var projectPaths = build.ProjectPaths
                .Select(TsConfigLoader.ResolveProjectPath)
                .ToArray();
            Environment.ExitCode = build.Options.Watch
                ? ProjectCommandRunner.Watch(projectPaths, build.Options, buildMode: true)
                : ProjectCommandRunner.Run(
                    projectPaths, build.Options, buildMode: true, force: build.Options.Force);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            Environment.ExitCode = 1;
        }
        break;

    case ParsedCommand.Repl repl:
        // tsconfig discovery starts at the CWD for the REPL, matching the manifest lookup below.
        var (replOptions, replConfig) = ApplyTsConfig(repl.Options, Environment.CurrentDirectory);
        if (replOptions.ShowConfig)
        {
            PrintResolvedConfig(replOptions, repl.Options.Strictness, replConfig);
            return Environment.ExitCode;
        }
        var replRefs = LoadDotNetReferences(Environment.CurrentDirectory, replOptions.References);
        if (replRefs.ManifestPath != null)
            Console.WriteLine($"Loaded {replRefs.ManifestPath}: {replRefs.References.Count} assembly reference(s)");
        if (replConfig != null)
            Console.WriteLine($"Loaded {replConfig.ConfigPath}");
        RunPromptAsync(replOptions).GetAwaiter().GetResult();
        break;

    case ParsedCommand.Script script:
        // Discovery starts at the entry script's directory — the same convention
        // LoadDotNetReferences uses for sharpts.json.
        var (scriptOptions, scriptConfig) = ApplyTsConfig(
            script.Options, Path.GetDirectoryName(Path.GetFullPath(script.ScriptPath)) ?? ".");
        if (scriptOptions.ShowConfig)
        {
            PrintResolvedConfig(scriptOptions, script.Options.Strictness, scriptConfig);
            return Environment.ExitCode;
        }
        RunFile(script.ScriptPath, scriptOptions, script.ScriptArgs, scriptConfig);
        break;

    case ParsedCommand.Compile compile:
        return RunCompileCommand(compile);

    case ParsedCommand.GenDecl genDecl:
        RequireManagedBuild("--gen-decl"); // DiscoveryGenerator needs Assembly.LoadFrom; the by-name fallback returns truncated metadata
        GenerateDeclarations(genDecl.TypeOrAssembly, genDecl.OutputPath, genDecl.Json, genDecl.References);
        break;
}

return Environment.ExitCode;
}

static int RunCompileCommand(ParsedCommand.Compile compile)
{
    var totalStartedAt = Stopwatch.GetTimestamp();
    var timings = compile.CompileOptions.Timings || compile.CompileOptions.TimingsJson
        ? new ExecutionTimingCollector()
        : null;
    bool json = compile.CompileOptions.TimingsJson;
    TextWriter? originalOut = null;
    int exitCode = 1;

    try
    {
        if (json)
        {
            originalOut = Console.Out;
            Console.SetOut(Console.Error);
        }

        var resolved = MeasurePhase(timings,
            ExecutionPhaseTiming.ResolveConfiguration,
            () => ApplyTsConfig(
                compile.GlobalOptions,
                Path.GetDirectoryName(Path.GetFullPath(compile.InputFile)) ?? ".",
                propagateErrors: true));
        var (compileOptions, compileConfig) = resolved;

        if (compile.CompileOptions.VerifyIL &&
            !System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
        {
            Console.Error.WriteLine(FormatManagedBuildRequiredError("--verify"));
            exitCode = 1;
        }
        else
        {
            var outputOptions = new OutputOptions(
                compile.CompileOptions.MsBuildErrors,
                compile.CompileOptions.QuietMode || json,
                compile.CompileOptions.Standalone,
                compile.CompileOptions.EmitDebugSymbols);
            exitCode = CompileFile(
                compile.InputFile,
                compile.OutputFile,
                compile.CompileOptions.PreserveConstEnums ||
                    (compileConfig?.PreserveConstEnums ?? false),
                compile.CompileOptions.UseReferenceAssemblies,
                compile.CompileOptions.SdkPath,
                compile.CompileOptions.VerifyIL,
                compileOptions.DecoratorMode,
                compileOptions.EmitDecoratorMetadata,
                compile.PackOptions,
                outputOptions,
                compile.CompileOptions.References,
                compile.CompileOptions.Target,
                compile.CompileOptions.Bundler,
                compile.CompileOptions.Hosted,
                compileOptions,
                timings,
                compileConfig);
        }
    }
    catch (SharpTSException ex)
    {
        new DiagnosticReporter
        {
            MsBuildFormat = compile.CompileOptions.MsBuildErrors
        }.Report(ex.Diagnostic);
        exitCode = 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        exitCode = 1;
    }
    finally
    {
        if (originalOut is not null)
            Console.SetOut(originalOut);

        if (compile.CompileOptions.Timings || json)
        {
            PrintCompilationTimings(
                exitCode == 0,
                Stopwatch.GetElapsedTime(totalStartedAt).TotalMilliseconds,
                timings!.Snapshot(),
                json,
                originalOut ?? Console.Out);
        }
    }

    return exitCode;
}

static T MeasurePhase<T>(ExecutionTimingCollector? timings, string name, Func<T> action) =>
    timings is null ? action() : timings.Measure(name, action);

static void MeasurePhase(ExecutionTimingCollector? timings, string name, Action action)
{
    if (timings is null)
        action();
    else
        timings.Measure(name, action);
}

static void PrintCompilationTimings(
    bool success,
    double totalDurationMs,
    IReadOnlyList<ExecutionPhaseTiming> timings,
    bool json,
    TextWriter jsonOutput)
{
    if (json)
    {
        var report = new CompilationTimingReport(success, totalDurationMs, timings.ToArray());
        jsonOutput.WriteLine(JsonSerializer.Serialize(
            report,
            CompilationTimingJsonContext.Default.CompilationTimingReport));
        return;
    }

    Console.Error.WriteLine("Compilation timings:");
    Console.Error.WriteLine($"{"Phase",-28} {"Status",-10} {"Duration (ms)",14}");
    foreach (var timing in timings)
        Console.Error.WriteLine($"{timing.Name,-28} {timing.Status,-10} {timing.DurationMs,14:F3}");
    Console.Error.WriteLine($"{"total",-28} {(success ? "completed" : "failed"),-10} {totalDurationMs,14:F3}");
}

/// <summary>
/// Fails fast (print + exit 1, the CLI's config-error seam) when a feature that fundamentally
/// requires the managed runtime is invoked from a Native AOT build (#1324). A no-op on the
/// managed builds, where dynamic code is always supported; each gated feature's error names the
/// fix instead of letting it die later on an obscure loader exception.
/// </summary>
static void RequireManagedBuild(string feature)
{
    if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
    {
        Console.Error.WriteLine(FormatManagedBuildRequiredError(feature));
        Environment.Exit(1);
    }
}

/// <summary>
/// Formats the stable CLI diagnostic used when a runtime SKU cannot provide a managed-only
/// capability. Automation must match the code and feature context, not the mutable prose.
/// </summary>
internal static string FormatManagedBuildRequiredError(string feature) =>
    ManagedBuildRequiredException.CreateDiagnostic(feature).ToHumanFormat();

/// <summary>
/// Discovers tsconfig.json (or loads the one named by -p/--project) and folds it under the
/// command line, which wins per key. Returns the options execution should actually use.
/// </summary>
/// <remarks>
/// Config errors are user-facing configuration problems, so they follow the same seam as
/// <see cref="LoadDotNetReferences"/>: print and exit 1.
/// <para>Only strictness merges with full tri-state fidelity. <c>checkJs</c>,
/// <c>preserveConstEnums</c> and <c>emitDecoratorMetadata</c> are plain bools on
/// <see cref="GlobalOptions"/>, so an absent flag is indistinguishable from an explicit
/// <c>false</c> and tsconfig can only turn them ON — the same one-way merge
/// SharpTS.Sdk/Sdk/Sdk.targets already performs. Decorator mode applies from tsconfig only when
/// the command line left it at its default.</para>
/// </remarks>
static (GlobalOptions Options, TsConfigResult? Config) ApplyTsConfig(
    GlobalOptions cli,
    string startDirectory,
    bool propagateErrors = false)
{
    if (cli.NoTsConfig)
        return (cli, null);

    try
    {
        string? path = cli.ProjectPath is { } explicitPath
            ? TsConfigLoader.ResolveProjectPath(explicitPath)
            : TsConfigLoader.Discover(startDirectory);

        if (path is null)
            return (cli, null);

        var config = TsConfigLoader.Load(path);

        // Warnings never affect the exit code. They go straight to the console rather than
        // through the diagnostic stream, which the run path only prints when there are errors.
        if (!cli.ShowConfig)
        {
            foreach (var warning in config.Warnings)
                Console.WriteLine(warning);
        }

        var merged = cli with
        {
            Strictness = new StrictnessOptions
            {
                Strict = cli.Strictness.Strict ?? config.Strictness.Strict,
                StrictNullChecks = cli.Strictness.StrictNullChecks ?? config.Strictness.StrictNullChecks,
                StrictFunctionTypes = cli.Strictness.StrictFunctionTypes ?? config.Strictness.StrictFunctionTypes,
                NoImplicitAny = cli.Strictness.NoImplicitAny ?? config.Strictness.NoImplicitAny,
                NoImplicitThis = cli.Strictness.NoImplicitThis ?? config.Strictness.NoImplicitThis,
                StrictPropertyInitialization = cli.Strictness.StrictPropertyInitialization ?? config.Strictness.StrictPropertyInitialization,
                ExactOptionalPropertyTypes = cli.Strictness.ExactOptionalPropertyTypes ?? config.Strictness.ExactOptionalPropertyTypes,
                NoUncheckedIndexedAccess = cli.Strictness.NoUncheckedIndexedAccess ?? config.Strictness.NoUncheckedIndexedAccess,
            },
            CheckJs = cli.CheckJs || (config.CheckJs ?? false),
            EmitDecoratorMetadata = cli.EmitDecoratorMetadata || (config.EmitDecoratorMetadata ?? false),
            Declaration = cli.Declaration || config.Declaration == true ||
                config.EmitDeclarationOnly == true || config.Composite == true,
            EmitDeclarationOnly = cli.EmitDeclarationOnly || config.EmitDeclarationOnly == true,
            DeclarationDir = cli.DeclarationDir is not null
                ? Path.GetFullPath(cli.DeclarationDir)
                : config.DeclarationDir,
            DecoratorMode = cli.DecoratorMode == DecoratorMode.Stage3 && config.DecoratorMode is { } m
                ? m
                : cli.DecoratorMode,
            Lib = cli.Lib ?? config.Lib,
            NoLib = cli.NoLib ?? config.NoLib,
            Types = cli.Types ?? config.Types,
            TypeRoots = cli.TypeRoots ?? config.TypeRoots,
            Jsx = cli.Jsx ?? config.Jsx,
            JsxFactory = cli.JsxFactory ?? config.JsxFactory,
            JsxFragmentFactory = cli.JsxFragmentFactory ?? config.JsxFragmentFactory,
            JsxImportSource = cli.JsxImportSource ?? config.JsxImportSource,
        };

        return (merged, config);
    }
    catch (Exception ex)
    {
        if (propagateErrors)
            throw;
        Console.WriteLine(ex.Message);
        Environment.Exit(1);
        throw; // unreachable
    }
}

/// <summary>
/// Prints the resolved configuration and where each value came from, then exits 0. Output is a
/// single JSON document so it can be piped to a tool; SharpTS-specific detail is namespaced
/// under "sharpts" so the "compilerOptions" block stays tsc-shaped.
/// </summary>
/// <param name="cliStrictness">
/// The command line's own strictness layer, BEFORE the tsconfig fold — provenance is
/// unrecoverable from the merged options, where a tsconfig value is indistinguishable from a
/// flag.
/// </param>
static void PrintResolvedConfig(GlobalOptions options, StrictnessOptions cliStrictness, TsConfigResult? config)
{
    string Origin(bool? cliValue, bool? configValue) =>
        cliValue is not null ? "cli" : configValue is not null ? "tsconfig" : "default";

    var effective = options.TypeCheckerOptions;
    var cliLayer = cliStrictness;
    var configLayer = config?.Strictness ?? new StrictnessOptions();

    // `strict` acts as the fallback, so a key it supplied reports the umbrella as its source.
    string OriginVia(bool? cliValue, bool? configValue)
    {
        string origin = Origin(cliValue, configValue);
        if (origin != "default") return origin;
        return cliLayer.Strict is not null ? "cli (via strict)"
            : configLayer.Strict is not null ? "tsconfig (via strict)"
            : "default";
    }

    var payload = new Dictionary<string, object?>
    {
        ["compilerOptions"] = new Dictionary<string, object?>
        {
            ["strictNullChecks"] = effective.StrictNullChecks,
            ["strictFunctionTypes"] = effective.StrictFunctionTypes,
            ["noImplicitAny"] = effective.NoImplicitAny,
            ["noImplicitThis"] = effective.NoImplicitThis,
            ["strictPropertyInitialization"] = effective.StrictPropertyInitialization,
            ["exactOptionalPropertyTypes"] = effective.ExactOptionalPropertyTypes,
            ["noUncheckedIndexedAccess"] = effective.NoUncheckedIndexedAccess,
            ["checkJs"] = options.CheckJs,
            ["emitDecoratorMetadata"] = options.EmitDecoratorMetadata,
            ["declaration"] = options.Declaration,
            ["emitDeclarationOnly"] = options.EmitDeclarationOnly,
            ["declarationDir"] = options.DeclarationDir,
            ["lib"] = options.Lib,
            ["noLib"] = options.NoLib ?? false,
            ["types"] = options.Types,
            ["typeRoots"] = options.TypeRoots,
            ["jsx"] = options.ResolvedJsxOptions.Mode switch
            {
                JsxMode.React => "react",
                JsxMode.ReactJsx => "react-jsx",
                JsxMode.ReactJsxDev => "react-jsxdev",
                _ => "none",
            },
            ["jsxFactory"] = options.ResolvedJsxOptions.Factory,
            ["jsxFragmentFactory"] = options.ResolvedJsxOptions.FragmentFactory,
            ["jsxImportSource"] = options.ResolvedJsxOptions.ImportSource,
        },
        ["sharpts"] = new Dictionary<string, object?>
        {
            ["configFile"] = config?.ConfigPath,
            ["extendsChain"] = config?.ExtendsChain ?? [],
            ["decoratorMode"] = options.DecoratorMode.ToString(),
            ["entryPoint"] = config?.EntryFile,
            ["outDir"] = config?.OutDir,
            ["rootFiles"] = config?.RootFiles ?? [],
            ["declarationFiles"] = config?.DeclarationFiles ?? [],
            ["projectReferences"] = config?.ProjectReferences ?? [],
            ["moduleResolution"] = config?.ModuleResolution.Mode.ToString(),
            ["baseUrl"] = config?.ModuleResolution.BaseUrl,
            ["paths"] = config?.ModuleResolution.Paths,
            ["lib"] = config?.Lib,
            ["noLib"] = config?.NoLib,
            ["types"] = config?.Types,
            ["typeRoots"] = config?.TypeRoots,
            ["incremental"] = options.Incremental || config?.Incremental == true,
            ["composite"] = config?.Composite,
            ["buildInfoFile"] = config?.BuildInfoFile,
            ["provenance"] = new Dictionary<string, object?>
            {
                ["strictNullChecks"] = OriginVia(cliLayer.StrictNullChecks, configLayer.StrictNullChecks),
                ["strictFunctionTypes"] = OriginVia(cliLayer.StrictFunctionTypes, configLayer.StrictFunctionTypes),
                ["noImplicitAny"] = OriginVia(cliLayer.NoImplicitAny, configLayer.NoImplicitAny),
                ["noImplicitThis"] = OriginVia(cliLayer.NoImplicitThis, configLayer.NoImplicitThis),
                ["strictPropertyInitialization"] = OriginVia(
                    cliLayer.StrictPropertyInitialization, configLayer.StrictPropertyInitialization),
                ["exactOptionalPropertyTypes"] = Origin(
                    cliLayer.ExactOptionalPropertyTypes, configLayer.ExactOptionalPropertyTypes),
                ["noUncheckedIndexedAccess"] = Origin(
                    cliLayer.NoUncheckedIndexedAccess, configLayer.NoUncheckedIndexedAccess),
            },
            ["notes"] = config?.Warnings ?? [],
        },
    };

    // JsonGraphWriter, not JsonSerializer.Serialize(object): --show-config must work in the
    // native SKU, where the reflection resolver is unavailable (#1324 Phase 1).
    Console.WriteLine(SharpTS.Runtime.BuiltIns.JsonGraphWriter.Write(payload, indented: true));
}

/// <summary>
/// Resolves and loads third-party reference assemblies (sharpts.json manifest + -r flags)
/// before any module loading or type checking, so every resolution seam sees the types.
/// Reference errors are user-facing configuration problems: print and exit.
/// </summary>
static ReferenceSet LoadDotNetReferences(
    string startDirectory,
    IReadOnlyList<string> cliReferences,
    bool propagateErrors = false)
{
    try
    {
        return DotNetReferences.Load(startDirectory, cliReferences);
    }
    catch (SharpTSException ex)
    {
        if (propagateErrors)
            throw;
        new DiagnosticReporter().Report(ex.Diagnostic);
        Environment.Exit(1);
        throw; // unreachable
    }
    catch (Exception ex)
    {
        if (propagateErrors)
            throw;
        Console.WriteLine(ex.Message);
        Environment.Exit(1);
        throw; // unreachable
    }
}

static void RunFile(
    string path,
    GlobalOptions options,
    string[]? scriptArgs = null,
    TsConfigResult? project = null)
{
    string absolutePath = Path.GetFullPath(path);

    // Third-party assembly references (sharpts.json walked up from the entry script,
    // plus -r flags) load before any module resolution so dotnet: imports and
    // @DotNetType declarations can see the types.
    LoadDotNetReferences(Path.GetDirectoryName(absolutePath) ?? ".", options.References);

    // Set script arguments for process.argv
    SharpTS.Runtime.BuiltIns.ProcessBuiltIns.SetScriptArguments(absolutePath, scriptArgs ?? []);

    // A TypeScript program always includes its selected declaration libraries,
    // even when the entry file has no imports. The module driver also preserves
    // shared-script execution semantics for such files.
    RunModuleFile(absolutePath, options, scriptArgs, project);
}

static void RunModuleFile(
    string absolutePath,
    GlobalOptions options,
    string[]? scriptArgs = null,
    TsConfigResult? project = null)
{
    var decoratorMode = options.DecoratorMode;
    try
    {
        // Load the entry module and all dependencies
        var resolver = new ModuleResolver(
            absolutePath,
            project?.ModuleResolution ?? ModuleResolutionOptions.Default,
            virtualFiles: null,
            options.TypeScriptProgramOptions)
        {
            JsxOptions = options.ResolvedJsxOptions,
        };
        var declarationModules = (project?.DeclarationFiles ?? [])
            .Select(path => resolver.LoadModule(path, decoratorMode))
            .ToArray();
        resolver.RegisterAmbientModuleDeclarations(declarationModules);
        var entryModule = resolver.LoadProgram(absolutePath, decoratorMode);
        var runtimeModules = resolver.GetRuntimeModulesInOrder(entryModule);
        var allModules = resolver.GetModulesInOrder(declarationModules.Append(entryModule));

        // Type checking across all modules (still uses Check-style API for modules)
        // Module type checking has its own error handling
        var checker = new TypeChecker(options.TypeCheckerOptions);
        checker.SetDecoratorMode(decoratorMode);
        var typeMap = checker.CheckModules(allModules, resolver);

        // Check for type errors — warnings (from lenient CJS modules) don't block execution
        var diagnostics = checker.GetDiagnostics();
        bool hasErrors = diagnostics.Any(d => d.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        if (hasErrors)
        {
            foreach (var d in diagnostics.Where(d => d.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error))
                Console.WriteLine($"Error: {d}");
            Environment.ExitCode = 1;
            return;
        }

        // --noEmit: type-check only, never interpret.
        if (options.NoEmit) return;

        // Interpretation
        var interpreter = new Interpreter();
        interpreter.SetDecoratorMode(decoratorMode);
        // Program main interpreter: fire beforeExit/exit at event-loop drain and
        // receive signal events (#1080/#1081).
        interpreter.EmitProcessLifecycleEvents = true;

        // If this process was forked, wire its IPC channel to this interpreter's loop so
        // 'message' handlers run with an interpreter and the child stays alive (#1017).
        SharpTS.Runtime.Types.ForkIpcClient.Instance?.AttachLoop(interpreter);

        // Variable Resolution Phase (enables O(1) lookups)
        var varResolver = new VariableResolver(interpreter);
        foreach (var module in runtimeModules)
        {
            if (!module.IsBuiltIn)
                varResolver.Resolve(module.Statements);
        }

        interpreter.InterpretModules(runtimeModules, resolver, typeMap);

        // Node default: an unhandled promise rejection makes the process
        // exit nonzero. The rejection itself was already reported to stderr
        // by the interpreter when it was observed (#228).
        if (interpreter.HadUnhandledRejection)
        {
            Environment.Exit(1);
        }
    }
    catch (SharpTS.Runtime.Exceptions.ThrowException tex)
    {
        Console.WriteLine($"Error: {tex.Value}");
        Environment.ExitCode = 1;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
        Environment.ExitCode = 1;
    }
}

static async Task RunPromptAsync(GlobalOptions options)
{
    var decoratorMode = options.DecoratorMode;
    PrintBanner();
    if (decoratorMode != DecoratorMode.None)
    {
        Console.WriteLine($"Decorator mode: {decoratorMode}");
    }
    Console.WriteLine("Type expressions to evaluate. Press Ctrl+C to cancel input.");
    Console.WriteLine("Type .help for available commands.");
    Console.WriteLine();

    using var repl = new SharpTS.Repl.ReplEngine(decoratorMode, options.TypeCheckerOptions);
    await repl.RunAsync();
}

static int CompileFile(string inputPath, string outputPath, bool preserveConstEnums, bool useReferenceAssemblies, string? sdkPath, bool verifyIL, DecoratorMode decoratorMode, bool emitDecoratorMetadata, PackOptions packOptions, OutputOptions outputOptions, IReadOnlyList<string> references, OutputTarget target, BundlerMode bundlerMode, bool hosted, GlobalOptions globalOptions, ExecutionTimingCollector? timings, TsConfigResult? project = null)
{
    try
    {
        string absolutePath = Path.GetFullPath(inputPath);

        // Third-party assembly references (sharpts.json + -r) load into this process
        // before module resolution: dotnet: imports resolve at module-load time, and
        // the returned set drives the post-Save co-location of referenced DLLs.
        var externalRefs = MeasurePhase(timings,
            ExecutionPhaseTiming.LoadReferences,
            () => LoadDotNetReferences(
                Path.GetDirectoryName(absolutePath) ?? ".", references, propagateErrors: true));

        // Load package.json if packaging is enabled
        PackageJson? packageJson = null;
        AssemblyMetadata? metadata = null;
        if (packOptions.Pack)
        {
            var inputDir = Path.GetDirectoryName(absolutePath) ?? ".";
            packageJson = MeasurePhase(timings,
                ExecutionPhaseTiming.LoadPackageMetadata,
                () =>
                {
                    var loadedPackage = PackageJsonLoader.FindAndLoad(inputDir);
                    if (loadedPackage == null && packOptions.PackageIdOverride == null)
                    {
                        Console.WriteLine(
                            "Error: No package.json found. Provide --package-id and --version, or create a package.json.");
                        throw new CompilationAbortedException();
                    }
                    return loadedPackage;
                });

            // Create assembly metadata from package.json and overrides
            if (packageJson != null)
            {
                metadata = AssemblyMetadata.FromPackageJson(packageJson);
                if (!string.IsNullOrEmpty(packOptions.VersionOverride))
                {
                    var versionPart = packOptions.VersionOverride.Split('-')[0];
                    if (Version.TryParse(versionPart, out var ver))
                    {
                        metadata = metadata with { Version = ver, InformationalVersion = packOptions.VersionOverride };
                    }
                }
            }
            else
            {
                // Create minimal metadata from CLI overrides
                Version? version = null;
                if (!string.IsNullOrEmpty(packOptions.VersionOverride))
                {
                    var versionPart = packOptions.VersionOverride.Split('-')[0];
                    Version.TryParse(versionPart, out version);
                }
                metadata = new AssemblyMetadata(
                    Version: version,
                    Title: packOptions.PackageIdOverride,
                    InformationalVersion: packOptions.VersionOverride
                );
            }
        }

        // Even a single script is a TypeScript program with default declaration
        // libraries. The module compiler preserves script/global semantics while
        // keeping declaration-only inputs out of emitted IL.
        CompileModuleFile(absolutePath, outputPath, preserveConstEnums, useReferenceAssemblies,
            sdkPath, verifyIL, decoratorMode, outputOptions, metadata, references, target,
            bundlerMode, hosted, externalRefs, globalOptions, timings, project);

        // These modes stopped before any assembly was written, so there is nothing to pack.
        if (globalOptions.NoEmit || globalOptions.EmitDeclarationOnly) return 0;

        // Package if requested
        if (packOptions.Pack)
        {
            CreateNuGetPackage(
                outputPath, packageJson, packOptions, timings, outputOptions.QuietMode);
        }
        return 0;
    }
    catch (SharpTSException ex)
    {
        var reporter = new DiagnosticReporter { MsBuildFormat = outputOptions.MsBuildErrors };
        reporter.Report(ex.Diagnostic);
        return 1;
    }
    catch (CompilationAbortedException)
    {
        return 1;
    }
    catch (Exception ex)
    {
        // PROBE (gate, #1324): full stack for diagnosing native-AOT compile walls.
        if (Environment.GetEnvironmentVariable("SHARPTS_DEBUG_STACK") == "1")
            Console.Error.WriteLine(ex);
        if (outputOptions.MsBuildErrors)
        {
            // MSBuild error format: file(line,col): error CODE: message
            Console.Error.WriteLine($"{inputPath}(1,1): error SHARPTS000: {ex.Message}");
        }
        else if (ex.Message.StartsWith("Parse Error", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
        }
        else
        {
            // Errors belong on stderr: release smokes (publish.yml) assert bundler
            // refusals there, and compiled-output diffs must not see error text on stdout.
            Console.Error.WriteLine($"Error: {ex.Message}");
        }
        return 1;
    }
}

static void CompileModuleFile(string absolutePath, string outputPath, bool preserveConstEnums, bool useReferenceAssemblies, string? sdkPath, bool verifyIL, DecoratorMode decoratorMode, OutputOptions outputOptions, AssemblyMetadata? metadata, IReadOnlyList<string> references, OutputTarget target, BundlerMode bundlerMode, bool hosted, ReferenceSet externalRefs, GlobalOptions globalOptions, ExecutionTimingCollector? timings, TsConfigResult? project = null)
{
    var loaded = MeasurePhase(timings, ExecutionPhaseTiming.LoadModules, () =>
    {
        var loadedResolver = new ModuleResolver(
            absolutePath,
            project?.ModuleResolution ?? ModuleResolutionOptions.Default,
            virtualFiles: null,
            globalOptions.TypeScriptProgramOptions)
        {
            JsxOptions = globalOptions.ResolvedJsxOptions,
        };
        var loadedDeclarations = (project?.DeclarationFiles ?? [])
            .Select(path => loadedResolver.LoadModule(path, decoratorMode))
            .ToArray();
        loadedResolver.RegisterAmbientModuleDeclarations(loadedDeclarations);
        var loadedEntry = loadedResolver.LoadProgram(absolutePath, decoratorMode);
        return (
            Resolver: loadedResolver,
            Declarations: loadedDeclarations,
            Entry: loadedEntry,
            RuntimeModules: loadedResolver.GetRuntimeModulesInOrder(loadedEntry),
            TypeModules: loadedResolver.GetModulesInOrder(loadedDeclarations.Append(loadedEntry)));
    });
    var resolver = loaded.Resolver;
    var declarationModules = loaded.Declarations;
    var entryModule = loaded.Entry;
    var allModules = loaded.RuntimeModules;
    var typeModules = loaded.TypeModules;

    var checker = new TypeChecker(globalOptions.TypeCheckerOptions);
    checker.SetDecoratorMode(decoratorMode);
    if (hosted)
        checker.EnableHostedTopLevelAwait();
    TypeMap typeMap;
    var typeCheckStartedAt = timings?.Start() ?? 0;
    try
    {
        typeMap = checker.CheckModules(typeModules, resolver);
    }
    catch
    {
        timings?.Fail(ExecutionPhaseTiming.TypeCheck, typeCheckStartedAt);
        throw;
    }

    var reporter = new DiagnosticReporter
        { MsBuildFormat = outputOptions.MsBuildErrors, QuietMode = outputOptions.QuietMode };
    var diagnostics = checker.GetDiagnostics();
    if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
    {
        timings?.Fail(ExecutionPhaseTiming.TypeCheck, typeCheckStartedAt);
        reporter.ReportAll(diagnostics);
        throw new CompilationAbortedException();
    }
    timings?.Complete(ExecutionPhaseTiming.TypeCheck, typeCheckStartedAt);

    var dynamicModules = new List<ParsedModule>();
    var dynamicModulePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var processedDynamicImports = new HashSet<(string Specifier, string ImportingModulePath)>();
    while (true)
    {
        var pendingImports = checker.DynamicImportReferences
            .Where(reference => processedDynamicImports.Add(reference))
            .ToArray();
        if (pendingImports.Length == 0)
            break;
        var newModules = MeasurePhase(timings,
            ExecutionPhaseTiming.LoadDynamicImports,
            () => pendingImports
                .SelectMany(reference => resolver.LoadDynamicImportModules(
                    [reference.Specifier],
                    reference.ImportingModulePath,
                    decoratorMode))
                .Where(module => dynamicModulePaths.Add(module.Path))
                .ToList());
        if (newModules.Count > 0)
        {
            dynamicModules.AddRange(newModules);
            allModules = resolver.GetRuntimeModulesInOrder(
                dynamicModules.Append(entryModule));
            typeModules = resolver.GetModulesInOrder(
                declarationModules.Concat(dynamicModules).Append(entryModule));

            typeCheckStartedAt = timings?.Start() ?? 0;
            try
            {
                typeMap = checker.CheckModules(typeModules, resolver);
            }
            catch
            {
                timings?.Fail(ExecutionPhaseTiming.TypeCheckDynamicImports, typeCheckStartedAt);
                throw;
            }

            diagnostics = checker.GetDiagnostics();
            if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                timings?.Fail(ExecutionPhaseTiming.TypeCheckDynamicImports, typeCheckStartedAt);
                reporter.ReportAll(diagnostics);
                throw new CompilationAbortedException();
            }
            timings?.Complete(ExecutionPhaseTiming.TypeCheckDynamicImports, typeCheckStartedAt);
        }
    }

    // --noEmit: type-check only, never produce an assembly.
    if (globalOptions.NoEmit)
        return;

    if (globalOptions.Declaration)
    {
        IReadOnlyList<string> sources = project is null
            ? allModules
                .Select(module => module.Path)
                .Where(path => IsDeclarationSource(path) && !IsNodeModulesPath(path))
                .ToArray()
            : project.RootFiles;
        var declarations = MeasurePhase(timings, ExecutionPhaseTiming.EmitDeclarations, () =>
        {
            var emitted = SourceDeclarationEmitter.EmitModules(
                typeModules,
                typeMap,
                sources,
                project?.RootDir,
                globalOptions.DeclarationDir,
                project?.OutDir);
            SourceDeclarationEmitter.WriteAll(emitted);
            return emitted;
        });
        if (!outputOptions.QuietMode)
            foreach (var declaration in declarations)
                Console.WriteLine($"Declaration emitted to {declaration.OutputPath}");
    }

    if (globalOptions.EmitDeclarationOnly)
        return;

    // Dead Code Analysis
    var emittedModules = allModules.Where(module => !module.IsDeclarationFile).ToList();
    var allStatements = emittedModules.SelectMany(m => m.Statements).ToList();
    DeadCodeInfo deadCodeInfo = MeasurePhase(timings,
        ExecutionPhaseTiming.AnalyzeDeadCode,
        () => new DeadCodeAnalyzer(typeMap).Analyze(allStatements));

    // Compilation
    EmitCompiledAssembly(outputPath, preserveConstEnums, useReferenceAssemblies, sdkPath, verifyIL, decoratorMode, outputOptions, metadata, references, target, bundlerMode, hosted, externalRefs, timings,
        compiler => compiler.CompileModules(emittedModules, resolver, typeMap, deadCodeInfo));
}

/// <summary>
/// Shared EXE/DLL emission tail for both compile drivers. Constructs the ILCompiler, runs the
/// supplied compile step, then saves, verifies (--verify), bundles into a single-file EXE (Exe
/// target) or writes the runtimeconfig (DLL target), and co-locates SharpTS.dll / external
/// references when the compilation soft-depends on them. <paramref name="compileBody"/> receives
/// the configured compiler and performs the one step that differs between the drivers
/// (whole-module-graph vs single-file compile).
/// </summary>
static void EmitCompiledAssembly(string outputPath, bool preserveConstEnums, bool useReferenceAssemblies, string? sdkPath, bool verifyIL, DecoratorMode decoratorMode, OutputOptions outputOptions, AssemblyMetadata? metadata, IReadOnlyList<string> references, OutputTarget target, BundlerMode bundlerMode, bool hosted, ReferenceSet externalRefs, ExecutionTimingCollector? timings, Action<ILCompiler> compileBody)
{
    string assemblyName = Path.GetFileNameWithoutExtension(outputPath);

    if (hosted && target != OutputTarget.Dll)
        throw new InvalidOperationException("Hosted ABI output is valid only for DLL output.");

    if (target == OutputTarget.Exe)
    {
        // For EXE output, first compile to a temp DLL, then bundle into single-file EXE
        var tempDllPath = Path.Combine(Path.GetTempPath(), $"{assemblyName}_{Guid.NewGuid():N}.dll");
        try
        {
            // Compile to DLL format (will be bundled into EXE)
            ILCompiler compiler = MeasurePhase(timings,
                ExecutionPhaseTiming.InitializeCompiler,
                () => new ILCompiler(assemblyName, preserveConstEnums, useReferenceAssemblies,
                    sdkPath, metadata, references, OutputTarget.Dll));
            compiler.SetTimingCollector(timings);
            compiler.SetDecoratorMode(decoratorMode);
            compiler.EmitDebugSymbols = outputOptions.EmitDebugSymbols;
            if (hosted) compiler.EnableHostedOutput();
            compileBody(compiler);
            PrintCompilerWarnings(compiler);
            ValidateCompiledRuntimeRequirements(compiler);
            // Symbols belong beside the final executable, not beside the temporary DLL that gets
            // bundled into it.
            compiler.Save(tempDllPath, outputOptions.EmitDebugSymbols ? Path.ChangeExtension(outputPath, ".pdb") : null);

            // Run IL verification on the DLL if requested
            if (verifyIL)
            {
                MeasurePhase(timings,
                    ExecutionPhaseTiming.VerifyAssembly,
                    () => VerifyCompiledAssembly(tempDllPath, sdkPath, externalRefs));
            }

            // Bundle into single-file EXE
            try
            {
                var bundleResult = MeasurePhase(timings,
                    ExecutionPhaseTiming.BundleExecutable,
                    () => AppHostGenerator.CreateSingleFileExecutable(
                        new BundleRequest
                        {
                            EntryAssemblyPath = tempDllPath,
                            OutputPath = outputPath,
                            AssemblyName = assemblyName,
                            // SharpTS targets net10.0. Do not let a Native AOT host's
                            // Environment.Version (the ILC runtime-pack version) leak into the
                            // generated application's runtimeconfig.
                            FrameworkVersion = new Version(10, 0)
                        },
                        bundlerMode));

                if (!outputOptions.QuietMode)
                {
                    Console.WriteLine($"Compiled to {outputPath} (using {bundleResult.TechniqueDescription})");
                }

                // Co-locate SharpTS.dll next to the EXE when the program uses a feature that
                // late-binds into the SharpTS runtime (eval, Proxy, Intl, vm, dns, @DotNetType
                // dynamic events). Honors --standalone. Pure programs stay a single file.
                if (compiler.RequiredSharpTSRuntimeReasons.Count > 0)
                    MeasurePhase(timings, ExecutionPhaseTiming.CopyRuntime,
                        () => CopySharpTSRuntimeIfNeeded(compiler, outputPath, outputOptions));
                if (compiler.ExternalInteropAssemblies.Count > 0)
                    MeasurePhase(timings, ExecutionPhaseTiming.CopyDependencies,
                        () => CopyExternalReferencesIfNeeded(compiler, externalRefs, outputPath, outputOptions));
            }
            catch (Exception ex) when (bundlerMode != BundlerMode.Auto)
            {
                var bundlerName = bundlerMode == BundlerMode.Sdk ? "SDK" : "built-in";
                Console.Error.WriteLine($"Error: {bundlerName} bundler failed: {ex.Message}");
                Console.Error.WriteLine($"The {bundlerName} bundler was explicitly requested. Use '--bundler auto' to allow fallback.");
                throw new CompilationAbortedException();
            }
        }
        finally
        {
            // Clean up temp DLL
            try { File.Delete(tempDllPath); } catch { }
        }
    }
    else
    {
        // Standard DLL output
        ILCompiler compiler = MeasurePhase(timings,
            ExecutionPhaseTiming.InitializeCompiler,
            () => new ILCompiler(assemblyName, preserveConstEnums, useReferenceAssemblies,
                sdkPath, metadata, references, target));
        compiler.SetTimingCollector(timings);
        compiler.SetDecoratorMode(decoratorMode);
        compiler.EmitDebugSymbols = outputOptions.EmitDebugSymbols;
        if (hosted) compiler.EnableHostedOutput();
        compileBody(compiler);
        PrintCompilerWarnings(compiler);
        ValidateCompiledRuntimeRequirements(compiler);
        compiler.Save(outputPath);
        if (hosted)
            CopyHostedAbstractions(outputPath);

        MeasurePhase(timings,
            ExecutionPhaseTiming.GenerateRuntimeConfig,
            () => GenerateRuntimeConfig(outputPath));
        if (compiler.RequiredSharpTSRuntimeReasons.Count > 0)
            MeasurePhase(timings, ExecutionPhaseTiming.CopyRuntime,
                () => CopySharpTSRuntimeIfNeeded(compiler, outputPath, outputOptions));
        if (compiler.ExternalInteropAssemblies.Count > 0)
            MeasurePhase(timings, ExecutionPhaseTiming.CopyDependencies,
                () => CopyExternalReferencesIfNeeded(compiler, externalRefs, outputPath, outputOptions));
        if (!outputOptions.QuietMode)
        {
            Console.WriteLine($"Compiled to {outputPath}");
        }

        // Run IL verification if requested
        if (verifyIL)
        {
            MeasurePhase(timings,
                ExecutionPhaseTiming.VerifyAssembly,
                () => VerifyCompiledAssembly(outputPath, sdkPath, externalRefs));
        }
    }
}

static void CopyHostedAbstractions(string outputPath)
{
    string source = typeof(SharpTSHostedAbi).Assembly.Location;
    if (string.IsNullOrEmpty(source) || !File.Exists(source))
    {
        throw new InvalidOperationException(
            "SharpTS.Hosting.Abstractions.dll is unavailable for hosted output.");
    }
    string destination = Path.Combine(
        Path.GetDirectoryName(Path.GetFullPath(outputPath))!,
        Path.GetFileName(source));
    if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination),
            StringComparison.OrdinalIgnoreCase))
    {
        File.Copy(source, destination, overwrite: true);
    }
}

static bool IsDeclarationSource(string path) =>
    !path.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase) &&
    !path.EndsWith(".d.mts", StringComparison.OrdinalIgnoreCase) &&
    !path.EndsWith(".d.cts", StringComparison.OrdinalIgnoreCase) &&
    (path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
     path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
     path.EndsWith(".mts", StringComparison.OrdinalIgnoreCase) ||
     path.EndsWith(".cts", StringComparison.OrdinalIgnoreCase));

static bool IsNodeModulesPath(string path) =>
    Path.GetFullPath(path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .Any(part => part.Equals("node_modules", StringComparison.OrdinalIgnoreCase));

/// <summary>
/// Prints the compiler's collected non-fatal warnings to stderr. They are collected on the
/// compiler (not written inside Compilation/) so embedders observe them and compiler chatter
/// can never interleave with a compiled program's stdout.
/// </summary>
static void PrintCompilerWarnings(ILCompiler compiler)
{
    foreach (var warning in compiler.Warnings)
        Console.Error.WriteLine($"Warning: {warning}");
}

/// <summary>
/// Rejects compiled features whose typed deployment capabilities require the managed compiler
/// SKU. Human-readable reason strings are diagnostic text only and never drive this decision.
/// </summary>
static void ValidateCompiledRuntimeRequirements(ILCompiler compiler)
{
    if (compiler.RequiredSharpTSRuntimeRequirements.HasFlag(
            SharpTSRuntimeRequirements.ManagedCompilerHost) &&
        !System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
    {
        throw new ManagedBuildRequiredException(
            $"compiled output requiring the managed SharpTS host " +
            $"({string.Join(", ", compiler.RequiredSharpTSRuntimeReasons)})");
    }
}

/// <summary>
/// Co-locates SharpTS.dll with the compiled output when, and only when, the compilation emitted
/// late binding into the SharpTS runtime whose normal execution needs it (eval, Proxy, Intl, vm,
/// dns, @DotNetType dynamic events). Programs that use none of these stay fully standalone — no
/// copy. <c>--standalone</c> suppresses the copy (the soft-dependent features then throw a clear
/// "not supported" error at runtime instead).
/// </summary>
[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
    "SingleFile",
    "IL3000",
    Justification = "The managed assembly location is checked for an empty bundled value; native and single-file builds extract the embedded managed runtime instead.")]
static void CopySharpTSRuntimeIfNeeded(ILCompiler compiler, string outputPath, OutputOptions outputOptions)
{
    var reasons = compiler.RequiredSharpTSRuntimeReasons;
    if (reasons.Count == 0)
        return; // fully standalone — nothing to co-locate

    string reasonList = string.Join(", ", reasons);

    if (outputOptions.Standalone)
    {
        if (!outputOptions.QuietMode)
            Console.WriteLine(
                $"Note: output uses features needing the SharpTS runtime ({reasonList}); " +
                "--standalone set, so SharpTS.dll was not copied. These features will throw at runtime unless SharpTS.dll is present.");
        return;
    }

    var outDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
    if (outDir == null)
    {
        if (!outputOptions.QuietMode)
            Console.WriteLine($"Warning: could not resolve the output directory; features ({reasonList}) may fail at runtime.");
        return;
    }

    var sharpTsPath = typeof(SharpTS.Execution.Interpreter).Assembly.Location;
    var destPath = Path.Combine(outDir, "SharpTS.dll");
    try
    {
        bool copiedFromManagedBuild = !string.IsNullOrEmpty(sharpTsPath) && File.Exists(sharpTsPath);
        if (copiedFromManagedBuild)
        {
            if (!string.Equals(Path.GetFullPath(sharpTsPath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
                File.Copy(sharpTsPath, destPath, overwrite: true);
        }
        else if (!SharpTS.Runtime.EmbeddedManagedRuntime.TryExtractTo(destPath, out string? extractionError))
        {
            // The native SKU has no Assembly.Location fallback — the embedded payload is
            // the only soft-dependency mechanism. Silently shipping an output whose
            // eval/Proxy/Intl/vm paths throw at runtime would be the one native-SKU
            // limitation that doesn't fail fast; treat it like the others instead.
            if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
            {
                Console.Error.WriteLine(
                    $"Error: the compiled output requires the SharpTS runtime ({reasonList}), but the " +
                    $"embedded SharpTS.dll could not be extracted ({extractionError}). " +
                    "The output will not run until SharpTS.dll is placed next to it.");
                throw new CompilationAbortedException();
            }
            if (!outputOptions.QuietMode)
                Console.WriteLine(
                    $"Warning: could not extract the embedded SharpTS.dll ({extractionError}); " +
                    $"features ({reasonList}) may fail at runtime.");
            return;
        }

        // child_process.fork() starts SharpTS as a separate compiler process, while
        // sharpts:execution embeds its compile-and-run facade in the current process.
        // Both execute compiler paths backed by SharpTS's managed dependency closure,
        // so co-locate the runtime files rather than copying only SharpTS.dll.
        bool requiresFullRuntime = compiler.RequiredSharpTSRuntimeRequirements.HasFlag(
            SharpTSRuntimeRequirements.FullDependencyClosure);
        if (requiresFullRuntime)
        {
            // Native builds are rejected before Save by ValidateCompiledRuntimeRequirements.
            // This closure copy is retained for the managed SKU.
            var sharpTsDir = Path.GetDirectoryName(sharpTsPath);
            if (!string.IsNullOrEmpty(sharpTsDir))
            {
                foreach (var src in Directory.EnumerateFiles(sharpTsDir))
                {
                    var name = Path.GetFileName(src);
                    var ext = Path.GetExtension(name);
                    bool isRuntimeFile = ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("SharpTS.runtimeconfig.json", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("SharpTS.deps.json", StringComparison.OrdinalIgnoreCase);
                    if (!isRuntimeFile) continue;
                    var dst = Path.Combine(outDir, name);
                    if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase))
                        continue;
                    File.Copy(src, dst, overwrite: true);
                }
            }
        }

        if (!outputOptions.QuietMode)
        {
            var what = requiresFullRuntime ? "SharpTS runtime" : "SharpTS.dll";
            var action = copiedFromManagedBuild ? "Copied" : "Extracted embedded";
            Console.WriteLine($"{action} {what} next to output — required at runtime by: {reasonList}");
        }
    }
    catch (Exception ex)
    {
        if (!outputOptions.QuietMode)
            Console.WriteLine($"Warning: failed to co-locate SharpTS.dll with output ({reasonList}): {ex.Message}");
    }
}

/// <summary>
/// Co-locates third-party reference assemblies (sharpts.json / -r) with the compiled output.
/// Unlike the SharpTS runtime soft-dependency, these are HARD metadata references — the
/// emitted IL calls into them by token, and default host probing only searches the app
/// directory. Copies only the assemblies whose types the program actually used, plus their
/// transitive dependency closure (assets-graph subtree for NuGet packages, AssemblyName walk
/// for local DLLs). <c>--standalone</c> suppresses the copy but lists what deployment needs.
/// </summary>
[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
    "SingleFile",
    "IL3000",
    Justification = "Bundled assemblies have an empty location and are explicitly skipped; only external on-disk interop assemblies participate in the deployment copy set.")]
[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
    "Trimming",
    "IL2026",
    Justification = "Native AOT rejects third-party reference loading before compilation; this dependency walk is reachable only after a managed host loaded those assemblies.")]
static void CopyExternalReferencesIfNeeded(ILCompiler compiler, ReferenceSet externalRefs, string outputPath, OutputOptions outputOptions)
{
    if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
    {
        if (compiler.ExternalInteropAssemblies.Count == 0)
            return;

        string? outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (outputDirectory is null)
            throw new InvalidOperationException($"Could not resolve the output directory for '{outputPath}'.");

        if (outputOptions.Standalone)
        {
            if (!outputOptions.QuietMode)
                Console.WriteLine(
                    "Note: output uses native-host .NET interop assemblies; --standalone set, " +
                    "so their managed payloads were not extracted.");
            return;
        }

        INativeDotNetCatalog catalog = NativeDotNetInterop.Catalog
            ?? throw new PlatformNotSupportedException(ManagedDotNetInterop.ManagedBuildRequiredMessage);
        IReadOnlyList<string> extracted = catalog.ExtractAssemblyPayloads(outputDirectory);
        if (extracted.Count > 0 && !outputOptions.QuietMode)
        {
            Console.WriteLine(
                $"Extracted {extracted.Count} native-host .NET interop " +
                $"assembl{(extracted.Count == 1 ? "y" : "ies")} next to output");
        }
        return;
    }

    if (externalRefs.IsEmpty)
        return;

    // Which reference DLLs did the compilation actually bind types from?
    var used = new List<ResolvedReference>();
    foreach (var assembly in compiler.ExternalInteropAssemblies)
    {
        string location;
        try { location = assembly.Location; } catch { continue; }
        if (string.IsNullOrEmpty(location)) continue;
        var match = externalRefs.FindByPath(Path.GetFullPath(location));
        if (match != null && !used.Contains(match)) used.Add(match);
    }
    if (used.Count == 0)
        return; // external types came only from the BCL / already-deployed assemblies

    // Loaded assemblies by full path, for the local-DLL dependency walk.
    var loadedByPath = new Dictionary<string, Assembly>(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
    {
        try
        {
            if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                loadedByPath[Path.GetFullPath(assembly.Location)] = assembly;
        }
        catch { }
    }

    var copySet = new List<string>();
    var seen = new HashSet<string>(loadedByPath.Comparer);

    void AddLocalClosure(string dllPath)
    {
        if (!seen.Add(dllPath)) return;
        copySet.Add(dllPath);

        // Dependencies resolve from the reference set (recurse) or as siblings of the
        // DLL that references them (copy). Shared-framework names match neither and
        // fall out naturally.
        if (!loadedByPath.TryGetValue(dllPath, out var assembly)) return;
        foreach (var dependency in assembly.GetReferencedAssemblies())
        {
            string fileName = dependency.Name + ".dll";
            var inSet = externalRefs.References.FirstOrDefault(r =>
                string.Equals(Path.GetFileName(r.Path), fileName, StringComparison.OrdinalIgnoreCase));
            if (inSet != null)
            {
                AddLocalClosure(inSet.Path);
                continue;
            }
            string sibling = Path.Combine(Path.GetDirectoryName(dllPath)!, fileName);
            if (File.Exists(sibling) && seen.Add(sibling))
                copySet.Add(sibling);
        }
    }

    foreach (var reference in used)
    {
        if (reference.Origin == ReferenceOrigin.Package)
        {
            foreach (var asset in externalRefs.RuntimeClosureFor(reference))
            {
                if (seen.Add(asset)) copySet.Add(asset);
            }
            // The closure always contains the package's own assets, but keep the used
            // DLL itself even if the closure lookup came up empty.
            if (seen.Add(reference.Path)) copySet.Add(reference.Path);
        }
        else
        {
            AddLocalClosure(reference.Path);
        }
    }

    string usedNames = string.Join(", ", used.Select(r => Path.GetFileNameWithoutExtension(r.Path)));

    if (outputOptions.Standalone)
    {
        if (!outputOptions.QuietMode)
            Console.WriteLine(
                $"Note: output references external .NET assemblies ({usedNames}); --standalone set, so they were " +
                "not copied. The output will not run unless these assemblies are deployed next to it: " +
                string.Join(", ", copySet.Select(Path.GetFileName)));
        return;
    }

    var outDir = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
    int copied = 0;
    foreach (var source in copySet)
    {
        var destPath = Path.Combine(outDir, Path.GetFileName(source));
        try
        {
            if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(source, destPath, overwrite: true);
                copied++;
            }
        }
        catch (Exception ex)
        {
            if (!outputOptions.QuietMode)
                Console.WriteLine($"Warning: failed to copy referenced assembly '{source}' next to output: {ex.Message}");
        }
    }

    if (copied > 0 && !outputOptions.QuietMode)
    {
        Console.WriteLine($"Copied {copied} referenced assembl{(copied == 1 ? "y" : "ies")} next to output — required by external .NET types: {usedNames}");
    }
}

static void GenerateRuntimeConfig(string outputPath)
{
    string runtimeConfigPath = Path.ChangeExtension(outputPath, ".runtimeconfig.json");
    string runtimeConfig = """
        {
          "runtimeOptions": {
            "tfm": "net10.0",
            "framework": {
              "name": "Microsoft.NETCore.App",
              "version": "10.0.0"
            }
          }
        }
        """;
    File.WriteAllText(runtimeConfigPath, runtimeConfig);
}

static void VerifyCompiledAssembly(string outputPath, string? sdkPath, ReferenceSet? externalRefs = null)
{
    // The verifier resolves against the shared-framework runtime directory; an explicit
    // --sdk-path and the directories of third-party reference assemblies (whose types the
    // emitted IL references by token) are additional probe locations.
    var probeDirs = (externalRefs?.References
        .Select(r => Path.GetDirectoryName(r.Path))
        .Where(d => !string.IsNullOrEmpty(d))
        .Cast<string>() ?? [])
        .Append(Path.GetDirectoryName(Path.GetFullPath(outputPath))!)
        .Distinct(StringComparer.OrdinalIgnoreCase);
    using var verifier = new ILVerifier(sdkPath, probeDirs);
    using var stream = File.OpenRead(outputPath);
    verifier.VerifyAndReport(stream);
}

static void CreateNuGetPackage(
    string assemblyPath,
    PackageJson? packageJson,
    PackOptions packOptions,
    ExecutionTimingCollector? timings,
    bool quietMode)
{
    var package = MeasurePhase(timings, ExecutionPhaseTiming.CreatePackage, () =>
    {
        packageJson ??= new PackageJson
        {
            Name = packOptions.PackageIdOverride,
            Version = packOptions.VersionOverride ?? "1.0.0"
        };

        var validation = PackageValidator.Validate(
            assemblyPath,
            packageJson,
            packOptions.PackageIdOverride,
            packOptions.VersionOverride);

        foreach (var warning in validation.Warnings)
            Console.WriteLine($"Warning: {warning}");

        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
                Console.WriteLine($"Error: {error}");
            throw new CompilationAbortedException();
        }

        var packager = new NuGetPackager(
            packageJson, packOptions.PackageIdOverride, packOptions.VersionOverride);
        var outputDir = Path.GetDirectoryName(assemblyPath) ?? ".";

        string? readmePath = null;
        var candidateReadme = Path.Combine(outputDir, "README.md");
        if (File.Exists(candidateReadme))
            readmePath = candidateReadme;

        var nupkgPath = packager.CreatePackage(assemblyPath, outputDir, readmePath);
        if (!quietMode)
            Console.WriteLine($"Created package: {nupkgPath}");

        var symbolPackager = new SymbolPackager(packager.PackageId, packager.Version, packageJson.Author);
        var snupkgPath = symbolPackager.CreateSymbolPackage(assemblyPath, outputDir);
        if (snupkgPath != null && !quietMode)
            Console.WriteLine($"Created symbol package: {snupkgPath}");

        return (Packager: packager, PackagePath: nupkgPath, SymbolsPath: snupkgPath);
    });

    if (!string.IsNullOrEmpty(packOptions.PushSource))
    {
        MeasurePhase(timings, ExecutionPhaseTiming.PushPackage, () =>
        {
            if (string.IsNullOrEmpty(packOptions.ApiKey))
            {
                Console.WriteLine("Error: --api-key is required when using --push.");
                throw new CompilationAbortedException();
            }

            if (!quietMode)
                Console.WriteLine($"Pushing to {packOptions.PushSource}...");
            var publisher = new NuGetPublisher(packOptions.ApiKey, packOptions.PushSource);
            var success = publisher.PushWithSymbolsAsync(
                package.PackagePath, package.SymbolsPath).GetAwaiter().GetResult();

            if (!success)
            {
                Console.WriteLine("Push failed.");
                throw new CompilationAbortedException();
            }

            if (!quietMode)
                Console.WriteLine(
                    $"Successfully pushed {package.Packager.PackageId} {package.Packager.Version}");
        });
    }
}

static void GenerateDeclarations(string typeOrAssembly, string? outputPath, bool json, IReadOnlyList<string> references)
{
    // Manifest (from CWD) + -r assemblies load first so type/namespace discovery
    // sees third-party types, not just already-loaded ones.
    LoadDotNetReferences(Environment.CurrentDirectory, references);

    try
    {
        // --gen-decl is a .NET interop *discovery* tool (issue #1194): it inspects a type,
        // namespace, or assembly and reports which members are usable from TypeScript interop,
        // with the `dotnet:` import line for usable types. It no longer emits pasteable TS
        // source (which was lossy for Span<T>/ref/pointer surfaces — see #1193).
        var generator = new DiscoveryGenerator();
        var report = generator.Generate(typeOrAssembly);
        string result = json ? DiscoveryEmitter.EmitJson(report) : DiscoveryEmitter.EmitText(report);

        // Output to file or console
        if (outputPath != null)
        {
            File.WriteAllText(outputPath, result);
            Console.WriteLine($"Wrote discovery report: {outputPath}");
        }
        else
        {
            Console.WriteLine(result);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
        Environment.Exit(1);
    }
}

static string GetVersion()
{
    var assembly = typeof(Program).Assembly;
    var infoVersion = assembly.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    if (infoVersion != null)
    {
        // Strip build metadata (everything after +) if present
        var plusIndex = infoVersion.IndexOf('+');
        return plusIndex >= 0 ? infoVersion[..plusIndex] : infoVersion;
    }
    return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}

static void PrintBanner()
{
    Console.WriteLine("""
      ____  _                      _____ ____
     / ___|| |__   __ _ _ __ _ __ |_   _/ ___|
     \___ \| '_ \ / _` | '__| '_ \  | | \___ \
      ___) | | | | (_| | |  | |_) | | |  ___) |
     |____/|_| |_|\__,_|_|  | .__/  |_| |____/
                            |_|
    """);
    Console.WriteLine($"    v{GetVersion()} - TypeScript interpreter and compiler for .NET");
    Console.WriteLine();
}

static void PrintHelp()
{
    PrintBanner();
    Console.WriteLine("Usage:");
    Console.WriteLine("  sharpts [options] [script.ts] [args...]");
    Console.WriteLine("  sharpts [options] script.ts -- [script-args...]");
    Console.WriteLine("  sharpts -p <tsconfig> [--watch] [--incremental]");
    Console.WriteLine("  sharpts --build [project ...] [--watch] [--force]");
    Console.WriteLine("  sharpts --compile <script.ts> [compile-options]");
    Console.WriteLine("  sharpts new avalonia -n <name> [-o directory] [--sdk-version version]");
    Console.WriteLine("  sharpts app run [entry.tsx] [--host avalonia|console] [--mode mode] [-- args]");
    Console.WriteLine("  sharpts app build [entry.tsx] [--host avalonia|console]");
    Console.WriteLine("  sharpts app publish [entry.tsx] [--rid rid] [--self-contained true|false]");
    Console.WriteLine("                      [--single-file true|false] [-o directory]");
    Console.WriteLine("  sharpts --gen-decl <TypeName|Namespace|AssemblyPath> [--json] [-o output.txt]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -h, --help                    Show this help message");
    Console.WriteLine("  -v, --version                 Show version information");
    Console.WriteLine("  --experimentalDecorators      Enable Legacy (Stage 2) decorators");
    Console.WriteLine("  --decorators                  Enable TC39 Stage 3 decorators");
    Console.WriteLine("  --emitDecoratorMetadata       Emit design-time type metadata");
    Console.WriteLine("  -r, --reference <asm.dll>     Add a .NET assembly reference (repeatable; all modes)");
    Console.WriteLine("  --checkJs                     Type-check .js/.cjs/.mjs/.jsx files too");
    Console.WriteLine("  --noEmit                      Type-check only; don't run or emit an assembly");
    Console.WriteLine("  --declaration                 Emit .d.ts declarations for TypeScript sources");
    Console.WriteLine("  --emitDeclarationOnly         Emit declarations without a .NET assembly");
    Console.WriteLine("  --declarationDir <path>       Directory for generated declarations");
    Console.WriteLine();
    Console.WriteLine("Type Checking (tsc-compatible; all accept =false, e.g. --strict=false):");
    Console.WriteLine("  --strict                      Enable the TypeScript strictness umbrella");
    Console.WriteLine("  --strictNullChecks            null/undefined are not assignable to other types");
    Console.WriteLine("                                (SharpTS default: on)");
    Console.WriteLine("  --strictFunctionTypes         Compare function parameters contravariantly");
    Console.WriteLine("                                (SharpTS default: off)");
    Console.WriteLine("  --noImplicitAny               Report unannotated parameters of declared");
    Console.WriteLine("                                functions, methods and constructors");
    Console.WriteLine("  --noImplicitThis              Report untyped this expressions");
    Console.WriteLine("  --strictPropertyInitialization Require class fields to be initialized");
    Console.WriteLine("  --exactOptionalPropertyTypes  Keep optional properties distinct from | undefined");
    Console.WriteLine("  --noUncheckedIndexedAccess    Add undefined to unchecked indexed access");
    Console.WriteLine("                                (SharpTS default: off)");
    Console.WriteLine("  --lib <names>                 Comma-separated TypeScript declaration libraries");
    Console.WriteLine("  --noLib                       Do not load the default declaration library");
    Console.WriteLine("  --types <names>               Comma-separated ambient type packages");
    Console.WriteLine("  --typeRoots <paths>           Comma-separated ambient package roots");
    Console.WriteLine();
    Console.WriteLine("JSX (.tsx files):");
    Console.WriteLine("  --jsx <mode>                  react-jsx (default), react-jsxdev, react, or none");
    Console.WriteLine("                                (none = tsc's error-without---jsx behavior, TS17004)");
    Console.WriteLine("  --jsxFactory <expr>           Classic-mode factory (default React.createElement)");
    Console.WriteLine("  --jsxFragmentFactory <expr>   Classic-mode fragment (default React.Fragment)");
    Console.WriteLine("  --jsxImportSource <pkg>       Automatic-mode runtime package (default react)");
    Console.WriteLine();
    Console.WriteLine("Configuration (tsconfig.json):");
    Console.WriteLine("  -p, --project <path>          Use this tsconfig.json (file or directory)");
    Console.WriteLine("  -b, --build [project ...]     Check project references in dependency order");
    Console.WriteLine("  -w, --watch                   Recheck a project graph when inputs change");
    Console.WriteLine("  --incremental                 Reuse SharpTS build state when inputs are unchanged");
    Console.WriteLine("  --force                       Ignore build state and check every project");
    Console.WriteLine("  --no-tsconfig                 Skip tsconfig.json discovery entirely");
    Console.WriteLine("  --showConfig                  Print the resolved config as JSON and exit");
    Console.WriteLine();
    Console.WriteLine("  A tsconfig.json next to (or above) the entry script is discovered");
    Console.WriteLine("  automatically. Command-line flags win over it. 'extends' chains are");
    Console.WriteLine("  followed. With no script, -p checks the roots selected by files/include/");
    Console.WriteLine("  exclude. Project references, incremental/watch builds, lib/types/typeRoots,");
    Console.WriteLine("  baseUrl/paths, and classic/node10/node16/nodenext/bundler resolution are");
    Console.WriteLine("  supported. lib/noLib/types/typeRoots select declaration inputs.");
    Console.WriteLine("  jsx/jsxFactory/jsxFragmentFactory/jsxImportSource are honored for .tsx;");
    Console.WriteLine("  target/module emit settings do not apply to .NET IL output.");
    Console.WriteLine("  declaration, emitDeclarationOnly, declarationDir, rootDir, and outDir");
    Console.WriteLine("  control .d.ts output for --compile and project commands.");
    Console.WriteLine("  Set SHARPTS_TSCONFIG_VERBOSE=1 to list every option that was ignored.");
    Console.WriteLine("  Script and --compile commands still use their named file as the runtime");
    Console.WriteLine("  entry point; project commands emit no runtime assembly but may emit declarations.");
    Console.WriteLine();
    Console.WriteLine(".NET References:");
    Console.WriteLine("  A sharpts.json next to (or above) the entry script supplies project-level");
    Console.WriteLine("  references for dotnet: imports and @DotNetType:");
    Console.WriteLine("    { \"references\": [\"./libs/MyLib.dll\"], \"packages\": { \"Some.Package\": \"1.2.3\" } }");
    Console.WriteLine("  Packages restore via 'dotnet restore' into the global NuGet cache (.sharpts/");
    Console.WriteLine("  holds the restore cache; add it to .gitignore).");
    Console.WriteLine();
    Console.WriteLine("Script Arguments:");
    Console.WriteLine("  Arguments after script.ts are passed to process.argv");
    Console.WriteLine("  Use -- separator when script args conflict with SharpTS flags");
    Console.WriteLine("  process.argv format: [runtime_path, script_path, ...user_args]");
    Console.WriteLine();
    Console.WriteLine("Compile Options:");
    Console.WriteLine("  -c, --compile <file.ts>       Compile TypeScript to .NET assembly");
    Console.WriteLine("  -o <path>                     Output file path (default: <input>.dll or .exe)");
    Console.WriteLine("  -t, --target <type>           Output type: dll (default) or exe");
    Console.WriteLine("  --bundler <mode>              Bundler selection: auto (default), sdk, or builtin");
    Console.WriteLine("  --preserveConstEnums          Preserve const enum declarations");
    Console.WriteLine("  --declaration                 Emit .d.ts declarations alongside the assembly");
    Console.WriteLine("  --emitDeclarationOnly         Emit declarations without a .NET assembly");
    Console.WriteLine("  --declarationDir <path>       Directory for generated declarations");
    Console.WriteLine("  --ref-asm                     Emit reference-assembly-compatible output");
    Console.WriteLine("  --sdk-path <path>             Path to .NET SDK reference assemblies");
    Console.WriteLine("  --verify                      Verify emitted IL");
    Console.WriteLine("  -g, --debug                   Emit a portable PDB for TypeScript-source debugging");
    Console.WriteLine("  --msbuild-errors              Output errors in MSBuild format");
    Console.WriteLine("  --quiet                       Suppress success messages");
    Console.WriteLine("  --timings                     Print compilation timings to stderr");
    Console.WriteLine("  --timings-json                Print compilation timings as JSON to stdout");
    Console.WriteLine();
    Console.WriteLine("Packaging Options:");
    Console.WriteLine("  --pack                        Generate NuGet package");
    Console.WriteLine("  --push <source>               Push to NuGet feed (implies --pack)");
    Console.WriteLine("  --api-key <key>               NuGet API key for push");
    Console.WriteLine("  --package-id <id>             Override package ID");
    Console.WriteLine("  --version <ver>               Override package version");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  sharpts                           Start REPL");
    Console.WriteLine("  sharpts script.ts                 Run TypeScript file");
    Console.WriteLine("  sharpts script.ts arg1 arg2       Run script with arguments");
    Console.WriteLine("  sharpts script.ts -- --flag val   Pass flags to script (use -- separator)");
    Console.WriteLine("  sharpts --compile app.ts          Compile to app.dll");
    Console.WriteLine("  sharpts --compile lib.ts --emitDeclarationOnly --declarationDir types");
    Console.WriteLine("  sharpts -p .                      Check every tsconfig project root");
    Console.WriteLine("  sharpts --build                   Check referenced projects incrementally");
    Console.WriteLine("  sharpts --build --watch           Keep the project graph up to date");
    Console.WriteLine("  sharpts --compile app.ts -t exe   Compile to executable");
    Console.WriteLine("  sharpts --compile app.ts --pack   Compile and create NuGet package");
    Console.WriteLine("  sharpts --gen-decl System.Text.StringBuilder   Inspect a .NET type for interop");
    Console.WriteLine("  sharpts --gen-decl System.Text     List the interop-usable types in a namespace");
}

static void PrintCompileUsage()
{
    Console.WriteLine();
    Console.WriteLine("Usage: sharpts --compile <file.ts> [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -o <path>              Output file path (default: <input>.dll or .exe)");
    Console.WriteLine("  -t, --target <type>    Output type: dll (default) or exe");
    Console.WriteLine("  --bundler <mode>       Bundler selection: auto (default), sdk, or builtin");
    Console.WriteLine("  -r, --reference <dll>  Add assembly reference (repeatable)");
    Console.WriteLine("  --preserveConstEnums   Preserve const enum declarations");
    Console.WriteLine("  --declaration          Emit .d.ts declarations alongside the assembly");
    Console.WriteLine("  --emitDeclarationOnly  Emit declarations without a .NET assembly");
    Console.WriteLine("  --declarationDir <dir> Declaration output directory");
    Console.WriteLine("  --ref-asm              Emit reference-assembly-compatible output");
    Console.WriteLine("  --sdk-path <path>      Path to .NET SDK reference assemblies");
    Console.WriteLine("  --verify               Verify emitted IL");
    Console.WriteLine("  -g, --debug            Emit a portable PDB for TypeScript-source debugging");
    Console.WriteLine("  --msbuild-errors       Output errors in MSBuild format");
    Console.WriteLine("  --quiet                Suppress success messages");
    Console.WriteLine("  --timings              Print compilation timings to stderr");
    Console.WriteLine("  --timings-json         Print compilation timings as JSON to stdout");
    Console.WriteLine("  --pack                 Generate NuGet package");
    Console.WriteLine("  --push <source>        Push to NuGet feed (implies --pack)");
    Console.WriteLine("  --api-key <key>        NuGet API key for push");
    Console.WriteLine("  --package-id <id>      Override package ID");
    Console.WriteLine("  --version <ver>        Override package version");
}

private sealed class CompilationAbortedException : Exception;

record OutputOptions(bool MsBuildErrors, bool QuietMode, bool Standalone = false, bool EmitDebugSymbols = false);
}
