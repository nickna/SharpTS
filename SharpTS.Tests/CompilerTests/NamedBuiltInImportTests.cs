using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Tests for named imports from built-in modules in IL compilation mode.
/// These tests verify that import { func } from 'module' works correctly.
/// </summary>
public class NamedBuiltInImportTests
{
    [Fact]
    public void Fs_NamedImport_ExistsSync_Works()
    {
        var testFile = Path.GetTempFileName();
        try
        {
            var files = new Dictionary<string, string>
            {
                ["main.ts"] = $$"""
                    import { existsSync } from 'fs';
                    console.log(existsSync('{{testFile.Replace("\\", "\\\\")}}'));
                    """
            };

            var result = TestHarness.RunModulesCompiled(files, "main.ts").TrimEnd();
            Assert.Equal("true", result.ToLower());
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public void Fs_NamedImport_WriteFileSync_ReadFileSync_Works()
    {
        var testFile = Path.Combine(Path.GetTempPath(), $"sharptstest_{Guid.NewGuid()}.txt");
        try
        {
            var files = new Dictionary<string, string>
            {
                ["main.ts"] = $$"""
                    import { writeFileSync, readFileSync } from 'fs';
                    writeFileSync('{{testFile.Replace("\\", "\\\\")}}', 'hello world');
                    const content = readFileSync('{{testFile.Replace("\\", "\\\\")}}', 'utf-8');
                    console.log(content);
                    """
            };

            var result = TestHarness.RunModulesCompiled(files, "main.ts").TrimEnd();
            Assert.Equal("hello world", result);
        }
        finally
        {
            if (File.Exists(testFile))
                File.Delete(testFile);
        }
    }

    [Fact]
    public void Fs_NamedImport_MultipleImports_Work()
    {
        var testFile = Path.Combine(Path.GetTempPath(), $"sharptstest_{Guid.NewGuid()}.txt");
        try
        {
            var files = new Dictionary<string, string>
            {
                ["main.ts"] = $$"""
                    import { writeFileSync, existsSync, unlinkSync } from 'fs';
                    writeFileSync('{{testFile.Replace("\\", "\\\\")}}', 'test');
                    console.log(existsSync('{{testFile.Replace("\\", "\\\\")}}'));
                    unlinkSync('{{testFile.Replace("\\", "\\\\")}}');
                    console.log(existsSync('{{testFile.Replace("\\", "\\\\")}}'));
                    """
            };

            var result = TestHarness.RunModulesCompiled(files, "main.ts");
            var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim().ToLower())
                .ToArray();
            Assert.Equal(["true", "false"], lines);
        }
        finally
        {
            if (File.Exists(testFile))
                File.Delete(testFile);
        }
    }

    [Fact]
    public void Path_NamedImport_Basename_Works()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { basename } from 'path';
                console.log(basename('/foo/bar/baz.txt'));
                """
        };

        var result = TestHarness.RunModulesCompiled(files, "main.ts").TrimEnd();
        Assert.Equal("baz.txt", result);
    }

    [Fact]
    public void Path_NamedImport_Dirname_Works()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { dirname } from 'path';
                console.log(dirname('/foo/bar/baz.txt'));
                """
        };

        var result = TestHarness.RunModulesCompiled(files, "main.ts").TrimEnd();
        // On Windows, this might be /foo/bar or \foo\bar
        Assert.Contains("foo", result);
        Assert.Contains("bar", result);
    }

    [Fact]
    public void Path_NamedImport_Extname_Works()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { extname } from 'path';
                console.log(extname('test.ts'));
                """
        };

        var result = TestHarness.RunModulesCompiled(files, "main.ts").TrimEnd();
        Assert.Equal(".ts", result);
    }

    [Fact]
    public void Path_NamedImport_Join_Works()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { join } from 'path';
                const result = join('foo', 'bar', 'baz.txt');
                console.log(result.includes('foo'));
                console.log(result.includes('bar'));
                console.log(result.includes('baz.txt'));
                """
        };

        var result = TestHarness.RunModulesCompiled(files, "main.ts");
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().ToLower())
            .ToArray();
        Assert.Equal(["true", "true", "true"], lines);
    }

    [Fact]
    public void Path_NamedImport_IsAbsolute_Works()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { isAbsolute } from 'path';
                console.log(isAbsolute('foo/bar'));
                """
        };

        var result = TestHarness.RunModulesCompiled(files, "main.ts").TrimEnd().ToLower();
        // Relative path is never absolute
        Assert.Equal("false", result);
    }

    [Fact]
    public void Path_NamedImport_MultipleImports_Work()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { basename, dirname, extname } from 'path';
                console.log(basename('/foo/bar/test.ts'));
                console.log(extname('test.ts'));
                """
        };

        var result = TestHarness.RunModulesCompiled(files, "main.ts");
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .ToArray();
        Assert.Equal("test.ts", lines[0]);
        Assert.Equal(".ts", lines[1]);
    }

    [Fact]
    public void NamespaceImports_StillWork()
    {
        // Verify namespace imports still work after our changes
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                console.log(path.basename('/foo/bar/baz.txt'));
                console.log(path.extname('test.ts'));
                """
        };

        var result = TestHarness.RunModulesCompiled(files, "main.ts");
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .ToArray();
        Assert.Equal("baz.txt", lines[0]);
        Assert.Equal(".ts", lines[1]);
    }
}
