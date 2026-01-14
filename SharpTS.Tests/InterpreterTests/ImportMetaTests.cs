using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for import.meta.url feature in interpreter mode.
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
}
