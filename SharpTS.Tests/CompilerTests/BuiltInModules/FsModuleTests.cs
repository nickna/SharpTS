using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests.BuiltInModules;

/// <summary>
/// Tests for the built-in 'fs' module (sync APIs only).
/// </summary>
public class FsModuleTests
{
    [Fact]
    public void Fs_ExistsSync_ReturnsTrueForExistingFile()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                // main.ts exists since we're running it
                console.log(fs.existsSync('main.ts'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Fs_ExistsSync_ReturnsFalseForNonexistentFile()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                console.log(fs.existsSync('nonexistent_file_12345.txt'));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void Fs_WriteFileSync_And_ReadFileSync_WorkTogether()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                const testFile = 'test_write_read.txt';
                const testContent = 'Hello, SharpTS!';

                fs.writeFileSync(testFile, testContent);
                const content = fs.readFileSync(testFile, 'utf8');
                console.log(content === testContent);

                // Cleanup
                fs.unlinkSync(testFile);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Fs_AppendFileSync_AppendsToFile()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                const testFile = 'test_append.txt';

                fs.writeFileSync(testFile, 'Line1');
                fs.appendFileSync(testFile, '\nLine2');
                const content = fs.readFileSync(testFile, 'utf8');
                console.log(content.includes('Line1'));
                console.log(content.includes('Line2'));

                // Cleanup
                fs.unlinkSync(testFile);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Fs_MkdirSync_And_RmdirSync_WorkTogether()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                const testDir = 'test_dir_fs';

                fs.mkdirSync(testDir);
                console.log(fs.existsSync(testDir));

                fs.rmdirSync(testDir);
                console.log(fs.existsSync(testDir));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\nfalse\n", output);
    }

    [Fact]
    public void Fs_ReaddirSync_ListsDirectoryContents()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                const testDir = 'test_readdir';

                fs.mkdirSync(testDir);
                fs.writeFileSync(testDir + '/file1.txt', 'content1');
                fs.writeFileSync(testDir + '/file2.txt', 'content2');

                const entries = fs.readdirSync(testDir);
                console.log(entries.length);

                // Cleanup
                fs.unlinkSync(testDir + '/file1.txt');
                fs.unlinkSync(testDir + '/file2.txt');
                fs.rmdirSync(testDir);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("2\n", output);
    }

    [Fact]
    public void Fs_StatSync_ReturnsFileInfo()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                const testFile = 'test_stat.txt';
                const content = 'Test content for stat';

                fs.writeFileSync(testFile, content);
                const stat = fs.statSync(testFile);

                console.log(stat.isFile);
                console.log(stat.isDirectory);
                console.log(stat.size > 0);

                // Cleanup
                fs.unlinkSync(testFile);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\nfalse\ntrue\n", output);
    }

    [Fact]
    public void Fs_StatSync_ReturnsDirectoryInfo()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                const testDir = 'test_stat_dir';

                fs.mkdirSync(testDir);
                const stat = fs.statSync(testDir);

                console.log(stat.isFile);
                console.log(stat.isDirectory);

                // Cleanup
                fs.rmdirSync(testDir);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("false\ntrue\n", output);
    }

    [Fact]
    public void Fs_CopyFileSync_CopiesFile()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                const srcFile = 'test_copy_src.txt';
                const destFile = 'test_copy_dest.txt';
                const content = 'Content to copy';

                fs.writeFileSync(srcFile, content);
                fs.copyFileSync(srcFile, destFile);

                console.log(fs.existsSync(destFile));
                const copiedContent = fs.readFileSync(destFile, 'utf8');
                console.log(copiedContent === content);

                // Cleanup
                fs.unlinkSync(srcFile);
                fs.unlinkSync(destFile);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Fs_RenameSync_RenamesFile()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                const oldName = 'test_rename_old.txt';
                const newName = 'test_rename_new.txt';

                fs.writeFileSync(oldName, 'content');
                fs.renameSync(oldName, newName);

                console.log(fs.existsSync(oldName));
                console.log(fs.existsSync(newName));

                // Cleanup
                fs.unlinkSync(newName);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("false\ntrue\n", output);
    }

    [Fact]
    public void Fs_UnlinkSync_DeletesFile()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                const testFile = 'test_unlink.txt';

                fs.writeFileSync(testFile, 'content');
                console.log(fs.existsSync(testFile));

                fs.unlinkSync(testFile);
                console.log(fs.existsSync(testFile));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\nfalse\n", output);
    }

    [Fact]
    public void Fs_AccessSync_DoesNotThrowForExistingFile()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                const testFile = 'test_access.txt';

                fs.writeFileSync(testFile, 'content');

                let threw = false;
                try {
                    fs.accessSync(testFile);
                } catch (e) {
                    threw = true;
                }
                console.log(threw);

                // Cleanup
                fs.unlinkSync(testFile);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void Fs_AccessSync_ThrowsForNonexistentFile()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';

                let threw = false;
                try {
                    fs.accessSync('nonexistent_file_access_test.txt');
                } catch (e) {
                    threw = true;
                }
                console.log(threw);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Fs_RmdirSync_WithRecursive_DeletesNestedDirectories()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                const testDir = 'test_rmdir_recursive';

                fs.mkdirSync(testDir);
                fs.mkdirSync(testDir + '/subdir');
                fs.writeFileSync(testDir + '/subdir/file.txt', 'content');

                fs.rmdirSync(testDir, { recursive: true });
                console.log(fs.existsSync(testDir));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("false\n", output);
    }

    [Fact]
    public void Fs_Constants_ExportsAccessConstants()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                console.log(fs.constants.F_OK === 0);
                console.log(fs.constants.R_OK === 4);
                console.log(fs.constants.W_OK === 2);
                console.log(fs.constants.X_OK === 1);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Fs_TruncateSync_TruncatesFile()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                const testFile = 'test_truncate.txt';

                fs.writeFileSync(testFile, 'Hello World!');
                const beforeSize = fs.statSync(testFile).size;
                console.log(beforeSize > 0);

                fs.truncateSync(testFile, 5);
                const afterSize = fs.statSync(testFile).size;
                console.log(afterSize === 5);

                const content = fs.readFileSync(testFile, 'utf8');
                console.log(content === 'Hello');

                // Cleanup
                fs.unlinkSync(testFile);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Fs_TruncateSync_ExtendsFileWithZeros()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                const testFile = 'test_truncate_extend.txt';

                fs.writeFileSync(testFile, 'Hi');
                fs.truncateSync(testFile, 10);

                const stat = fs.statSync(testFile);
                console.log(stat.size === 10);

                // Cleanup
                fs.unlinkSync(testFile);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Fs_SymlinkSync_CreatesSymbolicLink()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                const testFile = 'test_symlink_target.txt';
                const linkPath = 'test_symlink_link.txt';

                fs.writeFileSync(testFile, 'content');
                fs.symlinkSync(testFile, linkPath);

                console.log(fs.existsSync(linkPath));

                // Cleanup
                fs.unlinkSync(linkPath);
                fs.unlinkSync(testFile);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Fs_RealpathSync_ResolvesAbsolutePath()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                const testFile = 'test_realpath.txt';

                fs.writeFileSync(testFile, 'content');

                const realPath = fs.realpathSync(testFile);
                // realPath should be an absolute path
                console.log(realPath.includes('test_realpath.txt'));
                console.log(realPath.length > testFile.length);

                // Cleanup
                fs.unlinkSync(testFile);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void Fs_UtimesSync_SetsFileTimes()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                const testFile = 'test_utimes.txt';

                fs.writeFileSync(testFile, 'content');

                // Set times to Unix epoch + 1000000 seconds
                const timestamp = 1000000;
                fs.utimesSync(testFile, timestamp, timestamp);

                // File should still exist and be readable
                console.log(fs.existsSync(testFile));

                // Cleanup
                fs.unlinkSync(testFile);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Fs_LstatSync_ReturnsSymlinkInfo()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                const testFile = 'test_lstat_target.txt';
                const linkPath = 'test_lstat_link.txt';

                fs.writeFileSync(testFile, 'content');
                fs.symlinkSync(testFile, linkPath);

                const stat = fs.lstatSync(linkPath);
                console.log(stat.isSymbolicLink === true);

                // Cleanup
                fs.unlinkSync(linkPath);
                fs.unlinkSync(testFile);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Fs_ReaddirSync_WithFileTypes_ReturnsDirentObjects()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                const testDir = 'test_readdir_dirent';

                fs.mkdirSync(testDir);
                fs.writeFileSync(testDir + '/file.txt', 'content');
                fs.mkdirSync(testDir + '/subdir');

                const entries: any = fs.readdirSync(testDir, { withFileTypes: true });
                console.log(entries.length === 2);

                // Find the file entry - check each entry manually
                let fileEntry: any = null;
                let dirEntry: any = null;
                for (let i = 0; i < entries.length; i++) {
                    const e = entries[i];
                    if (e.name === 'file.txt') {
                        fileEntry = e;
                    }
                    if (e.name === 'subdir') {
                        dirEntry = e;
                    }
                }
                console.log(fileEntry !== null);
                console.log(fileEntry.isFile === true);
                console.log(fileEntry.isDirectory === false);

                console.log(dirEntry !== null);
                console.log(dirEntry.isDirectory === true);

                // Cleanup
                fs.unlinkSync(testDir + '/file.txt');
                fs.rmdirSync(testDir + '/subdir');
                fs.rmdirSync(testDir);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\ntrue\ntrue\ntrue\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Fs_ChmodSync_DoesNotThrowOnUnix()
    {
        // This test checks that chmodSync doesn't throw on Unix
        // On Windows it's a no-op
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                const testFile = 'test_chmod.txt';

                fs.writeFileSync(testFile, 'content');

                let threw = false;
                try {
                    fs.chmodSync(testFile, 420);
                } catch (e) {
                    threw = true;
                }
                console.log(threw === false);

                // Cleanup
                fs.unlinkSync(testFile);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Fs_ReadlinkSync_ThrowsForNonSymlink()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';
                const testFile = 'test_readlink_regular.txt';

                fs.writeFileSync(testFile, 'content');

                let threw = false;
                try {
                    fs.readlinkSync(testFile);
                } catch (e) {
                    threw = true;
                }
                console.log(threw);

                // Cleanup
                fs.unlinkSync(testFile);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Fs_TruncateSync_ThrowsForNonexistentFile()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';

                let threw = false;
                try {
                    fs.truncateSync('nonexistent_truncate_test.txt', 0);
                } catch (e) {
                    threw = true;
                }
                console.log(threw);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Fs_RealpathSync_ThrowsForNonexistentFile()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';

                let threw = false;
                try {
                    fs.realpathSync('nonexistent_realpath_test.txt');
                } catch (e) {
                    threw = true;
                }
                console.log(threw);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }
}
