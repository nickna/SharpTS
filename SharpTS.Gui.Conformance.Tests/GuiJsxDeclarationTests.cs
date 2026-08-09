using System.Diagnostics;
using Xunit;

namespace SharpTS.Gui.Conformance.Tests;

public sealed class GuiJsxDeclarationTests
{
    [Fact]
    public async Task PositiveGuiAndKeyedComponentUsageTypeChecks()
    {
        ProcessResult result = await CheckAsync("positive");
        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Fact]
    public async Task InvalidChildrenRequiredPropsAndRefsAreRejected()
    {
        ProcessResult result = await CheckAsync("negative");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Property 'onPress' is missing", result.Output, StringComparison.Ordinal);
        Assert.Contains("Element' is not assignable", result.Output, StringComparison.Ordinal);
        Assert.Contains("Element[]' is not assignable", result.Output, StringComparison.Ordinal);
        Assert.Contains("__textBlockHandle", result.Output, StringComparison.Ordinal);
        Assert.Contains("__buttonHandle", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClassAlternateChildrenUnionGenericCallableAndOverloadContractsTypeCheck()
    {
        ProcessResult result = await CheckAsync("advanced-positive");
        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Fact]
    public async Task InvalidClassChildrenUnionCallableAndAsyncComponentsAreRejected()
    {
        ProcessResult result = await CheckAsync("advanced-negative");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("async components are not supported", result.Output, StringComparison.Ordinal);
        Assert.Contains("union constituent", result.Output, StringComparison.Ordinal);
        Assert.Contains("No overload matches", result.Output, StringComparison.Ordinal);
    }

    private static async Task<ProcessResult> CheckAsync(string fixture)
    {
        string root = FindRepositoryRoot();
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        string compiler = Path.Combine(root, "bin", configuration, "net10.0", "SharpTS.dll");
        string bridge = Path.Combine(root, "SharpTS.Gui", "bin", configuration, "net10.0", "SharpTS.Gui.dll");
        string config = Path.Combine(root, "SharpTS.Gui.Conformance.Tests", "Fixtures", "GuiTyping", fixture, "tsconfig.json");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(compiler);
        start.ArgumentList.Add("-p");
        start.ArgumentList.Add(config);
        start.ArgumentList.Add("-r");
        start.ArgumentList.Add(bridge);
        start.ArgumentList.Add("--noEmit");

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the SharpTS JSX checker.");
        string stdout = await process.StandardOutput.ReadToEndAsync();
        string stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, stdout + stderr);
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory, "SharpTS.sln"))) return directory;
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException("Could not locate the SharpTS repository root.");
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
