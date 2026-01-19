using Xunit;

namespace SharpTS.Tests.IntegrationTests;

/// <summary>
/// Tests for CLI compilation flags (--compile, -c, -o, -t, etc.).
/// </summary>
public class CliCompileTests
{
    [Fact]
    public void Compile_ValidScript_ProducesDll()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("hello.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli($"--compile \"{scriptPath}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(tempDir.GetPath("hello.dll")));
        Assert.True(File.Exists(tempDir.GetPath("hello.runtimeconfig.json")));
    }

    [Fact]
    public void Compile_ShortFlag_ProducesDll()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("test.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(tempDir.GetPath("test.dll")));
    }

    [Fact]
    public void Compile_CustomOutput_ProducesDllAtPath()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("hello.ts", CliFixtures.SimpleHelloWorld);
        var outputPath = tempDir.GetPath("custom.dll");

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\" -o \"{outputPath}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath));
        // Original name should not exist
        Assert.False(File.Exists(tempDir.GetPath("hello.dll")));
    }

    [Fact]
    public void Compile_TargetExe_ProducesExe()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("app.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\" -t exe", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(tempDir.GetPath("app.exe")));
    }

    [Fact]
    public void Compile_TargetLongFlag_ProducesExe()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("app.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\" --target exe", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(tempDir.GetPath("app.exe")));
    }

    [Fact]
    public void Compile_TargetDll_ProducesDll()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("app.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\" -t dll", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(tempDir.GetPath("app.dll")));
    }

    [Fact]
    public void Compile_QuietFlag_NoSuccessMessage()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("hello.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\" --quiet", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(tempDir.GetPath("hello.dll")));
        Assert.DoesNotContain("Compiled to", result.StandardOutput);
    }

    [Fact]
    public void Compile_WithoutQuiet_ShowsSuccessMessage()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("hello.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Compiled to", result.StandardOutput);
    }

    [Fact]
    public void Compile_VerifyFlag_ValidIL()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("hello.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\" --verify", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(tempDir.GetPath("hello.dll")));
    }

    [Fact]
    public void Compile_InvalidTarget_ExitsCode64()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("hello.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\" -t invalid", tempDir.Path);

        Assert.Equal(64, result.ExitCode);
        Assert.Contains("Invalid target", result.StandardOutput);
    }

    [Fact]
    public void Compile_MissingInputFile_ExitsCode64()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();

        var result = CliTestHelper.RunCli("--compile", tempDir.Path);

        Assert.Equal(64, result.ExitCode);
        Assert.Contains("Missing input file", result.StandardOutput);
    }

    [Fact]
    public void Compile_NonexistentFile_ExitsCode1()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();

        var result = CliTestHelper.RunCli($"-c \"{tempDir.GetPath("missing.ts")}\"", tempDir.Path);

        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void Compile_TypeError_ExitsCode1()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("typeerror.ts", CliFixtures.TypeErrorScript);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\"", tempDir.Path);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Error:", result.StandardOutput);
    }

    [Fact]
    public void Compile_ParseError_ExitsCode1()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("parseerror.ts", CliFixtures.ParseErrorScript);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\"", tempDir.Path);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Error:", result.StandardOutput);
    }

    [Fact]
    public void Compile_TargetMissingValue_ExitsCode64()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("hello.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\" -t", tempDir.Path);

        Assert.Equal(64, result.ExitCode);
        Assert.Contains("requires a value", result.StandardOutput);
    }

    [Fact]
    public void Compile_NumericScript_CanExecuteOutput()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("numeric.ts", CliFixtures.NumericScript);

        // Compile
        var compileResult = CliTestHelper.RunCli($"-c \"{scriptPath}\"", tempDir.Path);
        Assert.Equal(0, compileResult.ExitCode);

        // Execute the compiled DLL
        var dllPath = tempDir.GetPath("numeric.dll");
        Assert.True(File.Exists(dllPath));

        var psi = new System.Diagnostics.ProcessStartInfo("dotnet", $"\"{dllPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = tempDir.Path
        };

        using var process = System.Diagnostics.Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("15", output); // 1+2+3+4+5 = 15
    }

    [Fact]
    public void Compile_PreserveConstEnums_Flag()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var enumScript = """
            const enum Color {
                Red = 1,
                Green = 2,
                Blue = 3
            }
            console.log(Color.Red);
            """;
        var scriptPath = tempDir.CreateFile("enum.ts", enumScript);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\" --preserveConstEnums", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(tempDir.GetPath("enum.dll")));
    }

    [Fact]
    public void Compile_RefAsm_Flag()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("hello.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\" --ref-asm", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(tempDir.GetPath("hello.dll")));
    }
}
