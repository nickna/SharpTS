using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class NestedAsyncGeneratorCaptureTests
{
    [Theory, ModeData]
    public void NestedResultHelpersPreserveRejectionIdentityAndCloseExclusions(ExecutionMode mode)
    {
        const string source = """
            async function check(request, final, getter) {
                const marker = {};
                let first = true, returns = 0;
                function step() {
                    return {
                        get done() { if (getter === 'done') throw marker; return final; },
                        get value() { if (getter === 'value') throw marker; return Promise.reject(marker); }
                    };
                }
                const iterator = {
                    next() {
                        if (request !== 'next' && first) { first = false; return { value: 1, done: false }; }
                        return step();
                    },
                    throw() { return step(); },
                    return() { console.log('return', ++returns, arguments.length); return step(); }
                };
                async function* run() { yield* { [Symbol.iterator]() { return iterator; } }; }
                const generator = run();
                if (request !== 'next') await generator.next();
                try {
                    if (request === 'return') await generator.return(9);
                    else if (request === 'throw') await generator.throw('injected');
                    else await generator.next();
                } catch (error) { console.log(error === marker, returns); }
            }
            async function main() {
                await check('next', true, 'none');
                await check('throw', true, 'none');
                await check('return', false, 'none');
                await check('return', true, 'none');
                await check('next', false, 'done');
                await check('next', false, 'value');
                await check('throw', false, 'done');
                await check('throw', false, 'value');
            }
            main();
            """;
        Assert.Equal("true 0\ntrue 0\nreturn 1 1\ntrue 1\nreturn 1 1\ntrue 1\n" +
            "true 0\ntrue 0\ntrue 0\ntrue 0\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void DescendantFactoriesRetainEachParentActivation(ExecutionMode mode)
    {
        const string source = """
            PARENT {
                let current = start;
                const iterator = {
                    next() { return { value: current++, done: false }; },
                    return() { console.log('close', current); return { done: true }; }
                };
                async function* run() {
                    let calls = 0;
                    yield* { [Symbol.iterator]() { console.log('factory', ++calls); return iterator; } };
                }
                await Promise.resolve();
                return { generator: run(), bump() { current += 10; } };
            };
            async function main() {
                const first = await make(1);
                const second = await make(100);
                console.log((await first.generator.next()).value, (await second.generator.next()).value);
                first.bump();
                console.log((await first.generator.next()).value, (await second.generator.next()).value);
                await first.generator.return();
                await second.generator.return();
            }
            main();
            """;
        foreach (var parent in new[] { "async function make(start)", "const make = async (start) =>" })
            Assert.Equal("factory 1\nfactory 1\n1 100\n12 101\nclose 13\nclose 102\n",
                TestHarness.RunUnchecked(source.Replace("PARENT", parent), mode));
    }
}
