using System.Diagnostics;
using System.Reflection;

namespace SharpTS.Tests.IntegrationTests;

/// <summary>
/// Test infrastructure for end-to-end CLI tests.
/// Spawns actual SharpTS processes and captures output for assertions.
/// </summary>
public static class CliTestHelper
{
    /// <summary>
    /// Default timeout for CLI operations (30 seconds).
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Result from running the CLI.
    /// </summary>
    public record CliResult(int ExitCode, string StandardOutput, string StandardError);

    /// <summary>
    /// Runs the SharpTS CLI with the given arguments.
    /// </summary>
    /// <param name="arguments">Command-line arguments</param>
    /// <param name="workingDirectory">Optional working directory (defaults to temp)</param>
    /// <param name="timeout">Optional timeout (defaults to 30 seconds)</param>
    /// <returns>CLI result with exit code and output</returns>
    public static CliResult RunCli(string arguments, string? workingDirectory = null, TimeSpan? timeout = null)
    {
        var sharpTsDll = FindSharpTsDll();
        var effectiveTimeout = timeout ?? DefaultTimeout;
        workingDirectory ??= Path.GetTempPath();

        var psi = new ProcessStartInfo("dotnet", $"\"{sharpTsDll}\" {arguments}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        if (!process.WaitForExit((int)effectiveTimeout.TotalMilliseconds))
        {
            process.Kill();
            throw new TimeoutException(
                $"CLI execution exceeded {effectiveTimeout.TotalSeconds}s timeout. " +
                $"Arguments: {arguments}");
        }

        return new CliResult(process.ExitCode, NormalizeOutput(stdout), NormalizeOutput(stderr));
    }

    /// <summary>
    /// Finds the SharpTS.dll relative to the test assembly location.
    /// </summary>
    private static string FindSharpTsDll()
    {
        // Get the test assembly location
        var testAssemblyPath = Assembly.GetExecutingAssembly().Location;
        var testDir = Path.GetDirectoryName(testAssemblyPath)!;

        // SharpTS.dll should be in the same output directory
        var sharpTsDll = Path.Combine(testDir, "SharpTS.dll");
        if (File.Exists(sharpTsDll))
        {
            return sharpTsDll;
        }

        // Fallback: search up the directory tree
        var dir = testDir;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "SharpTS.dll");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            $"Could not find SharpTS.dll. Test assembly location: {testAssemblyPath}");
    }

    /// <summary>
    /// Creates an isolated temporary directory for test files.
    /// </summary>
    public static TempTestDirectory CreateTempDirectory()
    {
        return new TempTestDirectory();
    }

    /// <summary>
    /// Normalizes line endings for cross-platform test consistency.
    /// </summary>
    public static string NormalizeOutput(string output)
    {
        return output.Replace("\r\n", "\n");
    }
}

/// <summary>
/// Temporary test directory that cleans up on disposal.
/// </summary>
public sealed class TempTestDirectory : IDisposable
{
    /// <summary>
    /// The absolute path to the temporary directory.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Creates a new isolated temp directory with a GUID-based name.
    /// </summary>
    public TempTestDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"sharpts_cli_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(Path);
    }

    /// <summary>
    /// Creates a file in the temp directory with the given content.
    /// </summary>
    /// <param name="relativePath">Relative path within the temp directory</param>
    /// <param name="content">File content</param>
    /// <returns>Absolute path to the created file</returns>
    public string CreateFile(string relativePath, string content)
    {
        var fullPath = GetPath(relativePath);
        var dir = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    /// <summary>
    /// Gets the absolute path for a relative path within the temp directory.
    /// </summary>
    public string GetPath(string relativePath)
    {
        return System.IO.Path.Combine(Path, relativePath);
    }

    /// <summary>
    /// Cleans up the temporary directory.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}

/// <summary>
/// Test fixture scripts for CLI tests.
/// </summary>
public static class CliFixtures
{
    /// <summary>
    /// Simple Hello World script.
    /// </summary>
    public const string SimpleHelloWorld = """
        console.log("Hello, World!");
        """;

    /// <summary>
    /// Script with a type error.
    /// </summary>
    public const string TypeErrorScript = """
        let x: number = "not a number";
        """;

    /// <summary>
    /// Script with a parse error.
    /// </summary>
    public const string ParseErrorScript = """
        function { broken syntax
        """;

    /// <summary>
    /// Script that outputs process.argv.
    /// </summary>
    public const string ProcessArgvScript = """
        for (const arg of process.argv) {
            console.log(arg);
        }
        """;

    /// <summary>
    /// Script that just outputs argv length and args (excluding runtime/script paths).
    /// </summary>
    public const string ProcessArgvArgsOnlyScript = """
        const args = process.argv.slice(2);
        console.log("arg count: " + args.length);
        for (const arg of args) {
            console.log("arg: " + arg);
        }
        """;

    /// <summary>
    /// Simple script that returns a value for decorator testing.
    /// </summary>
    public const string DecoratorTestScript = """
        function log(target: any, key: string) {
            console.log("decorated: " + key);
        }

        class Test {
            @log
            greet(): void {
                console.log("hello");
            }
        }

        const t = new Test();
        t.greet();
        """;

    /// <summary>
    /// Simple numeric computation for testing compilation.
    /// </summary>
    public const string NumericScript = """
        let sum: number = 0;
        for (let i: number = 1; i <= 5; i = i + 1) {
            sum = sum + i;
        }
        console.log(sum);
        """;
}
