using SharpTS.Packaging;
using Xunit;

namespace SharpTS.Tests.PackagingTests;

/// <summary>
/// Tests for PackageValidator validation logic.
/// </summary>
public class PackageValidatorTests
{
    [Theory]
    [InlineData("my-package")]
    [InlineData("MyPackage")]
    [InlineData("My.Package")]
    [InlineData("My_Package")]
    [InlineData("My-Package")]
    [InlineData("Package123")]
    [InlineData("A")]
    public void Validate_ValidPackageId_NoErrors(string packageId)
    {
        var package = new PackageJson { Name = packageId, Version = "1.0.0" };

        var result = PackageValidator.Validate(
            CreateDummyAssembly(),
            package);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(".invalid")]
    [InlineData("invalid.")]
    [InlineData("inva..lid")]
    [InlineData("invalid!")]
    [InlineData("invalid@name")]
    [InlineData("invalid name")]
    public void Validate_InvalidPackageId_ReturnsError(string packageId)
    {
        var package = new PackageJson { Name = packageId, Version = "1.0.0" };

        var result = PackageValidator.Validate(
            CreateDummyAssembly(),
            package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Invalid package ID"));
    }

    [Fact]
    public void Validate_MissingPackageId_ReturnsError()
    {
        var package = new PackageJson { Version = "1.0.0" };

        var result = PackageValidator.Validate(
            CreateDummyAssembly(),
            package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Package ID is required"));
    }

    [Fact]
    public void Validate_PackageIdOverride_UsesOverride()
    {
        var package = new PackageJson { Version = "1.0.0" }; // No name

        var result = PackageValidator.Validate(
            CreateDummyAssembly(),
            package,
            packageIdOverride: "ValidOverride");

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("2.0.0-beta")]
    [InlineData("1.0.0-alpha.1")]
    [InlineData("3.0.0-rc.1+build.123")]
    [InlineData("0.0.1")]
    public void Validate_ValidVersion_NoErrors(string version)
    {
        var package = new PackageJson { Name = "test", Version = version };

        var result = PackageValidator.Validate(
            CreateDummyAssembly(),
            package);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("not.a.version")]
    [InlineData("v1.0.0")]
    public void Validate_InvalidVersion_ReturnsError(string version)
    {
        var package = new PackageJson { Name = "test", Version = version };

        var result = PackageValidator.Validate(
            CreateDummyAssembly(),
            package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Invalid version"));
    }

    [Fact]
    public void Validate_MissingVersion_ReturnsError()
    {
        var package = new PackageJson { Name = "test" };

        var result = PackageValidator.Validate(
            CreateDummyAssembly(),
            package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Version is required"));
    }

    [Fact]
    public void Validate_VersionOverride_UsesOverride()
    {
        var package = new PackageJson { Name = "test" }; // No version

        var result = PackageValidator.Validate(
            CreateDummyAssembly(),
            package,
            versionOverride: "2.0.0");

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_AssemblyNotFound_ReturnsError()
    {
        var package = new PackageJson { Name = "test", Version = "1.0.0" };

        var result = PackageValidator.Validate(
            "nonexistent.dll",
            package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Assembly not found"));
    }

    /// <summary>
    /// Creates a dummy assembly file for testing validation.
    /// </summary>
    private static string CreateDummyAssembly()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.dll");
        // Create an empty file - just for existence check
        File.WriteAllBytes(tempPath, []);
        return tempPath;
    }
}
