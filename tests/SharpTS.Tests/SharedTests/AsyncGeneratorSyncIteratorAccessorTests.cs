using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class AsyncGeneratorSyncIteratorAccessorTests
{
    public const string NextAccessorSource = """
        const iterable = {
            [Symbol.iterator]() {
                console.log("acquire", this === iterable);
                const iterator = {
                    count: 0,
                    get next() {
                        console.log("lookup next", this === iterator);
                        return function (sent) {
                            if (this !== iterator) throw new Error("wrong receiver");
                            if (++this.count === 1) return { done: false, value: 7 };
                            return { done: true, value: sent };
                        };
                    }
                };
                return iterator;
            }
        };
        async function* run() { return yield* iterable; }
        async function main() {
            const generator = run();
            const first = await generator.next();
            console.log(first.value, first.done);
            const last = await generator.next(9);
            console.log(last.value, last.done);
        }
        main();
        """;

    [Theory, ModeData]
    public void YieldStar_CapturesSyncNextAccessorOnceWithIteratorReceiver(ExecutionMode mode)
    {
        Assert.Equal("acquire true\nlookup next true\n7 false\n9 true\n",
            TestHarness.RunUnchecked(NextAccessorSource, mode));
    }

    [Theory, ModeData]
    public void YieldStar_SyncNextGetterFailureReachesOuterCatch(ExecutionMode mode)
    {
        const string source = """
            const token = {};
            const iterable = {
                [Symbol.iterator]() {
                    return {
                        get next() { throw token; },
                        return() { console.log("unexpected close"); return {}; }
                    };
                }
            };
            async function* run() {
                try { yield* iterable; }
                catch (error) { yield error === token; }
            }
            run().next().then(result => console.log(result.value, result.done));
            """;
        Assert.Equal("true false\n", TestHarness.RunUnchecked(source, mode));
    }
}
