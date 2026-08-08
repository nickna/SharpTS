using System.Xml.Linq;
using Xunit;

namespace SharpTS.Gui.Conformance.Tests;

public sealed class ProductizationContractTests
{
    [Fact]
    public void SupportedPlatforms_DriveSdkAndAreCoveredByTheWorkflow()
    {
        string root = FindRepositoryRoot();
        string platformsPath = Path.Combine(root, "SharpTS.Gui.Sdk", "Sdk", "SupportedPlatforms.props");
        XDocument platforms = XDocument.Load(platformsPath);
        string[] runtimeIdentifiers = platforms
            .Descendants("SharpTSGuiSupportedRuntimeIdentifier")
            .Select(item => item.Attribute("Include")?.Value)
            .Where(item => item is not null)
            .Cast<string>()
            .ToArray();

        Assert.Equal(["win-x64", "win-arm64"], runtimeIdentifiers);
        string[] runtimeAssetIdentifiers = platforms
            .Descendants("SharpTSGuiRuntimeAsset")
            .Select(item => item.Element("RuntimeIdentifier")?.Value)
            .Where(item => item is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(runtimeIdentifiers, runtimeAssetIdentifiers);

        string sdkTargets = File.ReadAllText(Path.Combine(root, "SharpTS.Gui.Sdk", "Sdk", "Sdk.targets"));
        string packageProject = File.ReadAllText(Path.Combine(root, "SharpTS.Gui.Sdk", "SharpTS.Gui.Sdk.csproj"));
        string harness = File.ReadAllText(Path.Combine(root, "SharpTS.Gui.Sdk.Consumer", "Run-PackagedConsumer.ps1"));
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "windows-desktop-preview.yml"));

        Assert.Contains("SharpTSGuiSupportedRuntimeIdentifiers", sdkTargets, StringComparison.Ordinal);
        Assert.Contains("@(SharpTSGuiRuntimeAsset)", packageProject, StringComparison.Ordinal);
        Assert.Contains("SupportedPlatforms.props", harness, StringComparison.Ordinal);
        foreach (string runtimeIdentifier in runtimeIdentifiers)
            Assert.Contains($"-RuntimeIdentifier {runtimeIdentifier}", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ConformanceHooks_AreIsolatedFromThePublicPackageEntryPoint()
    {
        string root = FindRepositoryRoot();
        string packageRoot = Path.Combine(root, "SharpTS.Gui.Sdk", "GuiPackage");
        string publicEntry = File.ReadAllText(Path.Combine(packageRoot, "index.ts"));
        string conformanceEntry = File.ReadAllText(Path.Combine(packageRoot, "internal-testing.ts"));

        Assert.DoesNotContain("DesktopConformanceBridge", publicEntry, StringComparison.Ordinal);
        Assert.DoesNotContain("traceControlIdentities", publicEntry, StringComparison.Ordinal);
        Assert.Contains("DesktopConformanceBridge", conformanceEntry, StringComparison.Ordinal);
        Assert.Contains("traceControlIdentities", conformanceEntry, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory, "SharpTS.sln")))
                return directory;
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException("Could not locate the SharpTS repository root.");
    }
}
