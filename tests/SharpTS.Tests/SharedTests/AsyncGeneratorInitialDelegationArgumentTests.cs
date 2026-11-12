using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class AsyncGeneratorInitialDelegationArgumentTests
{
    public const string Source = """
        function source(symbol) {
            return {
                [symbol]() {
                    let count = 0;
                    return {
                        next(sent) {
                            console.log(arguments.length, sent);
                            if (count++ === 0) return { done: false, value: 7 };
                            return { done: true, value: sent };
                        }
                    };
                }
            };
        }
        async function* immediate(iterable) { return yield* iterable; }
        async function* later(iterable) {
            const sent = yield "pause";
            console.log("outer", sent);
            return yield* iterable;
        }
        async function show(result) { console.log(result.value, result.done); }
        async function main() {
            for (const symbol of [Symbol.iterator, Symbol.asyncIterator]) {
                const first = immediate(source(symbol));
                await show(await first.next(42));
                await show(await first.next(9));
                const second = later(source(symbol));
                await show(await second.next(42));
                await show(await second.next(55));
                await show(await second.next(9));
            }
        }
        main();
        """;

    public const string OneProtocolExpected =
        "1 undefined\n7 false\n1 9\n9 true\npause false\nouter 55\n1 undefined\n7 false\n1 9\n9 true\n";

    [Theory, ModeData]
    public void YieldStar_StartsWithUndefinedAndThenForwardsResumeValues(ExecutionMode mode)
        => Assert.Equal(OneProtocolExpected + OneProtocolExpected, TestHarness.RunUnchecked(Source, mode));
}
