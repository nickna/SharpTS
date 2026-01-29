using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests.BuiltInModules;

/// <summary>
/// Tests for the stream module in interpreter mode.
/// </summary>
public class StreamModuleTests
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("object\n", output);
    }

    [Fact]
    public void Stream_NamedImport_All()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Readable, Writable, Duplex, Transform, PassThrough } from 'stream';
                console.log(typeof Readable);
                console.log(typeof Writable);
                console.log(typeof Duplex);
                console.log(typeof Transform);
                console.log(typeof PassThrough);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("function\nfunction\nfunction\nfunction\nfunction\n", output);
    }

    [Fact]
    public void Stream_NamespaceImport()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as stream from 'stream';
                const readable = new stream.Readable();
                console.log(typeof readable);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\nfalse\n0\n1\n", output);
    }

    [Fact]
    public void Readable_Destroy()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Readable } from 'stream';
                const readable = new Readable();

                console.log(readable.destroyed);
                readable.destroy();
                console.log(readable.destroyed);
                console.log(readable.readable);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("false\ntrue\nfalse\n", output);
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("finish event fired\n", output);
    }

    [Fact]
    public void Writable_Cork_Uncork()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Writable } from 'stream';

                const chunks: string[] = [];
                const writable = new Writable({
                    write(chunk: string, encoding: string, callback: () => void) {
                        chunks.push('wrote: ' + chunk);
                        callback();
                    }
                });

                writable.cork();
                writable.write('a');
                writable.write('b');
                console.log('before uncork: ' + chunks.length);
                writable.uncork();
                console.log('after uncork: ' + chunks.length);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("before uncork: 0\nafter uncork: 2\n", output);
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("hello world\n", output);
    }

    [Fact]
    public void Readable_Pipe_ReturnsDestination()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Readable, Writable } from 'stream';

                const readable = new Readable();
                const writable = new Writable();

                const result = readable.pipe(writable);
                console.log(result === writable);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\n", output);
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("write: hello\nworld\n", output);
    }

    [Fact]
    public void Duplex_Properties()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Duplex } from 'stream';
                const duplex = new Duplex();

                console.log(duplex.readable);
                console.log(duplex.writable);
                console.log(duplex.readableEnded);
                console.log(duplex.writableEnded);
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("true\ntrue\nfalse\nfalse\n", output);
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("HELLO\n", output);
    }

    [Fact]
    public void Transform_Pipe_Chain()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Readable, Transform, Writable } from 'stream';

                const chunks: string[] = [];

                const readable = new Readable();
                const transform = new Transform({
                    transform(chunk: string, encoding: string, callback: any) {
                        // Add prefix to each chunk
                        callback(null, '[' + chunk + ']');
                    }
                });
                const writable = new Writable({
                    write(chunk: string, encoding: string, callback: () => void) {
                        chunks.push(chunk);
                        callback();
                    }
                });

                readable.push('hello');
                readable.push('world');
                readable.push(null);

                readable.pipe(transform).pipe(writable);

                console.log(chunks.join(' '));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("[hello] [world]\n", output);
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
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

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("hello world\n", output);
    }

    // ============ EVENTS ============

    [Fact]
    public void Stream_CloseEvent()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Readable } from 'stream';
                const readable = new Readable();

                readable.on('close', () => {
                    console.log('close event fired');
                });

                readable.destroy();
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("close event fired\n", output);
    }

    [Fact]
    public void Writable_End_WithChunk()
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
                writable.end(' world');

                console.log(chunks.join(''));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("hello world\n", output);
    }

    [Fact]
    public void Writable_Final_Callback()
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Writable } from 'stream';

                const events: string[] = [];
                const writable = new Writable({
                    write(chunk: string, encoding: string, callback: () => void) {
                        events.push('write: ' + chunk);
                        callback();
                    },
                    final(callback: () => void) {
                        events.push('final called');
                        callback();
                    }
                });

                writable.write('data');
                writable.end();

                console.log(events.join(', '));
                """
        };

        var output = TestHarness.RunModulesInterpreted(files, "./main.ts");
        Assert.Equal("write: data, final called\n", output);
    }
}
