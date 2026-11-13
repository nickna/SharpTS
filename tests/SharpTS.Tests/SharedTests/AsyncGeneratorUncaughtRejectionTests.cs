using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class AsyncGeneratorUncaughtRejectionTests
{
    [Theory, ModeData]
    public void EscapingDelegatedRejectionClosesGenerator(ExecutionMode mode)
    {
        const string source = """
            const marker = {};
            function source(kind) {
                let first = true;
                const iterator = {
                    next() {
                        if (first) { first = false; return { value: 1, done: false }; }
                        if (kind === 'pending') return {
                            value: new Promise((resolve, reject) => setTimeout(() => reject(marker), 1)), done: false
                        };
                        return { value: Promise.reject(marker), done: false };
                    },
                    throw() { throw marker; }
                };
                if (kind === 'missing') delete iterator.throw;
                return { [Symbol.iterator]() { return iterator; } };
            }
            async function* run(iterable) { yield* iterable; }
            async function check(kind) {
                const generator = run(source(kind));
                console.log((await generator.next()).value);
                try {
                    if (kind === 'next' || kind === 'pending') await generator.next();
                    else await generator.throw(marker);
                } catch (error) { console.log(kind === 'missing' ? error.name : error === marker); }
                console.log((await generator.next()).done, (await generator.next()).value);
                console.log((await generator.return(9)).value);
                try { await generator.throw(marker); }
                catch (error) { console.log(error === marker); }
            }
            async function main() {
                await check('missing');
                await check('throw');
                await check('next');
                await check('pending');
            }
            main();
            """;
        Assert.Equal("1\nTypeError\ntrue undefined\n9\ntrue\n" +
            string.Concat(Enumerable.Repeat("1\ntrue\ntrue undefined\n9\ntrue\n", 3)),
            TestHarness.RunUnchecked(source, mode));
    }
}
