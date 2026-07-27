using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for fs.createReadStream() and fs.createWriteStream().
/// </summary>
public class FsStreamTests
{
    private static string EscapePath(string path) => path.Replace("\\", "\\\\");

    #region createReadStream

    [Theory, ModeData]
    public void CreateReadStream_ReadsFile(ExecutionMode mode)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "hello world");
            var p = EscapePath(tempFile);

            var files = new Dictionary<string, string>
            {
                ["main.ts"] = "import * as fs from 'fs';\n" +
                    "const stream = fs.createReadStream('" + p + "', { encoding: 'utf8' });\n" +
                    "const chunks: string[] = [];\n" +
                    "stream.on('data', (chunk: string) => { chunks.push(chunk); });\n" +
                    "stream.on('end', () => { console.log(chunks.join('')); });\n"
            };

            var output = TestHarness.RunModules(files, "main.ts", mode);
            Assert.Equal("hello world\n", output);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Theory, ModeData]
    public void CreateReadStream_EndEvent(ExecutionMode mode)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "test data");
            var p = EscapePath(tempFile);

            var files = new Dictionary<string, string>
            {
                // 'end' fires on the next tick (Node-faithful, #980) — log from the
                // handler rather than synchronously after attaching it.
                ["main.ts"] = "import * as fs from 'fs';\n" +
                    "const stream = fs.createReadStream('" + p + "', { encoding: 'utf8' });\n" +
                    "stream.on('data', () => {});\n" +
                    "stream.on('end', () => { console.log('true'); });\n"
            };

            var output = TestHarness.RunModules(files, "main.ts", mode);
            Assert.Equal("true\n", output);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Theory, ModeData]
    public void CreateReadStream_Path_Property(ExecutionMode mode)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "test");
            var p = EscapePath(tempFile);

            var files = new Dictionary<string, string>
            {
                ["main.ts"] = "import * as fs from 'fs';\n" +
                    "const stream = fs.createReadStream('" + p + "');\n" +
                    "console.log(stream.path === '" + p + "');\n"
            };

            var output = TestHarness.RunModules(files, "main.ts", mode);
            Assert.Equal("true\n", output);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion

    #region createWriteStream

    [Theory, ModeData]
    public void CreateWriteStream_WritesFile(ExecutionMode mode)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var p = EscapePath(tempFile);

            var files = new Dictionary<string, string>
            {
                ["main.ts"] = "import * as fs from 'fs';\n" +
                    "const stream = fs.createWriteStream('" + p + "');\n" +
                    "stream.write('hello');\n" +
                    "stream.write(' world');\n" +
                    "stream.end();\n" +
                    "console.log('done');\n"
            };

            var output = TestHarness.RunModules(files, "main.ts", mode);
            Assert.Equal("done\n", output);

            var content = File.ReadAllText(tempFile);
            Assert.Equal("hello world", content);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Theory, ModeData]
    public void CreateWriteStream_End_WritesAndCloses(ExecutionMode mode)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var p = EscapePath(tempFile);

            var files = new Dictionary<string, string>
            {
                ["main.ts"] = "import * as fs from 'fs';\n" +
                    "const stream = fs.createWriteStream('" + p + "');\n" +
                    "stream.write('first ');\n" +
                    "stream.end('last');\n" +
                    "console.log('done');\n"
            };

            var output = TestHarness.RunModules(files, "main.ts", mode);
            Assert.Equal("done\n", output);

            var content = File.ReadAllText(tempFile);
            Assert.Equal("first last", content);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Theory, ModeData]
    public void CreateWriteStream_Path_Property(ExecutionMode mode)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var p = EscapePath(tempFile);

            var files = new Dictionary<string, string>
            {
                ["main.ts"] = "import * as fs from 'fs';\n" +
                    "const stream = fs.createWriteStream('" + p + "');\n" +
                    "console.log(stream.path === '" + p + "');\n" +
                    "stream.end();\n"
            };

            var output = TestHarness.RunModules(files, "main.ts", mode);
            Assert.Equal("true\n", output);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Theory, ModeData]
    public void CreateReadStream_PipeToWriteStream(ExecutionMode mode)
    {
        var srcFile = Path.GetTempFileName();
        var dstFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(srcFile, "piped content");
            var srcPath = EscapePath(srcFile);
            var dstPath = EscapePath(dstFile);

            var files = new Dictionary<string, string>
            {
                ["main.ts"] = "import * as fs from 'fs';\n" +
                    "const readStream = fs.createReadStream('" + srcPath + "');\n" +
                    "const writeStream = fs.createWriteStream('" + dstPath + "');\n" +
                    "readStream.pipe(writeStream);\n" +
                    "console.log('done');\n"
            };

            var output = TestHarness.RunModules(files, "main.ts", mode);
            Assert.Equal("done\n", output);

            var content = File.ReadAllText(dstFile);
            Assert.Equal("piped content", content);
        }
        finally
        {
            File.Delete(srcFile);
            File.Delete(dstFile);
        }
    }

    #endregion

    #region option/event parity (#980)

    [Theory, ModeData]
    public void ReadStream_EventsBytesReadChunking(ExecutionMode mode)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "HELLO STREAM WORLD");
            var p = EscapePath(tempFile);
            var files = new Dictionary<string, string>
            {
                ["main.ts"] = "import * as fs from 'fs';\n" +
                    "const ev: string[] = [];\n" +
                    "const rs: any = fs.createReadStream('" + p + "', { encoding: 'utf8', highWaterMark: 4 });\n" +
                    "let acc = ''; let n = 0;\n" +
                    "rs.on('open', () => ev.push('open'));\n" +
                    "rs.on('ready', () => ev.push('ready'));\n" +
                    "rs.on('data', (c: any) => { acc += c; n++; });\n" +
                    "rs.on('end', () => ev.push('end'));\n" +
                    "rs.on('close', () => { ev.push('close'); console.log(ev.join('>') + '|' + acc + '|chunks=' + n + '|bytesRead=' + rs.bytesRead); });\n"
            };
            // highWaterMark 4 over 18 bytes => 5 chunks, identical in both modes.
            Assert.Equal("open>ready>end>close|HELLO STREAM WORLD|chunks=5|bytesRead=18\n",
                TestHarness.RunModules(files, "main.ts", mode));
        }
        finally { File.Delete(tempFile); }
    }

    [Theory, ModeData]
    public void ReadStream_EmitCloseFalse_And_StartEnd(ExecutionMode mode)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "HELLO STREAM WORLD");
            var p = EscapePath(tempFile);
            var files = new Dictionary<string, string>
            {
                ["main.ts"] = "import * as fs from 'fs';\n" +
                    "async function main() {\n" +
                    "  await new Promise((res: any) => {\n" +
                    "    const rs: any = fs.createReadStream('" + p + "', { encoding: 'utf8', emitClose: false });\n" +
                    "    let closed = false;\n" +
                    "    rs.on('close', () => { closed = true; });\n" +
                    "    rs.on('data', () => {});\n" +
                    "    rs.on('end', () => { console.log('emitClose:' + closed); res(0); });\n" +
                    "  });\n" +
                    "  await new Promise((res: any) => {\n" +
                    "    const rs: any = fs.createReadStream('" + p + "', { encoding: 'utf8', start: 6, end: 10 });\n" +
                    "    let acc = '';\n" +
                    "    rs.on('data', (c: any) => { acc += c; });\n" +
                    "    rs.on('end', () => { console.log('slice:' + acc); res(0); });\n" +
                    "  });\n" +
                    "}\n" +
                    "main();\n"
            };
            Assert.Equal("emitClose:false\nslice:STREA\n", TestHarness.RunModules(files, "main.ts", mode));
        }
        finally { File.Delete(tempFile); }
    }

    [Theory, ModeData]
    public void WriteStream_EventsBytesWrittenContent(ExecutionMode mode)
    {
        var dstFile = Path.GetTempFileName();
        try
        {
            var p = EscapePath(dstFile);
            var files = new Dictionary<string, string>
            {
                ["main.ts"] = "import * as fs from 'fs';\n" +
                    "const ev: string[] = [];\n" +
                    "const ws: any = fs.createWriteStream('" + p + "');\n" +
                    "ws.on('open', () => ev.push('open'));\n" +
                    "ws.on('ready', () => ev.push('ready'));\n" +
                    "ws.on('finish', () => ev.push('finish'));\n" +
                    "ws.on('close', () => { ev.push('close'); console.log(ev.join('>') + '|bw=' + ws.bytesWritten + '|c=' + fs.readFileSync('" + p + "', 'utf8')); });\n" +
                    "ws.write('AB'); ws.write('CD'); ws.end();\n"
            };
            Assert.Equal("open>ready>finish>close|bw=4|c=ABCD\n", TestHarness.RunModules(files, "main.ts", mode));
        }
        finally { File.Delete(dstFile); }
    }

    [Theory, ModeData]
    public void Pipe_ReadToWrite_MovesContent(ExecutionMode mode)
    {
        var srcFile = Path.GetTempFileName();
        var dstFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(srcFile, "PIPED STREAM CONTENT");
            var src = EscapePath(srcFile);
            var dst = EscapePath(dstFile);
            var files = new Dictionary<string, string>
            {
                ["main.ts"] = "import * as fs from 'fs';\n" +
                    "const rs: any = fs.createReadStream('" + src + "');\n" +
                    "const ws: any = fs.createWriteStream('" + dst + "');\n" +
                    "ws.on('close', () => { console.log(fs.readFileSync('" + dst + "', 'utf8')); });\n" +
                    "rs.pipe(ws);\n"
            };
            Assert.Equal("PIPED STREAM CONTENT\n", TestHarness.RunModules(files, "main.ts", mode));
        }
        finally { File.Delete(srcFile); File.Delete(dstFile); }
    }

    #endregion
}
