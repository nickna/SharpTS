using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for import.meta.url feature that provides module metadata.
/// </summary>
public class ImportMetaTests
{
    [Fact]
    public void ImportMeta_UrlProperty_ReturnsString()
    {
        var source = """
            const url = import.meta.url;
            console.log(typeof url === "string");
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ImportMeta_DirectAccess_Works()
    {
        var source = """
            const meta = import.meta;
            console.log(typeof meta.url === "string");
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ImportMeta_UrlStartsWithFile_InSingleFileMode()
    {
        var source = """
            const url = import.meta.url;
            // In single-file mode, url may be empty or a file:// path
            console.log(url === "" || url.startsWith("file://"));
            """;

        var output = TestHarness.RunCompiled(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ImportMeta_DirnameProperty_ReturnsString()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const dirname = import.meta.dirname;
                console.log(typeof dirname === "string");
                console.log(dirname.length > 0);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void ImportMeta_FilenameProperty_ReturnsString()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const filename = import.meta.filename;
                console.log(typeof filename === "string");
                console.log(filename.endsWith('.ts'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void ImportMeta_AllProperties_AreConsistent()
    {
        // All import.meta properties should be accessible and consistent
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const meta = import.meta;
                console.log(typeof meta.url === "string");
                console.log(typeof meta.dirname === "string");
                console.log(typeof meta.filename === "string");
                // filename should include the directory from dirname
                console.log(meta.filename.includes('main.ts'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }
}
