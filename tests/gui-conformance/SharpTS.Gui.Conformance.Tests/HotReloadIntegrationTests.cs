using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace SharpTS.Gui.Conformance.Tests;

[Collection(DesktopRendererCollection.Name)]
public sealed class HotReloadIntegrationTests
{
    [Fact]
    public async Task InterpretedWatch_RemountsFreshStateAfterSourceChange()
    {
        string repositoryRoot = FindRepositoryRoot();
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        string hostSource = Path.Combine(
            repositoryRoot, "src", "SharpTS.Gui.Host", "bin", configuration, "net10.0");
        string fixture = Path.Combine(
            repositoryRoot, "tests", "gui-conformance", "SharpTS.Gui.Conformance.Tests", "Fixtures", "HotReload", "main.tsx");
        string temporaryRoot = Path.Combine(
            Path.GetTempPath(), $"sharpts-gui-hot-reload-{Guid.NewGuid():N}");
        Process? process = null;
        try
        {
            CopyDirectory(hostSource, temporaryRoot);
            string guestDirectory = Path.Combine(temporaryRoot, "Guest");
            Directory.CreateDirectory(guestDirectory);
            string entry = Path.Combine(guestDirectory, "main.tsx");
            File.Copy(fixture, entry, overwrite: true);
            GuiInterpretedTestAssets.Stage(repositoryRoot, configuration, temporaryRoot);

            string tracePath = Path.Combine(temporaryRoot, "hot-reload-trace.json");
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = temporaryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(Path.Combine(temporaryRoot, "SharpTS.Gui.Host.dll"));
            start.ArgumentList.Add("--mode");
            start.ArgumentList.Add("interpreted");
            start.ArgumentList.Add("--headless");
            start.ArgumentList.Add("--watch");
            start.ArgumentList.Add("--trace");
            start.ArgumentList.Add(tracePath);

            process = Process.Start(start)
                ?? throw new InvalidOperationException("Could not start the hot-reload GUI host.");
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string? readyLine = await process.StandardOutput.ReadLineAsync(timeout.Token);
            Assert.Equal("HOT_RELOAD_VERSION_1", readyLine);
            Assert.False(process.HasExited, "Hot-reload host exited during initial mount.");
            string source = await File.ReadAllTextAsync(entry, timeout.Token);
            Assert.Contains("const version = 1", source, StringComparison.Ordinal);
            await File.WriteAllTextAsync(
                entry,
                source.Replace("const version = 1", "const version = 2", StringComparison.Ordinal),
                timeout.Token);
            Assert.Contains("const version = 2", await File.ReadAllTextAsync(entry, timeout.Token), StringComparison.Ordinal);
            await process.WaitForExitAsync(timeout.Token);
            string output = readyLine + Environment.NewLine + await process.StandardOutput.ReadToEndAsync(timeout.Token);
            string errors = await stderr;
            string traceDebug = File.Exists(tracePath) ? await File.ReadAllTextAsync(tracePath) : "<missing>";
            Assert.True(process.ExitCode == 0,
                $"Hot-reload host failed with {process.ExitCode}.\nstdout:\n{output}\nstderr:\n{errors}\ntrace:\n{traceDebug}");

            using JsonDocument trace = JsonDocument.Parse(await File.ReadAllTextAsync(tracePath));
            string[] stages = trace.RootElement.EnumerateArray()
                .Select(item => item.GetProperty("Stage").GetString()!)
                .ToArray();
            Assert.Contains("hot-reload-watch", stages);
            Assert.Contains("hot-reload-begin", stages);
            Assert.Contains("hot-reload-end", stages);
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
                process.Dispose();
            }
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
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
