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
}

