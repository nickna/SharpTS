using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Regression coverage for #1440: Promise reactions are FIFO microtasks and
/// never run inline merely because their Task-backed source is already settled.
/// </summary>
public sealed class PromiseJobSchedulingTests
{
    private const string SettledOrderingSource = """
        const order: string[] = ["start"];
        Promise.resolve(1).then((): void => { order.push("then"); });
        Promise.reject("reason").catch((): void => { order.push("catch"); });
        Promise.resolve(1).finally((): void => { order.push("finally"); });
        order.push("after");
        queueMicrotask((): void => {
            order.push("queueMicrotask");
            console.log(order.join(":"));
        });
        """;

    [Theory, ModeData]
    public void SettledThenCatchFinally_RunAfterCurrentJobInFifoOrder(
        ExecutionMode mode)
    {
        Assert.Equal(
            "start:after:then:catch:finally:queueMicrotask\n",
            TestHarness.Run(SettledOrderingSource, mode));
    }

    [Theory, ModeData]
    public void NestedPromiseJobs_AppendToTheSharedMicrotaskFifo(
        ExecutionMode mode)
    {
        const string source = """
            const order: string[] = [];
            Promise.resolve(1).then((): void => {
                order.push("promise-1");
                Promise.resolve(2).then((): void => { order.push("nested"); });
            });
            queueMicrotask((): void => { order.push("microtask"); });
            Promise.resolve(3).then((): void => { order.push("promise-2"); });
            setTimeout((): void => console.log(order.join(":")), 0);
            """;

        Assert.Equal(
            "promise-1:microtask:promise-2:nested\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PendingSource_QueuesReactionAtSettlementBeforeLaterMicrotasks(
        ExecutionMode mode)
    {
        const string source = """
            const order: string[] = [];
            let settle!: (value: number) => void;
            const pending: Promise<number> = new Promise<number>(
                (resolve): void => { settle = resolve; });
            pending.then((): void => { order.push("reaction"); });
            settle(1);
            queueMicrotask((): void => { order.push("microtask"); });
            setTimeout((): void => console.log(order.join(":")), 0);
            """;

        Assert.Equal(
            "reaction:microtask\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void QueuedReaction_AdoptsThenableAndRejectsThrownHandlerExactlyOnce(
        ExecutionMode mode)
    {
        const string source = """
            let calls: number = 0;
            const adopted: Promise<number> = Promise.resolve(1)
                .then((_value: number): any => ({
                    then(resolve: (value: number) => void): void {
                        resolve(41);
                        resolve(99);
                    }
                }))
                .then((value: number): number => {
                    calls += 1;
                    throw new Error(String(value + 1));
                });
            adopted.catch((error: Error): void => {
                console.log(error.message + ":" + calls);
            });
            console.log("sync");
            """;

        Assert.Equal("sync\n42:1\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void StandaloneCompiledOutput_DrainsQueuedPromiseJobs()
    {
        Assert.Equal(
            "start:after:then:catch:finally:queueMicrotask\n",
            TestHarness.RunCompiledStandalone(SettledOrderingSource));
    }
}
