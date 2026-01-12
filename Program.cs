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

using SharpTS.Compilation;
using SharpTS.Declaration;
using SharpTS.Execution;
using SharpTS.LspBridge;
using SharpTS.LspBridge.Project;
using SharpTS.Modules;
using SharpTS.Packaging;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

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
        Console.WriteLine("Usage: sharpts --compile <file.ts> [-o output.dll] [-r <assembly.dll>]...");
        Console.WriteLine("       [--preserveConstEnums] [--ref-asm] [--sdk-path <path>] [--verify]");
        Console.WriteLine("       [--msbuild-errors] [--quiet]");
        Console.WriteLine("       [--pack] [--push <source>] [--api-key <key>] [--package-id <id>] [--version <ver>]");
        Environment.Exit(64);
    }

    string inputFile = remainingArgs[1];
    string outputFile = Path.ChangeExtension(inputFile, ".dll");
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
            outputFile = remainingArgs[++i];
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

    var packOptions = new PackOptions(pack, pushSource, apiKey, packageIdOverride, versionOverride);
    var outputOptions = new OutputOptions(msbuildErrors, quietMode);
    CompileFile(inputFile, outputFile, preserveConstEnums, useReferenceAssemblies, sdkPath, verifyIL, options.DecoratorMode, options.EmitDecoratorMetadata, packOptions, outputOptions, references);
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
else if (remainingArgs.Length == 1)
{
    RunFile(remainingArgs[0], options.DecoratorMode, options.EmitDecoratorMetadata);
}
else
{
    Console.WriteLine("Usage: sharpts [script]");
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

    return new GlobalOptions(decoratorMode, emitDecoratorMetadata, remaining.ToArray());
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

static void RunFile(string path, DecoratorMode decoratorMode, bool emitDecoratorMetadata)
{
    string absolutePath = Path.GetFullPath(path);
    string source = File.ReadAllText(absolutePath);

    // Check if the file contains imports - if so, use module mode
    if (source.Contains("import ") || source.Contains("export "))
    {
        RunModuleFile(absolutePath, decoratorMode, emitDecoratorMetadata);
    }
    else
    {
        Run(source, decoratorMode, emitDecoratorMetadata);
    }
}

static void RunModuleFile(string absolutePath, DecoratorMode decoratorMode, bool emitDecoratorMetadata)
{
    try
    {
        // Load the entry module and all dependencies
        var resolver = new ModuleResolver(absolutePath);
        var entryModule = resolver.LoadModule(absolutePath, decoratorMode);
        var allModules = resolver.GetModulesInOrder(entryModule);

        // Type checking across all modules
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
    Console.WriteLine("SharpTS REPL (v0.1)");
    if (decoratorMode != DecoratorMode.None)
    {
        Console.WriteLine($"Decorator mode: {decoratorMode}");
    }
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
    try
    {
        List<Stmt> statements = parser.Parse();

        // Static Analysis Phase
        TypeChecker checker = new();
        checker.SetDecoratorMode(decoratorMode);
        TypeMap typeMap = checker.Check(statements);

        // Interpretation Phase
        interpreter.Interpret(statements, typeMap);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}

static void CompileFile(string inputPath, string outputPath, bool preserveConstEnums, bool useReferenceAssemblies, string? sdkPath, bool verifyIL, DecoratorMode decoratorMode, bool emitDecoratorMetadata, PackOptions packOptions, OutputOptions outputOptions, IReadOnlyList<string> references)
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
        List<Stmt> statements = parser.Parse();

        // Check AST for import/export statements
        bool hasModules = statements.Any(s => s is Stmt.Import or Stmt.Export);

        if (hasModules)
        {
            CompileModuleFile(absolutePath, outputPath, preserveConstEnums, useReferenceAssemblies, sdkPath, verifyIL, decoratorMode, outputOptions, metadata, references);
        }
        else
        {
            CompileSingleFile(statements, outputPath, preserveConstEnums, useReferenceAssemblies, sdkPath, verifyIL, decoratorMode, outputOptions, metadata, references);
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

static void CompileModuleFile(string absolutePath, string outputPath, bool preserveConstEnums, bool useReferenceAssemblies, string? sdkPath, bool verifyIL, DecoratorMode decoratorMode, OutputOptions outputOptions, AssemblyMetadata? metadata, IReadOnlyList<string> references)
{
    // Load all dependencies via ModuleResolver
    var resolver = new ModuleResolver(absolutePath);
    var entryModule = resolver.LoadModule(absolutePath, decoratorMode);
    var allModules = resolver.GetModulesInOrder(entryModule);

    // Type checking across all modules
    var checker = new TypeChecker();
    checker.SetDecoratorMode(decoratorMode);
    var typeMap = checker.CheckModules(allModules, resolver);

    // Dead Code Analysis
    DeadCodeAnalyzer deadCodeAnalyzer = new(typeMap);
    var allStatements = allModules.SelectMany(m => m.Statements).ToList();
    DeadCodeInfo deadCodeInfo = deadCodeAnalyzer.Analyze(allStatements);

    // Compilation
    string assemblyName = Path.GetFileNameWithoutExtension(outputPath);
    ILCompiler compiler = new(assemblyName, preserveConstEnums, useReferenceAssemblies, sdkPath, metadata, references);
    compiler.SetDecoratorMode(decoratorMode);
    compiler.CompileModules(allModules, resolver, typeMap, deadCodeInfo);
    compiler.Save(outputPath);

    GenerateRuntimeConfig(outputPath);
    CopySharpTsDll(outputPath);
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

static void CompileSingleFile(List<Stmt> statements, string outputPath, bool preserveConstEnums, bool useReferenceAssemblies, string? sdkPath, bool verifyIL, DecoratorMode decoratorMode, OutputOptions outputOptions, AssemblyMetadata? metadata, IReadOnlyList<string> references)
{
    // Static Analysis Phase
    TypeChecker checker = new();
    checker.SetDecoratorMode(decoratorMode);
    TypeMap typeMap = checker.Check(statements);

    // Dead Code Analysis Phase
    DeadCodeAnalyzer deadCodeAnalyzer = new(typeMap);
    DeadCodeInfo deadCodeInfo = deadCodeAnalyzer.Analyze(statements);

    // Compilation Phase
    string assemblyName = Path.GetFileNameWithoutExtension(outputPath);
    ILCompiler compiler = new(assemblyName, preserveConstEnums, useReferenceAssemblies, sdkPath, metadata, references);
    compiler.SetDecoratorMode(decoratorMode);
    compiler.Compile(statements, typeMap, deadCodeInfo);
    compiler.Save(outputPath);

    GenerateRuntimeConfig(outputPath);
    CopySharpTsDll(outputPath);
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

static void CopySharpTsDll(string outputPath)
{
    string outputDir = Path.GetDirectoryName(outputPath) ?? ".";
    string sharpTsSource = typeof(SharpTS.Compilation.RuntimeTypes).Assembly.Location;
    string sharpTsDest = Path.Combine(outputDir, "SharpTS.dll");
    if (!string.IsNullOrEmpty(sharpTsSource) && File.Exists(sharpTsSource) && sharpTsSource != sharpTsDest)
    {
        File.Copy(sharpTsSource, sharpTsDest, overwrite: true);
    }
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

record GlobalOptions(DecoratorMode DecoratorMode, bool EmitDecoratorMetadata, string[] RemainingArgs);
record PackOptions(bool Pack, string? PushSource, string? ApiKey, string? PackageIdOverride, string? VersionOverride);
record OutputOptions(bool MsBuildErrors, bool QuietMode);
