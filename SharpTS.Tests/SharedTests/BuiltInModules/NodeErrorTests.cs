using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for Node.js-compatible error codes in fs module operations.
/// Uses namespace imports (import * as fs) for compiled mode compatibility.
/// </summary>
public class NodeErrorTests
{
    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ReadFileSync_NonexistentFile_ThrowsENOENT(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                try {
                    fs.readFileSync('nonexistent_file_xyz.txt', 'utf8');
                    console.log('no error');
                } catch (e) {
                    console.log(e.code === 'ENOENT');
                    console.log(e.syscall === 'open');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void StatSync_NonexistentFile_ThrowsENOENT(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                try {
                    fs.statSync('nonexistent_file_xyz.txt');
                    console.log('no error');
                } catch (e) {
                    console.log(e.code === 'ENOENT');
                    console.log(e.syscall === 'stat');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void ReaddirSync_NonexistentDir_ThrowsENOENT(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                try {
                    fs.readdirSync('nonexistent_dir_xyz');
                    console.log('no error');
                } catch (e) {
                    console.log(e.code === 'ENOENT');
                    console.log(e.syscall === 'readdir');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void UnlinkSync_NonexistentFile_ThrowsENOENT(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                try {
                    fs.unlinkSync('nonexistent_file_xyz.txt');
                    console.log('no error');
                } catch (e) {
                    console.log(e.code === 'ENOENT');
                    console.log(e.syscall === 'unlink');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void RmdirSync_NonexistentDir_ThrowsENOENT(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                try {
                    fs.rmdirSync('nonexistent_dir_xyz');
                    console.log('no error');
                } catch (e) {
                    console.log(e.code === 'ENOENT');
                    console.log(e.syscall === 'rmdir');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void RenameSync_NonexistentFile_ThrowsENOENT(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                try {
                    fs.renameSync('nonexistent_file_xyz.txt', 'new_name.txt');
                    console.log('no error');
                } catch (e) {
                    console.log(e.code === 'ENOENT');
                    console.log(e.syscall === 'rename');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void CopyFileSync_NonexistentFile_ThrowsENOENT(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                try {
                    fs.copyFileSync('nonexistent_file_xyz.txt', 'dest.txt');
                    console.log('no error');
                } catch (e) {
                    console.log(e.code === 'ENOENT');
                    console.log(e.syscall === 'copyfile');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AccessSync_NonexistentFile_ThrowsENOENT(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                try {
                    fs.accessSync('nonexistent_file_xyz.txt');
                    console.log('no error');
                } catch (e) {
                    console.log(e.code === 'ENOENT');
                    console.log(e.syscall === 'access');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NodeError_HasPathProperty(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                try {
                    fs.readFileSync('specific_path.txt', 'utf8');
                } catch (e) {
                    console.log(e.path === 'specific_path.txt');
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void NodeError_MessageIncludesCode(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                try {
                    fs.readFileSync('nonexistent.txt', 'utf8');
                } catch (e) {
                    console.log(e.message.includes('ENOENT'));
                }
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }
}
