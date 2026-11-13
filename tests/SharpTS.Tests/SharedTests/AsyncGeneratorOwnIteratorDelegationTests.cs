using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class AsyncGeneratorOwnIteratorDelegationTests
{
    [Theory, ModeData]
    public void YieldStar_UsesAsyncGeneratorOwnAsyncIterator(ExecutionMode mode)
    {
        const string source = """
            async function* source() { console.log("unexpected source"); yield 1; }
            const generator = source();
            generator[Symbol.asyncIterator] = function () {
                console.log("acquire", this === generator);
                let first = true;
                return {
                    next(sent) {
                        if (!first) return { done: true, value: sent };
                        first = false;
                        return { done: false, value: 7 };
                    }
                };
            };
            async function* outer() { return yield* generator; }
            async function main() {
                const delegated = outer();
                const first = await delegated.next();
                console.log(first.value, first.done);
                const last = await delegated.next(9);
                console.log(last.value, last.done);
            }
            main();
            """;
        Assert.Equal("acquire true\n7 false\n9 true\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void YieldStar_UsesAsyncGeneratorOwnAsyncIteratorGetterOnce(ExecutionMode mode)
    {
        var source = AsyncGeneratorIteratorProtocolTests.AsyncIteratorOverrideSource.Replace(
            "for await (const value of generator) console.log(value);",
            "async function* outer() { yield* generator; } console.log((await outer().next()).value);");
        Assert.Equal("lookup true\ncall true\n7\n", TestHarness.RunUnchecked(source, mode));
    }
}
