using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ForAwaitArrayBindingCompletionTests
{
    [Theory, ModeData]
    public void SelfDeletingIteratorGetterDoesNotRestoreNativeFallback(ExecutionMode mode)
    {
        const string source = """
            async function main() {
                for (const inherited of [false, true]) {
                    const source = new Uint8Array([7]);
                    const owner = inherited ? {} : source;
                    Object.setPrototypeOf(source, inherited ? owner : null);
                    let reads = 0;
                    Object.defineProperty(owner, Symbol.iterator, {
                        configurable: true,
                        get() { reads++; delete owner[Symbol.iterator]; return undefined; }
                    });
                    try { for await (const [value] of [source]) console.log('unexpected', value); }
                    catch (error) { console.log(error instanceof TypeError, reads); }
                }
            }
            main();
            """;
        Assert.Equal("true 1\ntrue 1\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void AsyncArrowCountsComputedKeyAwaitsInDestructuringDefaults(ExecutionMode mode)
    {
        const string source = """
            function later(value) { return new Promise(resolve => setTimeout(() => resolve(value), 0)); }
            const run = async () => {
                let saved = 7;
                let target;
                [target = { [await later('x')]: saved, [await later('y')]: await later(9) }] = [undefined];
                console.log(target.x, target.y, saved);
            };
            run();
            """;
        Assert.Equal("7 9 7\n", TestHarness.RunUnchecked(source, mode));
    }

    public const string GeneratorReturnSource = """
        function source(tag, value, kind, log, closeMarker) {
            return {
                [Symbol.iterator]() { return this; },
                next() { return { value, done: false }; },
                return() {
                    log.push(tag);
                    if (tag === 'inner' && kind === 'invalid') return null;
                    if (kind !== 'normal') throw closeMarker;
                    return {};
                }
            };
        }
        async function* binding(outer) {
            for await (const [[value = yield 'pause']] of outer) console.log('unexpected');
        }
        async function* assigning(outer) {
            let value;
            for await ([[value = yield 'pause']] of outer) console.log('unexpected');
        }
        async function check(kind, assignment) {
            const log = [];
            const marker = {};
            const closeMarker = {};
            const outer = {
                [Symbol.asyncIterator]() { return this; },
                next() {
                    const inner = source('inner', undefined, kind, log, closeMarker);
                    return { value: source('head', inner, kind, log, closeMarker), done: false };
                },
                return() { log.push('outer'); return {}; }
            };
            const iterator = assignment ? assigning(outer) : binding(outer);
            const first = await iterator.next();
            console.log(first.value, first.done);
            try {
                const result = kind === 'throw' ? await iterator.throw(marker) : await iterator.return(9);
                console.log(result.value, result.done, log.join(','));
            } catch (error) {
                console.log(error === marker, error === closeMarker, error instanceof TypeError, log.join(','));
            }
        }
        async function main() {
            for (const assignment of [false, true]) {
                for (const kind of ['normal', 'close-error', 'invalid', 'throw']) await check(kind, assignment);
            }
        }
        main();
        """;

    public const string GeneratorReturnOutput =
        "pause false\n9 true inner,head,outer\n" +
        "pause false\nfalse true false inner,head,outer\n" +
        "pause false\nfalse false true inner,head,outer\n" +
        "pause false\ntrue false false inner,head,outer\n" +
        "pause false\n9 true inner,head,outer\n" +
        "pause false\nfalse true false inner,head,outer\n" +
        "pause false\nfalse false true inner,head,outer\n" +
        "pause false\ntrue false false inner,head,outer\n";

    [Theory, ModeData]
    public void GeneratorReturnClosesSuspendedHeadsAndPreservesCompletionPrecedence(ExecutionMode mode)
        => Assert.Equal(GeneratorReturnOutput, TestHarness.RunUnchecked(GeneratorReturnSource, mode));
}
