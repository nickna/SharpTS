using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests.BuiltInModules;

/// <summary>
/// Tests for built-in module support (fs, path, os) in interpreter mode.
/// </summary>
public class InterpreterModuleTests
{
    // ============ PATH MODULE TESTS ============

    [Fact]
    public void Path_Join_CombinesPaths()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                console.log(path.join('a', 'b', 'c'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        // On Windows: a\b\c, on Unix: a/b/c
        Assert.True(output.Contains("a") && output.Contains("b") && output.Contains("c"));
    }

    [Fact]
    public void Path_Basename_ReturnsFilename()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { basename } from 'path';
                console.log(basename('/foo/bar/baz.txt'));
                console.log(basename('/foo/bar/baz.txt', '.txt'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Contains("baz.txt", output);
        Assert.Contains("baz\n", output);
    }

    [Fact]
    public void Path_Dirname_ReturnsDirectory()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { dirname } from 'path';
                const dir = dirname('/foo/bar/baz.txt');
                console.log(dir.includes('bar'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Path_Extname_ReturnsExtension()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { extname } from 'path';
                console.log(extname('file.txt'));
                console.log(extname('file'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Contains(".txt", output);
        Assert.Contains("\n\n", output); // Empty extension for 'file'
    }

    [Fact]
    public void Path_IsAbsolute_ChecksPathType()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { isAbsolute } from 'path';
                console.log(isAbsolute('/absolute/path'));
                console.log(isAbsolute('relative/path'));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Contains("true", output);
        Assert.Contains("false", output);
    }

    [Fact]
    public void Path_Sep_ReturnsPathSeparator()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { sep } from 'path';
                console.log(sep === '\\' || sep === '/');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Path_Parse_ReturnsPathComponents()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { parse } from 'path';
                const parsed = parse('/home/user/file.txt');
                console.log(parsed.name === 'file');
                console.log(parsed.ext === '.txt');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    // ============ OS MODULE TESTS ============

    [Fact]
    public void Os_Platform_ReturnsValidPlatform()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as os from 'os';
                const platform = os.platform();
                console.log(platform === 'win32' || platform === 'linux' || platform === 'darwin' || platform === 'unknown');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_Arch_ReturnsValidArchitecture()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { arch } from 'os';
                const a = arch();
                console.log(a === 'x64' || a === 'x86' || a === 'arm' || a === 'arm64' || a === 'ia32' || a === 'unknown');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_Hostname_ReturnsNonEmpty()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { hostname } from 'os';
                console.log(hostname().length > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_Homedir_ReturnsPath()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { homedir } from 'os';
                console.log(homedir().length > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_Tmpdir_ReturnsPath()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { tmpdir } from 'os';
                console.log(tmpdir().length > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_EOL_ReturnsNewline()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { EOL } from 'os';
                console.log(EOL === '\n' || EOL === '\r\n');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_Cpus_ReturnsArray()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { cpus } from 'os';
                const cpuList = cpus();
                console.log(Array.isArray(cpuList));
                console.log(cpuList.length > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Os_Totalmem_ReturnsPositiveNumber()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { totalmem } from 'os';
                console.log(totalmem() > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Os_UserInfo_ReturnsObject()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { userInfo } from 'os';
                const info = userInfo();
                console.log(typeof info.username === 'string');
                console.log(info.username.length > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    // ============ FS MODULE TESTS ============

    [Fact]
    public void Fs_ExistsSync_ChecksFileExistence()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { existsSync, writeFileSync, unlinkSync } from 'fs';
                // Create a test file and check existence
                writeFileSync('test_exists.txt', 'test');
                console.log(existsSync('test_exists.txt'));
                console.log(existsSync('nonexistent_file_xyz.txt'));
                unlinkSync('test_exists.txt');
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\nfalse\n", output);
    }

    [Fact]
    public void Fs_WriteFileSync_And_ReadFileSync()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { writeFileSync, readFileSync, unlinkSync } from 'fs';
                const testFile = 'test_output.txt';
                writeFileSync(testFile, 'hello world');
                const content = readFileSync(testFile, 'utf8');
                console.log(content);
                unlinkSync(testFile);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("hello world\n", output);
    }

    [Fact]
    public void Fs_AppendFileSync_AppendsContent()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { writeFileSync, appendFileSync, readFileSync, unlinkSync } from 'fs';
                const testFile = 'test_append.txt';
                writeFileSync(testFile, 'hello');
                appendFileSync(testFile, ' world');
                const content = readFileSync(testFile, 'utf8');
                console.log(content);
                unlinkSync(testFile);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("hello world\n", output);
    }

    [Fact]
    public void Fs_MkdirSync_And_RmdirSync()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { mkdirSync, rmdirSync, existsSync } from 'fs';
                const testDir = 'test_mkdir_dir';
                mkdirSync(testDir);
                console.log(existsSync(testDir));
                rmdirSync(testDir);
                console.log(existsSync(testDir));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\nfalse\n", output);
    }

    [Fact]
    public void Fs_ReaddirSync_ListsEntries()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { readdirSync } from 'fs';
                const entries = readdirSync('.');
                console.log(Array.isArray(entries));
                console.log(entries.length > 0);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Fs_StatSync_ReturnsFileInfo()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { writeFileSync, statSync, unlinkSync } from 'fs';
                const testFile = 'test_stat.txt';
                writeFileSync(testFile, 'test content');
                const stats = statSync(testFile);
                console.log(stats.isFile);
                console.log(stats.isDirectory);
                console.log(stats.size > 0);
                unlinkSync(testFile);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\nfalse\ntrue\n", output);
    }

    [Fact]
    public void Fs_StatSync_DetectsDirectory()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { mkdirSync, statSync, rmdirSync } from 'fs';
                const testDir = 'test_stat_dir';
                mkdirSync(testDir);
                const stats = statSync(testDir);
                console.log(stats.isFile);
                console.log(stats.isDirectory);
                rmdirSync(testDir);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("false\ntrue\n", output);
    }

    [Fact]
    public void Fs_RenameSync_MovesFile()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { writeFileSync, renameSync, existsSync, unlinkSync, readFileSync } from 'fs';
                const oldFile = 'test_rename_old.txt';
                const newFile = 'test_rename_new.txt';
                writeFileSync(oldFile, 'content');
                renameSync(oldFile, newFile);
                console.log(existsSync(oldFile));
                console.log(existsSync(newFile));
                console.log(readFileSync(newFile, 'utf8'));
                unlinkSync(newFile);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("false\ntrue\ncontent\n", output);
    }

    [Fact]
    public void Fs_CopyFileSync_CopiesFile()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { writeFileSync, copyFileSync, readFileSync, unlinkSync } from 'fs';
                const srcFile = 'test_copy_src.txt';
                const destFile = 'test_copy_dest.txt';
                writeFileSync(srcFile, 'copy content');
                copyFileSync(srcFile, destFile);
                console.log(readFileSync(destFile, 'utf8'));
                unlinkSync(srcFile);
                unlinkSync(destFile);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("copy content\n", output);
    }

    // ============ MIXED IMPORTS ============

    [Fact]
    public void MixedModuleImports_WorkTogether()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as path from 'path';
                import * as os from 'os';
                import * as fs from 'fs';

                // Use all three modules together
                const tempDir = os.tmpdir();
                const testFile = path.join(tempDir, 'sharpts_test_mixed.txt');
                fs.writeFileSync(testFile, 'mixed test');
                const content = fs.readFileSync(testFile, 'utf8');
                console.log(content);
                fs.unlinkSync(testFile);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("mixed test\n", output);
    }
}
