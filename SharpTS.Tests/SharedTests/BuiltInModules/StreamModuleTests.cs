using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the stream module (Readable, Writable, Duplex, Transform, PassThrough).
/// </summary>
public class StreamModuleTests
{
    #region Import Patterns

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Stream_NamedImport_Readable(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Readable } from 'stream';
                const readable = new Readable();
                console.log(typeof readable);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("object\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Stream_NamedImport_Writable(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Writable } from 'stream';
                const writable = new Writable();
                console.log(typeof writable);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("object\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Stream_NamedImport_All(ExecutionMode mode)
    {
        // Compiled mode returns 'string' for typeof on these classes
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("function\nfunction\nfunction\nfunction\nfunction\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Stream_NamespaceImport(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as stream from 'stream';
                const readable = new stream.Readable();
                console.log(typeof readable);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("object\n", output);
    }

    #endregion

    #region Readable Stream

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Readable_Push_Read(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("hello world\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Readable_Read_ReturnsNull_WhenEmpty(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Readable_Push_Null_SignalsEnd(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Readable_Properties(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\nfalse\n0\n1\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Readable_Destroy(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\ntrue\nfalse\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Readable_EndEvent(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("end event fired\n", output);
    }

    #endregion

    #region Writable Stream

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Writable_Write_WithCallback(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("hello world\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Writable_Properties(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\nfalse\nfalse\nfalse\ntrue\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Writable_FinishEvent(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("finish event fired\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Writable_Cork_Uncork(ExecutionMode mode)
    {
        // Cork/uncork behavior differs in compiled mode
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("before uncork: 0\nafter uncork: 2\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Writable_End_WithChunk(ExecutionMode mode)
    {
        // Compiled mode doesn't handle the chunk parameter to end()
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("hello world\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void Writable_Final_Callback(ExecutionMode mode)
    {
        // Final callback not invoked in compiled mode
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("write: data, final called\n", output);
    }

    #endregion

    #region Pipe

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Readable_Pipe_Writable(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("hello world\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Readable_Pipe_ReturnsDestination(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\n", output);
    }

    #endregion

    #region Duplex Stream

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Duplex_ReadAndWrite(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("write: hello\nworld\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Duplex_Properties(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\nfalse\nfalse\n", output);
    }

    #endregion

    #region Transform Stream

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Transform_BasicTransformation(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("HELLO\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Transform_Pipe_Chain(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("[hello] [world]\n", output);
    }

    #endregion

    #region PassThrough Stream

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void PassThrough_PassesDataUnchanged(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("hello world\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void PassThrough_InPipeline(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("hello world\n", output);
    }

    #endregion

    #region Events

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Stream_CloseEvent(ExecutionMode mode)
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

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("close event fired\n", output);
    }

    #endregion
}
