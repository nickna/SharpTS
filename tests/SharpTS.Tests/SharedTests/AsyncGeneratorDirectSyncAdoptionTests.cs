using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class AsyncGeneratorDirectSyncAdoptionTests
{
    [Theory, ModeData]
    public void DirectSyncDelegationAdoptsYieldedAndCompletionValues(ExecutionMode mode)
    {
        const string source = """
            const marker = {};
            function* values() {
                yield Promise.resolve(7);
                yield { then(resolve) { resolve(8); } };
                return Promise.resolve(9);
            }
            function* rejected() {
                try { yield Promise.reject(marker); }
                finally { console.log('inner finally'); }
            }
            const poisoned = Promise.resolve(1);
            Object.defineProperty(poisoned, 'constructor', { get() { throw marker; } });
            function* poisonedValues() {
                try { yield poisoned; }
                finally { console.log('poison finally'); }
            }
            function* cleanup() {
                try { yield 1; }
                finally { yield Promise.resolve(2); return Promise.resolve(3); }
            }
            async function* run(iterable) { return yield* iterable; }
            async function main() {
                const generator = run(values());
                for (let i = 0; i < 3; i++) {
                    const result = await generator.next();
                    console.log(result.value, result.done);
                }
                for (const iterable of [rejected(), poisonedValues()]) {
                    const failing = run(iterable);
                    try { await failing.next(); }
                    catch (error) { console.log(error === marker); }
                    console.log((await failing.next()).done);
                }
                const returning = run(cleanup());
                await returning.next();
                const first = await returning.return(9);
                const last = await returning.next();
                console.log(first.value, first.done, last.value, last.done);
            }
            main();
            """;
        Assert.Equal("7 false\n8 false\n9 true\ninner finally\ntrue\ntrue\npoison finally\ntrue\ntrue\n2 false 3 true\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void DirectSyncGeneratorIteratorOverrideIsAcquiredOnce(ExecutionMode mode)
    {
        const string source = """
            function* original() { console.log('unexpected original'); yield 1; }
            function* replacement() { yield Promise.resolve(7); return Promise.resolve(9); }
            const iterable = original();
            Object.defineProperty(iterable, Symbol.iterator, { get() {
                console.log('lookup', this === iterable);
                return function () { console.log('acquire', this === iterable); return replacement(); };
            } });
            async function* run() { return yield* iterable; }
            async function main() {
                const generator = run();
                const first = await generator.next();
                const last = await generator.next();
                console.log(first.value, first.done, last.value, last.done);
            }
            main();
            """;
        Assert.Equal("lookup true\nacquire true\n7 false 9 true\n", TestHarness.RunUnchecked(source, mode));
    }
}
