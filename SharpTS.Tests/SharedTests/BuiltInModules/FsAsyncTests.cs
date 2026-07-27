using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for async fs methods (fs/promises module and fs.promises namespace).
/// Tests run sequentially to avoid file conflicts.
/// </summary>
[Collection("TimerTests")]
public class FsAsyncTests
{
    #region fs/promises module - Basic Operations

    [Theory, ModeData]
    public void FsPromises_WriteFile_And_ReadFile_WithEncoding(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { writeFile, readFile, unlink } from 'fs/promises';

                async function test() {
                    await writeFile('test-async-1.txt', 'Hello Async World');
                    const content = await readFile('test-async-1.txt', 'utf8');
                    await unlink('test-async-1.txt');
                    console.log(content);
                }

                test();
            """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("Hello Async World\n", output);
    }

    [Theory, ModeData]
    public void FsPromises_WriteFile_And_ReadFile_AsBuffer(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { writeFile, readFile, unlink } from 'fs/promises';

                async function test() {
                    await writeFile('test-async-2.txt', 'Buffer Test');
                    const buffer = await readFile('test-async-2.txt');
                    await unlink('test-async-2.txt');
                    console.log(buffer.toString('utf8'));
                }

                test();
            """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("Buffer Test\n", output);
    }

    [Theory, ModeData]
    public void FsPromises_AppendFile(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { writeFile, appendFile, readFile, unlink } from 'fs/promises';

                async function test() {
                    await writeFile('test-async-3.txt', 'Hello');
                    await appendFile('test-async-3.txt', ' World');
                    const content = await readFile('test-async-3.txt', 'utf8');
                    await unlink('test-async-3.txt');
                    console.log(content);
                }

                test();
            """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("Hello World\n", output);
    }

    #endregion

    #region fs/promises module - File Stats

    [Theory, ModeData]
    public void FsPromises_Stat_File(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { writeFile, stat, unlink } from 'fs/promises';

                async function test() {
                    await writeFile('test-stat-1.txt', 'Test content');
                    const stats = await stat('test-stat-1.txt');
                    await unlink('test-stat-1.txt');
                    console.log(stats.isFile(), stats.isDirectory(), stats.size);
                }

                test();
            """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true false 12\n", output);
    }

    [Theory, ModeData]
    public void FsPromises_Stat_Directory(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { mkdir, stat, rmdir } from 'fs/promises';

                async function test() {
                    await mkdir('test-stat-dir');
                    const stats = await stat('test-stat-dir');
                    await rmdir('test-stat-dir');
                    console.log(stats.isFile(), stats.isDirectory());
                }

                test();
            """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false true\n", output);
    }

    #endregion

    #region fs/promises module - Directory Operations

    [Theory, ModeData]
    public void FsPromises_Mkdir_And_Rmdir(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { mkdir, rmdir, stat } from 'fs/promises';

                async function test() {
                    await mkdir('test-dir-1');
                    const stats = await stat('test-dir-1');
                    const isDir = stats.isDirectory();
                    await rmdir('test-dir-1');
                    console.log(isDir);
                }

                test();
            """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void FsPromises_Mkdir_Recursive(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { mkdir, rmdir, stat } from 'fs/promises';

                async function test() {
                    await mkdir('test-dir-nested/sub/deep', { recursive: true });
                    const stats = await stat('test-dir-nested/sub/deep');
                    const isDir = stats.isDirectory();
                    await rmdir('test-dir-nested/sub/deep');
                    await rmdir('test-dir-nested/sub');
                    await rmdir('test-dir-nested');
                    console.log(isDir);
                }

                test();
            """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void FsPromises_Readdir(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { mkdir, writeFile, readdir, unlink, rmdir } from 'fs/promises';

                async function test() {
                    await mkdir('test-readdir');
                    await writeFile('test-readdir/a.txt', 'a');
                    await writeFile('test-readdir/b.txt', 'b');
                    const entries = await readdir('test-readdir');
                    await unlink('test-readdir/a.txt');
                    await unlink('test-readdir/b.txt');
                    await rmdir('test-readdir');
                    console.log(entries.sort().join(','));
                }

                test();
            """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("a.txt,b.txt\n", output);
    }

    #endregion

    #region fs/promises module - File Operations

    [Theory, ModeData]
    public void FsPromises_Rename(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { writeFile, rename, readFile, unlink } from 'fs/promises';

                async function test() {
                    await writeFile('test-rename-old.txt', 'content');
                    await rename('test-rename-old.txt', 'test-rename-new.txt');
                    const content = await readFile('test-rename-new.txt', 'utf8');
                    await unlink('test-rename-new.txt');
                    console.log(content);
                }

                test();
            """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("content\n", output);
    }

    [Theory, ModeData]
    public void FsPromises_CopyFile(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { writeFile, copyFile, readFile, unlink } from 'fs/promises';

                async function test() {
                    await writeFile('test-copy-src.txt', 'copy content');
                    await copyFile('test-copy-src.txt', 'test-copy-dest.txt');
                    const content = await readFile('test-copy-dest.txt', 'utf8');
                    await unlink('test-copy-src.txt');
                    await unlink('test-copy-dest.txt');
                    console.log(content);
                }

                test();
            """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("copy content\n", output);
    }

    [Theory, ModeData]
    public void FsPromises_Access(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { writeFile, access, unlink } from 'fs/promises';

                async function test() {
                    await writeFile('test-access.txt', 'content');
                    let hasAccess = true;
                    try {
                        await access('test-access.txt');
                    } catch {
                        hasAccess = false;
                    }
                    await unlink('test-access.txt');
                    console.log(hasAccess);
                }

                test();
            """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void FsPromises_Access_NonExistent_Throws(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { access } from 'fs/promises';

                async function test() {
                    try {
                        await access('non-existent-file-12345.txt');
                        console.log('no error');
                    } catch {
                        console.log('error thrown');
                    }
                }

                test();
            """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("error thrown\n", output);
    }

    #endregion

    #region fs.promises namespace

    [Theory, ModeData]
    public void FsPromisesNamespace_WriteFile_And_ReadFile(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';

                async function test() {
                    await fs.promises.writeFile('test-ns-1.txt', 'Namespace Test');
                    const content = await fs.promises.readFile('test-ns-1.txt', 'utf8');
                    await fs.promises.unlink('test-ns-1.txt');
                    console.log(content);
                }

                test();
            """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("Namespace Test\n", output);
    }

    [Theory, ModeData]
    public void FsPromisesNamespace_Stat(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';

                async function test() {
                    await fs.promises.writeFile('test-ns-stat.txt', 'content');
                    const stats = await fs.promises.stat('test-ns-stat.txt');
                    await fs.promises.unlink('test-ns-stat.txt');
                    console.log(stats.isFile());
                }

                test();
            """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void FsPromisesNamespace_Constants(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';

                console.log(fs.promises.constants !== undefined);
                console.log(fs.promises.constants.F_OK);
                console.log(fs.promises.constants.R_OK);
            """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n0\n4\n", output);
    }

    [Theory, ModeData]
    public void FsPromisesNamespace_AppendFile(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';

                async function test() {
                    await fs.promises.writeFile('test-ns-append.txt', 'Hello');
                    await fs.promises.appendFile('test-ns-append.txt', ' World');
                    const content = await fs.promises.readFile('test-ns-append.txt', 'utf8');
                    await fs.promises.unlink('test-ns-append.txt');
                    console.log(content);
                }

                test();
            """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("Hello World\n", output);
    }

    #endregion

    #region Error Handling

    [Theory, ModeData]
    public void FsPromises_ReadFile_NonExistent_Throws(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { readFile } from 'fs/promises';

                async function test() {
                    try {
                        await readFile('non-existent-file-67890.txt');
                        console.log('no error');
                    } catch (err) {
                        console.log('threw', err.code !== undefined);
                    }
                }

                test();
            """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("threw true\n", output);
    }

    [Theory, ModeData]
    public void FsPromises_Unlink_NonExistent_Throws(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { unlink } from 'fs/promises';

                async function test() {
                    try {
                        await unlink('non-existent-file-for-unlink.txt');
                        console.log('no error');
                    } catch {
                        console.log('error thrown');
                    }
                }

                test();
            """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("error thrown\n", output);
    }

    #endregion

    #region Truncate

    [Theory, ModeData]
    public void FsPromises_Truncate(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { writeFile, truncate, readFile, unlink } from 'fs/promises';

                async function test() {
                    await writeFile('test-truncate.txt', 'Hello World!');
                    await truncate('test-truncate.txt', 5);
                    const content = await readFile('test-truncate.txt', 'utf8');
                    await unlink('test-truncate.txt');
                    console.log(content);
                }

                test();
            """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("Hello\n", output);
    }

    #endregion
}
