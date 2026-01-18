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
// Compilation flags:
//   --ref-asm                            - Emit reference-assembly-compatible output
//   --sdk-path <path>                    - Explicit path to .NET SDK reference assemblies
//   --preserveConstEnums                 - Preserve const enum declarations
//   --verify                             - Verify emitted IL using Microsoft.ILVerification
//   -r, --reference <assembly.dll>       - Add assembly reference (can be repeated)
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
using SharpTS.Compilation;
using SharpTS.Declaration;
using SharpTS.Execution;
using SharpTS.LspBridge;
using SharpTS.LspBridge.Project;
using SharpTS.Modules;
using SharpTS.Packaging;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

// Handle --help and --version before other processing
if (args.Length > 0)
{
    if (args[0] is "--help" or "-h")
    {
        PrintHelp();
        return;
    }
    if (args[0] is "--version" or "-v")
    {
        Console.WriteLine($"sharpts {GetVersion()}");
        return;
    }
}

// Parse global options that apply to all modes
var options = ParseGlobalOptions(args);
var remainingArgs = options.RemainingArgs;

if (remainingArgs.Length == 0)
{
    RunPrompt(options.DecoratorMode);
}
else if (remainingArgs[0] == "--compile" || remainingArgs[0] == "-c")
{
    if (remainingArgs.Length < 2)
    {
        Console.WriteLine("Error: Missing input file");
        PrintCompileUsage();
        Environment.Exit(64);
    }

    string inputFile = remainingArgs[1];
    OutputTarget target = OutputTarget.Dll;
    string? explicitOutput = null;
    bool preserveConstEnums = false;
    bool useReferenceAssemblies = false;
    bool verifyIL = false;
    bool msbuildErrors = false;
    bool quietMode = false;
    string? sdkPath = null;

    // Packaging options
    bool pack = false;
    string? pushSource = null;
    string? apiKey = null;
    string? packageIdOverride = null;
    string? versionOverride = null;

    // Assembly references
    List<string> references = [];

    // Parse remaining arguments
    for (int i = 2; i < remainingArgs.Length; i++)
    {
        if (remainingArgs[i] == "-o" && i + 1 < remainingArgs.Length)
        {
            explicitOutput = remainingArgs[++i];
        }
        else if (remainingArgs[i] == "-t" || remainingArgs[i] == "--target")
        {
            if (i + 1 >= remainingArgs.Length)
            {
                Console.WriteLine($"Error: {remainingArgs[i]} requires a value (dll or exe)");
                PrintCompileUsage();
                Environment.Exit(64);
            }
            var targetArg = remainingArgs[++i].ToLowerInvariant();
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
                Console.WriteLine($"Error: Invalid target '{targetArg}'. Use 'dll' or 'exe'.");
                PrintCompileUsage();
                Environment.Exit(64);
            }
        }
        else if (remainingArgs[i] == "--preserveConstEnums")
        {
            preserveConstEnums = true;
        }
        else if (remainingArgs[i] == "--ref-asm")
        {
            useReferenceAssemblies = true;
        }
        else if (remainingArgs[i] == "--sdk-path" && i + 1 < remainingArgs.Length)
        {
            sdkPath = remainingArgs[++i];
        }
        else if (remainingArgs[i] == "--verify")
        {
            verifyIL = true;
        }
        else if (remainingArgs[i] == "--msbuild-errors")
        {
            msbuildErrors = true;
        }
        else if (remainingArgs[i] == "--quiet")
        {
            quietMode = true;
        }
        else if (remainingArgs[i] == "--pack")
        {
            pack = true;
        }
        else if (remainingArgs[i] == "--push" && i + 1 < remainingArgs.Length)
        {
            pushSource = remainingArgs[++i];
            pack = true; // --push implies --pack
        }
        else if (remainingArgs[i] == "--api-key" && i + 1 < remainingArgs.Length)
        {
            apiKey = remainingArgs[++i];
        }
        else if (remainingArgs[i] == "--package-id" && i + 1 < remainingArgs.Length)
        {
            packageIdOverride = remainingArgs[++i];
        }
        else if (remainingArgs[i] == "--version" && i + 1 < remainingArgs.Length)
        {
            versionOverride = remainingArgs[++i];
        }
        else if ((remainingArgs[i] == "-r" || remainingArgs[i] == "--reference") && i + 1 < remainingArgs.Length)
        {
            references.Add(remainingArgs[++i]);
        }
    }

    // Determine output file: use explicit output if provided, otherwise derive from input + target
    string outputFile = explicitOutput ?? Path.ChangeExtension(inputFile, target == OutputTarget.Exe ? ".exe" : ".dll");

    var packOptions = new PackOptions(pack, pushSource, apiKey, packageIdOverride, versionOverride);
    var outputOptions = new OutputOptions(msbuildErrors, quietMode);
    CompileFile(inputFile, outputFile, preserveConstEnums, useReferenceAssemblies, sdkPath, verifyIL, options.DecoratorMode, options.EmitDecoratorMetadata, packOptions, outputOptions, references, target);
}
else if (remainingArgs[0] == "--gen-decl")
{
    if (remainingArgs.Length < 2)
    {
        Console.WriteLine("Usage: sharpts --gen-decl <TypeName|AssemblyPath> [-o output.d.ts]");
        Console.WriteLine("Examples:");
        Console.WriteLine("  sharpts --gen-decl System.Console              # Generate declaration for a type");
        Console.WriteLine("  sharpts --gen-decl ./MyAssembly.dll            # Generate declarations for all types in assembly");
        Console.WriteLine("  sharpts --gen-decl System.Console -o console.d.ts  # Write to file");
        Environment.Exit(64);
    }

    string typeOrAssembly = remainingArgs[1];
    string? outputPath = null;

    // Parse options
    for (int i = 2; i < remainingArgs.Length; i++)
    {
        if (remainingArgs[i] == "-o" && i + 1 < remainingArgs.Length)
        {
            outputPath = remainingArgs[++i];
        }
    }

    GenerateDeclarations(typeOrAssembly, outputPath);
}
else if (remainingArgs[0] == "lsp-bridge")
{
    // LSP Bridge mode for IDE integration
    string? projectFile = null;
    string? sdkPath = null;
    List<string> references = [];

    // Parse lsp-bridge specific options
    for (int i = 1; i < remainingArgs.Length; i++)
    {
        if (remainingArgs[i] == "--project" && i + 1 < remainingArgs.Length)
        {
            projectFile = remainingArgs[++i];
        }
        else if (remainingArgs[i] == "--sdk-path" && i + 1 < remainingArgs.Length)
        {
            sdkPath = remainingArgs[++i];
        }
        else if ((remainingArgs[i] == "-r" || remainingArgs[i] == "--reference") && i + 1 < remainingArgs.Length)
        {
            references.Add(remainingArgs[++i]);
        }
    }

    RunLspBridge(projectFile, references, sdkPath);
}
else if (remainingArgs.Length >= 1)
{
    // Check if it looks like an unknown flag
    if (remainingArgs[0].StartsWith('-'))
    {
        Console.WriteLine($"Error: Unknown option '{remainingArgs[0]}'");
        Console.WriteLine();
        Console.WriteLine("Use 'sharpts --help' for usage information.");
        Environment.Exit(64);
    }

    // First arg is script path, rest are script arguments
    string scriptPath = remainingArgs[0];

    // Combine any additional args after script name with args after -- separator
    string[] allScriptArgs;
    if (remainingArgs.Length > 1)
    {
        // Script args from after script name + args from after --
        var extraArgs = remainingArgs[1..];
        allScriptArgs = [..extraArgs, ..options.ScriptArgs];
    }
    else
    {
        allScriptArgs = options.ScriptArgs;
    }

    RunFile(scriptPath, options.DecoratorMode, options.EmitDecoratorMetadata, allScriptArgs);
}
else
{
    Console.WriteLine("Usage: sharpts [script] [args...]");
    Console.WriteLine("       sharpts --compile <script.ts> [-o output.dll]");
    Console.WriteLine("       sharpts --gen-decl <TypeName|AssemblyPath> [-o output.d.ts]");
    Console.WriteLine("       sharpts lsp-bridge [--project <csproj>] [-r <assembly.dll>]");
    Environment.Exit(64);
}

static GlobalOptions ParseGlobalOptions(string[] args)
{
    var decoratorMode = DecoratorMode.None;
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
            case "--decorators":
                decoratorMode = DecoratorMode.Stage3;
                break;
            case "--emitDecoratorMetadata":
                emitDecoratorMetadata = true;
                break;
            default:
                remaining.Add(arg);
                break;
        }
    }

    return new GlobalOptions(decoratorMode, emitDecoratorMetadata, remaining.ToArray(), scriptArgs.ToArray());
}

static void RunLspBridge(string? projectFile, List<string> references, string? sdkPath)
{
    try
    {
        // If a project file is specified, parse it for additional references
        if (projectFile != null && File.Exists(projectFile))
        {
            var projectRefs = CsprojParser.Parse(projectFile);
            references.AddRange(projectRefs);
        }

        using var bridge = new LspBridge(references, sdkPath);
        bridge.Run();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[LspBridge Fatal] {ex.Message}");
        Environment.Exit(1);
    }
}

static void RunFile(string path, DecoratorMode decoratorMode, bool emitDecoratorMetadata, string[]? scriptArgs = null)
{
    string absolutePath = Path.GetFullPath(path);
    string source = File.ReadAllText(absolutePath);

    // Set script arguments for process.argv
    SharpTS.Runtime.BuiltIns.ProcessBuiltIns.SetScriptArguments(absolutePath, scriptArgs ?? []);

    // Check if the file contains imports - if so, use module mode
    if (source.Contains("import ") || source.Contains("export "))
    {
        RunModuleFile(absolutePath, decoratorMode, emitDecoratorMetadata, scriptArgs);
    }
    else
    {
        Run(source, decoratorMode, emitDecoratorMetadata);
    }
}

static void RunModuleFile(string absolutePath, DecoratorMode decoratorMode, bool emitDecoratorMetadata, string[]? scriptArgs = null)
{
    try
    {
        // Load the entry module and all dependencies
        var resolver = new ModuleResolver(absolutePath);
        var entryModule = resolver.LoadModule(absolutePath, decoratorMode);
        var allModules = resolver.GetModulesInOrder(entryModule);

        // Type checking across all modules (still uses Check-style API for modules)
        // Module type checking has its own error handling
        var checker = new TypeChecker();
        checker.SetDecoratorMode(decoratorMode);
        var typeMap = checker.CheckModules(allModules, resolver);

        // Interpretation
        var interpreter = new Interpreter();
        interpreter.SetDecoratorMode(decoratorMode);
        interpreter.InterpretModules(allModules, resolver, typeMap);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}

static void RunPrompt(DecoratorMode decoratorMode)
{
    Interpreter interpreter = new();
    interpreter.SetDecoratorMode(decoratorMode);
    PrintBanner();
    if (decoratorMode != DecoratorMode.None)
    {
        Console.WriteLine($"Decorator mode: {decoratorMode}");
    }
    Console.WriteLine("Type expressions to evaluate. Press Ctrl+C to exit.");
    for (; ; )
    {
        Console.Write("> ");
        string? line = Console.ReadLine();
        if (line == null) break;
        Run(line, decoratorMode, false, interpreter);
    }
}

static void Run(string source, DecoratorMode decoratorMode, bool emitDecoratorMetadata = false, Interpreter? interpreter = null)
{
    interpreter ??= new Interpreter();
    interpreter.SetDecoratorMode(decoratorMode);

    Lexer lexer = new(source);
    List<Token> tokens = lexer.ScanTokens();

    Parser parser = new(tokens, decoratorMode);
    ParseResult parseResult = parser.Parse();

    if (!parseResult.IsSuccess)
    {
        foreach (var error in parseResult.Errors)
            Console.WriteLine($"Error: {error}");
        if (parseResult.HitErrorLimit)
            Console.WriteLine("Too many errors, stopping.");
        return;
    }

    try
    {
        // Static Analysis Phase
        TypeChecker checker = new();
        checker.SetDecoratorMode(decoratorMode);
        TypeCheckResult typeResult = checker.CheckWithRecovery(parseResult.Statements);

        if (!typeResult.IsSuccess)
        {
            foreach (var error in typeResult.Errors)
                Console.WriteLine($"Error: {error}");
            if (typeResult.HitErrorLimit)
                Console.WriteLine("Too many errors, stopping.");
            return;
        }

        // Interpretation Phase
        interpreter.Interpret(parseResult.Statements, typeResult.TypeMap);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}

static void CompileFile(string inputPath, string outputPath, bool preserveConstEnums, bool useReferenceAssemblies, string? sdkPath, bool verifyIL, DecoratorMode decoratorMode, bool emitDecoratorMetadata, PackOptions packOptions, OutputOptions outputOptions, IReadOnlyList<string> references, OutputTarget target)
{
    try
    {
        string absolutePath = Path.GetFullPath(inputPath);
        string source = File.ReadAllText(absolutePath);

        // Load package.json if packaging is enabled
        PackageJson? packageJson = null;
        AssemblyMetadata? metadata = null;
        if (packOptions.Pack)
        {
            var inputDir = Path.GetDirectoryName(absolutePath) ?? ".";
            packageJson = PackageJsonLoader.FindAndLoad(inputDir);

            if (packageJson == null && packOptions.PackageIdOverride == null)
            {
                Console.WriteLine("Error: No package.json found. Provide --package-id and --version, or create a package.json.");
                Environment.Exit(1);
            }

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

        // Parse first to check for module statements
        Lexer lexer = new(source);
        List<Token> tokens = lexer.ScanTokens();
        Parser parser = new(tokens, decoratorMode);
        ParseResult parseResult = parser.Parse();

        if (!parseResult.IsSuccess)
        {
            foreach (var error in parseResult.Errors)
            {
                if (outputOptions.MsBuildErrors)
                    Console.Error.WriteLine($"{inputPath}({error.Line},1): error SHARPTS001: {error.Message}");
                else
                    Console.WriteLine($"Error: {error}");
            }
            if (parseResult.HitErrorLimit)
                Console.WriteLine("Too many errors, stopping.");
            Environment.Exit(1);
        }

        var statements = parseResult.Statements;

        // Check AST for import/export statements
        bool hasModules = statements.Any(s => s is Stmt.Import or Stmt.Export);

        if (hasModules)
        {
            CompileModuleFile(absolutePath, outputPath, preserveConstEnums, useReferenceAssemblies, sdkPath, verifyIL, decoratorMode, outputOptions, metadata, references, target);
        }
        else
        {
            CompileSingleFile(statements, outputPath, preserveConstEnums, useReferenceAssemblies, sdkPath, verifyIL, decoratorMode, outputOptions, metadata, references, target);
        }

        // Package if requested
        if (packOptions.Pack)
        {
            CreateNuGetPackage(outputPath, packageJson, packOptions);
        }
    }
    catch (Exception ex)
    {
        if (outputOptions.MsBuildErrors)
        {
            // MSBuild error format: file(line,col): error CODE: message
            Console.Error.WriteLine($"{inputPath}(1,1): error SHARPTS000: {ex.Message}");
        }
        else
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        Environment.Exit(1);
    }
}

static void CompileModuleFile(string absolutePath, string outputPath, bool preserveConstEnums, bool useReferenceAssemblies, string? sdkPath, bool verifyIL, DecoratorMode decoratorMode, OutputOptions outputOptions, AssemblyMetadata? metadata, IReadOnlyList<string> references, OutputTarget target)
{
    // Phase 1: Load all static dependencies via ModuleResolver
    var resolver = new ModuleResolver(absolutePath);
    var entryModule = resolver.LoadModule(absolutePath, decoratorMode);
    var allModules = resolver.GetModulesInOrder(entryModule);

    // Phase 2: Initial type checking to discover dynamic import paths
    var checker = new TypeChecker();
    checker.SetDecoratorMode(decoratorMode);
    var typeMap = checker.CheckModules(allModules, resolver);

    // Phase 3: Load modules discovered through dynamic import string literals
    // These modules aren't in the static dependency graph but need to be compiled
    // for runtime dynamic imports to work
    var dynamicPaths = checker.DynamicImportPaths;
    if (dynamicPaths.Count > 0)
    {
        var newModules = resolver.LoadDynamicImportModules(dynamicPaths, absolutePath, decoratorMode);
        if (newModules.Count > 0)
        {
            // Re-get the module list to include newly discovered modules
            allModules = resolver.GetModulesInOrder(entryModule);

            // Re-run type checking with the expanded module list
            // (CheckModules is incremental - only checks newly added modules)
            typeMap = checker.CheckModules(allModules, resolver);
        }
    }

    // Dead Code Analysis
    DeadCodeAnalyzer deadCodeAnalyzer = new(typeMap);
    var allStatements = allModules.SelectMany(m => m.Statements).ToList();
    DeadCodeInfo deadCodeInfo = deadCodeAnalyzer.Analyze(allStatements);

    // Compilation
    string assemblyName = Path.GetFileNameWithoutExtension(outputPath);

    if (target == OutputTarget.Exe)
    {
        // For EXE output, first compile to a temp DLL, then bundle into single-file EXE
        var tempDllPath = Path.Combine(Path.GetTempPath(), $"{assemblyName}_{Guid.NewGuid():N}.dll");
        try
        {
            // Compile to DLL format (will be bundled into EXE)
            ILCompiler compiler = new(assemblyName, preserveConstEnums, useReferenceAssemblies, sdkPath, metadata, references, OutputTarget.Dll);
            compiler.SetDecoratorMode(decoratorMode);
            compiler.CompileModules(allModules, resolver, typeMap, deadCodeInfo);
            compiler.Save(tempDllPath);

            // Run IL verification on the DLL if requested
            if (verifyIL)
            {
                VerifyCompiledAssembly(tempDllPath, sdkPath);
            }

            // Bundle into single-file EXE
            AppHostGenerator.CreateSingleFileExecutableDirect(tempDllPath, outputPath, assemblyName);

            if (!outputOptions.QuietMode)
            {
                Console.WriteLine($"Compiled to {outputPath}");
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
        ILCompiler compiler = new(assemblyName, preserveConstEnums, useReferenceAssemblies, sdkPath, metadata, references, target);
        compiler.SetDecoratorMode(decoratorMode);
        compiler.CompileModules(allModules, resolver, typeMap, deadCodeInfo);
        compiler.Save(outputPath);

        GenerateRuntimeConfig(outputPath);
        if (!outputOptions.QuietMode)
        {
            Console.WriteLine($"Compiled to {outputPath}");
        }

        // Run IL verification if requested
        if (verifyIL)
        {
            VerifyCompiledAssembly(outputPath, sdkPath);
        }
    }
}

static void CompileSingleFile(List<Stmt> statements, string outputPath, bool preserveConstEnums, bool useReferenceAssemblies, string? sdkPath, bool verifyIL, DecoratorMode decoratorMode, OutputOptions outputOptions, AssemblyMetadata? metadata, IReadOnlyList<string> references, OutputTarget target)
{
    // Static Analysis Phase
    TypeChecker checker = new();
    checker.SetDecoratorMode(decoratorMode);
    TypeCheckResult typeResult = checker.CheckWithRecovery(statements);

    if (!typeResult.IsSuccess)
    {
        foreach (var error in typeResult.Errors)
        {
            if (outputOptions.MsBuildErrors)
                Console.Error.WriteLine($"{outputPath}({error.Line ?? 1},1): error SHARPTS002: {error.Message}");
            else
                Console.WriteLine($"Error: {error}");
        }
        if (typeResult.HitErrorLimit)
            Console.WriteLine("Too many errors, stopping.");
        Environment.Exit(1);
    }

    TypeMap typeMap = typeResult.TypeMap;

    // Dead Code Analysis Phase
    DeadCodeAnalyzer deadCodeAnalyzer = new(typeMap);
    DeadCodeInfo deadCodeInfo = deadCodeAnalyzer.Analyze(statements);

    // Compilation Phase
    string assemblyName = Path.GetFileNameWithoutExtension(outputPath);

    if (target == OutputTarget.Exe)
    {
        // For EXE output, first compile to a temp DLL, then bundle into single-file EXE
        var tempDllPath = Path.Combine(Path.GetTempPath(), $"{assemblyName}_{Guid.NewGuid():N}.dll");
        try
        {
            // Compile to DLL format (will be bundled into EXE)
            ILCompiler compiler = new(assemblyName, preserveConstEnums, useReferenceAssemblies, sdkPath, metadata, references, OutputTarget.Dll);
            compiler.SetDecoratorMode(decoratorMode);
            compiler.Compile(statements, typeMap, deadCodeInfo);
            compiler.Save(tempDllPath);

            // Run IL verification on the DLL if requested
            if (verifyIL)
            {
                VerifyCompiledAssembly(tempDllPath, sdkPath);
            }

            // Bundle into single-file EXE
            AppHostGenerator.CreateSingleFileExecutableDirect(tempDllPath, outputPath, assemblyName);

            if (!outputOptions.QuietMode)
            {
                Console.WriteLine($"Compiled to {outputPath}");
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
        ILCompiler compiler = new(assemblyName, preserveConstEnums, useReferenceAssemblies, sdkPath, metadata, references, target);
        compiler.SetDecoratorMode(decoratorMode);
        compiler.Compile(statements, typeMap, deadCodeInfo);
        compiler.Save(outputPath);

        GenerateRuntimeConfig(outputPath);
        if (!outputOptions.QuietMode)
        {
            Console.WriteLine($"Compiled to {outputPath}");
        }

        // Run IL verification if requested
        if (verifyIL)
        {
            VerifyCompiledAssembly(outputPath, sdkPath);
        }
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

static void VerifyCompiledAssembly(string outputPath, string? sdkPath)
{
    // Find SDK path for IL verification
    var verifierSdkPath = sdkPath ?? SdkResolver.FindReferenceAssembliesPath();
    if (verifierSdkPath == null)
    {
        Console.WriteLine("Warning: Cannot verify IL - SDK reference assemblies not found.");
        return;
    }

    using var verifier = new ILVerifier(verifierSdkPath);
    using var stream = File.OpenRead(outputPath);
    verifier.VerifyAndReport(stream);
}

static void CreateNuGetPackage(string assemblyPath, PackageJson? packageJson, PackOptions packOptions)
{
    // Create a minimal package.json if one wasn't found but we have CLI overrides
    packageJson ??= new PackageJson
    {
        Name = packOptions.PackageIdOverride,
        Version = packOptions.VersionOverride ?? "1.0.0"
    };

    // Validate the package configuration
    var validation = PackageValidator.Validate(
        assemblyPath,
        packageJson,
        packOptions.PackageIdOverride,
        packOptions.VersionOverride);

    // Print warnings
    foreach (var warning in validation.Warnings)
    {
        Console.WriteLine($"Warning: {warning}");
    }

    // Check for errors
    if (!validation.IsValid)
    {
        foreach (var error in validation.Errors)
        {
            Console.WriteLine($"Error: {error}");
        }
        Environment.Exit(1);
    }

    // Create the NuGet packager
    var packager = new NuGetPackager(packageJson, packOptions.PackageIdOverride, packOptions.VersionOverride);
    var outputDir = Path.GetDirectoryName(assemblyPath) ?? ".";

    // Look for README.md in the package.json directory
    string? readmePath = null;
    var candidateReadme = Path.Combine(outputDir, "README.md");
    if (File.Exists(candidateReadme))
    {
        readmePath = candidateReadme;
    }

    // Create the main package
    var nupkgPath = packager.CreatePackage(assemblyPath, outputDir, readmePath);
    Console.WriteLine($"Created package: {nupkgPath}");

    // Create symbol package
    var symbolPackager = new SymbolPackager(packager.PackageId, packager.Version, packageJson.Author);
    var snupkgPath = symbolPackager.CreateSymbolPackage(assemblyPath, outputDir);
    if (snupkgPath != null)
    {
        Console.WriteLine($"Created symbol package: {snupkgPath}");
    }

    // Push to NuGet feed if requested
    if (!string.IsNullOrEmpty(packOptions.PushSource))
    {
        if (string.IsNullOrEmpty(packOptions.ApiKey))
        {
            Console.WriteLine("Error: --api-key is required when using --push.");
            Environment.Exit(1);
        }

        Console.WriteLine($"Pushing to {packOptions.PushSource}...");
        var publisher = new NuGetPublisher(packOptions.ApiKey, packOptions.PushSource);
        var success = publisher.PushWithSymbolsAsync(nupkgPath, snupkgPath).GetAwaiter().GetResult();

        if (success)
        {
            Console.WriteLine($"Successfully pushed {packager.PackageId} {packager.Version}");
        }
        else
        {
            Console.WriteLine("Push failed.");
            Environment.Exit(1);
        }
    }
}

static void GenerateDeclarations(string typeOrAssembly, string? outputPath)
{
    try
    {
        var generator = new DeclarationGenerator();
        string result;

        // Check if this is an assembly file path
        if (typeOrAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            typeOrAssembly.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(typeOrAssembly))
            {
                Console.WriteLine($"Error: Assembly not found: {typeOrAssembly}");
                Environment.Exit(1);
            }
            result = generator.GenerateForAssembly(typeOrAssembly);
        }
        else
        {
            // Treat as a type name
            result = generator.GenerateForType(typeOrAssembly);
        }

        // Output to file or console
        if (outputPath != null)
        {
            File.WriteAllText(outputPath, result);
            Console.WriteLine($"Generated declarations: {outputPath}");
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
    Console.WriteLine("  sharpts --compile <script.ts> [compile-options]");
    Console.WriteLine("  sharpts --gen-decl <TypeName|AssemblyPath> [-o output.d.ts]");
    Console.WriteLine("  sharpts lsp-bridge [--project <csproj>] [-r <assembly.dll>]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -h, --help                    Show this help message");
    Console.WriteLine("  -v, --version                 Show version information");
    Console.WriteLine("  --experimentalDecorators      Enable Legacy (Stage 2) decorators");
    Console.WriteLine("  --decorators                  Enable TC39 Stage 3 decorators");
    Console.WriteLine("  --emitDecoratorMetadata       Emit design-time type metadata");
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
    Console.WriteLine("  -r, --reference <asm.dll>     Add assembly reference (repeatable)");
    Console.WriteLine("  --preserveConstEnums          Preserve const enum declarations");
    Console.WriteLine("  --ref-asm                     Emit reference-assembly-compatible output");
    Console.WriteLine("  --sdk-path <path>             Path to .NET SDK reference assemblies");
    Console.WriteLine("  --verify                      Verify emitted IL");
    Console.WriteLine("  --msbuild-errors              Output errors in MSBuild format");
    Console.WriteLine("  --quiet                       Suppress success messages");
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
    Console.WriteLine("  sharpts --compile app.ts -t exe   Compile to executable");
    Console.WriteLine("  sharpts --compile app.ts --pack   Compile and create NuGet package");
}

static void PrintCompileUsage()
{
    Console.WriteLine();
    Console.WriteLine("Usage: sharpts --compile <file.ts> [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -o <path>              Output file path (default: <input>.dll or .exe)");
    Console.WriteLine("  -t, --target <type>    Output type: dll (default) or exe");
    Console.WriteLine("  -r, --reference <dll>  Add assembly reference (repeatable)");
    Console.WriteLine("  --preserveConstEnums   Preserve const enum declarations");
    Console.WriteLine("  --ref-asm              Emit reference-assembly-compatible output");
    Console.WriteLine("  --sdk-path <path>      Path to .NET SDK reference assemblies");
    Console.WriteLine("  --verify               Verify emitted IL");
    Console.WriteLine("  --msbuild-errors       Output errors in MSBuild format");
    Console.WriteLine("  --quiet                Suppress success messages");
    Console.WriteLine("  --pack                 Generate NuGet package");
    Console.WriteLine("  --push <source>        Push to NuGet feed (implies --pack)");
    Console.WriteLine("  --api-key <key>        NuGet API key for push");
    Console.WriteLine("  --package-id <id>      Override package ID");
    Console.WriteLine("  --version <ver>        Override package version");
}

record GlobalOptions(DecoratorMode DecoratorMode, bool EmitDecoratorMetadata, string[] RemainingArgs, string[] ScriptArgs);
record PackOptions(bool Pack, string? PushSource, string? ApiKey, string? PackageIdOverride, string? VersionOverride);
record OutputOptions(bool MsBuildErrors, bool QuietMode);
