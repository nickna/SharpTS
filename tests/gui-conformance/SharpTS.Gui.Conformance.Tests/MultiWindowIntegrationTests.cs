using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace SharpTS.Gui.Conformance.Tests;

[Collection(DesktopRendererCollection.Name)]
public sealed class MultiWindowIntegrationTests
{
    [Fact]
    public async Task OwnedWindowsAndIsolatedFailuresMatchInBothGuestModes()
    {
        string repositoryRoot = FindRepositoryRoot();
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        string hostSource = Path.Combine(
            repositoryRoot, "tests", "gui-conformance", "SharpTS.Gui.ConformanceHost", "bin", configuration, "net10.0");
        string compiledGuest = Path.Combine(
            repositoryRoot, "tests", "gui-conformance", "SharpTS.Gui.Conformance.Tests", "obj", configuration,
            "net10.0", ".sharpts-gui-conformance", "MultiWindow.Guest.dll");
        string fixture = Path.Combine(
            repositoryRoot, "tests", "gui-conformance", "SharpTS.Gui.Conformance.Tests", "Fixtures", "MultiWindow", "main.tsx");
        Assert.True(File.Exists(compiledGuest), $"Multi-window guest was not built: {compiledGuest}");

        string temporaryRoot = Path.Combine(
            Path.GetTempPath(), $"sharpts-gui-multi-window-{Guid.NewGuid():N}");
        try
        {
            CopyDirectory(hostSource, temporaryRoot);
            Directory.CreateDirectory(Path.Combine(temporaryRoot, "Guest"));
            File.Copy(fixture, Path.Combine(temporaryRoot, "Guest", "main.tsx"), overwrite: true);
            File.Copy(compiledGuest, Path.Combine(temporaryRoot, "MultiWindow.Guest.dll"), overwrite: true);
            await File.WriteAllTextAsync(
                Path.Combine(temporaryRoot, ".sharpts", "app.json"),
                """
                {
                  "entryPath": "Guest/main.tsx",
                  "compiledAssembly": "MultiWindow.Guest.dll",
                  "hostedAbiVersion": 1,
                  "guiApiVersion": 1,
                  "descriptorSchemaVersion": 1,
                  "descriptorSchemaHash": "9d07a1a4b39807ac966b79f227cf81f012bd735c41c1cc340ea446bc69d48d27"
                }
                """);

            string[] interpreted = await RunAsync(temporaryRoot, "interpreted");
            string[] compiled = await RunAsync(temporaryRoot, "compiled");
            string[] expected =
            [
                "multi-window-platform-services",
                "multi-window-notification",
                "multi-window-mounted",
                "multi-window-secondary-closed",
                "multi-window-drop",
                "multi-window-advanced-surface",
                "multi-window-style-applied",
                "multi-window-style-retained",
                "multi-window-isolated-error",
                "multi-window-main-retained",
            ];
            Assert.Equal(expected, interpreted.Where(stage => stage.StartsWith("multi-window-", StringComparison.Ordinal)));
            Assert.Equal(expected, compiled.Where(stage => stage.StartsWith("multi-window-", StringComparison.Ordinal)));
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static async Task<string[]> RunAsync(string directory, string mode)
    {
        string tracePath = Path.Combine(directory, $"trace-{mode}.json");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Path.Combine(directory, "SharpTS.Gui.ConformanceHost.dll"));
        start.ArgumentList.Add("--mode");
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add("--headless");
        start.ArgumentList.Add("--trace");
        start.ArgumentList.Add(tracePath);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the multi-window GUI host.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == 0,
            $"GUI {mode} multi-window host failed with {process.ExitCode}.\n" +
            $"stdout:\n{await stdout}\nstderr:\n{await stderr}");

        using JsonDocument trace = JsonDocument.Parse(await File.ReadAllTextAsync(tracePath));
        return trace.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("Stage").GetString()!)
            .ToArray();
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (string directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
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
}
