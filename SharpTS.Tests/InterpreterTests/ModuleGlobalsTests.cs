using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for Node.js-style module globals (__dirname, __filename).
/// </summary>
public class ModuleGlobalsTests
{
    [Fact]
    public void Dirname_ReturnsString()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                console.log(typeof __dirname === 'string');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Filename_ReturnsString()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                console.log(typeof __filename === 'string');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Dirname_ContainsPath()
    {
        // __dirname should be a non-empty path string
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                console.log(__dirname.length > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Filename_EndsWithTs()
    {
        // __filename should end with .ts for our test file
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                console.log(__filename.endsWith('.ts'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Filename_ContainsMainTs()
    {
        // __filename should contain the file name
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                console.log(__filename.includes('main.ts'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Dirname_MatchesImportMetaDirname()
    {
        // __dirname and import.meta.dirname should return the same value
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                console.log(__dirname === import.meta.dirname);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Filename_MatchesImportMetaFilename()
    {
        // __filename and import.meta.filename should return the same value
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                console.log(__filename === import.meta.filename);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }
}
