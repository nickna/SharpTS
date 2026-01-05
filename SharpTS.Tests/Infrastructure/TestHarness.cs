using System.Diagnostics;
using SharpTS.Compilation;
using SharpTS.Execution;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// Test harness utilities for running TypeScript source through the interpreter
/// and compiler, capturing console output for assertions.
/// </summary>
public static class TestHarness
{
    // Lock to prevent concurrent Console.Out manipulation
    private static readonly object ConsoleLock = new();

    /// <summary>
    /// Runs TypeScript source through the interpreter and captures console output.
    /// </summary>
    /// <param name="source">TypeScript source code</param>
    /// <returns>Captured console output</returns>
    public static string RunInterpreted(string source)
    {
        lock (ConsoleLock)
        {
            var sw = new StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(sw);

            try
            {
                var lexer = new Lexer(source);
                var tokens = lexer.ScanTokens();
                var parser = new Parser(tokens);
                var statements = parser.Parse();

                var checker = new TypeChecker();
                var typeMap = checker.Check(statements);

                var interpreter = new Interpreter();
                interpreter.Interpret(statements, typeMap);

                // Normalize line endings for cross-platform test consistency
                return sw.ToString().Replace("\r\n", "\n");
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }

    /// <summary>
    /// Compiles TypeScript source to a .NET DLL, executes it, and captures output.
    /// </summary>
    /// <param name="source">TypeScript source code</param>
    /// <returns>Captured console output from the compiled executable</returns>
    public static string RunCompiled(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var dllPath = Path.Combine(tempDir, "test.dll");

            // Compile
            var lexer = new Lexer(source);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens);
            var statements = parser.Parse();

            var checker = new TypeChecker();
            var typeMap = checker.Check(statements);

            var deadCodeAnalyzer = new DeadCodeAnalyzer(typeMap);
            var deadCodeInfo = deadCodeAnalyzer.Analyze(statements);

            var compiler = new ILCompiler("test");
            compiler.Compile(statements, typeMap, deadCodeInfo);
            compiler.Save(dllPath);

            // Write runtimeconfig.json
            var configPath = Path.Combine(tempDir, "test.runtimeconfig.json");
            File.WriteAllText(configPath, """
                {
                  "runtimeOptions": {
                    "tfm": "net10.0",
                    "framework": {
                      "name": "Microsoft.NETCore.App",
                      "version": "10.0.0"
                    }
                  }
                }
                """);

            // Execute and capture output
            var psi = new ProcessStartInfo("dotnet", dllPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = tempDir
            };

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new Exception($"Compiled program exited with code {process.ExitCode}. Stderr: {error}");
            }

            // Normalize line endings for cross-platform test consistency
            return output.Replace("\r\n", "\n");
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Runs multiple TypeScript modules through the interpreter and captures console output.
    /// </summary>
    /// <param name="files">Dictionary mapping file paths to source code</param>
    /// <param name="entryPoint">The entry point file path (e.g., "./main.ts")</param>
    /// <returns>Captured console output</returns>
    public static string RunModulesInterpreted(Dictionary<string, string> files, string entryPoint)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_module_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Write all files to temp directory
            foreach (var (path, content) in files)
            {
                var fullPath = Path.Combine(tempDir, path.TrimStart('.', '/', '\\'));
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(fullPath, content);
            }

            string entryPath = Path.Combine(tempDir, entryPoint.TrimStart('.', '/', '\\'));

            lock (ConsoleLock)
            {
                var sw = new StringWriter();
                var originalOut = Console.Out;
                Console.SetOut(sw);

                try
                {
                    var resolver = new ModuleResolver(entryPath);
                    var entryModule = resolver.LoadModule(entryPath);
                    var allModules = resolver.GetModulesInOrder(entryModule);

                    var checker = new TypeChecker();
                    var typeMap = checker.CheckModules(allModules, resolver);

                    var interpreter = new Interpreter();
                    interpreter.InterpretModules(allModules, resolver, typeMap);

                    return sw.ToString().Replace("\r\n", "\n");
                }
                finally
                {
                    Console.SetOut(originalOut);
                }
            }
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Compiles multiple TypeScript modules to a .NET DLL, executes it, and captures output.
    /// </summary>
    /// <param name="files">Dictionary mapping file paths to source code</param>
    /// <param name="entryPoint">The entry point file path (e.g., "./main.ts")</param>
    /// <returns>Captured console output from the compiled executable</returns>
    public static string RunModulesCompiled(Dictionary<string, string> files, string entryPoint)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_module_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Write all files to temp directory
            foreach (var (path, content) in files)
            {
                var fullPath = Path.Combine(tempDir, path.TrimStart('.', '/', '\\'));
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(fullPath, content);
            }

            string entryPath = Path.Combine(tempDir, entryPoint.TrimStart('.', '/', '\\'));
            var dllPath = Path.Combine(tempDir, "test.dll");

            // Load and resolve modules
            var resolver = new ModuleResolver(entryPath);
            var entryModule = resolver.LoadModule(entryPath);
            var allModules = resolver.GetModulesInOrder(entryModule);

            // Type check
            var checker = new TypeChecker();
            var typeMap = checker.CheckModules(allModules, resolver);

            // Dead code analysis across all modules
            var allStatements = allModules.SelectMany(m => m.Statements).ToList();
            var deadCodeAnalyzer = new DeadCodeAnalyzer(typeMap);
            var deadCodeInfo = deadCodeAnalyzer.Analyze(allStatements);

            // Compile
            var compiler = new ILCompiler("test");
            compiler.CompileModules(allModules, resolver, typeMap, deadCodeInfo);
            compiler.Save(dllPath);

            // Write runtimeconfig.json
            var configPath = Path.Combine(tempDir, "test.runtimeconfig.json");
            File.WriteAllText(configPath, """
                {
                  "runtimeOptions": {
                    "tfm": "net10.0",
                    "framework": {
                      "name": "Microsoft.NETCore.App",
                      "version": "10.0.0"
                    }
                  }
                }
                """);

            // Execute and capture output
            var psi = new ProcessStartInfo("dotnet", dllPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = tempDir
            };

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new Exception($"Compiled program exited with code {process.ExitCode}. Stderr: {error}");
            }

            // Normalize line endings for cross-platform test consistency
            return output.Replace("\r\n", "\n");
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
