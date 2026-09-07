using System.Collections.Concurrent;
using SharpTS.Runtime.Types;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class WorkerDeliverySchedulingTests
{
    [Fact]
    public void EnqueueDuringDrain_IsRescheduledAfterOwnershipReset()
    {
        var callbacks = new Queue<Action>();
        var messages = new ConcurrentQueue<int>();
        var delivered = new List<int>();
        WorkerMessageHandler handler = null!;
        handler = new WorkerMessageHandler(callbacks.Enqueue, () => !messages.IsEmpty, () =>
        {
            Assert.True(messages.TryDequeue(out int value));
            delivered.Add(value);
            if (value == 1)
            {
                // Complete the enqueue while the first drain still owns scheduled=1.
                var sender = new Thread(() => { messages.Enqueue(2); handler.Schedule(); });
                sender.Start();
                Assert.True(sender.Join(TimeSpan.FromSeconds(5)));
            }
        });
        messages.Enqueue(1); // queued before bootstrap
        handler.Schedule();
        Assert.Empty(callbacks);
        handler.Start();
        Assert.Single(callbacks);
        callbacks.Dequeue()();
        Assert.Single(callbacks);
        callbacks.Dequeue()();
        Assert.Equal([1, 2], delivered);
        Assert.Empty(callbacks);
    }

    [Fact]
    public void BurstsCoalesce_StopSuppressesQueuedDrain_AndExceptionsReleaseOwnership()
    {
        var callbacks = new Queue<Action>();
        int pending = 1;
        int drains = 0;
        var handler = new WorkerMessageHandler(callbacks.Enqueue, () => pending != 0, () =>
        {
            drains++;
            if (drains == 1) throw new InvalidOperationException("callback");
            pending = 0;
        });
        handler.Start();
        for (int i = 0; i < 100; i++) handler.Schedule();
        Assert.Single(callbacks);
        Assert.Throws<InvalidOperationException>(callbacks.Dequeue());
        Assert.Single(callbacks);
        callbacks.Dequeue()();
        Assert.Equal(2, drains);
        pending = 1;
        handler.Schedule();
        handler.Stop();
        callbacks.Dequeue()();
        Assert.Equal(2, drains);
        Assert.Empty(callbacks);
    }

    [Theory, ModeData]
    public void EarlyBurstAndReentrantEnqueue_RetainFifo(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["worker.ts"] = """
                import { parentPort } from "worker_threads";
                let next: number = 0;
                parentPort!.on("message", (value: any) => {
                    if (value !== next) throw new Error("message order");
                    next++;
                    parentPort!.postMessage(value);
                    if (next === 513) parentPort!.close();
                });
                """,
            ["main.ts"] = """
                import { Worker } from "worker_threads";
                const w = new Worker(__dirname + "/worker.ts");
                let next: number = 0;
                w.on("message", (value: any) => {
                    if (value !== next) throw new Error("reply order");
                    next++;
                    if (next === 512) w.postMessage(512);
                    if (next === 513) console.log("done");
                });
                for (let i: number = 0; i < 512; i++) w.postMessage(i);
                """
        };
        Assert.Equal("done\n", TestHarness.RunModules(files, "main.ts", mode));
    }
}
