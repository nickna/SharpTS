using SharpTS.Packaging;
using System.Text.Json;
using Xunit;

namespace SharpTS.Tests.PackagingTests;

/// <summary>
/// Tests for PackageJson model parsing and property accessors.
/// </summary>
public class PackageJsonTests
{
    [Fact]
    public void Parse_BasicFields_SetsProperties()
    {
        var json = """
            {
                "name": "my-package",
                "version": "1.0.0",
                "description": "Test package"
            }
            """;

        var package = JsonSerializer.Deserialize<PackageJson>(json)!;

        Assert.Equal("my-package", package.Name);
        Assert.Equal("1.0.0", package.Version);
        Assert.Equal("Test package", package.Description);
    }

    [Fact]
    public void Author_StringFormat_ReturnsString()
    {
        var json = """{ "author": "John Doe" }""";
        var package = JsonSerializer.Deserialize<PackageJson>(json)!;

        Assert.Equal("John Doe", package.Author);
    }

    [Fact]
    public void Author_ObjectFormat_ReturnsNameAndEmail()
    {
        var json = """{ "author": { "name": "John Doe", "email": "john@example.com" } }""";
        var package = JsonSerializer.Deserialize<PackageJson>(json)!;

        Assert.Equal("John Doe <john@example.com>", package.Author);
    }

    [Fact]
    public void Author_ObjectFormatNameOnly_ReturnsName()
    {
        var json = """{ "author": { "name": "John Doe" } }""";
        var package = JsonSerializer.Deserialize<PackageJson>(json)!;

        Assert.Equal("John Doe", package.Author);
    }

    [Fact]
    public void Author_Null_ReturnsNull()
    {
        var json = """{ "name": "test" }""";
        var package = JsonSerializer.Deserialize<PackageJson>(json)!;

        Assert.Null(package.Author);
    }

    [Fact]
    public void RepositoryUrl_StringFormat_ReturnsString()
    {
        var json = """{ "repository": "https://github.com/user/repo" }""";
        var package = JsonSerializer.Deserialize<PackageJson>(json)!;

        Assert.Equal("https://github.com/user/repo", package.RepositoryUrl);
    }

    [Fact]
    public void RepositoryUrl_ObjectFormat_ReturnsUrl()
    {
        var json = """{ "repository": { "type": "git", "url": "https://github.com/user/repo.git" } }""";
        var package = JsonSerializer.Deserialize<PackageJson>(json)!;

        Assert.Equal("https://github.com/user/repo.git", package.RepositoryUrl);
    }

    [Fact]
    public void ProjectUrl_PrefersHomepage_OverRepository()
    {
        var json = """
            {
                "homepage": "https://myproject.com",
                "repository": { "url": "https://github.com/user/repo" }
            }
            """;
        var package = JsonSerializer.Deserialize<PackageJson>(json)!;

        Assert.Equal("https://myproject.com", package.ProjectUrl);
    }

    [Fact]
    public void ProjectUrl_FallsBackToRepository_WhenNoHomepage()
    {
        var json = """{ "repository": { "url": "https://github.com/user/repo" } }""";
        var package = JsonSerializer.Deserialize<PackageJson>(json)!;

        Assert.Equal("https://github.com/user/repo", package.ProjectUrl);
    }

    [Fact]
    public void Keywords_ParsesArray()
    {
        var json = """{ "keywords": ["typescript", "library", "dotnet"] }""";
        var package = JsonSerializer.Deserialize<PackageJson>(json)!;

        Assert.Equal(3, package.Keywords!.Count);
        Assert.Contains("typescript", package.Keywords);
        Assert.Contains("library", package.Keywords);
        Assert.Contains("dotnet", package.Keywords);
    }

    [Fact]
    public void IsValid_WithRequiredFields_ReturnsTrue()
    {
        var package = new PackageJson { Name = "test", Version = "1.0.0" };

        var isValid = package.IsValid(out var errors);

        Assert.True(isValid);
        Assert.Empty(errors);
    }

    [Fact]
    public void IsValid_MissingName_ReturnsFalse()
    {
        var package = new PackageJson { Version = "1.0.0" };

        var isValid = package.IsValid(out var errors);

        Assert.False(isValid);
        Assert.Contains(errors, e => e.Contains("name"));
    }

    [Fact]
    public void IsValid_MissingVersion_ReturnsFalse()
    {
        var package = new PackageJson { Name = "test" };

        var isValid = package.IsValid(out var errors);

        Assert.False(isValid);
        Assert.Contains(errors, e => e.Contains("version"));
    }

    [Fact]
    public void IsValid_MissingBoth_ReturnsBothErrors()
    {
        var package = new PackageJson();

        var isValid = package.IsValid(out var errors);

        Assert.False(isValid);
        Assert.Equal(2, errors.Count);
    }
}
