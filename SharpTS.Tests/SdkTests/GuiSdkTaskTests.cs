using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using SharpTS.Sdk.Tasks;
using Xunit;

namespace SharpTS.Tests.SdkTests;

public sealed class GuiSdkTaskTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"SharpTS_GuiSdk_{Guid.NewGuid():N}");
    private readonly MockBuildEngine _buildEngine = new();

    public GuiSdkTaskTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void WriteGuiTsConfigTask_WritesAbsoluteGuiMappingsAndExtendsConsumerConfig()
    {
        string project = Path.Combine(_root, "project with spaces");
        string package = Path.Combine(_root, "package", "gui");
        string baseConfig = Path.Combine(project, "tsconfig.json");
        string output = Path.Combine(_root, "obj", "tsconfig.json");
        Directory.CreateDirectory(project);
        Directory.CreateDirectory(package);
        File.WriteAllText(baseConfig, "{}");

        var task = new WriteGuiTsConfigTask
        {
            BuildEngine = _buildEngine,
            OutputPath = output,
            ProjectDirectory = project,
            GuiPackageDirectory = package,
            BaseTsConfigPath = baseConfig,
        };

        Assert.True(task.Execute());
        using var document = JsonDocument.Parse(File.ReadAllText(output));
        JsonElement root = document.RootElement;
        Assert.Equal(baseConfig.Replace('\\', '/'), root.GetProperty("extends").GetString());
        JsonElement options = root.GetProperty("compilerOptions");
        Assert.Equal("react-jsx", options.GetProperty("jsx").GetString());
        Assert.Equal("@sharpts/gui", options.GetProperty("jsxImportSource").GetString());
        Assert.Equal(
            Path.Combine(package, "index.ts").Replace('\\', '/'),
            options.GetProperty("paths").GetProperty("@sharpts/gui")[0].GetString());
    }

    [Fact]
    public void WriteGuiTsConfigTask_RejectsMissingBaseConfig()
    {
        var task = new WriteGuiTsConfigTask
        {
            BuildEngine = _buildEngine,
            OutputPath = Path.Combine(_root, "obj", "tsconfig.json"),
            ProjectDirectory = _root,
            GuiPackageDirectory = Path.Combine(_root, "gui"),
            BaseTsConfigPath = Path.Combine(_root, "missing.json"),
        };

        Assert.False(task.Execute());
        Assert.Contains(_buildEngine.Errors, error =>
            error.Contains("does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public void WriteGuiManifestTask_WritesHostedAbiAndNormalizedPaths()
    {
        string output = Path.Combine(_root, "obj", "app.json");
        var task = new WriteGuiManifestTask
        {
            BuildEngine = _buildEngine,
            OutputPath = output,
            EntryPath = "Guest\\src\\main.tsx",
            CompiledAssembly = "SharpTS.Gui.Guest.dll",
            HostedAbiVersion = "1",
            GuiApiVersion = "1",
            DescriptorSchemaVersion = "1",
            DescriptorSchemaHash = new string('a', 64),
        };

        Assert.True(task.Execute());
        using var document = JsonDocument.Parse(File.ReadAllText(output));
        Assert.Equal("Guest/src/main.tsx", document.RootElement.GetProperty("entryPath").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("hostedAbiVersion").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("guiApiVersion").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("descriptorSchemaVersion").GetInt32());
        Assert.Equal(new string('a', 64), document.RootElement.GetProperty("descriptorSchemaHash").GetString());
    }

    [Fact]
    public void PrepareGuiAssetsTask_PreparesLocalAssetsWithStableLogicalNames()
    {
        string asset = Path.Combine(_root, "Assets", "icons", "app.png");
        Directory.CreateDirectory(Path.GetDirectoryName(asset)!);
        File.WriteAllBytes(asset, [1, 2, 3]);
        var task = new PrepareGuiAssetsTask
        {
            BuildEngine = _buildEngine,
            ProjectDirectory = _root,
            OutputDirectory = Path.Combine(_root, "obj", "assets"),
            LocalAssets = [new TaskItem(asset)],
        };

        Assert.True(task.Execute());
        ITaskItem prepared = Assert.Single(task.PreparedAssets);
        Assert.Equal("icons/app.png", prepared.GetMetadata("LogicalName"));
        Assert.Equal(asset, prepared.ItemSpec);
    }

    [Fact]
    public void PrepareGuiAssetsTask_RejectsUnsafeAndUnpinnedAssets()
    {
        string asset = Path.Combine(_root, "asset.png");
        File.WriteAllBytes(asset, [1]);
        var unsafeLocal = new TaskItem(asset);
        unsafeLocal.SetMetadata("LogicalName", "../escape.png");
        var localTask = new PrepareGuiAssetsTask
        {
            BuildEngine = _buildEngine,
            ProjectDirectory = _root,
            OutputDirectory = Path.Combine(_root, "obj", "local"),
            LocalAssets = [unsafeLocal],
        };
        Assert.False(localTask.Execute());

        var remote = new TaskItem("https://example.invalid/image.png");
        remote.SetMetadata("LogicalName", "image.png");
        var remoteTask = new PrepareGuiAssetsTask
        {
            BuildEngine = _buildEngine,
            ProjectDirectory = _root,
            OutputDirectory = Path.Combine(_root, "obj", "remote"),
            RemoteAssets = [remote],
        };
        Assert.False(remoteTask.Execute());
        Assert.Contains(_buildEngine.Errors, error => error.Contains("Sha256", StringComparison.Ordinal));
    }

    [Fact]
    public void WriteGuiControlProviderRegistrationTask_EmitsDirectDeterministicConstructors()
    {
        string output = Path.Combine(_root, "obj", "ControlProviderRegistration.g.cs");
        var task = new WriteGuiControlProviderRegistrationTask
        {
            BuildEngine = _buildEngine,
            OutputPath = output,
            ProviderTypes =
            [
                new TaskItem("global::Contoso.Widgets.ChartProvider"),
                new TaskItem("Fabrikam.Controls.MapProvider"),
            ],
        };

        Assert.True(task.Execute());
        string first = File.ReadAllText(output);
        Assert.Contains("new global::Contoso.Widgets.ChartProvider()", first, StringComparison.Ordinal);
        Assert.Contains("new Fabrikam.Controls.MapProvider()", first, StringComparison.Ordinal);
        Assert.DoesNotContain("Reflection", first, StringComparison.Ordinal);
        Assert.True(task.Execute());
        Assert.Equal(first, File.ReadAllText(output));
    }

    [Theory]
    [InlineData("Vendor.Provider<T>")]
    [InlineData("Vendor.Provider();System.Console.WriteLine(1)")]
    [InlineData("Vendor..Provider")]
    public void WriteGuiControlProviderRegistrationTask_RejectsUnsafeTypeNames(string providerType)
    {
        var task = new WriteGuiControlProviderRegistrationTask
        {
            BuildEngine = _buildEngine,
            OutputPath = Path.Combine(_root, "obj", "providers.g.cs"),
            ProviderTypes = [new TaskItem(providerType)],
        };

        Assert.False(task.Execute());
        Assert.Contains(_buildEngine.Errors, error =>
            error.Contains("qualified C# type name", StringComparison.Ordinal));
    }
}
