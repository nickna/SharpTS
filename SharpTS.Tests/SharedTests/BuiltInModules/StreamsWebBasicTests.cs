using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the WHATWG Web Streams API (ReadableStream / WritableStream /
/// TransformStream) exposed as globals and via <c>node:stream/web</c>.
/// </summary>
/// <remarks>
/// v1 covers interpreter mode only; compiled-mode IL emission is a separate
/// follow-up phase.
/// </remarks>
public class StreamsWebBasicTests
{
    #region ReadableStream

    [Theory, ModeData]
    public void ReadableStream_ConstructorEnqueueCloseRead(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const rs = new ReadableStream({
                    start(c) {
                        c.enqueue("hello");
                        c.enqueue("world");
                        c.close();
                    }
                });
                const reader = rs.getReader();
                async function run() {
                    const r1 = await reader.read();
                    console.log(r1.value, r1.done);
                    const r2 = await reader.read();
                    console.log(r2.value, r2.done);
                    const r3 = await reader.read();
                    console.log(r3.value, r3.done);
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("hello false\nworld false\nundefined true\n", output);
    }

    [Theory, InterpretedOnlyData]
    public void ReadableStream_From_ImportedConstructor(ExecutionMode mode)
    {
        // #210: static-property dispatch on the imported constructor wrapper
        // (ReadableStream.from) must route through its GetProperty.
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { ReadableStream as RS } from "stream/web";
                const rs = (RS as any).from(["x", "y"]);
                const reader = rs.getReader();
                async function run() {
                    const r1 = await reader.read();
                    console.log(r1.value, r1.done);
                    const r2 = await reader.read();
                    console.log(r2.value, r2.done);
                    const r3 = await reader.read();
                    console.log(r3.value, r3.done);
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("x false\ny false\nundefined true\n", output);
    }

    #endregion

    #region Read result JS semantics (regression: MakeReadResult must behave like a real object)

    [Theory, ModeData]
    public void ReadResult_ObjectKeys(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const rs = new ReadableStream({
                    start(c) { c.enqueue("hello"); c.close(); }
                });
                const reader = rs.getReader();
                async function run() {
                    const r = await reader.read();
                    console.log(Object.keys(r).join(","));
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("value,done\n", output);
    }

    [Theory, ModeData]
    public void ReadResult_JsonStringify(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const rs = new ReadableStream({
                    start(c) { c.enqueue("hello"); c.close(); }
                });
                const reader = rs.getReader();
                async function run() {
                    const r = await reader.read();
                    console.log(JSON.stringify(r));
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("{\"value\":\"hello\",\"done\":false}\n", output);
    }

    [Theory, ModeData]
    public void ReadResult_ForInIteration(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const rs = new ReadableStream({
                    start(c) { c.enqueue(42); c.close(); }
                });
                const reader = rs.getReader();
                async function run() {
                    const r = await reader.read();
                    const keys: string[] = [];
                    for (const k in r) keys.push(k);
                    console.log(keys.sort().join(","));
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("done,value\n", output);
    }

    [Theory, ModeData]
    public void ReadResult_ObjectSpread(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const rs = new ReadableStream({
                    start(c) { c.enqueue("spread-me"); c.close(); }
                });
                const reader = rs.getReader();
                async function run() {
                    const r: any = await reader.read();
                    const copy: any = { ...r };
                    console.log(Object.keys(copy).sort().join(","), copy.value, copy.done);
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("done,value spread-me false\n", output);
    }

    #endregion

    #region WritableStream

    [Theory, ModeData]
    public void ReadableStream_IsLockedAfterGetReader(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const rs = new ReadableStream({ start(c) { c.close(); } });
                console.log(rs.locked);
                rs.getReader();
                console.log(rs.locked);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("false\ntrue\n", output);
    }

    [Theory, ModeData]
    public void ReadableStream_DesiredSizeReflectsBackpressure(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const rs = new ReadableStream({
                    start(c) {
                        console.log(c.desiredSize);
                        c.enqueue(1);
                        console.log(c.desiredSize);
                        c.enqueue(2);
                        console.log(c.desiredSize);
                        c.close();
                    }
                }, { highWaterMark: 2 });
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("2\n1\n0\n", output);
    }

    [Theory, ModeData]
    public void ReadableStream_PullCallbackInvokedOnRead(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                let i = 0;
                const rs = new ReadableStream({
                    pull(c) {
                        c.enqueue(i++);
                        if (i >= 3) c.close();
                    }
                });
                const reader = rs.getReader();
                async function run() {
                    const r1 = await reader.read();
                    const r2 = await reader.read();
                    const r3 = await reader.read();
                    const r4 = await reader.read();
                    console.log(r1.value, r2.value, r3.value, r4.done);
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("0 1 2 true\n", output);
    }

    [Theory, ModeData]
    public void ReadableStream_ErrorPropagatesToReader(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const rs = new ReadableStream({
                    start(c) {
                        c.error("boom");
                    }
                });
                const reader = rs.getReader();
                async function run() {
                    try {
                        await reader.read();
                        console.log("no throw");
                    } catch (e) {
                        console.log("caught:", e);
                    }
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("caught:", output);
        Assert.Contains("boom", output);
    }

    [Theory, ModeData]
    public void WritableStream_WriteCloseSequence(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const chunks: string[] = [];
                const ws = new WritableStream({
                    write(chunk) {
                        chunks.push(chunk);
                    }
                });
                const writer = ws.getWriter();
                async function run() {
                    await writer.write("a");
                    await writer.write("b");
                    await writer.close();
                    console.log(chunks.join(","));
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("a,b\n", output);
    }

    #endregion

    #region TransformStream

    [Theory, ModeData]
    public void TransformStream_BasicTransformViaPipeThrough(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const source = new ReadableStream({
                    start(c) {
                        c.enqueue(1);
                        c.enqueue(2);
                        c.enqueue(3);
                        c.close();
                    }
                });
                const doubler = new TransformStream({
                    transform(chunk, c) {
                        c.enqueue(chunk * 2);
                    }
                });
                const result = source.pipeThrough(doubler);
                const reader = result.getReader();
                async function run() {
                    const r1 = await reader.read();
                    const r2 = await reader.read();
                    const r3 = await reader.read();
                    const r4 = await reader.read();
                    console.log(r1.value, r2.value, r3.value, r4.done);
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("2 4 6 true\n", output);
    }

    #endregion

    #region pipeTo

    [Theory, ModeData]
    public void ReadableStream_PipeToWritable(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                const chunks: number[] = [];
                const source = new ReadableStream({
                    start(c) {
                        c.enqueue(10);
                        c.enqueue(20);
                        c.enqueue(30);
                        c.close();
                    }
                });
                const dest = new WritableStream({
                    write(chunk) { chunks.push(chunk); }
                });
                async function run() {
                    await source.pipeTo(dest);
                    console.log(chunks.join("+"));
                }
                run();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("10+20+30\n", output);
    }

    #endregion

    #region Module import

    [Theory, ModeData]
    public void StreamWeb_ImportNamedExports(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { ReadableStream, WritableStream, TransformStream } from 'stream/web';
                // Prove the imported bindings are usable as constructors via
                // TryEmitModuleQualifiedConstructor → emitted ctor.
                // (The imported binding value itself is a string placeholder
                // emitted by StreamWebModuleEmitter, matching the
                // EventsModuleEmitter pattern; making `typeof X === 'function'`
                // work requires $TSFunction wrapping and is a separate quality-
                // of-life enhancement.)
                const rs = new ReadableStream({ start(c) { c.close(); } });
                const ws = new WritableStream({ write() {} });
                const ts = new TransformStream();
                console.log(rs !== null && rs !== undefined);
                console.log(ws !== null && ws !== undefined);
                console.log(ts !== null && ts !== undefined);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\ntrue\n", output);
    }

    [Theory, ModeData]
    public void StreamWeb_CountQueuingStrategy(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { CountQueuingStrategy } from 'stream/web';
                const strat = new CountQueuingStrategy({ highWaterMark: 5 });
                console.log(strat.highWaterMark);
                console.log(strat.size(null));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("5\n1\n", output);
    }

    #endregion
}
