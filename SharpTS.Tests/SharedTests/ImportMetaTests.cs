using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for import.meta properties.
/// </summary>
public class ImportMetaTests
{
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ImportMeta_UrlProperty_ReturnsString(ExecutionMode mode)
    {
        var source = """
            const url = import.meta.url;
            console.log(typeof url === "string");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ImportMeta_DirectAccess_Works(ExecutionMode mode)
    {
        var source = """
            const meta = import.meta;
            console.log(typeof meta.url === "string");
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ImportMeta_DirnameProperty_ReturnsString(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const dirname = import.meta.dirname;
                console.log(typeof dirname === "string");
                console.log(dirname.length > 0);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ImportMeta_FilenameProperty_ReturnsString(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const filename = import.meta.filename;
                console.log(typeof filename === "string");
                console.log(filename.endsWith('.ts'));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ImportMeta_AllProperties_AreConsistent(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ImportMeta_UrlStartsWithFile_InSingleFileMode(ExecutionMode mode)
    {
        var source = """
            const url = import.meta.url;
            // In single-file mode, url may be empty or a file:// path
            console.log(url === "" || url.startsWith("file://"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }
}
