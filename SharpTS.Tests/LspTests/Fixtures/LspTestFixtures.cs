namespace SharpTS.Tests.LspTests.Fixtures;

/// <summary>
/// Test fixtures and sample data for LspBridge tests.
/// </summary>
public static class LspTestFixtures
{
    /// <summary>
    /// Sample .csproj with no references.
    /// </summary>
    public const string EmptyCsproj = """
        <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
            </PropertyGroup>
        </Project>
        """;

    /// <summary>
    /// Sample .csproj with HintPath references.
    /// </summary>
    public const string CsprojWithHintPath = """
        <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
            </PropertyGroup>
            <ItemGroup>
                <Reference Include="MyLibrary">
                    <HintPath>libs\MyLibrary.dll</HintPath>
                </Reference>
            </ItemGroup>
        </Project>
        """;

    /// <summary>
    /// Sample .csproj with PackageReference (Version as attribute).
    /// </summary>
    public const string CsprojWithPackageReferenceAttribute = """
        <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
            </PropertyGroup>
            <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
            </ItemGroup>
        </Project>
        """;

    /// <summary>
    /// Sample .csproj with PackageReference (Version as element).
    /// </summary>
    public const string CsprojWithPackageReferenceElement = """
        <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
            </PropertyGroup>
            <ItemGroup>
                <PackageReference Include="Newtonsoft.Json">
                    <Version>13.0.3</Version>
                </PackageReference>
            </ItemGroup>
        </Project>
        """;

    /// <summary>
    /// Sample .csproj with multiple references of different types.
    /// </summary>
    public const string CsprojWithMixedReferences = """
        <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
            </PropertyGroup>
            <ItemGroup>
                <Reference Include="LocalLib">
                    <HintPath>libs\LocalLib.dll</HintPath>
                </Reference>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
            </ItemGroup>
        </Project>
        """;

    /// <summary>
    /// Invalid XML content.
    /// </summary>
    public const string InvalidXml = """
        <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
            <Missing closing tags
        """;

    /// <summary>
    /// Sample .csproj with Reference but no HintPath.
    /// </summary>
    public const string CsprojWithReferenceNoHintPath = """
        <Project Sdk="Microsoft.NET.Sdk">
            <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
            </PropertyGroup>
            <ItemGroup>
                <Reference Include="System.Data" />
            </ItemGroup>
        </Project>
        """;

    /// <summary>
    /// Sample XML documentation content.
    /// </summary>
    public const string SampleXmlDoc = """
        <?xml version="1.0"?>
        <doc>
            <assembly>
                <name>TestAssembly</name>
            </assembly>
            <members>
                <member name="T:TestNamespace.TestClass">
                    <summary>
                    This is a test class for demonstration purposes.
                    </summary>
                </member>
                <member name="M:TestNamespace.TestClass.TestMethod">
                    <summary>
                    This method does something useful.
                    </summary>
                </member>
                <member name="P:TestNamespace.TestClass.TestProperty">
                    <summary>
                    Gets or sets the test property.
                    </summary>
                </member>
            </members>
        </doc>
        """;

    /// <summary>
    /// Creates a temporary file with the given content and returns its path.
    /// </summary>
    public static string CreateTempFile(string content, string extension = ".csproj")
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}{extension}");
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// Creates a temporary directory and returns its path.
    /// </summary>
    public static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Cleans up a temporary file if it exists.
    /// </summary>
    public static void CleanupTempFile(string path)
    {
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { }
        }
    }

    /// <summary>
    /// Cleans up a temporary directory if it exists.
    /// </summary>
    public static void CleanupTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            try { Directory.Delete(path, recursive: true); } catch { }
        }
    }
}
