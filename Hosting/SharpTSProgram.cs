using System.Diagnostics.CodeAnalysis;
using SharpTS.Configuration;
using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.References;
using SharpTS.TypeSystem;

namespace SharpTS.Hosting;

[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public sealed record SharpTSProgramLoadOptions
{
    public string? TsConfigPath { get; init; }
    public bool DiscoverTsConfig { get; init; } = true;
    public IReadOnlyList<string> References { get; init; } = [];
    public DecoratorMode? DecoratorMode { get; init; }
    public StrictnessOptions? Strictness { get; init; }
    public TypeCheckerOptions? TypeCheckerOptions { get; init; }
    public TypeScriptProgramOptions? TypeScriptProgramOptions { get; init; }
    public JsxParseOptions? JsxOptions { get; init; }
}

[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public sealed class SharpTSProgram
{
    internal SharpTSProgram(
        string entryPath,
        TsConfigResult? configuration,
        DecoratorMode decoratorMode,
        ReferenceSet references,
        ModuleResolver resolver,
        List<ParsedModule> runtimeModules,
        List<ParsedModule> typeModules,
        TypeMap typeMap,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        EntryPath = entryPath;
        Configuration = configuration;
        DecoratorMode = decoratorMode;
        References = references;
        Resolver = resolver;
        RuntimeModules = runtimeModules;
        TypeModules = typeModules;
        TypeMap = typeMap;
        Diagnostics = diagnostics;
    }

    public string EntryPath { get; }
    public TsConfigResult? Configuration { get; }
    public DecoratorMode DecoratorMode { get; }
    public ReferenceSet References { get; }
    public ModuleResolver Resolver { get; }
    public IReadOnlyList<ParsedModule> RuntimeModules { get; }
    public IReadOnlyList<ParsedModule> TypeModules { get; }
    public TypeMap TypeMap { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
}

[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public sealed class SharpTSProgramLoadException : Exception
{
    public SharpTSProgramLoadException(IReadOnlyList<Diagnostic> diagnostics)
        : base(BuildMessage(diagnostics))
    {
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    private static string BuildMessage(IReadOnlyList<Diagnostic> diagnostics) =>
        "SharpTS program preparation failed:" + Environment.NewLine +
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic => $"Error: {diagnostic}"));
}

[Experimental(SharpTSHostingDiagnostics.ExperimentalId)]
public static class SharpTSProgramLoader
{
    public static SharpTSProgram Load(string entryPath, SharpTSProgramLoadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPath);
        options ??= new SharpTSProgramLoadOptions();

        string absolutePath = Path.GetFullPath(entryPath);
        string startDirectory = Path.GetDirectoryName(absolutePath) ?? Directory.GetCurrentDirectory();
        TsConfigResult? configuration = LoadConfiguration(startDirectory, options);
        DecoratorMode decoratorMode = options.DecoratorMode
            ?? configuration?.DecoratorMode
            ?? DecoratorMode.Stage3;

        var references = DotNetReferences.Load(startDirectory, options.References);
        var resolver = new ModuleResolver(
            absolutePath,
            configuration?.ModuleResolution ?? ModuleResolutionOptions.Default,
            virtualFiles: null,
            options.TypeScriptProgramOptions ?? CreateProgramOptions(configuration))
        {
            JsxOptions = options.JsxOptions ?? CreateJsxOptions(configuration),
        };

        var declarationModules = (configuration?.DeclarationFiles ?? [])
            .Select(path => resolver.LoadModule(path, decoratorMode))
            .ToArray();
        resolver.RegisterAmbientModuleDeclarations(declarationModules);

        var entryModule = resolver.LoadProgram(absolutePath, decoratorMode);
        var runtimeModules = resolver.GetRuntimeModulesInOrder(entryModule);
        var typeModules = resolver.GetModulesInOrder(declarationModules.Append(entryModule));
        var checker = new TypeChecker(
            options.TypeCheckerOptions ?? StrictnessOptions.Resolve(options.Strictness, configuration?.Strictness));
        checker.SetDecoratorMode(decoratorMode);
        checker.EnableHostedTopLevelAwait();
        TypeMap typeMap = checker.CheckModules(typeModules, resolver);
        Diagnostic[] diagnostics = checker.GetDiagnostics().ToArray();
        Diagnostic[] errors = diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length != 0)
            throw new SharpTSProgramLoadException(errors);

        return new SharpTSProgram(
            absolutePath,
            configuration,
            decoratorMode,
            references,
            resolver,
            runtimeModules,
            typeModules,
            typeMap,
            diagnostics);
    }

    private static TsConfigResult? LoadConfiguration(
        string startDirectory,
        SharpTSProgramLoadOptions options)
    {
        if (options.TsConfigPath != null)
            return TsConfigLoader.Load(TsConfigLoader.ResolveProjectPath(options.TsConfigPath));
        return options.DiscoverTsConfig ? TsConfigLoader.FindAndLoad(startDirectory) : null;
    }

    private static TypeScriptProgramOptions CreateProgramOptions(TsConfigResult? configuration) => new()
    {
        LoadDefaultLib = true,
        NoLib = configuration?.NoLib ?? false,
        Lib = configuration?.Lib,
        Types = configuration?.Types,
        TypeRoots = configuration?.TypeRoots,
        PreferDeclarationFiles = true,
    };

    private static JsxParseOptions CreateJsxOptions(TsConfigResult? configuration) => new(
        configuration?.Jsx ?? JsxMode.ReactJsx,
        configuration?.JsxFactory ?? "React.createElement",
        configuration?.JsxFragmentFactory ?? "React.Fragment",
        configuration?.JsxImportSource ?? "react");
}
