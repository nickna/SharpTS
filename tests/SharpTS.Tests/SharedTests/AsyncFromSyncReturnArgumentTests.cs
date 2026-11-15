using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class AsyncFromSyncReturnArgumentTests
{
    public const string Source = """
        function source(tag) {
            let count = 0;
            const iterator = {
                [Symbol.iterator]() { return this; },
                next() { return { value: ++count, done: count > 1 }; },
                return(sent) {
                    console.log(tag, arguments.length, this === iterator, sent === undefined);
                    return { value: sent, done: true };
                }
            };
            return iterator;
        }
        async function returning() {
            for await (const value of source('return')) return 'returned';
        }
        async function* loop() {
            for await (const value of source('generator')) yield value;
        }
        async function* delegate() { yield* source('delegate'); }
        async function main() {
            for await (const value of source('break')) break;
            console.log(await returning());
            const marker = {};
            outer: for (let i = 0; i < 1; i++) {
                for await (const value of source('continue')) continue outer;
            }
            for await (const value of source('exhausted')) {}
            const looping = loop();
            await looping.next();
            console.log((await looping.return('stopped')).value);
            let generator = delegate();
            await generator.next();
            console.log((await generator.return()).done);
            generator = delegate();
            await generator.next();
            console.log((await generator.return(marker)).value === marker);
        }
        main();
        """;

    public const string Expected =
        "break 0 true true\nreturn 0 true true\nreturned\n" +
        "continue 0 true true\ngenerator 0 true true\nstopped\n" +
        "delegate 1 true true\ntrue\ndelegate 1 true false\ntrue\n";

    [Theory, ModeData]
    public void ImplicitCloseOmitsTheArgumentAndDelegationForwardsIt(ExecutionMode mode)
        => Assert.Equal(Expected, TestHarness.RunUnchecked(Source, mode));
}
