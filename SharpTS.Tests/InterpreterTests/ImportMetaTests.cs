using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for import.meta properties in interpreter mode.
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

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void ImportMeta_DirectAccess_Works()
    {
        var source = """
            const meta = import.meta;
            console.log(typeof meta.url === "string");
            """;

        var output = TestHarness.RunInterpreted(source);
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }
}
