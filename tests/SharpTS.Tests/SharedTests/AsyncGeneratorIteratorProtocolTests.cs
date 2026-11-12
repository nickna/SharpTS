using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class AsyncGeneratorIteratorProtocolTests
{
    public const string ValueGetterFailureSource = """
            const iterable = {
                [Symbol.asyncIterator]() {
                    return {
                        next() {
                            return Promise.resolve({
                                done: false,
                                get value() { throw new Error("step"); }
                            });
                        },
                        return() { console.log("unexpected close"); return { done: true }; }
                    };
                }
            };
            async function* run() {
                try { for await (const value of iterable) yield value; }
                catch (error) { yield error.message; }
                yield "after catch";
            }
            async function main() {
                const generator = run();
                console.log((await generator.next()).value);
                console.log((await generator.next()).value);
                console.log((await generator.next()).done);
            }
            main();
            """;

    [Theory, ModeData]
    public void ForAwaitValueGetterFailure_ReachesGeneratorCatchWithoutClosing(ExecutionMode mode)
    {
        Assert.Equal("step\nafter catch\ntrue\n", TestHarness.RunUnchecked(ValueGetterFailureSource, mode));
    }

    public const string AsyncIteratorOverrideSource = """
        async function* source() { console.log("unexpected source"); yield 1; }
        const generator = source();
        Object.defineProperty(generator, Symbol.asyncIterator, {
            get() {
                console.log("lookup", this === generator);
                return function () {
                    console.log("call", this === generator);
                    let done = false;
                    return {
                        next() {
                            if (done) return { done };
                            done = true;
                            return { done: false, value: 7 };
                        }
                    };
                };
            }
        });
        async function main() {
            for await (const value of generator) console.log(value);
        }
        main();
        """;

    [Theory, ModeData]
    public void ForAwaitAsyncGenerator_UsesOwnAsyncIteratorGetter(ExecutionMode mode)
    {
        Assert.Equal("lookup true\ncall true\n7\n", TestHarness.RunUnchecked(AsyncIteratorOverrideSource, mode));
    }

    public const string SyncDelegationSource = """
        const iterable = {
            [Symbol.iterator]() {
                console.log("acquire", this === iterable);
                const iterator = {
                    count: 0,
                    next(sent) {
                        if (this !== iterator) throw new Error("wrong receiver");
                        if (++this.count === 1) return { done: false, value: 7 };
                        return { done: true, value: sent };
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
    public void YieldStar_UsesCustomSyncIteratorAndForwardsSentValue(ExecutionMode mode)
    {
        Assert.Equal("acquire true\n7 false\n9 true\n",
            TestHarness.RunUnchecked(SyncDelegationSource, mode));
    }

    [Theory, ModeData]
    public void ForAwaitSyncIterator_CallsNextWithoutArguments(ExecutionMode mode)
    {
        const string source = """
            const iterable = {
                [Symbol.iterator]() {
                    let done = false;
                    return {
                        next() {
                            if (done) return { done };
                            done = true;
                            return { value: arguments.length, done: false };
                        }
                    };
                }
            };
            async function main() {
                for await (const count of iterable) console.log(count);
            }
            main();
            """;
        Assert.Equal("0\n", TestHarness.RunUnchecked(source, mode));
    }
}
