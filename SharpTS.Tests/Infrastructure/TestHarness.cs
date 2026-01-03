using System.Diagnostics;
using SharpTS.Compilation;
using SharpTS.Execution;
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
                checker.Check(statements);

                var interpreter = new Interpreter();
                interpreter.Interpret(statements);

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
            checker.Check(statements);

            var compiler = new ILCompiler("test");
            compiler.Compile(statements, checker);
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
