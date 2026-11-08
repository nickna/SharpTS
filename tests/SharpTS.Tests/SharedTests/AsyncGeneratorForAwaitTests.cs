using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncGeneratorForAwaitTests
{
    [Theory, ModeData]
    public void UsesCustomAsyncIteratorAndAwaitsEarlyClose(ExecutionMode mode)
    {
        const string source = """
            const iterable = {
                [Symbol.asyncIterator]() {
                    const iterator = {
                        next() {
                            console.log("next", this === iterator);
                            return Promise.resolve({ value: 1, done: false });
                        },
                        async return() {
                            await new Promise(resolve => setTimeout(resolve, 1));
                            console.log("close", this === iterator);
                            return { done: true };
                        }
                    };
                    console.log("iterator", this === iterable);
                    return iterator;
                }
            };
            async function* inspect() {
                for await (const value of iterable) {
                    yield value;
                    break;
                }
                yield "after";
            }
            async function run() {
                const generator = inspect();
                console.log((await generator.next()).value);
                console.log((await generator.next()).value);
            }
            run();
            """;

        Assert.Equal("iterator true\nnext true\n1\nclose true\nafter\n", TestHarness.RunUnchecked(source, mode));
    }
}
