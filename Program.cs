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
using SharpTS.Execution;
using SharpTS.Modules;
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
        Console.WriteLine("Usage: sharpts --compile <file.ts> [-o output.dll] [--preserveConstEnums] [--ref-asm] [--sdk-path <path>] [--verify]");
        Environment.Exit(64);
    }

    string inputFile = remainingArgs[1];
    string outputFile = Path.ChangeExtension(inputFile, ".dll");
    bool preserveConstEnums = false;
    bool useReferenceAssemblies = false;
    bool verifyIL = false;
    string? sdkPath = null;

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
    }

    CompileFile(inputFile, outputFile, preserveConstEnums, useReferenceAssemblies, sdkPath, verifyIL, options.DecoratorMode, options.EmitDecoratorMetadata);
}
else if (remainingArgs.Length == 1)
{
    RunFile(remainingArgs[0], options.DecoratorMode, options.EmitDecoratorMetadata);
}
else
{
    Console.WriteLine("Usage: sharpts [script] | sharpts --compile <script.ts> [-o output.dll] [--preserveConstEnums] [--experimentalDecorators] [--decorators]");
    Environment.Exit(64);
}

static GlobalOptions ParseGlobalOptions(string[] args)
{
    var decoratorMode = DecoratorMode.None;
    var emitDecoratorMetadata = false;
    var remaining = new List<string>();

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

static void CompileFile(string inputPath, string outputPath, bool preserveConstEnums, bool useReferenceAssemblies, string? sdkPath, bool verifyIL, DecoratorMode decoratorMode, bool emitDecoratorMetadata)
{
    try
    {
        string absolutePath = Path.GetFullPath(inputPath);
        string source = File.ReadAllText(absolutePath);

        // Parse first to check for module statements
        Lexer lexer = new(source);
        List<Token> tokens = lexer.ScanTokens();
        Parser parser = new(tokens, decoratorMode);
        List<Stmt> statements = parser.Parse();

        // Check AST for import/export statements
        bool hasModules = statements.Any(s => s is Stmt.Import or Stmt.Export);

        if (hasModules)
        {
            CompileModuleFile(absolutePath, outputPath, preserveConstEnums, useReferenceAssemblies, sdkPath, verifyIL, decoratorMode);
        }
        else
        {
            CompileSingleFile(statements, outputPath, preserveConstEnums, useReferenceAssemblies, sdkPath, verifyIL, decoratorMode);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
        Environment.Exit(1);
    }
}

static void CompileModuleFile(string absolutePath, string outputPath, bool preserveConstEnums, bool useReferenceAssemblies, string? sdkPath, bool verifyIL, DecoratorMode decoratorMode)
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
    ILCompiler compiler = new(assemblyName, preserveConstEnums, useReferenceAssemblies, sdkPath);
    compiler.SetDecoratorMode(decoratorMode);
    compiler.CompileModules(allModules, resolver, typeMap, deadCodeInfo);
    compiler.Save(outputPath);

    GenerateRuntimeConfig(outputPath);
    CopySharpTsDll(outputPath);
    Console.WriteLine($"Compiled to {outputPath}");

    // Run IL verification if requested
    if (verifyIL)
    {
        VerifyCompiledAssembly(outputPath, sdkPath);
    }
}

static void CompileSingleFile(List<Stmt> statements, string outputPath, bool preserveConstEnums, bool useReferenceAssemblies, string? sdkPath, bool verifyIL, DecoratorMode decoratorMode)
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
    ILCompiler compiler = new(assemblyName, preserveConstEnums, useReferenceAssemblies, sdkPath);
    compiler.SetDecoratorMode(decoratorMode);
    compiler.Compile(statements, typeMap, deadCodeInfo);
    compiler.Save(outputPath);

    GenerateRuntimeConfig(outputPath);
    CopySharpTsDll(outputPath);
    Console.WriteLine($"Compiled to {outputPath}");

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

record GlobalOptions(DecoratorMode DecoratorMode, bool EmitDecoratorMetadata, string[] RemainingArgs);
