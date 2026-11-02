using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class NativeAwaitJobOrderingTests
{
    public static IEnumerable<object[]> Cases =>
    [
        ["ready-await-fifo", """
            async function run() { console.log('start'); await Promise.resolve(1); console.log('ready'); }
            void run(); queueMicrotask(() => console.log('queued')); console.log('sync');
            """, "start\nsync\nready\nqueued\n"],
        ["pending-promise-fifo", """
            let resolve: any;
            const pending = new Promise<number>(r => { resolve = r; });
            async function run() { console.log('start'); console.log(await pending); }
            run();
            queueMicrotask(() => { console.log('settle'); resolve(7); queueMicrotask(() => console.log('tail')); });
            console.log('sync');
            """, "start\nsync\nsettle\n7\ntail\n"],
        ["pending-producer-does-not-inline-await", """
            function scenario() {
                let resolve: any; const order: string[] = [];
                const pending = new Promise<number>(r => { resolve = r; });
                async function run() { const value = await pending; order.push('await' + value); console.log(order.join(',')); }
                void run(); order.push('before'); resolve(7); order.push('after');
            }
            scenario();
            """, "before,after,await7\n"],
        ["read-fulfillment-no-reentry", """
            let controller: any;
            const stream = new ReadableStream({ start(c: any) { controller = c; } });
            const reader = stream.getReader(); const order: string[] = ['sync'];
            async function run() { const result = await reader.read(); order.push(result.value); console.log(order.join(',')); }
            run();
            queueMicrotask(() => { order.push('producer-before'); controller.enqueue('chunk'); order.push('producer-after'); });
            """, "sync,producer-before,producer-after,chunk\n"],
        ["read-close-pending-and-future", """
            let controller: any;
            const reader = new ReadableStream({ start(c: any) { controller = c; } }).getReader();
            async function run() {
                const first = await reader.read(); console.log(first.done, first.value === undefined);
                const second = await reader.read(); console.log(second.done);
            }
            run(); queueMicrotask(() => controller.close());
            """, "true true\ntrue\n"],
        ["read-rejection-fifo", """
            let controller: any; const reason = {}; const order: string[] = [];
            const reader = new ReadableStream({ start(c: any) { controller = c; } }).getReader();
            async function run() { try { await reader.read(); } catch (error: any) { order.push(error === reason ? 'reader' : 'wrong'); } }
            run();
            queueMicrotask(() => { order.push('producer'); controller.error(reason); queueMicrotask(() => { order.push('tail'); console.log(order.join(',')); }); });
            """, "producer,reader,tail\n"],
        ["two-pending-read-arrivals", """
            let controller: any;
            const reader = new ReadableStream({ start(c: any) { controller = c; } }).getReader();
            async function run() { const first = reader.read(); const second = reader.read(); console.log((await first).value, (await second).value); }
            run(); queueMicrotask(() => { controller.enqueue(1); controller.enqueue(2); controller.close(); });
            """, "1 2\n"],
        ["never-settling-read-idle-exit", """
            const reader = new ReadableStream({ pull() { return new Promise<void>(() => {}); } }).getReader();
            async function run() { await reader.read(); console.log('unreachable'); }
            run(); console.log('sync');
            """, "sync\n"],
        ["never-settling-promise-idle-exit", """
            async function run() { await new Promise<void>(() => {}); console.log('unreachable'); }
            run(); console.log('sync');
            """, "sync\n"],
    ];

    public static IEnumerable<object[]> Modes => Cases.SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { mode, row[0], row[1], row[2] }));

    [Theory, MemberData(nameof(Modes))]
    public void NativeAwaitPreservesJobsAndIdleExit(ExecutionMode mode, string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
            Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
