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
}
