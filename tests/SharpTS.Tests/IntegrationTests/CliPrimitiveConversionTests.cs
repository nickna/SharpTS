using Xunit;

namespace SharpTS.Tests.IntegrationTests;

/// <summary>
/// Tests for String(), Number(), Boolean() conversion functions in compiled mode (Issue #27).
/// </summary>
public class CliPrimitiveConversionTests
{
    private static (int ExitCode, string StdOut) CompileAndRun(
        TempTestDirectory tempDir, string entryFile)
    {
        var compile = CliTestHelper.RunCli($"-c \"{entryFile}\"", tempDir.Path);
        if (compile.ExitCode != 0)
        {
            return (compile.ExitCode, compile.StandardOutput + compile.StandardError);
        }

        var dllName = Path.GetFileNameWithoutExtension(entryFile) + ".dll";
        var dllPath = tempDir.GetPath(dllName);
        Assert.True(File.Exists(dllPath), $"Expected output DLL at {dllPath}");

        var psi = new System.Diagnostics.ProcessStartInfo("dotnet", $"\"{dllPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = tempDir.Path
        };

        using var process = System.Diagnostics.Process.Start(psi)!;
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, stdOut + stdErr);
    }

    [Fact]
    public void Compiled_StringConversion_Number()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("test.ts", """
            const s = String(42);
            console.log(s);
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("42", output);
    }

    [Fact]
    public void Compiled_StringConversion_Boolean()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("test.ts", """
            console.log(String(true));
            console.log(String(false));
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("true", output);
        Assert.Contains("false", output);
    }

    [Fact]
    public void Compiled_StringConversion_NoArgs()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("test.ts", """
            const s = String();
            console.log(">" + s + "<");
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("><", output);
    }

    [Fact]
    public void Compiled_NumberConversion_String()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("test.ts", """
            const n = Number("3.14");
            console.log(n);
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("3.14", output);
    }

    [Fact]
    public void Compiled_NumberConversion_Boolean()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("test.ts", """
            console.log(Number(true));
            console.log(Number(false));
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("1", output);
        Assert.Contains("0", output);
    }

    [Fact]
    public void Compiled_NumberConversion_EmptyString()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("test.ts", """
            console.log(Number(""));
            console.log(Number("  "));
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        var lines = output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("0", lines[0].Trim());
        Assert.Equal("0", lines[1].Trim());
    }

    [Fact]
    public void Compiled_NumberConversion_NoArgs()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("test.ts", """
            console.log(Number());
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("0", output);
    }

    [Fact]
    public void Compiled_BooleanConversion_Falsy()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("test.ts", """
            console.log(Boolean(0));
            console.log(Boolean(""));
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        var lines = output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("false", lines[0].Trim());
        Assert.Equal("false", lines[1].Trim());
    }

    [Fact]
    public void Compiled_BooleanConversion_Truthy()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("test.ts", """
            console.log(Boolean(1));
            console.log(Boolean("hello"));
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        var lines = output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("true", lines[0].Trim());
        Assert.Equal("true", lines[1].Trim());
    }

    [Fact]
    public void Compiled_BooleanConversion_NoArgs()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var entry = tempDir.CreateFile("test.ts", """
            console.log(Boolean());
            """);

        var (exit, output) = CompileAndRun(tempDir, entry);
        Assert.Equal(0, exit);
        Assert.Contains("false", output);
    }
}
