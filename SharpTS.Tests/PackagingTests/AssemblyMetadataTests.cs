using SharpTS.Packaging;
using Xunit;

namespace SharpTS.Tests.PackagingTests;

/// <summary>
/// Tests for AssemblyMetadata record and conversion logic.
/// </summary>
public class AssemblyMetadataTests
{
    [Fact]
    public void FromPackageJson_BasicFields_MapsCorrectly()
    {
        var package = new PackageJson
        {
            Name = "my-package",
            Version = "1.2.3",
            Description = "Test description"
        };

        var metadata = AssemblyMetadata.FromPackageJson(package);

        Assert.Equal("my-package", metadata.Title);
        Assert.Equal("my-package", metadata.Product);
        Assert.Equal("Test description", metadata.Description);
        Assert.Equal(new Version(1, 2, 3), metadata.Version);
        Assert.Equal("1.2.3", metadata.InformationalVersion);
    }

    [Fact]
    public void FromPackageJson_PreReleaseVersion_ParsesBaseVersion()
    {
        var package = new PackageJson
        {
            Name = "test",
            Version = "2.0.0-beta.1"
        };

        var metadata = AssemblyMetadata.FromPackageJson(package);

        // Base version is parsed (without pre-release)
        Assert.Equal(new Version(2, 0, 0), metadata.Version);
        // Full version is preserved in InformationalVersion
        Assert.Equal("2.0.0-beta.1", metadata.InformationalVersion);
    }

    [Fact]
    public void FromPackageJson_Author_MapsToCompany()
    {
        var package = new PackageJson { Name = "test", Version = "1.0.0" };
        // Set author via raw property
        package.GetType().GetProperty("AuthorRaw")!.SetValue(package, "John Doe");

        var metadata = AssemblyMetadata.FromPackageJson(package);

        Assert.Equal("John Doe", metadata.Company);
    }

    [Fact]
    public void FromPackageJson_License_MapsToCopyright()
    {
        var package = new PackageJson
        {
            Name = "test",
            Version = "1.0.0",
            License = "MIT"
        };

        var metadata = AssemblyMetadata.FromPackageJson(package);

        Assert.Equal("Licensed under MIT", metadata.Copyright);
    }

    [Fact]
    public void FromPackageJson_NoLicense_NullCopyright()
    {
        var package = new PackageJson
        {
            Name = "test",
            Version = "1.0.0"
        };

        var metadata = AssemblyMetadata.FromPackageJson(package);

        Assert.Null(metadata.Copyright);
    }

    [Fact]
    public void FromPackageJson_InvalidVersion_NullVersion()
    {
        var package = new PackageJson
        {
            Name = "test",
            Version = "invalid"
        };

        var metadata = AssemblyMetadata.FromPackageJson(package);

        Assert.Null(metadata.Version);
        Assert.Equal("invalid", metadata.InformationalVersion);
    }

    [Fact]
    public void EffectiveVersion_WithVersion_ReturnsVersion()
    {
        var metadata = new AssemblyMetadata(Version: new Version(2, 0, 0));

        Assert.Equal(new Version(2, 0, 0), metadata.EffectiveVersion);
    }

    [Fact]
    public void EffectiveVersion_NullVersion_ReturnsDefault()
    {
        var metadata = new AssemblyMetadata();

        Assert.Equal(new Version(1, 0, 0, 0), metadata.EffectiveVersion);
    }

    [Fact]
    public void Record_WithModification_CreatesNewInstance()
    {
        var original = new AssemblyMetadata(
            Version: new Version(1, 0, 0),
            Title: "Original"
        );

        var modified = original with { Title = "Modified" };

        Assert.Equal("Original", original.Title);
        Assert.Equal("Modified", modified.Title);
        Assert.Equal(original.Version, modified.Version);
    }
}
