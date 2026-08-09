using System.Diagnostics;
using Xunit;

namespace SharpTS.Gui.Conformance.Tests;

[Collection(DesktopRendererCollection.Name)]
public sealed class VisualRegressionIntegrationTests
{
    [Fact]
    public async Task HeadlessHost_CapturesAndVerifiesSkiaPngBaseline()
    {
        string repositoryRoot = FindRepositoryRoot();
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        string hostSource = Path.Combine(
            repositoryRoot, "SharpTS.Gui.Host", "bin", configuration, "net10.0");
        string fixture = Path.Combine(
            repositoryRoot,
            "SharpTS.Gui.Conformance.Tests",
            "Fixtures",
            "VisualRegression",
            "main.tsx");
        string temporaryRoot = Path.Combine(Path.GetTempPath(), $"sharpts-gui-visual-{Guid.NewGuid():N}");
        Process? process = null;
        try
        {
            CopyDirectory(hostSource, temporaryRoot);
            string guestDirectory = Path.Combine(temporaryRoot, "Guest");
            Directory.CreateDirectory(guestDirectory);
            File.Copy(fixture, Path.Combine(guestDirectory, "main.tsx"), overwrite: true);
            GuiInterpretedTestAssets.Stage(repositoryRoot, configuration, temporaryRoot);

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
            process = Process.Start(start)
                ?? throw new InvalidOperationException("Could not start the visual-regression GUI host.");
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            string output = await outputTask;
            string errors = await errorTask;
            Assert.True(process.ExitCode == 0,
                $"Visual-regression host failed with {process.ExitCode}.\nstdout:\n{output}\nstderr:\n{errors}");
            Assert.Contains("VISUAL_SNAPSHOT_", output, StringComparison.Ordinal);
            byte[] png = await File.ReadAllBytesAsync(Path.Combine(temporaryRoot, "visual-baseline.png"), timeout.Token);
            Assert.True(png.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }));
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
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
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
