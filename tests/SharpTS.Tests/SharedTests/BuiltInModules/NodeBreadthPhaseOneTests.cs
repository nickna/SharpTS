using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

/// <summary>
/// Cross-mode coverage for the breadth entry points added by issue #1282,
/// Phase 1. Deeper module-specific tests remain beside their parent modules.
/// </summary>
public class NodeBreadthPhaseOneTests
{
    [Theory, ModeData]
    public void Module_CatalogAndBuiltinLookup(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { builtinModules, isBuiltin, syncBuiltinESMExports } from 'node:module';
                console.log(builtinModules.length);
                console.log(isBuiltin('fs'), isBuiltin('node:fs'), isBuiltin('diagnostics_channel'));
                console.log(isBuiltin('not-a-node-module'));
                console.log(new Set(builtinModules).size === builtinModules.length);
                syncBuiltinESMExports();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("44\ntrue true true\nfalse\ntrue\n", output);
    }

    [Theory, ModeData]
    public void Module_CreateRequire_LoadsRelativeCommonJs(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { createRequire } from 'module';
                const require = createRequire(import.meta.url);
                const dependency = require('./dependency.cjs');
                console.log(dependency.answer);
                """,
            ["dependency.cjs"] = """
                module.exports = { answer: 42 };
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("42\n", output);
    }

    [Theory, ModeData]
    public void AliasModules_ExposeParentNamespaces(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import strictAssert, { equal, deepEqual } from 'node:assert/strict';
                import posix from 'path/posix';
                import win32 from 'node:path/win32';
                import types, { isMap } from 'util/types';
                import { log } from 'node:console';

                strictAssert(true);
                equal(1, 1);
                deepEqual({ value: 1 }, { value: 1 });
                console.log(posix.join('a', 'b'));
                console.log(win32.join('a', 'b'));
                console.log(isMap(new Map()), types.isSet(new Set()));
                log('console-module');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("a/b\na\\b\ntrue true\nconsole-module\n", output);
    }

    [Theory, ModeData]
    public void AssertStrict_CommonJsExportIsCallable(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.cjs"] = """
                const assert = require('assert/strict');
                assert(true);
                assert.equal(1, 1);
                console.log(typeof assert.strictEqual === 'function');
                """
        };

        var output = TestHarness.RunModules(files, "main.cjs", mode);
        Assert.Equal("true\n", output);
    }

    [Theory, ModeData]
    public void DiagnosticsChannel_PublishesSynchronouslyAndUsesSingletons(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { channel, hasSubscribers, subscribe, tracingChannel, unsubscribe } from 'diagnostics_channel';
                const seen: string[] = [];
                const listener = (message: any, name: any) => seen.push(name + ':' + message.value);
                subscribe('phase-one', listener);
                console.log(channel('phase-one') === channel('phase-one'));
                console.log(hasSubscribers('phase-one'));
                channel('phase-one').publish({ value: 42 });
                console.log(seen.join(','));
                console.log(unsubscribe('phase-one', listener), hasSubscribers('phase-one'));

                const trace = tracingChannel('phase-one-trace');
                const traceEvents: string[] = [];
                trace.subscribe({
                    start: () => traceEvents.push('start'),
                    end: () => traceEvents.push('end')
                });
                const traced = trace.traceSync((value: number) => value * 2, {}, undefined, 3);
                console.log(traced, traceEvents.join(','));
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\nphase-one:42\ntrue false\n6 start,end\n", output);
    }

    [Theory, ModeData]
    public void ReadlinePromises_ExposesPromiseQuestion(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { createInterface } from 'readline/promises';
                const rl = createInterface();
                console.log(typeof rl.question === 'function');
                console.log(typeof rl.close === 'function');
                rl.close();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("true\ntrue\n", output);
    }

    [Theory, ModeData]
    public void StreamConsumers_ConsumeNodeReadable(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { text, json, bytes } from 'node:stream/consumers';
                import { Readable } from 'stream';

                const first = new Readable();
                first.push('hello');
                first.push(null);
                const second = new Readable();
                second.push('{"ok":true}');
                second.push(null);
                const third = new Readable();
                third.push('A');
                third.push(null);

                async function consumeText(stream: any, consumer: any) {
                    console.log(await consumer(stream));
                }
                async function consumeJson(stream: any, consumer: any) {
                    console.log((await consumer(stream)).ok);
                }
                async function consumeBytes(stream: any, consumer: any) {
                    const value = await consumer(stream);
                    console.log(value.length, value.toString());
                }
                consumeText(first, text);
                consumeJson(second, json);
                consumeBytes(third, bytes);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("hello\ntrue\n1 A\n", output);
    }

    [Theory, ModeData]
    public void StreamConsumers_ConsumeQueuedWebReadableStream(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { text, bytes } from 'stream/consumers';

                const first = new ReadableStream({
                    start(controller) { controller.enqueue('web'); controller.close(); }
                });
                const second = new ReadableStream({
                    start(controller) { controller.enqueue('B'); controller.close(); }
                });

                async function consumeText(stream: any, consumer: any) {
                    console.log(await consumer(stream));
                }
                async function consumeBytes(stream: any, consumer: any) {
                    const value = await consumer(stream);
                    console.log(value.length, value.toString());
                }
                consumeText(first, text);
                consumeBytes(second, bytes);
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("web\n1 B\n", output);
    }

    [Theory, ModeData]
    public void StreamConsumers_ExposeBinaryConsumerShapes(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { arrayBuffer, blob, buffer } from 'stream/consumers';
                import { Readable } from 'stream';

                function readable(value: string) {
                    const stream = new Readable();
                    stream.push(value);
                    stream.push(null);
                    return stream;
                }

                async function consumeBuffer() {
                    const value = await buffer(readable('buffer'));
                    console.log(value.toString());
                }
                async function consumeBlob() {
                    const value = await blob(readable('blob'));
                    console.log(value.size, value.type);
                }
                async function consumeArrayBuffer() {
                    const value = await arrayBuffer(readable('A'));
                    const view = new Uint8Array(value);
                    console.log(view.length, view[0]);
                }
                consumeBuffer();
                consumeBlob();
                consumeArrayBuffer();
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("buffer\n4 \n1 65\n", output);
    }

    [Theory, ModeData]
    public void V8_SerializerRoundTripsReferencesAndCollections(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["main.ts"] = """
                import { serialize, deserialize, getHeapStatistics, getHeapSpaceStatistics, setFlagsFromString } from 'v8';
                import { Buffer } from 'buffer';

                const source: any = { value: 7, map: new Map([['key', 9]]), bytes: Buffer.from('ok') };
                source.self = source;
                const restored: any = deserialize(serialize(source));
                console.log(restored.value, restored.self === restored);
                console.log(restored.map.get('key'), restored.bytes.toString());
                console.log(typeof getHeapStatistics().used_heap_size === 'number');
                console.log(Array.isArray(getHeapSpaceStatistics()));
                setFlagsFromString('--expose-gc');
                """
        };

        var output = TestHarness.RunModules(files, "main.ts", mode);
        Assert.Equal("7 true\n9 ok\ntrue\ntrue\n", output);
    }
}
