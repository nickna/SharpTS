using SharpTS.Packaging;
using Xunit;

namespace SharpTS.Tests.PackagingTests;

/// <summary>
/// Tests for PackageJsonLoader file loading functionality.
/// </summary>
public class PackageJsonLoaderTests
{
    [Fact]
    public void Load_ValidFile_ReturnsPackageJson()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var packageJsonPath = Path.Combine(tempDir, "package.json");
            File.WriteAllText(packageJsonPath, """
                {
                    "name": "test-package",
                    "version": "1.0.0",
                    "description": "A test package"
                }
                """);

            var package = PackageJsonLoader.Load(packageJsonPath);

            Assert.Equal("test-package", package.Name);
            Assert.Equal("1.0.0", package.Version);
            Assert.Equal("A test package", package.Description);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Load_FileNotFound_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() =>
            PackageJsonLoader.Load("/nonexistent/path/package.json"));
    }

    [Fact]
    public void TryLoad_ValidFile_ReturnsPackageJson()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var packageJsonPath = Path.Combine(tempDir, "package.json");
            File.WriteAllText(packageJsonPath, """{ "name": "test", "version": "1.0.0" }""");

            var package = PackageJsonLoader.TryLoad(packageJsonPath);

            Assert.NotNull(package);
            Assert.Equal("test", package.Name);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void TryLoad_FileNotFound_ReturnsNull()
    {
        var result = PackageJsonLoader.TryLoad("/nonexistent/path/package.json");

        Assert.Null(result);
    }

    [Fact]
    public void TryLoad_InvalidJson_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var packageJsonPath = Path.Combine(tempDir, "package.json");
            File.WriteAllText(packageJsonPath, "{ invalid json }");

            var result = PackageJsonLoader.TryLoad(packageJsonPath);

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FindAndLoad_FileInCurrentDir_LoadsFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var packageJsonPath = Path.Combine(tempDir, "package.json");
            File.WriteAllText(packageJsonPath, """{ "name": "found", "version": "1.0.0" }""");

            var package = PackageJsonLoader.FindAndLoad(tempDir);

            Assert.NotNull(package);
            Assert.Equal("found", package.Name);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FindAndLoad_FileInParentDir_LoadsFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}");
        var subDir = Path.Combine(tempDir, "src", "lib");
        Directory.CreateDirectory(subDir);

        try
        {
            var packageJsonPath = Path.Combine(tempDir, "package.json");
            File.WriteAllText(packageJsonPath, """{ "name": "parent", "version": "2.0.0" }""");

            var package = PackageJsonLoader.FindAndLoad(subDir);

            Assert.NotNull(package);
            Assert.Equal("parent", package.Name);
            Assert.Equal("2.0.0", package.Version);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FindAndLoad_NoFileFound_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var result = PackageJsonLoader.FindAndLoad(tempDir);

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Load_WithComments_ParsesSuccessfully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var packageJsonPath = Path.Combine(tempDir, "package.json");
            File.WriteAllText(packageJsonPath, """
                {
                    // This is a comment
                    "name": "commented",
                    "version": "1.0.0"
                }
                """);

            var package = PackageJsonLoader.Load(packageJsonPath);

            Assert.Equal("commented", package.Name);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Load_WithTrailingCommas_ParsesSuccessfully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var packageJsonPath = Path.Combine(tempDir, "package.json");
            File.WriteAllText(packageJsonPath, """
                {
                    "name": "trailing",
                    "version": "1.0.0",
                }
                """);

            var package = PackageJsonLoader.Load(packageJsonPath);

            Assert.Equal("trailing", package.Name);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
