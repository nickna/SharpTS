using System.Text.Json;
using SharpTS.Gui.Host;
using Xunit;

namespace SharpTS.Gui.Conformance.Tests;

[Collection(DesktopRendererCollection.Name)]
public sealed class HostInfrastructureTests
{
    [Fact]
    public void OptionParser_ParsesSupportedModesAndRejectsIncompleteOptions()
    {
        HostOptions options = HostOptionsParser.Parse(
            ["--mode", "compiled", "--headless", "--auto-close", "--trace", "trace.json"],
            GuestMode.Interpreted);

        Assert.Equal(GuestMode.Compiled, options.Mode);
        Assert.True(options.Headless);
        Assert.True(options.AutoClose);
        Assert.Equal("trace.json", options.TracePath);
        Assert.Throws<ArgumentException>(() =>
            HostOptionsParser.Parse(["--mode"], GuestMode.Interpreted));
        Assert.Throws<ArgumentException>(() =>
            HostOptionsParser.Parse(["--unknown"], GuestMode.Interpreted));
    }

    [Fact]
    public void PayloadLoader_RejectsEscapingPathsAndAbiMismatches()
    {
        string root = Path.Combine(Path.GetTempPath(), "sharpts-gui-payload-" + Guid.NewGuid().ToString("N"));
        string metadata = Path.Combine(root, ".sharpts");
        Directory.CreateDirectory(metadata);
        try
        {
            string contained = GuiPayloadLoader.ResolvePath(root, "Guest/main.tsx");
            Assert.StartsWith(Path.GetFullPath(root), contained, StringComparison.OrdinalIgnoreCase);
            Assert.Throws<InvalidOperationException>(() =>
                GuiPayloadLoader.ResolvePath(root, "../outside.tsx"));

            File.WriteAllText(
                Path.Combine(metadata, "app.json"),
                JsonSerializer.Serialize(new
                {
                    EntryPath = "Guest/main.tsx",
                    CompiledAssembly = "SharpTS.Gui.Guest.dll",
                    HostedAbiVersion = int.MaxValue,
                    GuiApiVersion = 1
                }));
            Assert.Throws<InvalidOperationException>(() => GuiPayloadLoader.LoadFile(root));

            File.WriteAllText(
                Path.Combine(metadata, "app.json"),
                JsonSerializer.Serialize(new
                {
                    EntryPath = "Guest/main.tsx",
                    CompiledAssembly = "SharpTS.Gui.Guest.dll",
                    HostedAbiVersion = 1,
                    GuiApiVersion = int.MaxValue
                }));
            Assert.Throws<InvalidOperationException>(() => GuiPayloadLoader.LoadFile(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WindowsDiagnostics_UsesConfiguredLogWithoutOwningHostPolicy()
    {
        string root = Path.Combine(Path.GetTempPath(), "sharpts-gui-diagnostics-" + Guid.NewGuid().ToString("N"));
        string logPath = Path.Combine(root, "error.log");
        string? previous = Environment.GetEnvironmentVariable("SHARPTS_GUI_ERROR_LOG");
        Environment.SetEnvironmentVariable("SHARPTS_GUI_ERROR_LOG", logPath);
        try
        {
            var diagnostics = new WindowsFatalDiagnostics();
            Assert.Equal(logPath, diagnostics.TryWriteLog(new InvalidOperationException("expected failure")));
            Assert.Contains("expected failure", File.ReadAllText(logPath), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SHARPTS_GUI_ERROR_LOG", previous);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
