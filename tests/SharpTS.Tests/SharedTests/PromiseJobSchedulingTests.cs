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

    [Theory, ModeData]
    public void StablePrimitiveChain_QueuesEachLinkAsItsOwnFifoJob(
        ExecutionMode mode)
    {
        const string source = """
            const order: string[] = [];
            async function run(): Promise<void> {
                let chain: Promise<number> = Promise.resolve(0);
                chain = chain.then((value: number): number => {
                    order.push("first");
                    queueMicrotask((): void => { order.push("inside"); });
                    return value + 1;
                });
                queueMicrotask((): void => { order.push("between"); });
                chain = chain.then((value: number): number => {
                    order.push("second");
                    return value + 1;
                });
                await chain;
                order.push("after-await");
                console.log(order.join(":"));
            }
            run();
            """;

        Assert.Equal(
            "first:between:inside:second:after-await\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StablePrimitiveChain_PropagatesRejectionThroughQueuedLinks(
        ExecutionMode mode)
    {
        const string source = """
            const order: string[] = [];
            async function run(): Promise<void> {
                let chain: Promise<number> = Promise.resolve(0);
                chain = chain.then((_value: number): number => {
                    order.push("throw");
                    queueMicrotask((): void => { order.push("inside"); });
                    throw new Error("boom");
                });
                queueMicrotask((): void => { order.push("between"); });
                chain = chain.then((value: number): number => {
                    order.push("skipped-1");
                    return value;
                });
                chain = chain.then((value: number): number => {
                    order.push("skipped-2");
                    return value;
                });
                try {
                    await chain;
                } catch (_error) {
                    order.push("caught");
                }
                console.log(order.join(":"));
            }
            run();
            """;

        Assert.Equal(
            "throw:between:inside:caught\n",
            TestHarness.Run(source, mode));
    }

    [Fact]
    public void StandaloneCompiledOutput_DrainsQueuedPromiseJobs()
    {
        Assert.Equal(
            "start:after:then:catch:finally:queueMicrotask\n",
            TestHarness.RunCompiledStandalone(SettledOrderingSource));
    }
}
