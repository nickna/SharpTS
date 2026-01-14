using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests.BuiltInModules;

/// <summary>
/// Tests for Node.js-compatible error codes in fs module operations.
/// </summary>
public class NodeErrorTests
{
    [Fact]
    public void ReadFileSync_NonexistentFile_ThrowsENOENT()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { readFileSync } from 'fs';
                try {
                    readFileSync('nonexistent_file_xyz.txt', 'utf8');
                    console.log('no error');
                } catch (e) {
                    console.log(e.code === 'ENOENT');
                    console.log(e.syscall === 'open');
                }
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void StatSync_NonexistentFile_ThrowsENOENT()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { statSync } from 'fs';
                try {
                    statSync('nonexistent_file_xyz.txt');
                    console.log('no error');
                } catch (e) {
                    console.log(e.code === 'ENOENT');
                    console.log(e.syscall === 'stat');
                }
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void ReaddirSync_NonexistentDir_ThrowsENOENT()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { readdirSync } from 'fs';
                try {
                    readdirSync('nonexistent_dir_xyz');
                    console.log('no error');
                } catch (e) {
                    console.log(e.code === 'ENOENT');
                    console.log(e.syscall === 'readdir');
                }
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void UnlinkSync_NonexistentFile_ThrowsENOENT()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { unlinkSync } from 'fs';
                try {
                    unlinkSync('nonexistent_file_xyz.txt');
                    console.log('no error');
                } catch (e) {
                    console.log(e.code === 'ENOENT');
                    console.log(e.syscall === 'unlink');
                }
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void RmdirSync_NonexistentDir_ThrowsENOENT()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { rmdirSync } from 'fs';
                try {
                    rmdirSync('nonexistent_dir_xyz');
                    console.log('no error');
                } catch (e) {
                    console.log(e.code === 'ENOENT');
                    console.log(e.syscall === 'rmdir');
                }
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void RenameSync_NonexistentFile_ThrowsENOENT()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { renameSync } from 'fs';
                try {
                    renameSync('nonexistent_file_xyz.txt', 'new_name.txt');
                    console.log('no error');
                } catch (e) {
                    console.log(e.code === 'ENOENT');
                    console.log(e.syscall === 'rename');
                }
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void CopyFileSync_NonexistentFile_ThrowsENOENT()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { copyFileSync } from 'fs';
                try {
                    copyFileSync('nonexistent_file_xyz.txt', 'dest.txt');
                    console.log('no error');
                } catch (e) {
                    console.log(e.code === 'ENOENT');
                    console.log(e.syscall === 'copyfile');
                }
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void AccessSync_NonexistentFile_ThrowsENOENT()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { accessSync } from 'fs';
                try {
                    accessSync('nonexistent_file_xyz.txt');
                    console.log('no error');
                } catch (e) {
                    console.log(e.code === 'ENOENT');
                    console.log(e.syscall === 'access');
                }
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void NodeError_HasPathProperty()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { readFileSync } from 'fs';
                try {
                    readFileSync('specific_path.txt', 'utf8');
                } catch (e) {
                    console.log(e.path === 'specific_path.txt');
                }
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void NodeError_MessageIncludesCode()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { readFileSync } from 'fs';
                try {
                    readFileSync('nonexistent.txt', 'utf8');
                } catch (e) {
                    console.log(e.message.includes('ENOENT'));
                }
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\n", output);
    }
}
