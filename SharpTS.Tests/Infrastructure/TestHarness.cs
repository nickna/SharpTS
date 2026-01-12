using System.Diagnostics;
using System.Reflection;
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
    /// <summary>
    /// Lock to prevent concurrent Console.Out/Error manipulation across parallel tests.
    /// Any test that redirects Console.Out or Console.Error should acquire this lock.
    /// </summary>
    internal static readonly object ConsoleLock = new();

    /// <summary>
    /// Runs TypeScript source through the interpreter and captures console output.
    /// </summary>
    /// <param name="source">TypeScript source code</param>
    /// <returns>Captured console output</returns>
    public static string RunInterpreted(string source)
    {
        return RunInterpreted(source, DecoratorMode.None);
    }

    /// <summary>
    /// Runs TypeScript source through the interpreter with decorator support and captures console output.
    /// </summary>
    /// <param name="source">TypeScript source code</param>
    /// <param name="decoratorMode">Decorator mode (None, Legacy, Stage3)</param>
    /// <returns>Captured console output</returns>
    public static string RunInterpreted(string source, DecoratorMode decoratorMode)
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
                var parser = new Parser(tokens, decoratorMode);
                var statements = parser.Parse();

                var checker = new TypeChecker();
                checker.SetDecoratorMode(decoratorMode);
                var typeMap = checker.Check(statements);

                var interpreter = new Interpreter();
                interpreter.SetDecoratorMode(decoratorMode);
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
        return RunCompiled(source, DecoratorMode.None);
    }

    /// <summary>
    /// Compiles TypeScript source to a .NET DLL with decorator support, executes it, and captures output.
    /// </summary>
    /// <param name="source">TypeScript source code</param>
    /// <param name="decoratorMode">Decorator mode (None, Legacy, Stage3)</param>
    /// <returns>Captured console output from the compiled executable</returns>
    public static string RunCompiled(string source, DecoratorMode decoratorMode)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var dllPath = Path.Combine(tempDir, "test.dll");

            // Compile
            var lexer = new Lexer(source);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens, decoratorMode);
            var statements = parser.Parse();

            var checker = new TypeChecker();
            checker.SetDecoratorMode(decoratorMode);
            var typeMap = checker.Check(statements);

            var deadCodeAnalyzer = new DeadCodeAnalyzer(typeMap);
            var deadCodeInfo = deadCodeAnalyzer.Analyze(statements);

            var compiler = new ILCompiler("test");
            compiler.SetDecoratorMode(decoratorMode);
            compiler.Compile(statements, typeMap, deadCodeInfo);
            compiler.Save(dllPath);

            // Copy SharpTS.dll for runtime dependency (needed for Promise.all/race/allSettled/any)
            var sharpTsDll = typeof(RuntimeTypes).Assembly.Location;
            if (!string.IsNullOrEmpty(sharpTsDll) && File.Exists(sharpTsDll))
            {
                File.Copy(sharpTsDll, Path.Combine(tempDir, "SharpTS.dll"), overwrite: true);
            }

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
    /// Compiles TypeScript source, loads the assembly in-process, executes it, and returns both the assembly and output.
    /// This allows tests to perform reflection on the compiled types.
    /// </summary>
    /// <param name="source">TypeScript source code</param>
    /// <param name="decoratorMode">Decorator mode (None, Legacy, Stage3)</param>
    /// <returns>Tuple of (Assembly, console output)</returns>
    public static (Assembly assembly, string output) CompileAndRun(string source, DecoratorMode decoratorMode)
    {
        // Use unique assembly name to avoid conflicts when loading multiple assemblies
        var assemblyName = $"test_{Guid.NewGuid():N}";
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_{assemblyName}");
        Directory.CreateDirectory(tempDir);

        var dllPath = Path.Combine(tempDir, $"{assemblyName}.dll");

        // Compile
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens, decoratorMode);
        var statements = parser.Parse();

        var checker = new TypeChecker();
        checker.SetDecoratorMode(decoratorMode);
        var typeMap = checker.Check(statements);

        var deadCodeAnalyzer = new DeadCodeAnalyzer(typeMap);
        var deadCodeInfo = deadCodeAnalyzer.Analyze(statements);

        var compiler = new ILCompiler(assemblyName);
        compiler.SetDecoratorMode(decoratorMode);
        compiler.Compile(statements, typeMap, deadCodeInfo);
        compiler.Save(dllPath);

        // Load assembly in-process for reflection
        var assembly = Assembly.LoadFrom(dllPath);

        // Execute Main and capture output
        lock (ConsoleLock)
        {
            var sw = new StringWriter();
            var originalOut = Console.Out;
            Console.SetOut(sw);

            try
            {
                var programType = assembly.GetType("$Program");
                var mainMethod = programType?.GetMethod("Main", BindingFlags.Public | BindingFlags.Static);
                mainMethod?.Invoke(null, null);

                return (assembly, sw.ToString().Replace("\r\n", "\n"));
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }

    /// <summary>
    /// Compiles TypeScript source to a .NET DLL with reference assembly mode enabled.
    /// Returns the path to the compiled DLL (in a temp directory that caller should clean up).
    /// </summary>
    /// <param name="source">TypeScript source code</param>
    /// <returns>Tuple of (tempDir, dllPath) - caller must clean up tempDir</returns>
    public static (string tempDir, string dllPath) CompileWithRefAsm(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"sharpts_refasm_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

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

        // Use reference assembly mode
        var compiler = new ILCompiler("test", preserveConstEnums: false, useReferenceAssemblies: true, sdkPath: null);
        compiler.Compile(statements, typeMap, deadCodeInfo);
        compiler.Save(dllPath);

        // Copy SharpTS.dll for runtime dependency
        var sharpTsDll = typeof(RuntimeTypes).Assembly.Location;
        if (!string.IsNullOrEmpty(sharpTsDll) && File.Exists(sharpTsDll))
        {
            File.Copy(sharpTsDll, Path.Combine(tempDir, "SharpTS.dll"), overwrite: true);
        }

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

        return (tempDir, dllPath);
    }

    /// <summary>
    /// Executes a compiled DLL and returns its console output.
    /// </summary>
    /// <param name="dllPath">Path to the DLL</param>
    /// <returns>Console output</returns>
    public static string ExecuteCompiledDll(string dllPath)
    {
        var workingDir = Path.GetDirectoryName(dllPath)!;
        var psi = new ProcessStartInfo("dotnet", dllPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDir
        };

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Compiled program exited with code {process.ExitCode}. Stderr: {error}");
        }

        return output.Replace("\r\n", "\n");
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
