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

    #region POSIX Path Tests

    [Fact]
    public void Path_Posix_Sep_ReturnsForwardSlash()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                console.log(path.posix.sep);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("/\n", output);
    }

    [Fact]
    public void Path_Posix_Delimiter_ReturnsColon()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                console.log(path.posix.delimiter);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal(":\n", output);
    }

    [Fact]
    public void Path_Posix_Join_UsesForwardSlash()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                console.log(path.posix.join('foo', 'bar', 'baz'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("foo/bar/baz\n", output);
    }

    [Fact]
    public void Path_Posix_IsAbsolute_ChecksPosixPaths()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                console.log(path.posix.isAbsolute('/foo'));
                console.log(path.posix.isAbsolute('foo'));
                console.log(path.posix.isAbsolute('C:\\foo'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("true", lines[0]);  // /foo is absolute in POSIX
        Assert.Equal("false", lines[1]); // foo is relative
        Assert.Equal("false", lines[2]); // C:\foo is NOT absolute in POSIX
    }

    [Fact]
    public void Path_Posix_Basename_ReturnsFilename()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                console.log(path.posix.basename('/foo/bar/file.txt'));
                console.log(path.posix.basename('/foo/bar/file.txt', '.txt'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("file.txt\n", output);
        Assert.Contains("file\n", output);
    }

    [Fact]
    public void Path_Posix_Dirname_ReturnsDirectory()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                console.log(path.posix.dirname('/foo/bar/baz.txt'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("/foo/bar\n", output);
    }

    [Fact]
    public void Path_Posix_Normalize_NormalizesPosixPath()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                console.log(path.posix.normalize('/foo/bar/../baz'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("/foo/baz\n", output);
    }

    [Fact]
    public void Path_Posix_Parse_ParsesPosixPath()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                const parsed = path.posix.parse('/home/user/file.txt');
                console.log(parsed.root);
                console.log(parsed.dir);
                console.log(parsed.base);
                console.log(parsed.name);
                console.log(parsed.ext);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("/", lines[0]);           // root
        Assert.Equal("/home/user", lines[1]);  // dir
        Assert.Equal("file.txt", lines[2]);    // base
        Assert.Equal("file", lines[3]);        // name
        Assert.Equal(".txt", lines[4]);        // ext
    }

    #endregion

    #region Win32 Path Tests

    [Fact]
    public void Path_Win32_Sep_ReturnsBackslash()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                console.log(path.win32.sep);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("\\\n", output);
    }

    [Fact]
    public void Path_Win32_Delimiter_ReturnsSemicolon()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                console.log(path.win32.delimiter);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal(";\n", output);
    }

    [Fact]
    public void Path_Win32_Join_UsesBackslash()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                console.log(path.win32.join('foo', 'bar', 'baz'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("foo\\bar\\baz\n", output);
    }

    [Fact]
    public void Path_Win32_IsAbsolute_ChecksWin32Paths()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                console.log(path.win32.isAbsolute('C:\\foo'));
                console.log(path.win32.isAbsolute('\\\\server\\share'));
                console.log(path.win32.isAbsolute('/foo'));
                console.log(path.win32.isAbsolute('foo'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("true", lines[0]);  // C:\foo is absolute in Win32
        Assert.Equal("true", lines[1]);  // \\server\share is UNC path, absolute
        Assert.Equal("false", lines[2]); // /foo is NOT absolute in Win32 (no drive letter)
        Assert.Equal("false", lines[3]); // foo is relative
    }

    [Fact]
    public void Path_Win32_Basename_ReturnsFilename()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                console.log(path.win32.basename('C:\\foo\\bar\\file.txt'));
                console.log(path.win32.basename('C:\\foo\\bar\\file.txt', '.txt'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Contains("file.txt\n", output);
        Assert.Contains("file\n", output);
    }

    [Fact]
    public void Path_Win32_Dirname_ReturnsDirectory()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                console.log(path.win32.dirname('C:\\foo\\bar\\baz.txt'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("C:\\foo\\bar\n", output);
    }

    [Fact]
    public void Path_Win32_Normalize_NormalizesWin32Path()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                console.log(path.win32.normalize('C:\\foo\\bar\\..\\baz'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("C:\\foo\\baz\n", output);
    }

    [Fact]
    public void Path_Win32_Parse_ParsesWin32Path()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                const parsed = path.win32.parse('C:\\Users\\user\\file.txt');
                console.log(parsed.root);
                console.log(parsed.dir);
                console.log(parsed.base);
                console.log(parsed.name);
                console.log(parsed.ext);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("C:\\", lines[0]);              // root
        Assert.Equal("C:\\Users\\user", lines[1]);   // dir
        Assert.Equal("file.txt", lines[2]);          // base
        Assert.Equal("file", lines[3]);              // name
        Assert.Equal(".txt", lines[4]);              // ext
    }

    #endregion
}
