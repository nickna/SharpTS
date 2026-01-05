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

if (args.Length == 0)
{
    RunPrompt();
}
else if (args[0] == "--compile" || args[0] == "-c")
{
    if (args.Length < 2)
    {
        Console.WriteLine("Usage: sharpts --compile <file.ts> [-o output.dll] [--preserveConstEnums]");
        Environment.Exit(64);
    }

    string inputFile = args[1];
    string outputFile = Path.ChangeExtension(inputFile, ".dll");
    bool preserveConstEnums = false;

    // Parse remaining arguments
    for (int i = 2; i < args.Length; i++)
    {
        if (args[i] == "-o" && i + 1 < args.Length)
        {
            outputFile = args[++i];
        }
        else if (args[i] == "--preserveConstEnums")
        {
            preserveConstEnums = true;
        }
    }

    CompileFile(inputFile, outputFile, preserveConstEnums);
}
else if (args.Length == 1)
{
    RunFile(args[0]);
}
else
{
    Console.WriteLine("Usage: sharpts [script] | sharpts --compile <script.ts> [-o output.dll] [--preserveConstEnums]");
    Environment.Exit(64);
}

static void RunFile(string path)
{
    string absolutePath = Path.GetFullPath(path);
    string source = File.ReadAllText(absolutePath);

    // Check if the file contains imports - if so, use module mode
    if (source.Contains("import ") || source.Contains("export "))
    {
        RunModuleFile(absolutePath);
    }
    else
    {
        Run(source);
    }
}

static void RunModuleFile(string absolutePath)
{
    try
    {
        // Load the entry module and all dependencies
        var resolver = new ModuleResolver(absolutePath);
        var entryModule = resolver.LoadModule(absolutePath);
        var allModules = resolver.GetModulesInOrder(entryModule);

        // Type checking across all modules
        var checker = new TypeChecker();
        var typeMap = checker.CheckModules(allModules, resolver);

        // Interpretation
        var interpreter = new Interpreter();
        interpreter.InterpretModules(allModules, resolver, typeMap);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}

static void RunPrompt()
{
    Interpreter interpreter = new();
    Console.WriteLine("SharpTS REPL (v0.1)");
    for (; ; )
    {
        Console.Write("> ");
        string? line = Console.ReadLine();
        if (line == null) break;
        Run(line, interpreter);
    }
}

static void Run(string source, Interpreter? interpreter = null)
{
    interpreter ??= new Interpreter();

    Lexer lexer = new(source);
    List<Token> tokens = lexer.ScanTokens();

    Parser parser = new(tokens);
    try
    {
        List<Stmt> statements = parser.Parse();

        // Static Analysis Phase
        TypeChecker checker = new();
        TypeMap typeMap = checker.Check(statements);

        // Interpretation Phase
        interpreter.Interpret(statements, typeMap);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}

static void CompileFile(string inputPath, string outputPath, bool preserveConstEnums = false)
{
    try
    {
        string source = File.ReadAllText(inputPath);

        Lexer lexer = new(source);
        List<Token> tokens = lexer.ScanTokens();

        Parser parser = new(tokens);
        List<Stmt> statements = parser.Parse();

        // Static Analysis Phase
        TypeChecker checker = new();
        TypeMap typeMap = checker.Check(statements);

        // Dead Code Analysis Phase
        DeadCodeAnalyzer deadCodeAnalyzer = new(typeMap);
        DeadCodeInfo deadCodeInfo = deadCodeAnalyzer.Analyze(statements);

        // Compilation Phase
        string assemblyName = Path.GetFileNameWithoutExtension(outputPath);
        ILCompiler compiler = new(assemblyName, preserveConstEnums);
        compiler.Compile(statements, typeMap, deadCodeInfo);
        compiler.Save(outputPath);

        // Generate runtimeconfig.json for the compiled assembly
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

        Console.WriteLine($"Compiled to {outputPath}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
        Environment.Exit(1);
    }
}