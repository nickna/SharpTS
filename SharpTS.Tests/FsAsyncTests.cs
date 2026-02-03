using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests;

/// <summary>
/// Tests for async fs methods (fs/promises module and fs.promises namespace).
/// Tests run sequentially to avoid file conflicts.
/// </summary>
[Collection("TimerTests")]
public class FsAsyncTests
{
    #region fs/promises module - Basic Operations

    [Fact]
    public void FsPromises_WriteFile_And_ReadFile_WithEncoding()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("Hello Async World\n", output);
    }

    [Fact]
    public void FsPromises_WriteFile_And_ReadFile_AsBuffer()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("Buffer Test\n", output);
    }

    [Fact]
    public void FsPromises_AppendFile()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("Hello World\n", output);
    }

    #endregion

    #region fs/promises module - File Stats

    [Fact]
    public void FsPromises_Stat_File()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true false 12\n", output);
    }

    [Fact]
    public void FsPromises_Stat_Directory()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("false true\n", output);
    }

    #endregion

    #region fs/promises module - Directory Operations

    [Fact]
    public void FsPromises_Mkdir_And_Rmdir()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void FsPromises_Mkdir_Recursive()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void FsPromises_Readdir()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { mkdir, writeFile, readdir, unlink, rmdir } from 'fs/promises';

                async function test() {
                    await mkdir('test-readdir');
                    await writeFile('test-readdir/a.txt', 'a');
                    await writeFile('test-readdir/b.txt', 'b');
                    const files = await readdir('test-readdir');
                    await unlink('test-readdir/a.txt');
                    await unlink('test-readdir/b.txt');
                    await rmdir('test-readdir');
                    console.log(files.sort().join(','));
                }

                test();
            """
        };

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("a.txt,b.txt\n", output);
    }

    #endregion

    #region fs/promises module - File Operations

    [Fact]
    public void FsPromises_Rename()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("content\n", output);
    }

    [Fact]
    public void FsPromises_CopyFile()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("copy content\n", output);
    }

    [Fact]
    public void FsPromises_Access()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void FsPromises_Access_NonExistent_Throws()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("error thrown\n", output);
    }

    #endregion

    #region fs.promises namespace

    [Fact]
    public void FsPromisesNamespace_WriteFile_And_ReadFile()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("Namespace Test\n", output);
    }

    [Fact]
    public void FsPromisesNamespace_Stat()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void FsPromisesNamespace_Constants()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("true\n0\n4\n", output);
    }

    #endregion

    #region Error Handling

    [Fact]
    public void FsPromises_ReadFile_NonExistent_Throws()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("threw true\n", output);
    }

    [Fact]
    public void FsPromises_Unlink_NonExistent_Throws()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("error thrown\n", output);
    }

    #endregion

    #region Truncate and Other Operations

    [Fact]
    public void FsPromises_Truncate()
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

        var output = TestHarness.RunModulesInterpreted(files, "main.ts");
        Assert.Equal("Hello\n", output);
    }

    #endregion

    #region Compiler Parity Tests

    [Fact]
    public void Compiled_FsPromises_WriteFile_And_ReadFile()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { writeFile, readFile, unlink } from 'fs/promises';

                async function test() {
                    await writeFile('test-compiled-1.txt', 'Hello Compiled');
                    const content = await readFile('test-compiled-1.txt', 'utf8');
                    await unlink('test-compiled-1.txt');
                    console.log(content);
                }

                test();
            """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("Hello Compiled\n", output);
    }

    [Fact]
    public void Compiled_FsPromisesNamespace()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as fs from 'fs';

                async function test() {
                    await fs.promises.writeFile('test-compiled-ns.txt', 'Compiled NS');
                    const content = await fs.promises.readFile('test-compiled-ns.txt', 'utf8');
                    await fs.promises.unlink('test-compiled-ns.txt');
                    console.log(content);
                }

                test();
            """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("Compiled NS\n", output);
    }

    [Fact]
    public void Compiled_FsPromises_ReadFile_AsBuffer()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { writeFile, readFile, unlink } from 'fs/promises';

                async function test() {
                    await writeFile('test-compiled-buf.txt', 'Buffer Test');
                    const buffer = await readFile('test-compiled-buf.txt');
                    await unlink('test-compiled-buf.txt');
                    console.log(buffer.toString('utf8'));
                }

                test();
            """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("Buffer Test\n", output);
    }

    [Fact]
    public void Compiled_FsPromises_AppendFile()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { writeFile, appendFile, readFile, unlink } from 'fs/promises';

                async function test() {
                    await writeFile('test-compiled-append.txt', 'Hello');
                    await appendFile('test-compiled-append.txt', ' World');
                    const content = await readFile('test-compiled-append.txt', 'utf8');
                    await unlink('test-compiled-append.txt');
                    console.log(content);
                }

                test();
            """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("Hello World\n", output);
    }

    [Fact]
    public void Compiled_FsPromises_Stat_File()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { writeFile, stat, unlink } from 'fs/promises';

                async function test() {
                    await writeFile('test-compiled-stat.txt', 'Test content');
                    const stats = await stat('test-compiled-stat.txt');
                    await unlink('test-compiled-stat.txt');
                    console.log(stats.isFile(), stats.isDirectory(), stats.size);
                }

                test();
            """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true false 12\n", output);
    }

    [Fact]
    public void Compiled_FsPromises_Stat_Directory()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { mkdir, stat, rmdir } from 'fs/promises';

                async function test() {
                    await mkdir('test-compiled-stat-dir');
                    const stats = await stat('test-compiled-stat-dir');
                    await rmdir('test-compiled-stat-dir');
                    console.log(stats.isFile(), stats.isDirectory());
                }

                test();
            """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("false true\n", output);
    }

    [Fact]
    public void Compiled_FsPromises_Mkdir_And_Rmdir()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { mkdir, rmdir, stat } from 'fs/promises';

                async function test() {
                    await mkdir('test-compiled-dir');
                    const stats = await stat('test-compiled-dir');
                    const isDir = stats.isDirectory();
                    await rmdir('test-compiled-dir');
                    console.log(isDir);
                }

                test();
            """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_FsPromises_Mkdir_Recursive()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { mkdir, rmdir, stat } from 'fs/promises';

                async function test() {
                    await mkdir('test-compiled-nested/sub/deep', { recursive: true });
                    const stats = await stat('test-compiled-nested/sub/deep');
                    const isDir = stats.isDirectory();
                    await rmdir('test-compiled-nested/sub/deep');
                    await rmdir('test-compiled-nested/sub');
                    await rmdir('test-compiled-nested');
                    console.log(isDir);
                }

                test();
            """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_FsPromises_Readdir()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { mkdir, writeFile, readdir, unlink, rmdir } from 'fs/promises';

                async function test() {
                    await mkdir('test-compiled-readdir');
                    await writeFile('test-compiled-readdir/a.txt', 'a');
                    await writeFile('test-compiled-readdir/b.txt', 'b');
                    const entries = await readdir('test-compiled-readdir');
                    await unlink('test-compiled-readdir/a.txt');
                    await unlink('test-compiled-readdir/b.txt');
                    await rmdir('test-compiled-readdir');
                    console.log(entries.length, entries.includes('a.txt'), entries.includes('b.txt'));
                }

                test();
            """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("2 true true\n", output);
    }

    [Fact]
    public void Compiled_FsPromises_Rename()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { writeFile, rename, readFile, unlink } from 'fs/promises';
                import * as fs from 'fs';

                async function test() {
                    await writeFile('test-compiled-rename-old.txt', 'Rename Test');
                    await rename('test-compiled-rename-old.txt', 'test-compiled-rename-new.txt');
                    const content = await readFile('test-compiled-rename-new.txt', 'utf8');
                    await unlink('test-compiled-rename-new.txt');
                    console.log(content);
                }

                test();
            """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("Rename Test\n", output);
    }

    [Fact]
    public void Compiled_FsPromises_CopyFile()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { writeFile, copyFile, readFile, unlink } from 'fs/promises';

                async function test() {
                    await writeFile('test-compiled-copy-src.txt', 'Copy Test');
                    await copyFile('test-compiled-copy-src.txt', 'test-compiled-copy-dst.txt');
                    const srcContent = await readFile('test-compiled-copy-src.txt', 'utf8');
                    const dstContent = await readFile('test-compiled-copy-dst.txt', 'utf8');
                    await unlink('test-compiled-copy-src.txt');
                    await unlink('test-compiled-copy-dst.txt');
                    console.log(srcContent === dstContent);
                }

                test();
            """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_FsPromises_Access()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { writeFile, access, unlink } from 'fs/promises';

                async function test() {
                    await writeFile('test-compiled-access.txt', 'Access Test');
                    let hasAccess = false;
                    try {
                        await access('test-compiled-access.txt');
                        hasAccess = true;
                    } catch (e) {
                        hasAccess = false;
                    }
                    await unlink('test-compiled-access.txt');
                    console.log(hasAccess);
                }

                test();
            """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Compiled_FsPromises_Truncate()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { writeFile, truncate, readFile, unlink } from 'fs/promises';

                async function test() {
                    await writeFile('test-compiled-truncate.txt', 'Hello World!');
                    await truncate('test-compiled-truncate.txt', 5);
                    const content = await readFile('test-compiled-truncate.txt', 'utf8');
                    await unlink('test-compiled-truncate.txt');
                    console.log(content);
                }

                test();
            """
        };

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("Hello\n", output);
    }

    [Fact]
    public void Compiled_FsPromisesNamespace_AppendFile()
    {
        // Test appendFile via fs.promises namespace
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

        var output = TestHarness.RunModulesCompiled(files, "main.ts");
        Assert.Equal("Hello World\n", output);
    }

    #endregion
}
