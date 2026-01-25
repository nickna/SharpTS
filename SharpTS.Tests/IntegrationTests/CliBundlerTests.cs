using SharpTS.Compilation.Bundling;
using Xunit;

namespace SharpTS.Tests.IntegrationTests;

/// <summary>
/// Integration tests for CLI bundler output and technique reporting.
/// </summary>
public class CliBundlerTests
{
    [Fact]
    public void Compile_TargetExe_WhenSdkAvailable_UsesSdkBundler()
    {
        // Skip if SDK is not available on this machine
        if (!SdkBundlerDetector.IsSdkAvailable)
        {
            return; // Can't test SDK bundler without SDK
        }

        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("app.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\" -t exe", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(tempDir.GetPath("app.exe")));

        // When SDK is available, should specifically use SDK bundler (not fall back)
        Assert.Contains("SDK bundler", result.StandardOutput);
        Assert.DoesNotContain("built-in bundler", result.StandardOutput);
    }

    [Fact]
    public void Compile_TargetExe_WhenSdkAvailable_ExeActuallyWorks()
    {
        // Skip if SDK is not available on this machine
        if (!SdkBundlerDetector.IsSdkAvailable)
        {
            return;
        }

        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("hello.ts", CliFixtures.SimpleHelloWorld);

        var compileResult = CliTestHelper.RunCli($"-c \"{scriptPath}\" -t exe", tempDir.Path);

        // Must use SDK bundler specifically
        Assert.Equal(0, compileResult.ExitCode);
        Assert.Contains("SDK bundler", compileResult.StandardOutput);

        // The exe must actually run correctly
        var exePath = tempDir.GetPath("hello.exe");
        var psi = new System.Diagnostics.ProcessStartInfo(exePath)
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
        Assert.Contains("Hello, World!", output);
    }

    [Fact]
    public void Compile_TargetExe_ShowsBundlerTechnique()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("app.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\" -t exe", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(tempDir.GetPath("app.exe")));

        // Should show which bundler was used
        Assert.Contains("Compiled to", result.StandardOutput);
        Assert.True(
            result.StandardOutput.Contains("SDK bundler") ||
            result.StandardOutput.Contains("built-in bundler"),
            $"Expected output to contain bundler technique, got: {result.StandardOutput}");
    }

    [Fact]
    public void Compile_TargetExe_QuietMode_NoBundlerMessage()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("app.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\" -t exe --quiet", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(tempDir.GetPath("app.exe")));

        // Quiet mode should not show any output
        Assert.DoesNotContain("Compiled to", result.StandardOutput);
        Assert.DoesNotContain("bundler", result.StandardOutput);
    }

    [Fact]
    public void Compile_TargetExe_CanExecuteOutput()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("hello.ts", CliFixtures.SimpleHelloWorld);

        // Compile to exe
        var compileResult = CliTestHelper.RunCli($"-c \"{scriptPath}\" -t exe", tempDir.Path);
        Assert.Equal(0, compileResult.ExitCode);

        var exePath = tempDir.GetPath("hello.exe");
        Assert.True(File.Exists(exePath));

        // Execute the compiled exe
        var psi = new System.Diagnostics.ProcessStartInfo(exePath)
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
        Assert.Contains("Hello, World!", output);
    }

    [Fact]
    public void Compile_TargetExe_NumericScript_CanExecuteOutput()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("numeric.ts", CliFixtures.NumericScript);

        // Compile to exe
        var compileResult = CliTestHelper.RunCli($"-c \"{scriptPath}\" -t exe", tempDir.Path);
        Assert.Equal(0, compileResult.ExitCode);

        var exePath = tempDir.GetPath("numeric.exe");
        Assert.True(File.Exists(exePath));

        // Execute the compiled exe
        var psi = new System.Diagnostics.ProcessStartInfo(exePath)
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
    public void Compile_TargetDll_DoesNotShowBundlerTechnique()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("app.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\" -t dll", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(tempDir.GetPath("app.dll")));

        // DLL output should not mention bundler technique
        Assert.DoesNotContain("bundler", result.StandardOutput);
    }

    [Fact]
    public void Compile_DefaultTarget_DoesNotShowBundlerTechnique()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("app.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(tempDir.GetPath("app.dll")));

        // Default (DLL) output should not mention bundler technique
        Assert.DoesNotContain("bundler", result.StandardOutput);
    }

    [Fact]
    public void Compile_TargetExe_CustomOutput_ShowsBundlerTechnique()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("app.ts", CliFixtures.SimpleHelloWorld);
        var outputPath = tempDir.GetPath("custom.exe");

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\" -t exe -o \"{outputPath}\"", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(outputPath));
        Assert.Contains("custom.exe", result.StandardOutput);
        Assert.True(
            result.StandardOutput.Contains("SDK bundler") ||
            result.StandardOutput.Contains("built-in bundler"));
    }

    [Fact]
    public void Compile_TargetExe_BundlerSdk_UsesSdkBundler()
    {
        // Skip if SDK is not available on this machine
        if (!SdkBundlerDetector.IsSdkAvailable)
        {
            return; // Can't test SDK bundler without SDK
        }

        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("app.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\" -t exe --bundler sdk", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(tempDir.GetPath("app.exe")));

        // When --bundler sdk is specified, must use SDK bundler
        Assert.Contains("SDK bundler", result.StandardOutput);
        Assert.DoesNotContain("built-in bundler", result.StandardOutput);
    }

    [Fact]
    public void Compile_TargetExe_BundlerBuiltin_UsesBuiltinBundler()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("app.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\" -t exe --bundler builtin", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(tempDir.GetPath("app.exe")));

        // When --bundler builtin is specified, must use built-in bundler
        Assert.Contains("built-in bundler", result.StandardOutput);
        Assert.DoesNotContain("SDK bundler", result.StandardOutput);
    }

    [Fact]
    public void Compile_TargetExe_BundlerBuiltin_ExeActuallyWorks()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("hello.ts", CliFixtures.SimpleHelloWorld);

        var compileResult = CliTestHelper.RunCli($"-c \"{scriptPath}\" -t exe --bundler builtin", tempDir.Path);

        // Must use built-in bundler
        Assert.Equal(0, compileResult.ExitCode);
        Assert.Contains("built-in bundler", compileResult.StandardOutput);

        // The exe must actually run correctly
        var exePath = tempDir.GetPath("hello.exe");
        var psi = new System.Diagnostics.ProcessStartInfo(exePath)
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
        Assert.Contains("Hello, World!", output);
    }

    [Fact]
    public void Compile_TargetExe_BundlerAuto_WorksLikeDefault()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("app.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\" -t exe --bundler auto", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(tempDir.GetPath("app.exe")));

        // Auto mode should show which bundler was used (same as default)
        Assert.True(
            result.StandardOutput.Contains("SDK bundler") ||
            result.StandardOutput.Contains("built-in bundler"));
    }

    [Fact]
    public void Compile_BundlerInvalid_ShowsError()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("app.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\" -t exe --bundler invalid", tempDir.Path);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Invalid bundler", result.StandardOutput);
        Assert.Contains("auto", result.StandardOutput);
        Assert.Contains("sdk", result.StandardOutput);
        Assert.Contains("builtin", result.StandardOutput);
    }

    [Fact]
    public void Compile_BundlerWithoutValue_ShowsError()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("app.ts", CliFixtures.SimpleHelloWorld);

        var result = CliTestHelper.RunCli($"-c \"{scriptPath}\" -t exe --bundler", tempDir.Path);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("--bundler requires a value", result.StandardOutput);
    }
}
