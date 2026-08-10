using System.Text.Json;
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

        Assert.Equal(["win-x64", "win-arm64", "osx-arm64"], runtimeIdentifiers);
        Assert.Equal(
            ["win-x64", "win-arm64", "osx"],
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
        string desktopWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "desktop-gui.yml"));
        XDocument guiVersionProps = XDocument.Load(Path.Combine(root, "eng", "GuiVersion.props"));
        string guiVersion = Assert.Single(guiVersionProps.Descendants("SharpTSGuiSdkVersion")).Value;
        string marketingVersion = Assert.Single(guiVersionProps.Descendants("SharpTSGuiMarketingVersion")).Value;

        Assert.Contains("SharpTSGuiSupportedRuntimeIdentifiers", sdkTargets, StringComparison.Ordinal);
        Assert.Contains(
            "<PrepareResourceNamesDependsOn>SharpTSGuiCompile;$(PrepareResourceNamesDependsOn)</PrepareResourceNamesDependsOn>",
            sdkTargets,
            StringComparison.Ordinal);
        Assert.Contains("@(SharpTSGuiRuntimeAsset)", packageProject, StringComparison.Ordinal);
        Assert.Contains("SupportedPlatforms.props", harness, StringComparison.Ordinal);
        Assert.Contains("CandidatePackage", harness, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(
            harness,
            "Get-Item -LiteralPath $generatedProject -Force"));
        Assert.Contains(
            "Get-ChildItem -LiteralPath (Join-Path $cliRoot \".sharpts\\gui\\obj\") -Force",
            harness,
            StringComparison.Ordinal);
        Assert.EndsWith(
            "Write-Host \"SharpTS.Gui.Sdk packaged consumer verification passed for $RuntimeIdentifier.\"\nexit 0\n",
            harness.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
        Assert.Contains("gui-sdk-candidate", desktopWorkflow, StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(desktopWorkflow, "needs.gui-sdk-candidate.outputs.package_file_name"));
        Assert.Contains("steps.gui_version.outputs.package_file_name", desktopWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain($"SharpTS.Gui.Sdk.{guiVersion}.nupkg", desktopWorkflow, StringComparison.Ordinal);
        Assert.Contains("-RuntimeIdentifier win-x64", desktopWorkflow, StringComparison.Ordinal);
        Assert.Contains("-RuntimeIdentifier win-arm64", desktopWorkflow, StringComparison.Ordinal);
        Assert.Contains("-RuntimeIdentifier osx-arm64", desktopWorkflow, StringComparison.Ordinal);
        Assert.Contains("needs.gui-sdk-candidate.outputs.marketing_version", desktopWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain($"-ShortVersion {marketingVersion}", desktopWorkflow, StringComparison.Ordinal);
        Assert.Contains("runs-on: windows-11-vs2026-arm", desktopWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("self-hosted", desktopWorkflow, StringComparison.Ordinal);
        Assert.Contains("-RealWindow", desktopWorkflow, StringComparison.Ordinal);
        Assert.Contains("package-gui-macos.ps1", desktopWorkflow, StringComparison.Ordinal);
        int windowsJobStart = desktopWorkflow.IndexOf("  windows-x64:", StringComparison.Ordinal);
        int windowsJobEnd = desktopWorkflow.IndexOf("  windows-arm64-cross-publish:", StringComparison.Ordinal);
        string windowsJob = desktopWorkflow[windowsJobStart..windowsJobEnd];
        Assert.DoesNotContain("HostedInterpreterRuntimeTests", windowsJob, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet test SharpTS.Gui.Conformance.Tests", windowsJob, StringComparison.Ordinal);

        string macOsDistributionWorkflow = File.ReadAllText(
            Path.Combine(root, ".github", "workflows", "macos-gui-distribution.yml"));
        string reusableMacOsDistributionWorkflow = File.ReadAllText(
            Path.Combine(root, ".github", "workflows", "reusable-macos-gui-distribution.yml"));
        Assert.Contains("uses: ./.github/workflows/reusable-macos-gui-distribution.yml", macOsDistributionWorkflow, StringComparison.Ordinal);
        Assert.Contains("payload_artifact: macos-gui-certification-payload", macOsDistributionWorkflow, StringComparison.Ordinal);
        Assert.Contains("environment: ${{ inputs.environment_name }}", reusableMacOsDistributionWorkflow, StringComparison.Ordinal);
        Assert.Contains("-RequireSigned", reusableMacOsDistributionWorkflow, StringComparison.Ordinal);
        Assert.Contains("-RequireNotarized", reusableMacOsDistributionWorkflow, StringComparison.Ordinal);
        Assert.Contains("notarytool store-credentials", reusableMacOsDistributionWorkflow, StringComparison.Ordinal);

        string windowsDistributionWorkflow = File.ReadAllText(
            Path.Combine(root, ".github", "workflows", "windows-gui-distribution.yml"));
        string reusableWindowsDistributionWorkflow = File.ReadAllText(
            Path.Combine(root, ".github", "workflows", "reusable-windows-gui-distribution.yml"));
        Assert.Contains("uses: ./.github/workflows/reusable-windows-gui-distribution.yml", windowsDistributionWorkflow, StringComparison.Ordinal);
        Assert.Contains("payload_artifact: windows-gui-certification-payload", windowsDistributionWorkflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/distribution-input/publish", reusableWindowsDistributionWorkflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/distribution-input/assets", reusableWindowsDistributionWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Create workflow-only icon assets", reusableWindowsDistributionWorkflow, StringComparison.Ordinal);

        string publishWorkflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "publish.yml"));
        Assert.Contains("rid: osx-arm64", publishWorkflow, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(publishWorkflow, "rid: osx-arm64"));
        Assert.Contains("./scripts/sync-gui-version.ps1 -Version", publishWorkflow, StringComparison.Ordinal);
        Assert.Contains("./scripts/sync-gui-version.ps1 -Check -Version", publishWorkflow, StringComparison.Ordinal);
        Assert.Contains("SharpTS.Gui.Sdk.${{ steps.version.outputs.VERSION }}.nupkg", publishWorkflow, StringComparison.Ordinal);
        Assert.Contains("SharpTS.Gui.Sdk.${{ needs.build.outputs.version }}.nupkg", publishWorkflow, StringComparison.Ordinal);
        Assert.Contains("-p:SharpTSGuiHostLibrary=true", publishWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("SharpTSGuiSkipPack", publishWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-WebRequest", publishWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("gui_package_filename", publishWorkflow, StringComparison.Ordinal);
    }

    [Fact]
    public void SupportedTestingIsExportedAndPrivilegedConformanceHooksAreNotPackaged()
    {
        string root = FindRepositoryRoot();
        string packageRoot = Path.Combine(root, "SharpTS.Gui.Sdk", "GuiPackage");
        string testingEntry = File.ReadAllText(Path.Combine(packageRoot, "testing.ts"));
        string devtoolsEntry = File.ReadAllText(Path.Combine(packageRoot, "devtools.ts"));
        using JsonDocument packageManifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(packageRoot, "package.json")));
        Dictionary<string, string> exports = packageManifest.RootElement.GetProperty("exports")
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.Ordinal);

        Assert.Equal(
            [".", "./devtools", "./jsx-dev-runtime", "./jsx-runtime", "./testing"],
            exports.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("./index.ts", exports["."]);
        Assert.Equal("./devtools.ts", exports["./devtools"]);
        Assert.Equal("./jsx-dev-runtime.ts", exports["./jsx-dev-runtime"]);
        Assert.Equal("./jsx-runtime.ts", exports["./jsx-runtime"]);
        Assert.Equal("./testing.ts", exports["./testing"]);

        string[] expectedGuiPayload =
        [
            "control-docs.generated.json",
            "control-surface.generated.ts",
            "devtools.ts",
            "index.ts",
            "jsx-dev-runtime.ts",
            "jsx-runtime.ts",
            "package.json",
            "runtime-types.ts",
            "runtime.ts",
            "testing.ts",
        ];
        string[] actualGuiPayload = Directory.EnumerateFiles(packageRoot, "*", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileName(path)!)
            .Where(name => name != "tsconfig.overlay.json")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedGuiPayload, actualGuiPayload);

        Assert.Contains("DesktopTestingBridge", testingEntry, StringComparison.Ordinal);
        Assert.Contains("inspectDesktopTree", devtoolsEntry, StringComparison.Ordinal);
        Assert.Contains("assertHeadlessSnapshot", devtoolsEntry, StringComparison.Ordinal);

        string packageProject = File.ReadAllText(Path.Combine(root, "SharpTS.Gui.Sdk", "SharpTS.Gui.Sdk.csproj"));
        Assert.Contains("Exclude=\"GuiPackage\\tsconfig.overlay.json\"", packageProject, StringComparison.Ordinal);
        Assert.DoesNotContain("SharpTS.Gui.ConformanceSupport", packageProject, StringComparison.Ordinal);
        Assert.DoesNotContain("SharpTS.Gui.Conformance.Tests", packageProject, StringComparison.Ordinal);

        XDocument packageProjectXml = XDocument.Parse(packageProject);
        Assert.Equal(
            "true",
            Assert.Single(packageProjectXml.Descendants("NoDefaultExcludes")).Value);
        XElement templateManifestItem = Assert.Single(
            packageProjectXml.Descendants("None"),
            item => item.Attribute("Include")?.Value ==
                "$(MSBuildProjectDirectory)/Templates/sharpts-gui/.template.config/template.json");
        Assert.Equal("true", templateManifestItem.Attribute("Pack")?.Value);
        Assert.Equal(
            "content/Templates/sharpts-gui/.template.config/",
            templateManifestItem.Attribute("PackagePath")?.Value);
        XElement templateManifestTarget = Assert.Single(
            packageProjectXml.Descendants("Target"),
            item => item.Attribute("Name")?.Value == "IncludeGuiTemplatesInPackage");
        Assert.Equal("_GetPackageFiles", templateManifestTarget.Attribute("BeforeTargets")?.Value);
        XElement templateGlobItem = Assert.Single(
            packageProjectXml.Descendants("None"),
            item => item.Attribute("Include")?.Value ==
                "$(MSBuildProjectDirectory)/Templates/**/*");
        Assert.Equal(
            "$(MSBuildProjectDirectory)/Templates/sharpts-gui/.template.config/template.json",
            templateGlobItem.Attribute("Exclude")?.Value);
        Assert.Equal("true", templateGlobItem.Attribute("Pack")?.Value);
        Assert.Equal("content/Templates/", templateGlobItem.Attribute("PackagePath")?.Value);
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
