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
using SharpTS.Cli;
using SharpTS.Compilation;
using PEPacker;
using PEPacker.Bundling;
using SharpTS.Declaration;
using SharpTS.Diagnostics;
using SharpTS.Diagnostics.Exceptions;
using SharpTS.Execution;
using SharpTS.Modules;
using SharpTS.Packaging;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

// Initialize fork IPC if this process was spawned via child_process.fork()
SharpTS.Runtime.Types.ForkIpcClient.TryInitialize();

// Parse command-line arguments
var parser = new CommandLineParser();
var command = parser.Parse(args);

switch (command)
{
    case ParsedCommand.Help:
        PrintHelp();
        return;

    case ParsedCommand.Version:
        Console.WriteLine($"sharpts {GetVersion()}");
        return;

    case ParsedCommand.Error error:
        Console.WriteLine(error.Message);
        if (error.ShowCompileUsage)
            PrintCompileUsage();
        Environment.Exit(error.ExitCode);
        break;

    case ParsedCommand.Repl repl:
        RunPromptAsync(repl.Options.DecoratorMode).GetAwaiter().GetResult();
        break;

    case ParsedCommand.Script script:
        RunFile(script.ScriptPath, script.Options.DecoratorMode, script.Options.EmitDecoratorMetadata, script.ScriptArgs, script.Options.CheckJs);
        break;

    case ParsedCommand.Compile compile:
        var outputOptions = new OutputOptions(compile.CompileOptions.MsBuildErrors, compile.CompileOptions.QuietMode, compile.CompileOptions.Standalone);
        CompileFile(
            compile.InputFile,
            compile.OutputFile,
            compile.CompileOptions.PreserveConstEnums,
            compile.CompileOptions.UseReferenceAssemblies,
            compile.CompileOptions.SdkPath,
            compile.CompileOptions.VerifyIL,
            compile.GlobalOptions.DecoratorMode,
            compile.GlobalOptions.EmitDecoratorMetadata,
            compile.PackOptions,
            outputOptions,
            compile.CompileOptions.References,
            compile.CompileOptions.Target,
            compile.CompileOptions.Bundler
        );
        break;

    case ParsedCommand.GenDecl genDecl:
        GenerateDeclarations(genDecl.TypeOrAssembly, genDecl.OutputPath, genDecl.Json);
        break;
}

static void RunFile(string path, DecoratorMode decoratorMode, bool emitDecoratorMetadata, string[]? scriptArgs = null, bool checkJs = false)
{
    string absolutePath = Path.GetFullPath(path);
    string source = File.ReadAllText(absolutePath);

    // Set script arguments for process.argv
    SharpTS.Runtime.BuiltIns.ProcessBuiltIns.SetScriptArguments(absolutePath, scriptArgs ?? []);

    // Lex to check for triple-slash path references
    var lexer = new Lexer(source);
    lexer.ScanTokens();
    bool hasPathReferences = lexer.TripleSlashDirectives.Any(d => d.Type == TripleSlashReferenceType.Path);

    // CommonJS files need module mode for require()/module.exports semantics
    bool isCjsFile = SharpTS.Modules.CommonJsDetector.Detect(absolutePath)
        == SharpTS.Modules.CommonJsDetector.ModuleKind.CommonJs
        && (source.Contains("require(") || source.Contains("module.exports") || source.Contains("exports."));

    // Check if the file contains imports/exports or path references - if so, use module mode
    if (hasPathReferences || source.Contains("import ") || source.Contains("export ") || isCjsFile)
    {
        RunModuleFile(absolutePath, decoratorMode, emitDecoratorMetadata, scriptArgs);
    }
    else
    {
        Run(source, decoratorMode, emitDecoratorMetadata, filePath: absolutePath, checkJs: checkJs);
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

        // Check for type errors — warnings (from lenient CJS modules) don't block execution
        var diagnostics = checker.GetDiagnostics();
        bool hasErrors = diagnostics.Any(d => d.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error);
        if (hasErrors)
        {
            foreach (var d in diagnostics.Where(d => d.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error))
                Console.WriteLine($"Error: {d}");
            return;
        }

        // Interpretation
        var interpreter = new Interpreter();
        interpreter.SetDecoratorMode(decoratorMode);

        // If this process was forked, wire its IPC channel to this interpreter's loop so
        // 'message' handlers run with an interpreter and the child stays alive (#1017).
        SharpTS.Runtime.Types.ForkIpcClient.Instance?.AttachLoop(interpreter);

        // Variable Resolution Phase (enables O(1) lookups)
        var varResolver = new VariableResolver(interpreter);
        foreach (var module in allModules)
        {
            if (!module.IsBuiltIn)
                varResolver.Resolve(module.Statements);
        }

        interpreter.InterpretModules(allModules, resolver, typeMap);

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
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}

static async Task RunPromptAsync(DecoratorMode decoratorMode)
{
    PrintBanner();
    if (decoratorMode != DecoratorMode.None)
    {
        Console.WriteLine($"Decorator mode: {decoratorMode}");
    }
    Console.WriteLine("Type expressions to evaluate. Press Ctrl+C to cancel input.");
    Console.WriteLine("Type .help for available commands.");
    Console.WriteLine();

    var repl = new SharpTS.Repl.ReplEngine(decoratorMode);
    await repl.RunAsync();
}

static void Run(string source, DecoratorMode decoratorMode, bool emitDecoratorMetadata = false, Interpreter? interpreter = null, string? filePath = null, bool checkJs = false)
{
    interpreter ??= new Interpreter();
    interpreter.SetDecoratorMode(decoratorMode);

    // Forked-child IPC: attach this interpreter's loop (idempotent — only acts once).
    SharpTS.Runtime.Types.ForkIpcClient.Instance?.AttachLoop(interpreter);

    Lexer lexer = new(source);
    List<Token> tokens = lexer.ScanTokens();

    Parser parser = new(tokens, decoratorMode);
    var parseResult = parser.Parse();

    if (!parseResult.IsSuccess)
    {
        foreach (var diagnostic in parseResult.Diagnostics)
            Console.WriteLine($"Error: {diagnostic}");
        if (parseResult.HitErrorLimit)
            Console.WriteLine("Too many errors, stopping.");
        return;
    }

    try
    {
        // Static Analysis Phase — skipped for .js files unless --check-js or
        // // @ts-check opts in (matches tsc's checkJs:false default).
        TypeMap? typeMap = null;
        if (TypeCheckPolicy.ShouldTypeCheck(filePath, lexer.Pragmas, checkJsDefault: checkJs))
        {
            TypeChecker checker = new();
            checker.SetDecoratorMode(decoratorMode);
            var typeResult = checker.CheckWithRecovery(parseResult.Statements);

            // Apply // @ts-ignore / @ts-expect-error line directives.
            var filteredDiagnostics = TypeCheckPolicy.ApplyLineDirectives(typeResult.Diagnostics, lexer.Pragmas);
            bool hasErrors = filteredDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
            if (hasErrors)
            {
                foreach (var diagnostic in filteredDiagnostics)
                    Console.WriteLine($"Error: {diagnostic}");
                if (typeResult.HitErrorLimit)
                    Console.WriteLine("Too many errors, stopping.");
                Environment.Exit(1);
            }
            typeMap = typeResult.TypeMap;
        }

        // Variable Resolution Phase (enables O(1) lookups)
        var resolver = new VariableResolver(interpreter);
        resolver.Resolve(parseResult.Statements);

        // Interpretation Phase
        interpreter.Interpret(parseResult.Statements, typeMap);

        // Node default: an unhandled promise rejection makes the process
        // exit nonzero (#228).
        if (interpreter.HadUnhandledRejection)
        {
            Environment.Exit(1);
        }
    }
    catch (SharpTSException ex)
    {
        Console.WriteLine($"Error: {ex.Diagnostic}");
    }
    catch (SharpTS.Runtime.Exceptions.ThrowException tex)
    {
        Console.WriteLine($"Error: {tex.Value}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}

static void CompileFile(string inputPath, string outputPath, bool preserveConstEnums, bool useReferenceAssemblies, string? sdkPath, bool verifyIL, DecoratorMode decoratorMode, bool emitDecoratorMetadata, PackOptions packOptions, OutputOptions outputOptions, IReadOnlyList<string> references, OutputTarget target, BundlerMode bundlerMode)
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

        // Set up diagnostic reporter
        var reporter = new DiagnosticReporter { MsBuildFormat = outputOptions.MsBuildErrors, QuietMode = outputOptions.QuietMode };

        // Parse first to check for module statements and path references
        Lexer lexer = new(source);
        List<Token> tokens = lexer.ScanTokens();
        Parser parser = new Parser(tokens, decoratorMode).WithFilePath(absolutePath);
        var parseResult = parser.Parse();

        if (!parseResult.IsSuccess)
        {
            reporter.ReportAll(parseResult.Diagnostics);
            if (parseResult.HitErrorLimit)
                Console.WriteLine("Too many errors, stopping.");
            Environment.Exit(1);
        }

        var statements = parseResult.Statements;

        // Check for path references (script files with references need module resolution)
        bool hasPathReferences = lexer.TripleSlashDirectives.Any(d => d.Type == TripleSlashReferenceType.Path);

        // Check AST for import/export statements or path references
        // Include ImportRequire for CommonJS-style: import X = require('./module')
        bool hasModules = hasPathReferences || statements.Any(s => s is Stmt.Import or Stmt.Export or Stmt.ImportRequire);

        // CommonJS files (.cjs, or .js classified as CJS) also need module-mode compilation
        // because they use the CJS module pipeline (per-file class with $exports field).
        if (!hasModules)
        {
            bool isCjsFile = SharpTS.Modules.CommonJsDetector.Detect(absolutePath)
                == SharpTS.Modules.CommonJsDetector.ModuleKind.CommonJs
                && (source.Contains("require(") || source.Contains("module.exports") || source.Contains("exports."));
            if (isCjsFile)
            {
                hasModules = true;
            }
        }

        if (hasModules)
        {
            CompileModuleFile(absolutePath, outputPath, preserveConstEnums, useReferenceAssemblies, sdkPath, verifyIL, decoratorMode, outputOptions, metadata, references, target, bundlerMode);
        }
        else
        {
            CompileSingleFile(statements, outputPath, preserveConstEnums, useReferenceAssemblies, sdkPath, verifyIL, decoratorMode, outputOptions, metadata, references, target, bundlerMode, absolutePath, lexer.Pragmas);
        }

        // Package if requested
        if (packOptions.Pack)
        {
            CreateNuGetPackage(outputPath, packageJson, packOptions);
        }
    }
    catch (SharpTSException ex)
    {
        var reporter = new DiagnosticReporter { MsBuildFormat = outputOptions.MsBuildErrors };
        reporter.Report(ex.Diagnostic);
        Environment.Exit(1);
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

static void CompileModuleFile(string absolutePath, string outputPath, bool preserveConstEnums, bool useReferenceAssemblies, string? sdkPath, bool verifyIL, DecoratorMode decoratorMode, OutputOptions outputOptions, AssemblyMetadata? metadata, IReadOnlyList<string> references, OutputTarget target, BundlerMode bundlerMode)
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
            try
            {
                var bundleResult = AppHostGenerator.CreateSingleFileExecutable(tempDllPath, outputPath, assemblyName, bundlerMode);

                if (!outputOptions.QuietMode)
                {
                    Console.WriteLine($"Compiled to {outputPath} (using {bundleResult.TechniqueDescription})");
                }

                // Co-locate SharpTS.dll next to the EXE when the program uses a feature that
                // late-binds into the SharpTS runtime (eval, Proxy, Intl, vm, dns, @DotNetType
                // dynamic events). Honors --standalone. Pure programs stay a single file.
                CopySharpTSRuntimeIfNeeded(compiler, outputPath, outputOptions);
            }
            catch (Exception ex) when (bundlerMode != BundlerMode.Auto)
            {
                var bundlerName = bundlerMode == BundlerMode.Sdk ? "SDK" : "built-in";
                Console.WriteLine($"Error: {bundlerName} bundler failed: {ex.Message}");
                Console.WriteLine($"The {bundlerName} bundler was explicitly requested. Use '--bundler auto' to allow fallback.");
                Environment.Exit(1);
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
        CopySharpTSRuntimeIfNeeded(compiler, outputPath, outputOptions);
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

static void CompileSingleFile(List<Stmt> statements, string outputPath, bool preserveConstEnums, bool useReferenceAssemblies, string? sdkPath, bool verifyIL, DecoratorMode decoratorMode, OutputOptions outputOptions, AssemblyMetadata? metadata, IReadOnlyList<string> references, OutputTarget target, BundlerMode bundlerMode, string? sourcePath = null, TypeScriptPragmas? pragmas = null)
{
    // Set up diagnostic reporter
    var reporter = new DiagnosticReporter { MsBuildFormat = outputOptions.MsBuildErrors, QuietMode = outputOptions.QuietMode };

    // Static Analysis Phase — skipped for .js files unless `// @ts-check` opts in.
    // Compiler still needs a TypeMap; an empty one falls back to dynamic dispatch.
    TypeMap typeMap = new();
    var effectivePragmas = pragmas ?? TypeScriptPragmas.Empty;
    if (TypeCheckPolicy.ShouldTypeCheck(sourcePath, effectivePragmas, checkJsDefault: false))
    {
        TypeChecker checker = new TypeChecker().WithFilePath(outputPath);
        checker.SetDecoratorMode(decoratorMode);
        var typeResult = checker.CheckWithRecovery(statements);

        // Apply // @ts-ignore / @ts-expect-error line directives.
        var filteredDiagnostics = TypeCheckPolicy.ApplyLineDirectives(typeResult.Diagnostics, effectivePragmas);
        bool hasErrors = filteredDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        if (hasErrors)
        {
            reporter.ReportAll(filteredDiagnostics);
            if (typeResult.HitErrorLimit)
                Console.WriteLine("Too many errors, stopping.");
            Environment.Exit(1);
        }

        typeMap = typeResult.TypeMap;
    }

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
            try
            {
                var bundleResult = AppHostGenerator.CreateSingleFileExecutable(tempDllPath, outputPath, assemblyName, bundlerMode);

                if (!outputOptions.QuietMode)
                {
                    Console.WriteLine($"Compiled to {outputPath} (using {bundleResult.TechniqueDescription})");
                }

                // Co-locate SharpTS.dll next to the EXE when the program uses a feature that
                // late-binds into the SharpTS runtime (eval, Proxy, Intl, vm, dns, @DotNetType
                // dynamic events). Honors --standalone. Pure programs stay a single file.
                CopySharpTSRuntimeIfNeeded(compiler, outputPath, outputOptions);
            }
            catch (Exception ex) when (bundlerMode != BundlerMode.Auto)
            {
                var bundlerName = bundlerMode == BundlerMode.Sdk ? "SDK" : "built-in";
                Console.WriteLine($"Error: {bundlerName} bundler failed: {ex.Message}");
                Console.WriteLine($"The {bundlerName} bundler was explicitly requested. Use '--bundler auto' to allow fallback.");
                Environment.Exit(1);
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
        CopySharpTSRuntimeIfNeeded(compiler, outputPath, outputOptions);
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

/// <summary>
/// Co-locates SharpTS.dll with the compiled output when, and only when, the compilation emitted
/// late binding into the SharpTS runtime whose normal execution needs it (eval, Proxy, Intl, vm,
/// dns, @DotNetType dynamic events). Programs that use none of these stay fully standalone — no
/// copy. <c>--standalone</c> suppresses the copy (the soft-dependent features then throw a clear
/// "not supported" error at runtime instead).
/// </summary>
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

    var sharpTsPath = typeof(SharpTS.Execution.Interpreter).Assembly.Location;
    var outDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
    if (string.IsNullOrEmpty(sharpTsPath) || !File.Exists(sharpTsPath) || outDir == null)
    {
        if (!outputOptions.QuietMode)
            Console.WriteLine($"Warning: could not locate SharpTS.dll to co-locate with output; features ({reasonList}) may fail at runtime.");
        return;
    }

    var destPath = Path.Combine(outDir, Path.GetFileName(sharpTsPath));
    try
    {
        if (!string.Equals(Path.GetFullPath(sharpTsPath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
            File.Copy(sharpTsPath, destPath, overwrite: true);

        // child_process.fork() spawns a SEPARATE `dotnet exec SharpTS.dll <module>` process
        // (unlike Worker/eval which load SharpTS.dll in-process). That child needs SharpTS's
        // full runtime closure — its runtimeconfig.json, deps.json, and dependency DLLs — so
        // co-locate the whole SharpTS bin directory next to the output. Other soft-deps only
        // need SharpTS.dll loaded in-process.
        if (reasons.Contains("child_process.fork"))
        {
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
            var what = reasons.Contains("child_process.fork") ? "SharpTS runtime" : "SharpTS.dll";
            Console.WriteLine($"Copied {what} next to output — required at runtime by: {reasonList}");
        }
    }
    catch (Exception ex)
    {
        if (!outputOptions.QuietMode)
            Console.WriteLine($"Warning: failed to co-locate SharpTS.dll with output ({reasonList}): {ex.Message}");
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
    // The verifier resolves against the shared-framework runtime directory;
    // an explicit --sdk-path is only an additional probe location.
    using var verifier = new ILVerifier(sdkPath);
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

static void GenerateDeclarations(string typeOrAssembly, string? outputPath, bool json)
{
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
    Console.WriteLine("  sharpts --compile <script.ts> [compile-options]");
    Console.WriteLine("  sharpts --gen-decl <TypeName|Namespace|AssemblyPath> [--json] [-o output.txt]");
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
    Console.WriteLine("  --bundler <mode>              Bundler selection: auto (default), sdk, or builtin");
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

record OutputOptions(bool MsBuildErrors, bool QuietMode, bool Standalone = false);
