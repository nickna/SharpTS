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
        XElement[] runtimeItems = platforms
            .Descendants("SharpTSGuiSupportedRuntimeIdentifier")
            .ToArray();
        string[] runtimeIdentifiers = runtimeItems
            .Select(item => item.Attribute("Include")?.Value)
            .Where(item => item is not null)
            .Cast<string>()
            .ToArray();

        Assert.Equal(["win-x64", "win-arm64", "osx-x64", "osx-arm64"], runtimeIdentifiers);
        Assert.Equal(
            ["win-x64", "win-arm64", "osx", "osx"],
            runtimeItems.Select(item => item.Element("RuntimeAssetDirectory")?.Value ?? string.Empty).ToArray());
        string[] runtimeAssetIdentifiers = platforms
            .Descendants("SharpTSGuiRuntimeAsset")
            .Select(item => item.Element("RuntimeIdentifier")?.Value)
            .Where(item => item is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["win-x64", "win-arm64", "osx"], runtimeAssetIdentifiers);

        string sdkTargets = File.ReadAllText(Path.Combine(root, "SharpTS.Gui.Sdk", "Sdk", "Sdk.targets"));
        string packageProject = File.ReadAllText(Path.Combine(root, "SharpTS.Gui.Sdk", "SharpTS.Gui.Sdk.csproj"));
        string harness = File.ReadAllText(Path.Combine(root, "SharpTS.Gui.Sdk.Consumer", "Run-PackagedConsumer.ps1"));
        string windowsWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "windows-desktop-preview.yml"));
        string macOsWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "macos-desktop-preview.yml"));

        Assert.Contains("SharpTSGuiSupportedRuntimeIdentifiers", sdkTargets, StringComparison.Ordinal);
        Assert.Contains(
            "<PrepareResourceNamesDependsOn>SharpTSGuiCompile;$(PrepareResourceNamesDependsOn)</PrepareResourceNamesDependsOn>",
            sdkTargets,
            StringComparison.Ordinal);
        Assert.Contains("@(SharpTSGuiRuntimeAsset)", packageProject, StringComparison.Ordinal);
        Assert.Contains("SupportedPlatforms.props", harness, StringComparison.Ordinal);
        Assert.Contains("CandidatePackage", harness, StringComparison.Ordinal);
        Assert.Contains("gui-sdk-candidate", windowsWorkflow, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(windowsWorkflow, "-CandidatePackage artifacts/windows-preview/candidate/"));
        Assert.Contains("-RuntimeIdentifier win-x64", windowsWorkflow, StringComparison.Ordinal);
        Assert.Contains("-RuntimeIdentifier win-arm64", windowsWorkflow, StringComparison.Ordinal);
        Assert.Contains("gui-sdk-candidate", macOsWorkflow, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(macOsWorkflow, "-CandidatePackage artifacts/macos-preview/candidate/"));
        Assert.Contains("rid: osx-x64", macOsWorkflow, StringComparison.Ordinal);
        Assert.Contains("rid: osx-arm64", macOsWorkflow, StringComparison.Ordinal);
        Assert.Contains("-RealWindow", macOsWorkflow, StringComparison.Ordinal);
        Assert.Contains("package-gui-macos.ps1", macOsWorkflow, StringComparison.Ordinal);

        string macOsDistributionWorkflow = File.ReadAllText(
            Path.Combine(root, ".github", "workflows", "macos-gui-distribution.yml"));
        Assert.Contains("environment: macos-gui-distribution", macOsDistributionWorkflow, StringComparison.Ordinal);
        Assert.Contains("-RequireSigned", macOsDistributionWorkflow, StringComparison.Ordinal);
        Assert.Contains("-RequireNotarized", macOsDistributionWorkflow, StringComparison.Ordinal);
        Assert.Contains("notarytool store-credentials", macOsDistributionWorkflow, StringComparison.Ordinal);
    }

    [Fact]
    public void SupportedTestingIsExportedAndPrivilegedConformanceHooksAreNotPackaged()
    {
        string root = FindRepositoryRoot();
        string packageRoot = Path.Combine(root, "SharpTS.Gui.Sdk", "GuiPackage");
        string publicEntry = File.ReadAllText(Path.Combine(packageRoot, "index.ts"));
        string testingEntry = File.ReadAllText(Path.Combine(packageRoot, "testing.ts"));
        string devtoolsEntry = File.ReadAllText(Path.Combine(packageRoot, "devtools.ts"));
        string packageManifest = File.ReadAllText(Path.Combine(packageRoot, "package.json"));

        Assert.DoesNotContain("DesktopConformanceBridge", publicEntry, StringComparison.Ordinal);
        Assert.DoesNotContain("traceControlIdentities", publicEntry, StringComparison.Ordinal);
        Assert.Contains("DesktopTestingBridge", testingEntry, StringComparison.Ordinal);
        Assert.DoesNotContain("FailNextNativeSetter", testingEntry, StringComparison.Ordinal);
        Assert.Contains("inspectDesktopTree", devtoolsEntry, StringComparison.Ordinal);
        Assert.Contains("assertHeadlessSnapshot", devtoolsEntry, StringComparison.Ordinal);
        Assert.Contains("\"./devtools\"", packageManifest, StringComparison.Ordinal);
        Assert.Contains("\"./testing\"", packageManifest, StringComparison.Ordinal);
        Assert.DoesNotContain("internal-testing", packageManifest, StringComparison.Ordinal);
        Assert.DoesNotContain("conformance", packageManifest, StringComparison.Ordinal);
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

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }
}
