// =============================================================================
// CommandLineParser.cs - Command-line argument parsing for SharpTS
// =============================================================================
//
// Extracts and validates command-line arguments into strongly-typed commands.
// Follows the discriminated union pattern (like TypeInfo) for clean pattern matching.
//
// Usage:
//   var parser = new CommandLineParser();
//   var command = parser.Parse(args);
//   switch (command) { case ParsedCommand.Help: ... }
//
// See also: Program.cs
// =============================================================================

using SharpTS.Compilation;
using PEPacker.Bundling;
using SharpTS.Configuration;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Cli;

/// <summary>
/// Global options that apply across all execution modes.
/// </summary>
/// <param name="DecoratorMode">Decorator parsing mode (None, Legacy, Stage3). Defaults to Stage3.</param>
/// <param name="EmitDecoratorMetadata">Whether to emit design-time type metadata</param>
/// <param name="CheckJs">When true, type-check `.js`/`.cjs`/`.mjs`/`.jsx` files like `.ts`. Mirrors tsc's `checkJs` tsconfig option. Defaults to false (matches tsc).</param>
/// <param name="References">Assembly references from -r/--reference, applied in every mode
/// (run, compile, --gen-decl, REPL) on top of any sharpts.json manifest. Paths resolve
/// against the current working directory.</param>
/// <param name="Strictness">
/// Raw, per-flag record of the strictness flags the command line carried. Nullable per key so
/// the tsconfig layer can tell "explicitly false" from "absent". Fold it with
/// <see cref="Configuration.StrictnessOptions.Resolve"/> to get the checker's options.
/// </param>
/// <param name="NoEmit">
/// tsc's <c>--noEmit</c>: type-check only, then stop. CLI-only — deliberately not read from
/// tsconfig.json, where <c>"noEmit": true</c> is common in bundler setups and would silently
/// stop <c>sharpts app.ts</c> from running the program.
/// </param>
/// <param name="ProjectPath">
/// <c>-p</c>/<c>--project</c>: an explicit tsconfig.json file or a directory containing one.
/// Suppresses the upward walk — an explicit project that resolves to nothing must never
/// silently fall back to discovery.
/// </param>
/// <param name="NoTsConfig">
/// <c>--no-tsconfig</c>: skip tsconfig.json discovery entirely. SharpTS-specific (hence
/// kebab-case). The MSBuild SDK passes it so MSBuild stays the single source of truth for SDK
/// builds, and it makes CI runs immune to an ambient tsconfig.json further up the tree.
/// </param>
/// <param name="ShowConfig">
/// <c>--showConfig</c>: print the resolved configuration, with the source of each value, then
/// exit 0.
/// </param>
/// <param name="Watch">Recheck a project when its inputs change.</param>
/// <param name="Incremental">Reuse SharpTS project build state when inputs are unchanged.</param>
/// <param name="Force">Ignore build state and check every project.</param>
/// <param name="Declaration">Emit TypeScript declaration files for project-owned sources.</param>
/// <param name="EmitDeclarationOnly">Emit declarations without running or producing a .NET assembly.</param>
/// <param name="DeclarationDir">Optional root directory for generated declaration files.</param>
/// <param name="Lib">TypeScript declaration libraries to load.</param>
/// <param name="NoLib">Suppresses the default TypeScript declaration library.</param>
/// <param name="Types">Ambient type packages to include.</param>
/// <param name="TypeRoots">Directories containing ambient type packages.</param>
/// <param name="Jsx">JSX transform mode; null = not set on the CLI (tsconfig, then default, applies).</param>
/// <param name="JsxFactory">Classic-mode JSX factory expression.</param>
/// <param name="JsxFragmentFactory">Classic-mode JSX fragment expression.</param>
/// <param name="JsxImportSource">Automatic-mode package to import the JSX runtime from.</param>
public record GlobalOptions(
    DecoratorMode DecoratorMode = DecoratorMode.Stage3,
    bool EmitDecoratorMetadata = false,
    bool CheckJs = false,
    IReadOnlyList<string>? References = null,
    StrictnessOptions? Strictness = null,
    bool NoEmit = false,
    string? ProjectPath = null,
    bool NoTsConfig = false,
    bool ShowConfig = false,
    bool Watch = false,
    bool Incremental = false,
    bool Force = false,
    bool Declaration = false,
    bool EmitDeclarationOnly = false,
    string? DeclarationDir = null,
    IReadOnlyList<string>? Lib = null,
    bool? NoLib = null,
    IReadOnlyList<string>? Types = null,
    IReadOnlyList<string>? TypeRoots = null,
    JsxMode? Jsx = null,
    string? JsxFactory = null,
    string? JsxFragmentFactory = null,
    string? JsxImportSource = null
)
{
    public IReadOnlyList<string> References { get; init; } = References ?? [];

    public StrictnessOptions Strictness { get; init; } = Strictness ?? new StrictnessOptions();

    /// <summary>
    /// The checker options implied by the command line alone (no tsconfig layer). Program.cs
    /// re-resolves with the discovered tsconfig; this keeps direct consumers correct meanwhile.
    /// </summary>
    public TypeCheckerOptions TypeCheckerOptions => StrictnessOptions.Resolve(Strictness, null);

    /// <summary>
    /// The parser-facing JSX settings after applying SharpTS defaults (automatic runtime,
    /// React factories, "react" import source). Only consulted for .tsx/.jsx sources.
    /// </summary>
    public JsxParseOptions ResolvedJsxOptions => new(
        Jsx ?? JsxMode.ReactJsx,
        JsxFactory ?? "React.createElement",
        JsxFragmentFactory ?? "React.Fragment",
        JsxImportSource ?? "react");

    public TypeScriptProgramOptions TypeScriptProgramOptions => new()
    {
        LoadDefaultLib = true,
        NoLib = NoLib ?? false,
        Lib = Lib,
        Types = Types,
        TypeRoots = TypeRoots,
        PreferDeclarationFiles = true,
    };
}

/// <summary>
/// Options specific to compilation mode.
/// </summary>
/// <param name="Target">Output type: DLL or EXE</param>
/// <param name="PreserveConstEnums">Preserve const enum declarations</param>
/// <param name="UseReferenceAssemblies">Emit reference-assembly-compatible output</param>
/// <param name="SdkPath">Explicit path to .NET SDK reference assemblies</param>
/// <param name="VerifyIL">Verify emitted IL using Microsoft.ILVerification</param>
/// <param name="MsBuildErrors">Output errors in MSBuild format</param>
/// <param name="QuietMode">Suppress success messages</param>
/// <param name="References">Assembly references to add</param>
/// <param name="Bundler">Bundler selection mode for EXE targets</param>
/// <param name="EmitDebugSymbols">Emit a portable PDB beside the assembly (<c>--debug</c>/<c>-g</c>)</param>
public record CompileOptions(
    OutputTarget Target = OutputTarget.Dll,
    bool PreserveConstEnums = false,
    bool UseReferenceAssemblies = false,
    string? SdkPath = null,
    bool VerifyIL = false,
    bool MsBuildErrors = false,
    bool QuietMode = false,
    IReadOnlyList<string>? References = null,
    BundlerMode Bundler = BundlerMode.Auto,
    bool Standalone = false,
    bool EmitDebugSymbols = false
)
{
    public IReadOnlyList<string> References { get; init; } = References ?? [];
}

/// <summary>
/// Options specific to NuGet packaging.
/// </summary>
/// <param name="Pack">Generate NuGet package</param>
/// <param name="PushSource">NuGet feed URL for push (implies Pack)</param>
/// <param name="ApiKey">NuGet API key for push</param>
/// <param name="PackageIdOverride">Override package ID</param>
/// <param name="VersionOverride">Override package version</param>
public record PackOptions(
    bool Pack = false,
    string? PushSource = null,
    string? ApiKey = null,
    string? PackageIdOverride = null,
    string? VersionOverride = null
);

/// <summary>
/// Base record for parsed command-line commands.
/// Uses discriminated union pattern for clean switch expression handling.
/// </summary>
public abstract record ParsedCommand
{
    /// <summary>Display help message and exit.</summary>
    public sealed record Help() : ParsedCommand;

    /// <summary>Display version and exit.</summary>
    public sealed record Version() : ParsedCommand;

    /// <summary>Start interactive REPL mode.</summary>
    /// <param name="Options">Global options for the session</param>
    public sealed record Repl(GlobalOptions Options) : ParsedCommand;

    /// <summary>Type-check every root selected by a tsconfig project.</summary>
    public sealed record Project(GlobalOptions Options) : ParsedCommand;

    /// <summary>Type-check one or more project-reference graphs in dependency order.</summary>
    public sealed record Build(IReadOnlyList<string> ProjectPaths, GlobalOptions Options) : ParsedCommand;

    /// <summary>Execute a TypeScript file with optional arguments.</summary>
    /// <param name="ScriptPath">Path to the TypeScript file</param>
    /// <param name="ScriptArgs">Arguments passed to the script (process.argv)</param>
    /// <param name="Options">Global options for execution</param>
    public sealed record Script(string ScriptPath, string[] ScriptArgs, GlobalOptions Options) : ParsedCommand;

    /// <summary>Compile a TypeScript file to a .NET assembly.</summary>
    /// <param name="InputFile">Path to the TypeScript file</param>
    /// <param name="OutputFile">Output assembly path</param>
    /// <param name="CompileOptions">Compilation-specific options</param>
    /// <param name="PackOptions">Packaging-specific options</param>
    /// <param name="GlobalOptions">Global options</param>
    public sealed record Compile(
        string InputFile,
        string OutputFile,
        CompileOptions CompileOptions,
        PackOptions PackOptions,
        GlobalOptions GlobalOptions
    ) : ParsedCommand;

    /// <summary>Inspect a .NET type, namespace, or assembly for interop discovery (issue #1194).</summary>
    /// <param name="TypeOrAssembly">Type name, namespace, or assembly path to inspect</param>
    /// <param name="OutputPath">Optional output file path</param>
    /// <param name="Json">Emit machine-readable JSON instead of human-readable text</param>
    /// <param name="References">Assembly references (-r) to load before discovery</param>
    public sealed record GenDecl(
        string TypeOrAssembly,
        string? OutputPath,
        bool Json = false,
        IReadOnlyList<string>? References = null
    ) : ParsedCommand
    {
        public IReadOnlyList<string> References { get; init; } = References ?? [];
    }

    /// <summary>Parsing error with message and exit code.</summary>
    /// <param name="Message">Error message to display</param>
    /// <param name="ExitCode">Process exit code</param>
    /// <param name="ShowCompileUsage">Whether to show compile usage after error</param>
    public sealed record Error(string Message, int ExitCode, bool ShowCompileUsage = false) : ParsedCommand;
}

/// <summary>
/// Parses command-line arguments into strongly-typed commands.
/// </summary>
public class CommandLineParser
{
    /// <summary>
    /// Parses command-line arguments into a ParsedCommand.
    /// </summary>
    /// <param name="args">Command-line arguments</param>
    /// <returns>Parsed command for execution</returns>
    public ParsedCommand Parse(string[] args)
    {
        // Handle --help and --version first
        if (args.Length > 0)
        {
            if (args[0] is "--help" or "-h")
                return new ParsedCommand.Help();
            if (args[0] is "--version" or "-v")
                return new ParsedCommand.Version();
        }

        // Parse global options that apply to all modes
        var (globalOptions, remainingArgs, scriptArgs, globalError) = ParseGlobalOptions(args);
        if (globalError is not null)
            return globalError;
        if (globalOptions.ProjectPath is not null && globalOptions.NoTsConfig)
        {
            return new ParsedCommand.Error(
                "Error: --project cannot be combined with --no-tsconfig.",
                64);
        }
        if (globalOptions.NoEmit && globalOptions.EmitDeclarationOnly)
        {
            return new ParsedCommand.Error(
                "Error: --noEmit cannot be combined with --emitDeclarationOnly.",
                64);
        }

        if (remainingArgs.Length == 0)
        {
            if (globalOptions.ProjectPath is not null)
                return new ParsedCommand.Project(globalOptions);
            if (globalOptions.Declaration || globalOptions.EmitDeclarationOnly ||
                globalOptions.DeclarationDir is not null)
            {
                return new ParsedCommand.Error(
                    "Error: declaration options require --compile or -p/--project.",
                    64);
            }
            if (globalOptions.Watch || globalOptions.Incremental || globalOptions.Force)
            {
                return new ParsedCommand.Error(
                    "Error: --watch, --incremental, and --force require -p/--project or --build.",
                    64);
            }
            return new ParsedCommand.Repl(globalOptions);
        }

        // Handle project-reference build mode.
        if (remainingArgs[0] is "--build" or "-b")
        {
            if (globalOptions.ProjectPath is not null)
                return new ParsedCommand.Error("Error: --build cannot be combined with --project.", 64);
            var projects = remainingArgs.Skip(1).ToArray();
            if (projects.Any(path => path.StartsWith('-')))
                return new ParsedCommand.Error($"Error: Unknown build option '{projects.First(path => path.StartsWith('-'))}'.", 64);
            return new ParsedCommand.Build(projects.Length == 0 ? ["."] : projects, globalOptions);
        }

        if (globalOptions.Watch || globalOptions.Incremental || globalOptions.Force)
        {
            return new ParsedCommand.Error(
                "Error: --watch, --incremental, and --force apply to a project command or --build, not a script/compile command.",
                64);
        }

        // Handle --compile / -c
        if (remainingArgs[0] is "--compile" or "-c")
        {
            return ParseCompileCommand(remainingArgs, globalOptions);
        }

        // Handle --gen-decl
        if (remainingArgs[0] == "--gen-decl")
        {
            return ParseGenDeclCommand(remainingArgs, globalOptions);
        }

        if (globalOptions.Declaration || globalOptions.EmitDeclarationOnly ||
            globalOptions.DeclarationDir is not null)
        {
            return new ParsedCommand.Error(
                "Error: declaration options require --compile or -p/--project.",
                64);
        }

        // Handle script execution
        if (remainingArgs.Length >= 1)
        {
            // Check if it looks like an unknown flag
            if (remainingArgs[0].StartsWith('-'))
            {
                return new ParsedCommand.Error(
                    $"Error: Unknown option '{remainingArgs[0]}'\n\nUse 'sharpts --help' for usage information.",
                    64
                );
            }

            // First arg is script path, rest are script arguments
            string scriptPath = remainingArgs[0];

            // Combine any additional args after script name with args after -- separator
            string[] allScriptArgs;
            if (remainingArgs.Length > 1)
            {
                var extraArgs = remainingArgs[1..];
                allScriptArgs = [.. extraArgs, .. scriptArgs];
            }
            else
            {
                allScriptArgs = scriptArgs;
            }

            return new ParsedCommand.Script(scriptPath, allScriptArgs, globalOptions);
        }

        return new ParsedCommand.Error(
            "Usage: sharpts [script] [args...]\n" +
            "       sharpts --compile <script.ts> [-o output.dll]\n" +
            "       sharpts --gen-decl <TypeName|Namespace|AssemblyPath> [--json] [-o output.txt]",
            64
        );
    }

    /// <summary>
    /// Splits <c>--flag=value</c> into its parts on the FIRST '='. <c>--flag</c> yields a null
    /// value; the caller decides what an absent value means.
    /// </summary>
    private static (string Name, string? Value) SplitFlag(string arg)
    {
        int eq = arg.IndexOf('=');
        return eq < 0 ? (arg, null) : (arg[..eq], arg[(eq + 1)..]);
    }

    /// <summary>
    /// Interprets a boolean flag's value. Bare <c>--flag</c> means true; tsc's explicit
    /// <c>--flag=false</c> / <c>--flag=true</c> are the only negation form (tsc has no
    /// <c>--no*</c> prefixes, so SharpTS invents none).
    /// </summary>
    /// <returns>False when the value was present but not a boolean literal.</returns>
    private static bool TryParseFlagBool(string? value, out bool result)
    {
        if (value is null) { result = true; return true; }
        if (bool.TryParse(value, out result)) return true;
        result = false;
        return false;
    }

    private static bool TryParseJsxMode(string value, out JsxMode mode)
    {
        switch (value.ToLowerInvariant())
        {
            case "react": mode = JsxMode.React; return true;
            case "react-jsx": mode = JsxMode.ReactJsx; return true;
            case "react-jsxdev": mode = JsxMode.ReactJsxDev; return true;
            case "none": mode = JsxMode.None; return true;
            default: mode = default; return false;
        }
    }

    private static (GlobalOptions options, string[] remainingArgs, string[] scriptArgs, ParsedCommand.Error? error)
        ParseGlobalOptions(string[] args)
    {
        var decoratorMode = DecoratorMode.Stage3;  // Stage3 decorators enabled by default
        var emitDecoratorMetadata = false;
        var checkJs = false;  // Match tsc default: don't type-check .js files unless asked
        var noEmit = false;
        var noTsConfig = false;
        var showConfig = false;
        var watch = false;
        var incremental = false;
        var force = false;
        var declaration = false;
        var emitDeclarationOnly = false;
        string? declarationDir = null;
        bool? noLib = null;
        string? projectPath = null;
        IReadOnlyList<string>? lib = null, types = null, typeRoots = null;
        JsxMode? jsx = null;
        string? jsxFactory = null, jsxFragmentFactory = null, jsxImportSource = null;
        var strictness = new StrictnessOptions();
        List<string> references = [];
        List<string> remaining = [];
        List<string> scriptArgs = [];

        // Check for -- separator which indicates everything after is script args
        int doubleDashIndex = Array.IndexOf(args, "--");

        // If -- found, everything after it goes to scriptArgs
        if (doubleDashIndex >= 0)
        {
            for (int i = doubleDashIndex + 1; i < args.Length; i++)
            {
                scriptArgs.Add(args[i]);
            }
            // Process only args before --
            args = args[..doubleDashIndex];
        }

        ParsedCommand.Error? BadBool(string name, string? value) => new(
            $"Error: '{name}' expects 'true' or 'false', got '{value}'.\n\n" +
            "Use 'sharpts --help' for usage information.",
            64);

        for (int i = 0; i < args.Length; i++)
        {
            var (name, value) = SplitFlag(args[i]);
            bool flag;

            switch (name)
            {
                case "--experimentalDecorators":
                    decoratorMode = DecoratorMode.Legacy;
                    break;
                // Documented in --help and emitted by the MSBuild SDK (Sdk.targets), but until
                // now unhandled here: run mode rejected it and compile mode silently dropped it.
                case "--decorators":
                    decoratorMode = DecoratorMode.Stage3;
                    break;
                case "--noDecorators":
                    decoratorMode = DecoratorMode.None;
                    break;
                case "--emitDecoratorMetadata":
                    emitDecoratorMetadata = true;
                    break;
                case "--check-js":  // legacy kebab alias; --checkJs is the documented spelling
                case "--checkJs":
                    if (!TryParseFlagBool(value, out flag)) return (default!, [], [], BadBool(name, value));
                    checkJs = flag;
                    break;
                case "--noEmit":
                    if (!TryParseFlagBool(value, out flag)) return (default!, [], [], BadBool(name, value));
                    noEmit = flag;
                    break;
                case "--declaration":
                    if (!TryParseFlagBool(value, out flag)) return (default!, [], [], BadBool(name, value));
                    declaration = flag;
                    break;
                case "--emitDeclarationOnly":
                    if (!TryParseFlagBool(value, out flag)) return (default!, [], [], BadBool(name, value));
                    emitDeclarationOnly = flag;
                    if (flag) declaration = true;
                    break;
                case "--declarationDir" when value is not null:
                    declarationDir = value;
                    break;
                case "--declarationDir" when i + 1 < args.Length:
                    declarationDir = args[++i];
                    break;
                case "--declarationDir":
                    return (default!, [], [], new ParsedCommand.Error(
                        "Error: --declarationDir requires a path.",
                        64));
                case "--noLib":
                    if (!TryParseFlagBool(value, out flag)) return (default!, [], [], BadBool(name, value));
                    noLib = flag;
                    break;
                case "--lib" when value is not null:
                    lib = SplitList(value);
                    break;
                case "--lib" when i + 1 < args.Length:
                    lib = SplitList(args[++i]);
                    break;
                case "--types" when value is not null:
                    types = SplitList(value);
                    break;
                case "--types" when i + 1 < args.Length:
                    types = SplitList(args[++i]);
                    break;
                case "--typeRoots" when value is not null:
                    typeRoots = SplitList(value);
                    break;
                case "--typeRoots" when i + 1 < args.Length:
                    typeRoots = SplitList(args[++i]);
                    break;
                case "--lib" or "--types" or "--typeRoots":
                    return (default!, [], [], new ParsedCommand.Error(
                        $"Error: {name} requires a comma-separated value.", 64));
                case "--strict":
                    if (!TryParseFlagBool(value, out flag)) return (default!, [], [], BadBool(name, value));
                    strictness = strictness with { Strict = flag };
                    break;
                case "--strictNullChecks":
                    if (!TryParseFlagBool(value, out flag)) return (default!, [], [], BadBool(name, value));
                    strictness = strictness with { StrictNullChecks = flag };
                    break;
                case "--strictFunctionTypes":
                    if (!TryParseFlagBool(value, out flag)) return (default!, [], [], BadBool(name, value));
                    strictness = strictness with { StrictFunctionTypes = flag };
                    break;
                case "--noImplicitAny":
                    if (!TryParseFlagBool(value, out flag)) return (default!, [], [], BadBool(name, value));
                    strictness = strictness with { NoImplicitAny = flag };
                    break;
                case "--noImplicitThis":
                    if (!TryParseFlagBool(value, out flag)) return (default!, [], [], BadBool(name, value));
                    strictness = strictness with { NoImplicitThis = flag };
                    break;
                case "--strictPropertyInitialization":
                    if (!TryParseFlagBool(value, out flag)) return (default!, [], [], BadBool(name, value));
                    strictness = strictness with { StrictPropertyInitialization = flag };
                    break;
                case "--exactOptionalPropertyTypes":
                    if (!TryParseFlagBool(value, out flag)) return (default!, [], [], BadBool(name, value));
                    strictness = strictness with { ExactOptionalPropertyTypes = flag };
                    break;
                case "--noUncheckedIndexedAccess":
                    if (!TryParseFlagBool(value, out flag)) return (default!, [], [], BadBool(name, value));
                    strictness = strictness with { NoUncheckedIndexedAccess = flag };
                    break;
                case "--no-tsconfig":
                    if (!TryParseFlagBool(value, out flag)) return (default!, [], [], BadBool(name, value));
                    noTsConfig = flag;
                    break;
                case "--showConfig":
                    if (!TryParseFlagBool(value, out flag)) return (default!, [], [], BadBool(name, value));
                    showConfig = flag;
                    break;
                case "-w":
                case "--watch":
                    if (!TryParseFlagBool(value, out flag)) return (default!, [], [], BadBool(name, value));
                    watch = flag;
                    break;
                case "--incremental":
                    if (!TryParseFlagBool(value, out flag)) return (default!, [], [], BadBool(name, value));
                    incremental = flag;
                    break;
                case "--force":
                    if (!TryParseFlagBool(value, out flag)) return (default!, [], [], BadBool(name, value));
                    force = flag;
                    break;
                case "-p" or "--project" when value is not null:
                    projectPath = value;
                    break;
                case "-p" or "--project" when i + 1 < args.Length:
                    projectPath = args[++i];
                    break;
                case "-p" or "--project":
                    return (default!, [], [], new ParsedCommand.Error(
                        $"Error: {name} requires a path to a tsconfig.json file or a directory containing one.",
                        64));
                case "-r" or "--reference" when value is not null:
                    references.Add(value);
                    break;
                case "-r" or "--reference" when i + 1 < args.Length:
                    references.Add(args[++i]);
                    break;
                case "--jsx" when value is not null || i + 1 < args.Length:
                    string jsxValue = value ?? args[++i];
                    if (!TryParseJsxMode(jsxValue, out var jsxMode))
                        return (default!, [], [], new ParsedCommand.Error(
                            jsxValue.ToLowerInvariant() is "preserve" or "react-native"
                                ? $"Error: --jsx {jsxValue} is not supported: SharpTS executes TypeScript " +
                                  "directly and cannot emit .jsx output. Use react-jsx, react-jsxdev, or react."
                                : $"Error: --jsx expects react-jsx, react-jsxdev, react, or none; got '{jsxValue}'.",
                            64));
                    jsx = jsxMode;
                    break;
                case "--jsx":
                    return (default!, [], [], new ParsedCommand.Error(
                        "Error: --jsx requires a mode (react-jsx, react-jsxdev, react, or none).", 64));
                case "--jsxFactory" when value is not null:
                    jsxFactory = value;
                    break;
                case "--jsxFactory" when i + 1 < args.Length:
                    jsxFactory = args[++i];
                    break;
                case "--jsxFragmentFactory" when value is not null:
                    jsxFragmentFactory = value;
                    break;
                case "--jsxFragmentFactory" when i + 1 < args.Length:
                    jsxFragmentFactory = args[++i];
                    break;
                case "--jsxImportSource" when value is not null:
                    jsxImportSource = value;
                    break;
                case "--jsxImportSource" when i + 1 < args.Length:
                    jsxImportSource = args[++i];
                    break;
                case "--jsxFactory" or "--jsxFragmentFactory" or "--jsxImportSource":
                    return (default!, [], [], new ParsedCommand.Error(
                        $"Error: {name} requires a value.", 64));
                default:
                    remaining.Add(args[i]);
                    break;
            }
        }

        var options = new GlobalOptions(
            decoratorMode, emitDecoratorMetadata, checkJs, references, strictness, noEmit,
            projectPath, noTsConfig, showConfig, watch, incremental, force,
            declaration, emitDeclarationOnly, declarationDir, lib, noLib, types, typeRoots,
            jsx, jsxFactory, jsxFragmentFactory, jsxImportSource);
        return (options, remaining.ToArray(), scriptArgs.ToArray(), null);
    }

    private static string[] SplitList(string value) =>
        value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private ParsedCommand ParseCompileCommand(string[] args, GlobalOptions globalOptions)
    {
        if (args.Length < 2)
        {
            return new ParsedCommand.Error("Error: Missing input file", 64, ShowCompileUsage: true);
        }

        string inputFile = args[1];
        OutputTarget target = OutputTarget.Dll;
        string? explicitOutput = null;
        bool preserveConstEnums = false;
        bool useReferenceAssemblies = false;
        bool verifyIL = false;
        bool emitDebugSymbols = false;
        bool msbuildErrors = false;
        bool quietMode = false;
        bool standalone = false;
        string? sdkPath = null;
        BundlerMode bundlerMode = BundlerMode.Auto;

        // Packaging options
        bool pack = false;
        string? pushSource = null;
        string? apiKey = null;
        string? packageIdOverride = null;
        string? versionOverride = null;

        // Parse remaining arguments
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "-o" && i + 1 < args.Length)
            {
                explicitOutput = args[++i];
            }
            else if (args[i] is "-t" or "--target")
            {
                if (i + 1 >= args.Length)
                {
                    return new ParsedCommand.Error(
                        $"Error: {args[i]} requires a value (dll or exe)",
                        64,
                        ShowCompileUsage: true
                    );
                }
                var targetArg = args[++i].ToLowerInvariant();
                if (targetArg == "dll")
                {
                    target = OutputTarget.Dll;
                }
                else if (targetArg == "exe")
                {
                    target = OutputTarget.Exe;
                }
                else
                {
                    return new ParsedCommand.Error(
                        $"Error: Invalid target '{targetArg}'. Use 'dll' or 'exe'.",
                        64,
                        ShowCompileUsage: true
                    );
                }
            }
            else if (args[i] == "--bundler")
            {
                if (i + 1 >= args.Length)
                {
                    return new ParsedCommand.Error(
                        "Error: --bundler requires a value (auto, sdk, or builtin)",
                        64,
                        ShowCompileUsage: true
                    );
                }
                var bundlerArg = args[++i].ToLowerInvariant();
                bundlerMode = bundlerArg switch
                {
                    "auto" => BundlerMode.Auto,
                    "sdk" => BundlerMode.Sdk,
                    "builtin" => BundlerMode.BuiltIn,
                    _ => (BundlerMode)(-1) // Signal invalid value
                };
                if ((int)bundlerMode == -1)
                {
                    return new ParsedCommand.Error(
                        $"Error: Invalid bundler '{bundlerArg}'. Use 'auto', 'sdk', or 'builtin'.",
                        64,
                        ShowCompileUsage: true
                    );
                }
            }
            else if (args[i] == "--preserveConstEnums")
            {
                preserveConstEnums = true;
            }
            else if (args[i] == "--ref-asm")
            {
                useReferenceAssemblies = true;
            }
            else if (args[i] == "--sdk-path" && i + 1 < args.Length)
            {
                sdkPath = args[++i];
            }
            else if (args[i] == "--verify")
            {
                verifyIL = true;
            }
            else if (args[i] is "--debug" or "-g")
            {
                emitDebugSymbols = true;
            }
            else if (args[i] == "--msbuild-errors")
            {
                msbuildErrors = true;
            }
            else if (args[i] == "--quiet")
            {
                quietMode = true;
            }
            else if (args[i] == "--standalone")
            {
                standalone = true;
            }
            else if (args[i] == "--pack")
            {
                pack = true;
            }
            else if (args[i] == "--push" && i + 1 < args.Length)
            {
                pushSource = args[++i];
                pack = true; // --push implies --pack
            }
            else if (args[i] == "--api-key" && i + 1 < args.Length)
            {
                apiKey = args[++i];
            }
            else if (args[i] == "--package-id" && i + 1 < args.Length)
            {
                packageIdOverride = args[++i];
            }
            else if (args[i] == "--version" && i + 1 < args.Length)
            {
                versionOverride = args[++i];
            }
            else if (args[i].StartsWith('-'))
            {
                // Previously this chain had no else, so a typo like `--verfiy` — or a real flag
                // whose value was missing — was dropped in silence while the compile proceeded.
                // Global flags are already stripped by ParseGlobalOptions, so anything reaching
                // here is genuinely unrecognized by compile mode.
                bool needsValue = args[i] is "-o" or "-t" or "--target" or "--bundler"
                    or "--sdk-path" or "--push" or "--api-key" or "--package-id" or "--version";

                return new ParsedCommand.Error(
                    needsValue
                        ? $"Error: {args[i]} requires a value"
                        : $"Error: Unknown option '{args[i]}'",
                    64,
                    ShowCompileUsage: true);
            }
        }

        if ((globalOptions.NoEmit || globalOptions.EmitDeclarationOnly) && pack)
        {
            return new ParsedCommand.Error(
                globalOptions.NoEmit
                    ? "Error: --noEmit cannot be combined with --pack/--push (there is no assembly to package)."
                    : "Error: --emitDeclarationOnly cannot be combined with --pack/--push (there is no assembly to package).",
                64,
                ShowCompileUsage: true);
        }

        // Determine output file: use explicit output if provided, otherwise derive from input + target
        string outputFile = explicitOutput ?? Path.ChangeExtension(inputFile, target == OutputTarget.Exe ? ".exe" : ".dll");

        var compileOptions = new CompileOptions(
            Target: target,
            PreserveConstEnums: preserveConstEnums,
            UseReferenceAssemblies: useReferenceAssemblies,
            SdkPath: sdkPath,
            VerifyIL: verifyIL,
            MsBuildErrors: msbuildErrors,
            QuietMode: quietMode,
            References: globalOptions.References,
            Bundler: bundlerMode,
            Standalone: standalone,
            EmitDebugSymbols: emitDebugSymbols
        );

        var packOptions = new PackOptions(
            Pack: pack,
            PushSource: pushSource,
            ApiKey: apiKey,
            PackageIdOverride: packageIdOverride,
            VersionOverride: versionOverride
        );

        return new ParsedCommand.Compile(inputFile, outputFile, compileOptions, packOptions, globalOptions);
    }

    private ParsedCommand ParseGenDeclCommand(string[] args, GlobalOptions globalOptions)
    {
        if (args.Length < 2)
        {
            return new ParsedCommand.Error(
                "Usage: sharpts --gen-decl <TypeName|Namespace|AssemblyPath> [--json] [-o output.txt]\n" +
                "Inspects a .NET type/namespace/assembly and reports which members are usable from\n" +
                "TypeScript interop, with the dotnet: import line for usable types.\n" +
                "Examples:\n" +
                "  sharpts --gen-decl System.Text.StringBuilder       # Member breakdown for a type\n" +
                "  sharpts --gen-decl System.Text                     # List the types in a namespace\n" +
                "  sharpts --gen-decl ./MyAssembly.dll                # List the types in an assembly\n" +
                "  sharpts --gen-decl System.Guid --json              # Machine-readable JSON\n" +
                "  sharpts --gen-decl System.Guid -o guid.txt         # Write to file",
                64
            );
        }

        string typeOrAssembly = args[1];
        string? outputPath = null;
        bool json = false;

        // Parse options
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "-o" && i + 1 < args.Length)
            {
                outputPath = args[++i];
            }
            else if (args[i] == "--json")
            {
                json = true;
            }
        }

        return new ParsedCommand.GenDecl(typeOrAssembly, outputPath, json, globalOptions.References);
    }

}
