using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpTS.Gui;
using Xunit;

namespace SharpTS.Gui.Conformance.Tests;

public sealed class GeneratedControlContractTests
{
    [Fact]
    public void ManifestIsVersionedUniqueAndUsesOnlyReservedAdapters()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        Assert.Equal(1, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
        string[] reserved = manifest.RootElement.GetProperty("reservedAdapterIds").EnumerateArray()
            .Select(value => value.GetString()!).ToArray();
        JsonElement[] controls = manifest.RootElement.GetProperty("controls").EnumerateArray().ToArray();
        Assert.Equal(controls.Length, controls.Select(control => control.GetProperty("kind").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.All(controls, control => Assert.Contains(control.GetProperty("adapter").GetString()!, reserved));
        Assert.DoesNotContain(controls, control =>
            control.GetProperty("kind").GetString() == "Fragment");
        Assert.Null(DescriptorRegistry.Get("Fragment"));
        Assert.All(controls, control =>
        {
            string[] props = control.GetProperty("props").EnumerateArray().Select(prop => prop.GetProperty("name").GetString()!)
                .Concat(control.GetProperty("events").EnumerateArray().Select(prop => prop.GetProperty("name").GetString()!)).ToArray();
            Assert.Equal(props.Length, props.Distinct(StringComparer.Ordinal).Count());
        });
    }

    [Fact]
    public void GeneratedRuntimeSdkAndDocumentationHaveMatchingSemanticIdentityAndControlKinds()
    {
        Assert.Matches(new Regex("^[0-9a-f]{64}$"), DesktopBridge.DescriptorSchemaHash);
        Assert.Equal(1, DesktopBridge.DescriptorSchemaVersion);
        string root = FindRoot();
        string typescript = File.ReadAllText(Path.Combine(root, "SharpTS.Gui.Sdk", "GuiPackage", "control-surface.generated.ts"));
        Assert.Contains(DesktopBridge.DescriptorSchemaHash, typescript, StringComparison.Ordinal);
        using JsonDocument docs = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "SharpTS.Gui.Sdk", "GuiPackage", "control-docs.generated.json")));
        Assert.Equal(DesktopBridge.DescriptorSchemaHash, docs.RootElement.GetProperty("schemaHash").GetString());
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        Assert.Equal(
            manifest.RootElement.GetProperty("controls").EnumerateArray().Select(item => item.GetProperty("kind").GetString()).ToArray(),
            docs.RootElement.GetProperty("controls").EnumerateArray().Select(item => item.GetProperty("kind").GetString()).ToArray());
    }

    [Fact]
    public void CheckedInGeneratedOutputsPassStandaloneVerifyMode()
    {
        string root = FindRoot();
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        string generator = Path.Combine(root, "SharpTS.Gui.Generator", "bin", configuration, "net10.0", "SharpTS.Gui.Generator.dll");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(generator);
        start.ArgumentList.Add("verify");
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, output);
    }

    [Fact]
    public void CheckedInGeneratedOutputsUseUtf8WithoutBomAndCanonicalLf()
    {
        string root = FindRoot();
        string[] paths =
        [
            Path.Combine(root, "SharpTS.Gui", "Generated", "ControlContract.Generated.cs"),
            Path.Combine(root, "SharpTS.Gui.Sdk", "GuiPackage", "control-surface.generated.ts"),
            Path.Combine(root, "SharpTS.Gui.Sdk", "GuiPackage", "control-docs.generated.json"),
            Path.Combine(root, "SharpTS.Gui.Sdk", "Sdk", "DescriptorContract.Generated.props"),
        ];

        foreach (string path in paths)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Assert.NotEmpty(bytes);
            Assert.DoesNotContain((byte)'\r', bytes);
            Assert.Equal((byte)'\n', bytes[^1]);
            Assert.False(
                bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf,
                $"Generated GUI contract has a UTF-8 BOM: {Path.GetRelativePath(root, path)}");
        }
    }

    private static string ManifestPath() => Path.Combine(FindRoot(), "SharpTS.Gui", "Controls", "controls.v1.json");

    private static string FindRoot()
    {
        for (string? directory = AppContext.BaseDirectory; directory is not null; directory = Path.GetDirectoryName(directory))
            if (File.Exists(Path.Combine(directory, "SharpTS.sln"))) return directory;
        throw new InvalidOperationException("Could not locate SharpTS.sln.");
    }
}
