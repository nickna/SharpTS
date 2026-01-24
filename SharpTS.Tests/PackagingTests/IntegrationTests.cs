using System.IO.Compression;
using System.Xml.Linq;
using SharpTS.Tests.IntegrationTests;
using Xunit;

namespace SharpTS.Tests.PackagingTests;

/// <summary>
/// End-to-end integration tests for the --pack CLI workflow.
/// Tests the complete packaging pipeline from TypeScript source to NuGet package.
/// </summary>
public class IntegrationTests
{
    #region Test Fixtures

    /// <summary>
    /// Minimal package.json with only required fields.
    /// </summary>
    private const string MinimalPackageJson = """
        {
            "name": "TestPackage",
            "version": "1.0.0"
        }
        """;

    /// <summary>
    /// Full package.json with all metadata fields.
    /// </summary>
    private const string FullPackageJson = """
        {
            "name": "FullTestPackage",
            "version": "2.0.0-beta.1",
            "description": "A comprehensive test package for integration testing",
            "author": "Test Author",
            "license": "MIT",
            "keywords": ["typescript", "dotnet", "testing"],
            "repository": {
                "type": "git",
                "url": "https://github.com/test/repo"
            },
            "homepage": "https://example.com"
        }
        """;

    /// <summary>
    /// Package.json with author as an object.
    /// </summary>
    private const string ObjectAuthorPackageJson = """
        {
            "name": "ObjectAuthorPackage",
            "version": "1.0.0",
            "author": {
                "name": "Object Author Name",
                "email": "author@example.com"
            }
        }
        """;

    /// <summary>
    /// Simple TypeScript library script.
    /// </summary>
    private const string SimpleLibraryScript = """
        function add(a: number, b: number): number {
            return a + b;
        }

        console.log(add(2, 3));
        """;

    /// <summary>
    /// Sample README content.
    /// </summary>
    private const string ReadmeContent = """
        # Test Package

        This is a test package for integration testing.

        ## Installation

        ```bash
        dotnet add package TestPackage
        ```

        ## Usage

        ```typescript
        import { add } from 'TestPackage';
        console.log(add(2, 3));
        ```
        """;

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a standard test setup with TypeScript file and optional package.json.
    /// </summary>
    private static (string scriptPath, string packageJsonPath) CreateStandardTestSetup(
        TempTestDirectory tempDir,
        string script = SimpleLibraryScript,
        string? packageJson = MinimalPackageJson)
    {
        var scriptPath = tempDir.CreateFile("lib.ts", script);
        var packageJsonPath = packageJson != null
            ? tempDir.CreateFile("package.json", packageJson)
            : null;
        return (scriptPath, packageJsonPath!);
    }

    /// <summary>
    /// Extracts and parses the .nuspec file from a .nupkg archive.
    /// </summary>
    private static XDocument ExtractNuspec(string nupkgPath)
    {
        using var archive = ZipFile.OpenRead(nupkgPath);
        var nuspecEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec"));
        if (nuspecEntry == null)
            throw new InvalidOperationException("No .nuspec file found in package");

        using var stream = nuspecEntry.Open();
        return XDocument.Load(stream);
    }

    /// <summary>
    /// Gets a metadata value from the nuspec document.
    /// Handles both namespaced and non-namespaced elements.
    /// </summary>
    private static string? GetNuspecValue(XDocument nuspec, string elementName)
    {
        // Try multiple common NuGet nuspec namespaces
        XNamespace[] namespaces =
        [
            "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd",
            "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd",
            "http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd",
            "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd",
            XNamespace.None
        ];

        foreach (var ns in namespaces)
        {
            var value = nuspec.Descendants(ns + elementName).FirstOrDefault()?.Value;
            if (value != null)
                return value;
        }

        // Fallback: search by local name only
        return nuspec.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == elementName)?.Value;
    }

    /// <summary>
    /// Asserts that a package contains a file at the specified path.
    /// </summary>
    private static void AssertPackageContainsFile(string nupkgPath, string expectedPath)
    {
        using var archive = ZipFile.OpenRead(nupkgPath);
        var entry = archive.GetEntry(expectedPath);
        Assert.NotNull(entry);
    }

    /// <summary>
    /// Reads the content of a file from inside a package.
    /// </summary>
    private static string ReadPackageFile(string nupkgPath, string filePath)
    {
        using var archive = ZipFile.OpenRead(nupkgPath);
        var entry = archive.GetEntry(filePath);
        if (entry == null)
            throw new InvalidOperationException($"File '{filePath}' not found in package");

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Finds a .nupkg file by package ID and version.
    /// </summary>
    private static string? FindNupkg(string directory, string packageId, string version)
    {
        var pattern = $"{packageId}.{version}.nupkg";
        return Directory.GetFiles(directory, pattern).FirstOrDefault();
    }

    /// <summary>
    /// Finds a .snupkg file by package ID and version.
    /// </summary>
    private static string? FindSnupkg(string directory, string packageId, string version)
    {
        var pattern = $"{packageId}.{version}.snupkg";
        return Directory.GetFiles(directory, pattern).FirstOrDefault();
    }

    /// <summary>
    /// Lists all .nupkg files in a directory.
    /// </summary>
    private static string[] GetNupkgFiles(string directory)
    {
        return Directory.GetFiles(directory, "*.nupkg");
    }

    #endregion

    #region 1. Basic Packaging Tests

    [Fact]
    public void Pack_WithMinimalPackageJson_CreatesNupkg()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var (scriptPath, _) = CreateStandardTestSetup(tempDir);

        var result = CliTestHelper.RunCli($"--compile \"{scriptPath}\" --pack", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        var nupkgPath = FindNupkg(tempDir.Path, "TestPackage", "1.0.0");
        Assert.NotNull(nupkgPath);
        Assert.True(File.Exists(nupkgPath));
    }

    [Fact]
    public void Pack_WithFullPackageJson_CreatesNupkg()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var (scriptPath, _) = CreateStandardTestSetup(tempDir, packageJson: FullPackageJson);

        var result = CliTestHelper.RunCli($"--compile \"{scriptPath}\" --pack", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        var nupkgPath = FindNupkg(tempDir.Path, "FullTestPackage", "2.0.0-beta.1");
        Assert.NotNull(nupkgPath);
        Assert.True(File.Exists(nupkgPath));
    }

    [Fact]
    public void Pack_OutputsSuccessMessages()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var (scriptPath, _) = CreateStandardTestSetup(tempDir);

        var result = CliTestHelper.RunCli($"--compile \"{scriptPath}\" --pack", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Compiled to", result.StandardOutput);
        Assert.Contains("Created package:", result.StandardOutput);
        Assert.Contains(".nupkg", result.StandardOutput);
    }

    #endregion

    #region 2. .nuspec Content Verification Tests

    [Fact]
    public void Pack_NuspecContainsCorrectPackageId()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var (scriptPath, _) = CreateStandardTestSetup(tempDir);

        var result = CliTestHelper.RunCli($"--compile \"{scriptPath}\" --pack", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        var nupkgPath = FindNupkg(tempDir.Path, "TestPackage", "1.0.0")!;
        var nuspec = ExtractNuspec(nupkgPath);
        var id = GetNuspecValue(nuspec, "id");
        Assert.Equal("TestPackage", id);
    }

    [Fact]
    public void Pack_NuspecContainsCorrectVersion()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var (scriptPath, _) = CreateStandardTestSetup(tempDir, packageJson: FullPackageJson);

        var result = CliTestHelper.RunCli($"--compile \"{scriptPath}\" --pack", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        var nupkgPath = FindNupkg(tempDir.Path, "FullTestPackage", "2.0.0-beta.1")!;
        var nuspec = ExtractNuspec(nupkgPath);
        var version = GetNuspecValue(nuspec, "version");
        Assert.Equal("2.0.0-beta.1", version);
    }

    [Fact]
    public void Pack_NuspecContainsDescription()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var (scriptPath, _) = CreateStandardTestSetup(tempDir, packageJson: FullPackageJson);

        var result = CliTestHelper.RunCli($"--compile \"{scriptPath}\" --pack", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        var nupkgPath = FindNupkg(tempDir.Path, "FullTestPackage", "2.0.0-beta.1")!;
        var nuspec = ExtractNuspec(nupkgPath);
        var description = GetNuspecValue(nuspec, "description");
        Assert.Contains("comprehensive test package", description);
    }

    [Fact]
    public void Pack_NuspecContainsAuthor()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var (scriptPath, _) = CreateStandardTestSetup(tempDir, packageJson: FullPackageJson);

        var result = CliTestHelper.RunCli($"--compile \"{scriptPath}\" --pack", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        var nupkgPath = FindNupkg(tempDir.Path, "FullTestPackage", "2.0.0-beta.1")!;
        var nuspec = ExtractNuspec(nupkgPath);
        var authors = GetNuspecValue(nuspec, "authors");
        Assert.Equal("Test Author", authors);
    }

    [Fact]
    public void Pack_NuspecContainsTags()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var (scriptPath, _) = CreateStandardTestSetup(tempDir, packageJson: FullPackageJson);

        var result = CliTestHelper.RunCli($"--compile \"{scriptPath}\" --pack", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        var nupkgPath = FindNupkg(tempDir.Path, "FullTestPackage", "2.0.0-beta.1")!;
        var nuspec = ExtractNuspec(nupkgPath);
        var tags = GetNuspecValue(nuspec, "tags");
        Assert.NotNull(tags);
        Assert.Contains("typescript", tags);
        Assert.Contains("dotnet", tags);
        Assert.Contains("testing", tags);
    }

    [Fact]
    public void Pack_WithObjectAuthor_ParsesCorrectly()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var (scriptPath, _) = CreateStandardTestSetup(tempDir, packageJson: ObjectAuthorPackageJson);

        var result = CliTestHelper.RunCli($"--compile \"{scriptPath}\" --pack", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        var nupkgPath = FindNupkg(tempDir.Path, "ObjectAuthorPackage", "1.0.0")!;
        var nuspec = ExtractNuspec(nupkgPath);
        var authors = GetNuspecValue(nuspec, "authors");
        // Object author with name and email is formatted as "Name <email>"
        Assert.Contains("Object Author Name", authors);
    }

    #endregion

    #region 3. Package Structure Tests

    [Fact]
    public void Pack_ContainsDllInLibNet10Folder()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var (scriptPath, _) = CreateStandardTestSetup(tempDir);

        var result = CliTestHelper.RunCli($"--compile \"{scriptPath}\" --pack", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        var nupkgPath = FindNupkg(tempDir.Path, "TestPackage", "1.0.0")!;
        AssertPackageContainsFile(nupkgPath, "lib/net10.0/lib.dll");
    }

    [Fact]
    public void Pack_ContainsRuntimeConfig()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var (scriptPath, _) = CreateStandardTestSetup(tempDir);

        var result = CliTestHelper.RunCli($"--compile \"{scriptPath}\" --pack", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        var nupkgPath = FindNupkg(tempDir.Path, "TestPackage", "1.0.0")!;
        AssertPackageContainsFile(nupkgPath, "lib/net10.0/lib.runtimeconfig.json");
    }

    [Fact]
    public void Pack_WithReadme_IncludesReadmeAtRoot()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var (scriptPath, _) = CreateStandardTestSetup(tempDir);
        tempDir.CreateFile("README.md", ReadmeContent);

        var result = CliTestHelper.RunCli($"--compile \"{scriptPath}\" --pack", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        var nupkgPath = FindNupkg(tempDir.Path, "TestPackage", "1.0.0")!;
        AssertPackageContainsFile(nupkgPath, "README.md");

        // Verify README content
        var readmeFromPackage = ReadPackageFile(nupkgPath, "README.md");
        Assert.Contains("Test Package", readmeFromPackage);
    }

    [Fact]
    public void Pack_WithoutReadme_DoesNotIncludeReadme()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var (scriptPath, _) = CreateStandardTestSetup(tempDir);

        var result = CliTestHelper.RunCli($"--compile \"{scriptPath}\" --pack", tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        var nupkgPath = FindNupkg(tempDir.Path, "TestPackage", "1.0.0")!;

        using var archive = ZipFile.OpenRead(nupkgPath);
        var readmeEntry = archive.GetEntry("README.md");
        Assert.Null(readmeEntry);
    }

    #endregion

    #region 4. CLI Override Tests

    [Fact]
    public void Pack_VersionOverride_UsesOverrideInPackageName()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var (scriptPath, _) = CreateStandardTestSetup(tempDir);

        var result = CliTestHelper.RunCli(
            $"--compile \"{scriptPath}\" --pack --version 3.0.0-alpha",
            tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        var nupkgPath = FindNupkg(tempDir.Path, "TestPackage", "3.0.0-alpha");
        Assert.NotNull(nupkgPath);
    }

    [Fact]
    public void Pack_VersionOverride_UpdatesNuspecVersion()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var (scriptPath, _) = CreateStandardTestSetup(tempDir);

        var result = CliTestHelper.RunCli(
            $"--compile \"{scriptPath}\" --pack --version 5.0.0",
            tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        var nupkgPath = FindNupkg(tempDir.Path, "TestPackage", "5.0.0")!;
        var nuspec = ExtractNuspec(nupkgPath);
        var version = GetNuspecValue(nuspec, "version");
        Assert.Equal("5.0.0", version);
    }

    [Fact]
    public void Pack_PackageIdOverride_UsesOverrideInPackageName()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var (scriptPath, _) = CreateStandardTestSetup(tempDir);

        var result = CliTestHelper.RunCli(
            $"--compile \"{scriptPath}\" --pack --package-id MyCompany.CustomPackage",
            tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        var nupkgPath = FindNupkg(tempDir.Path, "MyCompany.CustomPackage", "1.0.0");
        Assert.NotNull(nupkgPath);
    }

    [Fact]
    public void Pack_PackageIdOverride_UpdatesNuspecId()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var (scriptPath, _) = CreateStandardTestSetup(tempDir);

        var result = CliTestHelper.RunCli(
            $"--compile \"{scriptPath}\" --pack --package-id OverriddenId",
            tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        var nupkgPath = FindNupkg(tempDir.Path, "OverriddenId", "1.0.0")!;
        var nuspec = ExtractNuspec(nupkgPath);
        var id = GetNuspecValue(nuspec, "id");
        Assert.Equal("OverriddenId", id);
    }

    [Fact]
    public void Pack_BothOverrides_UsesBothInPackageName()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var (scriptPath, _) = CreateStandardTestSetup(tempDir);

        var result = CliTestHelper.RunCli(
            $"--compile \"{scriptPath}\" --pack --package-id Combined.Package --version 9.9.9",
            tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        var nupkgPath = FindNupkg(tempDir.Path, "Combined.Package", "9.9.9");
        Assert.NotNull(nupkgPath);

        // Verify both in nuspec
        var nuspec = ExtractNuspec(nupkgPath);
        Assert.Equal("Combined.Package", GetNuspecValue(nuspec, "id"));
        Assert.Equal("9.9.9", GetNuspecValue(nuspec, "version"));
    }

    [Fact]
    public void Pack_WithoutPackageJson_RequiresOverrides()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("lib.ts", SimpleLibraryScript);
        // No package.json created

        var result = CliTestHelper.RunCli($"--compile \"{scriptPath}\" --pack", tempDir.Path);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("package.json", result.StandardOutput);
    }

    [Fact]
    public void Pack_WithoutPackageJson_SucceedsWithOverrides()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("lib.ts", SimpleLibraryScript);
        // No package.json created

        var result = CliTestHelper.RunCli(
            $"--compile \"{scriptPath}\" --pack --package-id CliOnlyPackage --version 1.0.0",
            tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        var nupkgPath = FindNupkg(tempDir.Path, "CliOnlyPackage", "1.0.0");
        Assert.NotNull(nupkgPath);
    }

    #endregion

    #region 5. Error Handling Tests

    [Fact]
    public void Pack_InvalidPackageId_ExitsWithError()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("lib.ts", SimpleLibraryScript);

        var result = CliTestHelper.RunCli(
            $"--compile \"{scriptPath}\" --pack --package-id Invalid@Package#Name --version 1.0.0",
            tempDir.Path);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Invalid package ID", result.StandardOutput);
    }

    [Fact]
    public void Pack_InvalidVersion_ExitsWithError()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("lib.ts", SimpleLibraryScript);

        var result = CliTestHelper.RunCli(
            $"--compile \"{scriptPath}\" --pack --package-id ValidId --version not.a.version",
            tempDir.Path);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Invalid version", result.StandardOutput);
    }

    [Fact]
    public void Pack_MissingVersion_ExitsWithError()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var packageJsonNoVersion = """
            {
                "name": "MissingVersionPackage"
            }
            """;
        var (scriptPath, _) = CreateStandardTestSetup(tempDir, packageJson: packageJsonNoVersion);

        var result = CliTestHelper.RunCli($"--compile \"{scriptPath}\" --pack", tempDir.Path);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Version is required", result.StandardOutput);
    }

    [Fact]
    public void Pack_MissingName_ExitsWithError()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var packageJsonNoName = """
            {
                "version": "1.0.0"
            }
            """;
        var (scriptPath, _) = CreateStandardTestSetup(tempDir, packageJson: packageJsonNoName);

        var result = CliTestHelper.RunCli($"--compile \"{scriptPath}\" --pack", tempDir.Path);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Package ID is required", result.StandardOutput);
    }

    [Fact]
    public void Pack_TypeScriptError_FailsBeforePackaging()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var errorScript = """
            let x: number = "not a number";
            """;
        var (scriptPath, _) = CreateStandardTestSetup(tempDir, script: errorScript);

        var result = CliTestHelper.RunCli($"--compile \"{scriptPath}\" --pack", tempDir.Path);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Error", result.StandardError);
        // Should not have created a package
        var packages = GetNupkgFiles(tempDir.Path);
        Assert.Empty(packages);
    }

    [Fact]
    public void Pack_InvalidJson_ExitsWithError()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var scriptPath = tempDir.CreateFile("lib.ts", SimpleLibraryScript);
        tempDir.CreateFile("package.json", "{ invalid json }");

        var result = CliTestHelper.RunCli($"--compile \"{scriptPath}\" --pack", tempDir.Path);

        Assert.NotEqual(0, result.ExitCode);
    }

    #endregion

    #region 6. Additional Tests

    [Fact]
    public void Pack_WithCustomOutputPath_PackageInSameDirectory()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var (scriptPath, _) = CreateStandardTestSetup(tempDir);

        // Create subdirectory for output
        var outputDir = tempDir.GetPath("output");
        Directory.CreateDirectory(outputDir);
        var outputPath = Path.Combine(outputDir, "custom.dll");

        var result = CliTestHelper.RunCli(
            $"--compile \"{scriptPath}\" -o \"{outputPath}\" --pack",
            tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        // Package should be in the same directory as the DLL
        var packages = Directory.GetFiles(outputDir, "*.nupkg");
        Assert.Single(packages);
    }

    [Theory]
    [InlineData("1.0.0-alpha")]
    [InlineData("1.0.0-alpha.1")]
    [InlineData("1.0.0-beta")]
    [InlineData("1.0.0-beta.2")]
    [InlineData("1.0.0-rc.1")]
    [InlineData("2.0.0-preview.3")]
    public void Pack_PrereleaseVersion_CreatesCorrectPackageName(string version)
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var (scriptPath, _) = CreateStandardTestSetup(tempDir);

        var result = CliTestHelper.RunCli(
            $"--compile \"{scriptPath}\" --pack --version {version}",
            tempDir.Path);

        Assert.Equal(0, result.ExitCode);
        var nupkgPath = FindNupkg(tempDir.Path, "TestPackage", version);
        Assert.NotNull(nupkgPath);
    }

    [Fact]
    public void Pack_CreatesSymbolPackage_WhenPdbExists()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var (scriptPath, _) = CreateStandardTestSetup(tempDir);

        var result = CliTestHelper.RunCli($"--compile \"{scriptPath}\" --pack", tempDir.Path);

        Assert.Equal(0, result.ExitCode);

        // Symbol package is only created if a .pdb file exists
        // The SharpTS compiler may or may not generate PDB files depending on configuration
        var snupkgPath = FindSnupkg(tempDir.Path, "TestPackage", "1.0.0");
        var pdbPath = Path.Combine(tempDir.Path, "lib.pdb");

        if (File.Exists(pdbPath))
        {
            Assert.Contains("Created symbol package:", result.StandardOutput);
            Assert.NotNull(snupkgPath);
        }
        else
        {
            // If no PDB, no symbol package should be created
            Assert.DoesNotContain("Created symbol package:", result.StandardOutput);
            Assert.Null(snupkgPath);
        }
    }

    [Fact]
    public void Pack_SymbolPackage_MatchesMainPackageVersion_WhenCreated()
    {
        using var tempDir = CliTestHelper.CreateTempDirectory();
        var (scriptPath, _) = CreateStandardTestSetup(tempDir);

        var result = CliTestHelper.RunCli(
            $"--compile \"{scriptPath}\" --pack --version 4.5.6",
            tempDir.Path);

        Assert.Equal(0, result.ExitCode);

        // Main package should always exist
        var nupkgPath = FindNupkg(tempDir.Path, "TestPackage", "4.5.6");
        Assert.NotNull(nupkgPath);

        // Symbol package only exists if PDB was generated
        var snupkgPath = FindSnupkg(tempDir.Path, "TestPackage", "4.5.6");
        if (snupkgPath != null)
        {
            // If symbol package exists, verify the version matches
            Assert.Contains("4.5.6", snupkgPath);
        }
    }

    #endregion
}
