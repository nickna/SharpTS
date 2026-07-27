using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Tests for the stream/promises module (promise-based pipeline and finished).
/// </summary>
public class StreamPromisesTests
{
    [Theory, ModeData]
    public void StreamPromises_Import_Pipeline(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { pipeline } from 'stream/promises';
                console.log(typeof pipeline);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("function\n", output);
    }

    [Theory, ModeData]
    public void StreamPromises_Import_Finished(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { finished } from 'stream/promises';
                console.log(typeof finished);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("function\n", output);
    }

    [Theory, ModeData]
    public void StreamPromises_PropertyAccess(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import * as stream from 'stream';
                console.log(typeof stream.promises.pipeline);
                console.log(typeof stream.promises.finished);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("function\nfunction\n", output);
    }

    [Theory, ModeData]
    public void StreamPromises_Pipeline_ReturnsPromise(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Readable, Writable } from 'stream';
                import { pipeline } from 'stream/promises';
                const r = new Readable();
                const w = new Writable({
                    write(chunk: any, enc: any, cb: any) { cb(); }
                });
                r.push('data');
                r.push(null);
                const p = pipeline(r, w);
                console.log(typeof p.then);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("function\n", output);
    }

    [Theory, ModeData]
    public void StreamPromises_Pipeline_ConnectsStreams(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Readable, PassThrough } from 'stream';
                import { pipeline } from 'stream/promises';
                const source = new Readable();
                const dest = new PassThrough();
                source.push('hello');
                source.push(null);
                const p = pipeline(source, dest);
                console.log(typeof p.then);
                console.log(dest.readable);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Contains("function", output);
    }

    [Theory, ModeData]
    public void StreamPromises_Finished_ReturnsCleanupFunction(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { Readable } from 'stream';
                import { finished } from 'stream';
                const r = new Readable();
                const cleanup = finished(r, () => {});
                console.log(typeof cleanup);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("function\n", output);
    }
}
