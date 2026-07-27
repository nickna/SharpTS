using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for named imports from built-in modules (import { func } from 'module').
/// Migrated from CompilerTests to run against both interpreter and compiler.
/// </summary>
public class NamedBuiltInImportTests
{
    [Theory, ModeData]
    public void Fs_NamedImport_ExistsSync_Works(ExecutionMode mode)
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

            var result = TestHarness.RunModules(files, "main.ts", mode).TrimEnd();
            Assert.Equal("true", result.ToLower());
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Theory, ModeData]
    public void Fs_NamedImport_WriteFileSync_ReadFileSync_Works(ExecutionMode mode)
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

            var result = TestHarness.RunModules(files, "main.ts", mode).TrimEnd();
            Assert.Equal("hello world", result);
        }
        finally
        {
            if (File.Exists(testFile))
                File.Delete(testFile);
        }
    }

    [Theory, ModeData]
    public void Fs_NamedImport_MultipleImports_Work(ExecutionMode mode)
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

            var result = TestHarness.RunModules(files, "main.ts", mode);
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

    [Theory, ModeData]
    public void Path_NamedImport_Basename_Works(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { basename } from 'path';
                console.log(basename('/foo/bar/baz.txt'));
                """
        };

        var result = TestHarness.RunModules(files, "main.ts", mode).TrimEnd();
        Assert.Equal("baz.txt", result);
    }

    [Theory, ModeData]
    public void Path_NamedImport_Dirname_Works(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { dirname } from 'path';
                console.log(dirname('/foo/bar/baz.txt'));
                """
        };

        var result = TestHarness.RunModules(files, "main.ts", mode).TrimEnd();
        // On Windows, this might be /foo/bar or \foo\bar
        Assert.Contains("foo", result);
        Assert.Contains("bar", result);
    }

    [Theory, ModeData]
    public void Path_NamedImport_Extname_Works(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { extname } from 'path';
                console.log(extname('test.ts'));
                """
        };

        var result = TestHarness.RunModules(files, "main.ts", mode).TrimEnd();
        Assert.Equal(".ts", result);
    }

    [Theory, ModeData]
    public void Path_NamedImport_Join_Works(ExecutionMode mode)
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

        var result = TestHarness.RunModules(files, "main.ts", mode);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().ToLower())
            .ToArray();
        Assert.Equal(["true", "true", "true"], lines);
    }

    [Theory, ModeData]
    public void Path_NamedImport_IsAbsolute_Works(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { isAbsolute } from 'path';
                console.log(isAbsolute('foo/bar'));
                """
        };

        var result = TestHarness.RunModules(files, "main.ts", mode).TrimEnd().ToLower();
        Assert.Equal("false", result);
    }

    [Theory, ModeData]
    public void Path_NamedImport_MultipleImports_Work(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { basename, dirname, extname } from 'path';
                console.log(basename('/foo/bar/test.ts'));
                console.log(extname('test.ts'));
                """
        };

        var result = TestHarness.RunModules(files, "main.ts", mode);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .ToArray();
        Assert.Equal("test.ts", lines[0]);
        Assert.Equal(".ts", lines[1]);
    }

    [Theory, ModeData]
    public void NamespaceImports_StillWork(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                console.log(path.basename('/foo/bar/baz.txt'));
                console.log(path.extname('test.ts'));
                """
        };

        var result = TestHarness.RunModules(files, "main.ts", mode);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .ToArray();
        Assert.Equal("baz.txt", lines[0]);
        Assert.Equal(".ts", lines[1]);
    }
}
