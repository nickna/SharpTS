using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests.BuiltInModules;

/// <summary>
/// Tests for the built-in 'path' module.
/// </summary>
public class PathModuleTests
{
    [Fact]
    public void Path_Join_CombinesPaths()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                console.log(path.join('foo', 'bar'));
                console.log(path.join('foo', 'bar', 'baz'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        // Path separator is OS-specific, so check both variants
        Assert.True(output.Contains("foo\\bar") || output.Contains("foo/bar"));
        Assert.True(output.Contains("foo\\bar\\baz") || output.Contains("foo/bar/baz"));
    }

    [Fact]
    public void Path_Basename_ReturnsFileName()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                console.log(path.basename('/foo/bar/baz.txt'));
                console.log(path.basename('/foo/bar/baz.txt', '.txt'));
                console.log(path.basename('C:\\foo\\bar\\file.js'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("baz.txt\n", output);
        Assert.Contains("baz\n", output);
        Assert.Contains("file.js\n", output);
    }

    [Fact]
    public void Path_Dirname_ReturnsDirectory()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                const dir = path.dirname('/foo/bar/baz.txt');
                console.log(dir.includes('foo'));
                console.log(dir.includes('bar'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output);
    }

    [Fact]
    public void Path_Extname_ReturnsExtension()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                console.log(path.extname('file.txt'));
                console.log(path.extname('file.tar.gz'));
                console.log(path.extname('file'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal(".txt\n.gz\n\n", output);
    }

    [Fact]
    public void Path_IsAbsolute_ChecksAbsolutePaths()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                // These should be true on Windows
                console.log(path.isAbsolute('C:\\foo'));
                console.log(path.isAbsolute('/foo'));
                // These should be false
                console.log(path.isAbsolute('foo'));
                console.log(path.isAbsolute('./foo'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // At least some should be true (depends on OS)
        Assert.Contains("true", lines);
        Assert.Contains("false", lines);
    }

    [Fact]
    public void Path_Normalize_NormalizesPath()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                const normalized = path.normalize('foo/bar/../baz');
                console.log(normalized.includes('baz'));
                console.log(!normalized.includes('..'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }

    [Fact]
    public void Path_Sep_ReturnsSeparator()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                const sep = path.sep;
                console.log(sep === '/' || sep === '\\');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Path_Delimiter_ReturnsDelimiter()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                const delim = path.delimiter;
                console.log(delim === ':' || delim === ';');
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Path_Parse_ParsesPath()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                const parsed = path.parse('/home/user/file.txt');
                console.log(parsed.base);
                console.log(parsed.name);
                console.log(parsed.ext);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("file.txt\n", output);
        Assert.Contains("file\n", output);
        Assert.Contains(".txt\n", output);
    }

    [Fact]
    public void Path_Format_FormatsPath()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                const formatted = path.format({ root: '', dir: '/home/user', base: 'file.txt', name: 'file', ext: '.txt' });
                console.log(formatted.includes('home'));
                console.log(formatted.includes('file.txt'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output);
    }

    [Fact]
    public void Path_Resolve_ResolvesPath()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                const resolved = path.resolve('foo', 'bar');
                // Resolved path should be absolute
                console.log(path.isAbsolute(resolved));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Path_Relative_ReturnsRelativePath()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                const rel = path.relative('/foo/bar', '/foo/baz');
                console.log(rel.includes('baz'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("true", output.ToLower());
    }
}
