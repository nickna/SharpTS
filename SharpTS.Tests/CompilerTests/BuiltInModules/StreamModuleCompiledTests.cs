using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests.BuiltInModules;

/// <summary>
/// Tests for the stream module in compiled mode.
/// </summary>
/// <remarks>
/// TODO: These tests require RuntimeEmitter to emit $Readable, $Writable, $Duplex,
/// $Transform, and $PassThrough types into compiled assemblies.
/// Currently skipped until compiler emission is implemented.
/// </remarks>
[Collection("StreamCompiledTests")]
public class StreamModuleCompiledTests
{
    // ============ IMPORT PATTERNS ============

    [Fact]
    public void Stream_NamedImport_Readable()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Readable } from 'stream';
                const readable = new Readable();
                console.log(typeof readable);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("object\n", output);
    }

    [Fact]
    public void Stream_NamedImport_Writable()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Writable } from 'stream';
                const writable = new Writable();
                console.log(typeof writable);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("object\n", output);
    }

    // ============ READABLE STREAM ============

    [Fact]
    public void Readable_Push_Read()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Readable } from 'stream';
                const readable = new Readable();

                readable.push('hello');
                readable.push(' world');
                readable.push(null);

                const data = readable.read();
                console.log(data);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("hello world\n", output);
    }

    [Fact]
    public void Readable_Read_ReturnsNull_WhenEmpty()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Readable } from 'stream';
                const readable = new Readable();

                const data = readable.read();
                console.log(data === null);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("true\n", output);
    }

    [Fact]
    public void Readable_Push_Null_SignalsEnd()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Readable } from 'stream';
                const readable = new Readable();

                console.log(readable.readableEnded);
                readable.push(null);
                console.log(readable.readableEnded);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("false\ntrue\n", output);
    }

    [Fact]
    public void Readable_Properties()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Readable } from 'stream';
                const readable = new Readable();

                console.log(readable.readable);
                console.log(readable.readableEnded);
                console.log(readable.readableLength);

                readable.push('data');
                console.log(readable.readableLength);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("true\nfalse\n0\n1\n", output);
    }

    [Fact]
    public void Readable_EndEvent()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Readable } from 'stream';
                const readable = new Readable();

                readable.on('end', () => {
                    console.log('end event fired');
                });

                readable.push('data');
                readable.push(null);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("end event fired\n", output);
    }

    // ============ WRITABLE STREAM ============

    [Fact]
    public void Writable_Write_WithCallback()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Writable } from 'stream';

                const chunks: string[] = [];
                const writable = new Writable({
                    write(chunk: string, encoding: string, callback: () => void) {
                        chunks.push(chunk);
                        callback();
                    }
                });

                writable.write('hello');
                writable.write(' world');
                writable.end();

                console.log(chunks.join(''));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("hello world\n", output);
    }

    [Fact]
    public void Writable_Properties()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Writable } from 'stream';
                const writable = new Writable();

                console.log(writable.writable);
                console.log(writable.writableEnded);
                console.log(writable.writableFinished);

                writable.end();

                console.log(writable.writable);
                console.log(writable.writableEnded);
                console.log(writable.writableFinished);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("true\nfalse\nfalse\nfalse\ntrue\ntrue\n", output);
    }

    [Fact]
    public void Writable_FinishEvent()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Writable } from 'stream';
                const writable = new Writable();

                writable.on('finish', () => {
                    console.log('finish event fired');
                });

                writable.end();
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("finish event fired\n", output);
    }

    // ============ PIPE ============

    [Fact]
    public void Readable_Pipe_Writable()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Readable, Writable } from 'stream';

                const chunks: string[] = [];
                const readable = new Readable();
                const writable = new Writable({
                    write(chunk: string, encoding: string, callback: () => void) {
                        chunks.push(chunk);
                        callback();
                    }
                });

                readable.push('hello');
                readable.push(' world');
                readable.push(null);

                readable.pipe(writable);

                console.log(chunks.join(''));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("hello world\n", output);
    }

    // ============ DUPLEX STREAM ============

    [Fact]
    public void Duplex_ReadAndWrite()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Duplex } from 'stream';

                const chunks: string[] = [];
                const duplex = new Duplex({
                    write(chunk: string, encoding: string, callback: () => void) {
                        chunks.push('write: ' + chunk);
                        callback();
                    }
                });

                // Write side
                duplex.write('hello');

                // Read side
                duplex.push('world');
                const data = duplex.read();

                console.log(chunks[0]);
                console.log(data);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("write: hello\nworld\n", output);
    }

    // ============ TRANSFORM STREAM ============

    [Fact]
    public void Transform_BasicTransformation()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Transform } from 'stream';

                const transform = new Transform({
                    transform(chunk: string, encoding: string, callback: any) {
                        callback(null, chunk.toUpperCase());
                    }
                });

                transform.write('hello');
                transform.end();

                const result = transform.read();
                console.log(result);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("HELLO\n", output);
    }

    // ============ PASSTHROUGH STREAM ============

    [Fact]
    public void PassThrough_PassesDataUnchanged()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { PassThrough } from 'stream';

                const passThrough = new PassThrough();

                passThrough.write('hello');
                passThrough.write(' world');
                passThrough.end();

                const result = passThrough.read();
                console.log(result);
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("hello world\n", output);
    }

    [Fact]
    public void PassThrough_InPipeline()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Readable, PassThrough, Writable } from 'stream';

                const chunks: string[] = [];

                const readable = new Readable();
                const passThrough = new PassThrough();
                const writable = new Writable({
                    write(chunk: string, encoding: string, callback: () => void) {
                        chunks.push(chunk);
                        callback();
                    }
                });

                readable.push('hello');
                readable.push(' world');
                readable.push(null);

                readable.pipe(passThrough).pipe(writable);

                console.log(chunks.join(''));
                """
        };

        var output = TestHarness.RunModulesCompiled(files, "./main.ts");
        Assert.Equal("hello world\n", output);
    }
}
