using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for object mode streams (Readable, Writable, Transform with objectMode: true).
/// </summary>
public class StreamObjectModeTests
{
    [Theory, ModeData]
    public void Readable_ObjectMode_PushAndReadObjects(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Readable } from 'stream';
                const r = new Readable({ objectMode: true });
                r.push({ name: 'Alice' });
                r.push({ name: 'Bob' });
                r.push(null);
                const obj1 = r.read();
                const obj2 = r.read();
                console.log(obj1.name === 'Alice');
                console.log(obj2.name === 'Bob');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Readable_ObjectMode_ReadReturnsOneObjectAtATime(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Readable } from 'stream';
                const r = new Readable({ objectMode: true });
                r.push(42);
                r.push('hello');
                r.push(null);
                const v1 = r.read();
                const v2 = r.read();
                console.log(v1 === 42);
                console.log(v2 === 'hello');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Readable_ObjectMode_ReadableObjectModeProperty(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Readable } from 'stream';
                const r = new Readable({ objectMode: true });
                console.log(r.readableObjectMode === true);
                const r2 = new Readable();
                console.log(r2.readableObjectMode === false);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Writable_ObjectMode_AcceptsObjects(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Writable } from 'stream';
                const chunks: any[] = [];
                const w = new Writable({
                    objectMode: true,
                    write(chunk: any, encoding: string, callback: Function) {
                        chunks.push(chunk);
                        callback();
                    }
                });
                w.write({ x: 1 });
                w.write({ x: 2 });
                w.end();
                console.log(chunks.length === 2);
                console.log(chunks[0].x === 1);
                console.log(chunks[1].x === 2);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Writable_ObjectMode_WritableObjectModeProperty(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Writable } from 'stream';
                const w = new Writable({ objectMode: true });
                console.log(w.writableObjectMode === true);
                const w2 = new Writable();
                console.log(w2.writableObjectMode === false);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Transform_ObjectMode_TransformsObjects(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Transform } from 'stream';
                const t = new Transform({
                    objectMode: true,
                    transform(chunk: any, encoding: string, callback: Function) {
                        callback(null, { ...chunk, doubled: chunk.value * 2 });
                    }
                });
                t.write({ value: 5 });
                t.end();
                const result = t.read();
                console.log(result.value === 5);
                console.log(result.doubled === 10);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Readable_ObjectMode_PushArraysAsValues(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Readable } from 'stream';
                const r = new Readable({ objectMode: true });
                r.push([1, 2, 3]);
                r.push(null);
                const arr = r.read();
                console.log(Array.isArray(arr));
                console.log(arr.length === 3);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Readable_ObjectMode_PushNumbers(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Readable } from 'stream';
                const r = new Readable({ objectMode: true });
                r.push(100);
                r.push(200);
                r.push(null);
                const a = r.read();
                const b = r.read();
                console.log(a === 100);
                console.log(b === 200);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Duplex_ObjectMode_BothSides(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Duplex } from 'stream';
                const received: any[] = [];
                const d = new Duplex({
                    objectMode: true,
                    write(chunk: any, encoding: string, callback: Function) {
                        received.push(chunk);
                        callback();
                    }
                });
                d.push({ from: 'readable' });
                d.push(null);
                d.write({ from: 'writable' });
                d.end();
                const readObj = d.read();
                console.log(readObj.from === 'readable');
                console.log(received[0].from === 'writable');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }
}
