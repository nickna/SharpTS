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
using SharpTS.Compilation.Bundling;
using SharpTS.Parsing;

namespace SharpTS.Cli;

/// <summary>
/// Global options that apply across all execution modes.
/// </summary>
/// <param name="DecoratorMode">Decorator parsing mode (None, Legacy, Stage3). Defaults to Stage3.</param>
/// <param name="EmitDecoratorMetadata">Whether to emit design-time type metadata</param>
public record GlobalOptions(
    DecoratorMode DecoratorMode = DecoratorMode.Stage3,
    bool EmitDecoratorMetadata = false
);

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
public record CompileOptions(
    OutputTarget Target = OutputTarget.Dll,
    bool PreserveConstEnums = false,
    bool UseReferenceAssemblies = false,
    string? SdkPath = null,
    bool VerifyIL = false,
    bool MsBuildErrors = false,
    bool QuietMode = false,
    IReadOnlyList<string>? References = null,
    BundlerMode Bundler = BundlerMode.Auto
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

    /// <summary>Generate TypeScript declarations for a .NET type or assembly.</summary>
    /// <param name="TypeOrAssembly">Type name or assembly path</param>
    /// <param name="OutputPath">Optional output file path</param>
    public sealed record GenDecl(string TypeOrAssembly, string? OutputPath) : ParsedCommand;

    /// <summary>Start LSP bridge mode for IDE integration.</summary>
    /// <param name="ProjectFile">Optional .csproj file path</param>
    /// <param name="References">Assembly references</param>
    /// <param name="SdkPath">SDK path for reference assemblies</param>
    public sealed record LspBridge(string? ProjectFile, List<string> References, string? SdkPath) : ParsedCommand;

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
        var (globalOptions, remainingArgs, scriptArgs) = ParseGlobalOptions(args);

        if (remainingArgs.Length == 0)
        {
            return new ParsedCommand.Repl(globalOptions);
        }

        // Handle --compile / -c
        if (remainingArgs[0] is "--compile" or "-c")
        {
            return ParseCompileCommand(remainingArgs, globalOptions);
        }

        // Handle --gen-decl
        if (remainingArgs[0] == "--gen-decl")
        {
            return ParseGenDeclCommand(remainingArgs);
        }

        // Handle lsp-bridge
        if (remainingArgs[0] == "lsp-bridge")
        {
            return ParseLspBridgeCommand(remainingArgs);
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
            "       sharpts --gen-decl <TypeName|AssemblyPath> [-o output.d.ts]\n" +
            "       sharpts lsp-bridge [--project <csproj>] [-r <assembly.dll>]",
            64
        );
    }

    private static (GlobalOptions options, string[] remainingArgs, string[] scriptArgs) ParseGlobalOptions(string[] args)
    {
        var decoratorMode = DecoratorMode.Stage3;  // Stage3 decorators enabled by default
        var emitDecoratorMetadata = false;
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

        foreach (var arg in args)
        {
            switch (arg)
            {
                case "--experimentalDecorators":
                    decoratorMode = DecoratorMode.Legacy;
                    break;
                case "--noDecorators":
                    decoratorMode = DecoratorMode.None;
                    break;
                case "--emitDecoratorMetadata":
                    emitDecoratorMetadata = true;
                    break;
                default:
                    remaining.Add(arg);
                    break;
            }
        }

        return (new GlobalOptions(decoratorMode, emitDecoratorMetadata), remaining.ToArray(), scriptArgs.ToArray());
    }

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
        bool msbuildErrors = false;
        bool quietMode = false;
        string? sdkPath = null;
        BundlerMode bundlerMode = BundlerMode.Auto;

        // Packaging options
        bool pack = false;
        string? pushSource = null;
        string? apiKey = null;
        string? packageIdOverride = null;
        string? versionOverride = null;

        // Assembly references
        List<string> references = [];

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
            else if (args[i] == "--msbuild-errors")
            {
                msbuildErrors = true;
            }
            else if (args[i] == "--quiet")
            {
                quietMode = true;
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
            else if ((args[i] is "-r" or "--reference") && i + 1 < args.Length)
            {
                references.Add(args[++i]);
            }
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
            References: references,
            Bundler: bundlerMode
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

    private ParsedCommand ParseGenDeclCommand(string[] args)
    {
        if (args.Length < 2)
        {
            return new ParsedCommand.Error(
                "Usage: sharpts --gen-decl <TypeName|AssemblyPath> [-o output.d.ts]\n" +
                "Examples:\n" +
                "  sharpts --gen-decl System.Console              # Generate declaration for a type\n" +
                "  sharpts --gen-decl ./MyAssembly.dll            # Generate declarations for all types in assembly\n" +
                "  sharpts --gen-decl System.Console -o console.d.ts  # Write to file",
                64
            );
        }

        string typeOrAssembly = args[1];
        string? outputPath = null;

        // Parse options
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "-o" && i + 1 < args.Length)
            {
                outputPath = args[++i];
            }
        }

        return new ParsedCommand.GenDecl(typeOrAssembly, outputPath);
    }

    private ParsedCommand ParseLspBridgeCommand(string[] args)
    {
        string? projectFile = null;
        string? sdkPath = null;
        List<string> references = [];

        // Parse lsp-bridge specific options
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--project" && i + 1 < args.Length)
            {
                projectFile = args[++i];
            }
            else if (args[i] == "--sdk-path" && i + 1 < args.Length)
            {
                sdkPath = args[++i];
            }
            else if ((args[i] is "-r" or "--reference") && i + 1 < args.Length)
            {
                references.Add(args[++i]);
            }
        }

        return new ParsedCommand.LspBridge(projectFile, references, sdkPath);
    }
}
