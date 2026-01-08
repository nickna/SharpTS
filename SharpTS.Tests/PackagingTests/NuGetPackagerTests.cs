using System.IO.Compression;
using SharpTS.Packaging;
using Xunit;

namespace SharpTS.Tests.PackagingTests;

/// <summary>
/// Integration tests for NuGetPackager package creation.
/// </summary>
public class NuGetPackagerTests : IDisposable
{
    private readonly string _tempDir;

    public NuGetPackagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"nuget_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { /* Ignore cleanup errors */ }
    }

    [Fact]
    public void CreatePackage_BasicPackage_CreatesNupkg()
    {
        var package = new PackageJson
        {
            Name = "TestLib",
            Version = "1.0.0",
            Description = "Test library"
        };

        var assemblyPath = CreateDummyAssembly("TestLib.dll");
        var packager = new NuGetPackager(package);

        var nupkgPath = packager.CreatePackage(assemblyPath, _tempDir);

        Assert.True(File.Exists(nupkgPath));
        Assert.Equal(Path.Combine(_tempDir, "TestLib.1.0.0.nupkg"), nupkgPath);
    }

    [Fact]
    public void CreatePackage_ContainsDllInLibFolder()
    {
        var package = new PackageJson
        {
            Name = "TestLib",
            Version = "1.0.0"
        };

        var assemblyPath = CreateDummyAssembly("TestLib.dll");
        var packager = new NuGetPackager(package);

        var nupkgPath = packager.CreatePackage(assemblyPath, _tempDir);

        using var archive = ZipFile.OpenRead(nupkgPath);
        var dllEntry = archive.GetEntry("lib/net10.0/TestLib.dll");
        Assert.NotNull(dllEntry);
    }

    [Fact]
    public void CreatePackage_ContainsNuspec()
    {
        var package = new PackageJson
        {
            Name = "TestLib",
            Version = "1.0.0",
            Description = "A test library"
        };

        var assemblyPath = CreateDummyAssembly("TestLib.dll");
        var packager = new NuGetPackager(package);

        var nupkgPath = packager.CreatePackage(assemblyPath, _tempDir);

        using var archive = ZipFile.OpenRead(nupkgPath);
        var nuspecEntry = archive.GetEntry("TestLib.nuspec");
        Assert.NotNull(nuspecEntry);

        using var reader = new StreamReader(nuspecEntry.Open());
        var nuspecContent = reader.ReadToEnd();
        Assert.Contains("TestLib", nuspecContent);
        Assert.Contains("1.0.0", nuspecContent);
        Assert.Contains("A test library", nuspecContent);
    }

    [Fact]
    public void CreatePackage_WithPackageIdOverride_UsesOverride()
    {
        var package = new PackageJson
        {
            Name = "original-name",
            Version = "1.0.0"
        };

        var assemblyPath = CreateDummyAssembly("Test.dll");
        var packager = new NuGetPackager(package, packageIdOverride: "MyCompany.Library");

        var nupkgPath = packager.CreatePackage(assemblyPath, _tempDir);

        Assert.Contains("MyCompany.Library.1.0.0.nupkg", nupkgPath);
    }

    [Fact]
    public void CreatePackage_WithVersionOverride_UsesOverride()
    {
        var package = new PackageJson
        {
            Name = "TestLib",
            Version = "1.0.0"
        };

        var assemblyPath = CreateDummyAssembly("Test.dll");
        var packager = new NuGetPackager(package, versionOverride: "2.0.0-beta");

        var nupkgPath = packager.CreatePackage(assemblyPath, _tempDir);

        Assert.Contains("TestLib.2.0.0-beta.nupkg", nupkgPath);
    }

    [Fact]
    public void CreatePackage_WithKeywords_IncludesInNuspec()
    {
        var package = new PackageJson
        {
            Name = "TestLib",
            Version = "1.0.0",
            Keywords = ["typescript", "dotnet", "library"]
        };

        var assemblyPath = CreateDummyAssembly("Test.dll");
        var packager = new NuGetPackager(package);

        var nupkgPath = packager.CreatePackage(assemblyPath, _tempDir);

        using var archive = ZipFile.OpenRead(nupkgPath);
        var nuspecEntry = archive.GetEntry("TestLib.nuspec")!;
        using var reader = new StreamReader(nuspecEntry.Open());
        var nuspecContent = reader.ReadToEnd();

        Assert.Contains("typescript", nuspecContent);
        Assert.Contains("dotnet", nuspecContent);
    }

    [Fact]
    public void CreatePackage_WithRuntimeConfig_IncludesInPackage()
    {
        var package = new PackageJson
        {
            Name = "TestLib",
            Version = "1.0.0"
        };

        var assemblyPath = CreateDummyAssembly("TestLib.dll");
        // Create runtime config
        var runtimeConfigPath = Path.ChangeExtension(assemblyPath, ".runtimeconfig.json");
        File.WriteAllText(runtimeConfigPath, """{ "runtimeOptions": {} }""");

        var packager = new NuGetPackager(package);

        var nupkgPath = packager.CreatePackage(assemblyPath, _tempDir);

        using var archive = ZipFile.OpenRead(nupkgPath);
        var configEntry = archive.GetEntry("lib/net10.0/TestLib.runtimeconfig.json");
        Assert.NotNull(configEntry);
    }

    [Fact]
    public void CreatePackage_WithReadme_IncludesInPackage()
    {
        var package = new PackageJson
        {
            Name = "TestLib",
            Version = "1.0.0"
        };

        var assemblyPath = CreateDummyAssembly("TestLib.dll");
        var readmePath = Path.Combine(_tempDir, "README.md");
        File.WriteAllText(readmePath, "# Test Library\nThis is a test.");

        var packager = new NuGetPackager(package);

        var nupkgPath = packager.CreatePackage(assemblyPath, _tempDir, readmePath);

        using var archive = ZipFile.OpenRead(nupkgPath);
        var readmeEntry = archive.GetEntry("README.md");
        Assert.NotNull(readmeEntry);
    }

    [Fact]
    public void PackageId_ReturnsEffectiveId()
    {
        var package = new PackageJson { Name = "original", Version = "1.0.0" };
        var packager = new NuGetPackager(package, packageIdOverride: "override");

        Assert.Equal("override", packager.PackageId);
    }

    [Fact]
    public void Version_ReturnsEffectiveVersion()
    {
        var package = new PackageJson { Name = "test", Version = "1.0.0" };
        var packager = new NuGetPackager(package, versionOverride: "3.0.0");

        Assert.Equal("3.0.0", packager.Version);
    }

    private string CreateDummyAssembly(string fileName)
    {
        var path = Path.Combine(_tempDir, fileName);
        // Create a minimal valid PE file header (just enough for tests)
        File.WriteAllBytes(path, [0x4D, 0x5A]); // MZ header
        return path;
    }
}
