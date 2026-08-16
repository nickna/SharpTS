using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for fs.watch(), fs.watchFile(), fs.unwatchFile(), and flowing stream mode.
/// </summary>
public class FsWatchTests
{
    private static string EscapePath(string path) => path.Replace("\\", "\\\\");

    #region fs.watch()

    [Theory, ModeData]
    public void Watch_DetectsFileChange(ExecutionMode mode)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "initial");
            var p = EscapePath(tempFile);

            var files = new Dictionary<string, string>
            {
                ["main.ts"] =
                    "import * as fs from 'fs';\n" +
                    "let detected = false;\n" +
                    "const watcher = fs.watch('" + p + "', (eventType: string, filename: string) => {\n" +
                    "    if (!detected) {\n" +
                    "        detected = true;\n" +
                    "        console.log('event:' + eventType);\n" +
                    "        watcher.close();\n" +
                    "    }\n" +
                    "});\n" +
                    "setTimeout(() => {\n" +
                    "    fs.writeFileSync('" + p + "', 'modified');\n" +
                    "}, 100);\n"
            };

            var output = TestHarness.RunModules(files, "main.ts", mode);
            Assert.Equal("event:change\n", output);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Theory, ModeData]
    public void Watch_CloseStopsWatching(ExecutionMode mode)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "sharpts_watch_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var p = EscapePath(tempDir);

            var files = new Dictionary<string, string>
            {
                ["main.ts"] =
                    "import * as fs from 'fs';\n" +
                    "const watcher = fs.watch('" + p + "');\n" +
                    "watcher.close();\n" +
                    "console.log('closed');\n"
            };

            var output = TestHarness.RunModules(files, "main.ts", mode);
            Assert.Equal("closed\n", output);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Theory, ModeData]
    public void Watch_OnMethodForEvents(ExecutionMode mode)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "initial");
            var p = EscapePath(tempFile);

            var files = new Dictionary<string, string>
            {
                ["main.ts"] =
                    "import * as fs from 'fs';\n" +
                    "let detected = false;\n" +
                    "const watcher = fs.watch('" + p + "');\n" +
                    "watcher.on('change', (eventType: string, filename: string) => {\n" +
                    "    if (!detected) {\n" +
                    "        detected = true;\n" +
                    "        console.log('on:' + eventType);\n" +
                    "        watcher.close();\n" +
                    "    }\n" +
                    "});\n" +
                    "setTimeout(() => {\n" +
                    "    fs.writeFileSync('" + p + "', 'changed');\n" +
                    "}, 100);\n"
            };

            var output = TestHarness.RunModules(files, "main.ts", mode);
            Assert.Equal("on:change\n", output);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion

    #region fs.watchFile()

    [Theory, ModeData]
    public void WatchFile_DetectsStatChange(ExecutionMode mode)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "hello");
            var p = EscapePath(tempFile);

            var files = new Dictionary<string, string>
            {
                ["main.ts"] =
                    "import * as fs from 'fs';\n" +
                    "let detected = false;\n" +
                    "fs.watchFile('" + p + "', { interval: 100 }, (curr: any, prev: any) => {\n" +
                    "    if (!detected) {\n" +
                    "        detected = true;\n" +
                    "        console.log('changed:' + (curr.size !== prev.size));\n" +
                    "        fs.unwatchFile('" + p + "');\n" +
                    "    }\n" +
                    "});\n" +
                    "setTimeout(() => {\n" +
                    "    fs.writeFileSync('" + p + "', 'hello world extended');\n" +
                    "}, 200);\n"
            };

            var output = TestHarness.RunModules(files, "main.ts", mode);
            Assert.Equal("changed:true\n", output);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Theory, ModeData]
    public void UnwatchFile_StopsWatching(ExecutionMode mode)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "data");
            var p = EscapePath(tempFile);

            var files = new Dictionary<string, string>
            {
                ["main.ts"] =
                    "import * as fs from 'fs';\n" +
                    "const watcher = fs.watchFile('" + p + "', { interval: 100 }, (curr: any, prev: any) => {\n" +
                    "    console.log('should not see this');\n" +
                    "});\n" +
                    "fs.unwatchFile('" + p + "');\n" +
                    "console.log('unwatched');\n"
            };

            var output = TestHarness.RunModules(files, "main.ts", mode);
            Assert.Equal("unwatched\n", output);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion

    #region Flowing Streams

    [Theory, ModeData]
    public void ReadStream_FlowingMode_AutoDrainsOnDataListener(ExecutionMode mode)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "flowing data test");
            var p = EscapePath(tempFile);

            var files = new Dictionary<string, string>
            {
                ["main.ts"] =
                    "import * as fs from 'fs';\n" +
                    "const stream = fs.createReadStream('" + p + "', { encoding: 'utf8' });\n" +
                    "let result = '';\n" +
                    "stream.on('data', (chunk: string) => { result += chunk; });\n" +
                    "stream.on('end', () => { console.log(result); });\n"
            };

            var output = TestHarness.RunModules(files, "main.ts", mode);
            Assert.Equal("flowing data test\n", output);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Theory, ModeData]
    public void Readable_PauseResume_FlowControl(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] =
                "import { Readable } from 'stream';\n" +
                "const r = new Readable();\n" +
                "r.push('a');\n" +
                "r.push('b');\n" +
                "r.push('c');\n" +
                "r.push(null);\n" +
                "const chunks: string[] = [];\n" +
                "r.on('data', (chunk: string) => { chunks.push(chunk); });\n" +
                "r.on('end', () => { console.log('chunks:' + chunks.join(',')); });\n"
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("chunks:a,b,c", output);
    }

    [Theory, ModeData]
    public void Readable_ResumeAfterPause(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] =
                "import { Readable } from 'stream';\n" +
                "const r = new Readable();\n" +
                "const chunks: string[] = [];\n" +
                "r.on('data', (chunk: string) => {\n" +
                "    chunks.push(chunk);\n" +
                "});\n" +
                "r.push('first');\n" +
                "r.pause();\n" +
                "r.push('second');\n" +
                "r.push('third');\n" +
                "r.resume();\n" +
                "r.push(null);\n" +
                "r.on('end', () => {\n" +
                "    console.log(chunks.join(','));\n" +
                "});\n"
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("first,second,third\n", output);
    }

    #endregion
}
