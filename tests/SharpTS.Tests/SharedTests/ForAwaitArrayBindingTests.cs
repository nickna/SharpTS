using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ForAwaitArrayBindingTests
{
    [Theory, ModeData]
    public void WithScope_PreservesHeadBindingsAndInternalIteratorOperations(ExecutionMode mode)
    {
        const string source = """
            async function main() {
                let steps = 0;
                let closes = 0;
                const inner = {
                    [Symbol.iterator]() { return this; },
                    next() { steps++; return { value: 7, done: false }; },
                    return() { closes++; return {}; }
                };
                const environment = { values: [inner], value: 99, total: 0 };
                with (environment) {
                    for await (const [value] of values) { total += value; }
                }
                console.log(environment.total, environment.value, steps, closes);
            }
            main();
            """;
        Assert.Equal("7 99 1 1\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void CheckedTupleHeadsAndOrdinaryHelperNamesRetainTheirMeaning(ExecutionMode mode)
    {
        const string source = """
            function __arrayBinding() { return 17; }
            async function main() {
                const entries: [number[], string][] = [[[2, 3], 'ok']];
                for await (const [[first, ...rest], tag] of entries) {
                    const value: number = first;
                    const text: string = tag;
                    console.log(value, rest.join(','), text, __arrayBinding());
                }
            }
            main();
            """;
        Assert.Equal("2 3 ok 17\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NativeSourcesHonorIteratorRemovalAndLiveTypedArrayValues(ExecutionMode mode)
    {
        const string source = """
            async function main() {
                const typed = new Uint8Array([1, 2]);
                const target = { set value(value) { typed[1] = 9; } };
                let next;
                for await ([target.value, next] of [typed]) console.log('live', next);
                for (const absent of [null, undefined]) {
                    const sources = [new Uint8Array([1]), new Map([[1, 2]]), new Set([1])];
                    for (const source of sources) {
                        Object.defineProperty(source, Symbol.iterator, { value: absent });
                        try { for await (const [value] of [source]) console.log('unreachable'); }
                        catch (error) { console.log(error instanceof TypeError); }
                    }
                    String.prototype[Symbol.iterator] = absent;
                    try { for await (const [value] of ['x']) console.log('unreachable'); }
                    catch (error) { console.log(error instanceof TypeError); }
                }
            }
            main();
            """;
        Assert.Equal("live 9\n" + string.Concat(Enumerable.Repeat("true\n", 8)),
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void AssignmentReferencesPrecedeSteppingAndAwaitedDefaults(ExecutionMode mode)
    {
        const string source = """
            let log = [];
            const marker = {};
            const receiver = { set value(v) { log.push('set-' + v); } };
            function target() { log.push('target'); return receiver; }
            function key() { log.push('key-expression'); return { toString() { log.push('key'); return 'value'; } }; }
            function fail() { log.push('target-throw'); throw marker; }
            function source() {
                return { [Symbol.iterator]() { return this; },
                    next() { log.push('next'); return { value: undefined, done: false }; },
                    return() { log.push('close'); return {}; } };
            }
            function fallback() { log.push('default'); return new Promise(resolve => setTimeout(() => resolve(7), 0)); }
            async function main() {
                for await ([target()[key()] = await fallback()] of [source()]) console.log(log.join(','));
                log = [];
                try { for await ([fail().value = 9] of [source()]) {} }
                catch (error) { console.log(error === marker, log.join(',')); }
                log = [];
                try { for await ([null[key()] = 9] of [source()]) {} }
                catch (error) { console.log(error instanceof TypeError, log.join(',')); }
            }
            main();
            """;
        Assert.Equal("target,key-expression,next,default,key,set-7,close\ntrue target-throw,close\ntrue key-expression,next,close\n",
            TestHarness.RunUnchecked(source, mode));
    }

    public const string PartialSource = """
        async function check(tag, invalidClose) {
            let steps = 0;
            let closes = 0;
            const inner = {
                [Symbol.iterator]() { return this; },
                next() { steps++; return { value: 7, done: false }; },
                return() { closes++; return invalidClose ? null : { done: true }; }
            };
            try {
                for await (const [value] of [inner]) console.log(tag, value, steps, closes);
            } catch (error) { console.log(tag, error instanceof TypeError, steps, closes); }
        }
        async function assignment() {
            let steps = 0;
            let closes = 0;
            const inner = {
                [Symbol.iterator]() { return this; },
                next() { steps++; return { value: 7, done: false }; },
                return() { closes++; return { done: true }; }
            };
            let value;
            for await ([value] of [inner]) console.log('assignment', value, steps, closes);
            for await (const [] of [inner]) console.log('empty', steps, closes);
        }
        async function main() {
            await check('binding', false);
            await assignment();
            await check('invalid-close', true);
        }
        main();
        """;
    public const string PartialOutput = "binding 7 1 1\nassignment 7 1 1\nempty 1 2\ninvalid-close true 1 1\n";

    [Theory, ModeData]
    public void FiniteHeadsCloseInfiniteSourcesBeforeTheBody(ExecutionMode mode)
        => Assert.Equal(PartialOutput, TestHarness.RunUnchecked(PartialSource, mode));

    [Theory, ModeData]
    public void NestedDefaultsAndElisionsKeepIncrementalOrder(ExecutionMode mode)
    {
        const string source = """
            let log = [];
            function make(tag, values) {
                let index = 0;
                const iterator = {
                    [Symbol.iterator]() { log.push(tag + '-factory'); return this; },
                    get next() {
                        log.push(tag + '-get-next');
                        return function() {
                            log.push(tag + '-next-' + (this === iterator) + '-' + arguments.length);
                            return { done: false, get value() { log.push(tag + '-value'); return values[index++]; } };
                        };
                    },
                    return() { log.push(tag + '-close-' + (this === iterator) + '-' + arguments.length); return {}; }
                };
                return iterator;
            }
            function fallback() { log.push('default'); return make('inner', [9]); }
            async function main() {
                for await (const [[value] = fallback(), , last = 11] of [make('outer', [undefined, undefined])]) {
                    console.log(value, last);
                    console.log(log.join(','));
                }
            }
            main();
            """;
        const string expected = "9 11\nouter-factory,outer-get-next,outer-next-true-0,outer-value,default,inner-factory,inner-get-next,inner-next-true-0,inner-value,inner-close-true-0,outer-next-true-0,outer-next-true-0,outer-value,outer-close-true-0\n";
        Assert.Equal(expected, TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void BindingFailureClosesNestedAndOuterIteratorsAndKeepsOriginalError(ExecutionMode mode)
    {
        const string source = """
            const marker = {};
            const log = [];
            function fail() { throw marker; }
            function make(tag, value) {
                return {
                    [Symbol.iterator]() { return this; },
                    next() { log.push(tag + '-next'); return { value, done: false }; },
                    return() { log.push(tag + '-close'); throw 'close failure'; }
                };
            }
            async function main() {
                const outer = {
                    [Symbol.asyncIterator]() { return this; },
                    next() { return Promise.resolve({ value: make('head', make('inner', undefined)), done: false }); },
                    return() { log.push('outer-close'); return Promise.resolve({}); }
                };
                try { for await (const [[value = fail()]] of outer) console.log('unreachable'); }
                catch (error) { console.log(error === marker, log.join(',')); }
            }
            main();
            """;
        Assert.Equal("true head-next,inner-next,inner-close,head-close,outer-close\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void RestExhaustionAndAwaitedDefaultsRetainState(ExecutionMode mode)
    {
        const string source = """
            async function main() {
                let calls = 0;
                let closes = 0;
                const source = { [Symbol.iterator]() { return this; }, next() { calls++; return { done: true }; }, return() { closes++; return {}; } };
                for await (const [first = await Promise.resolve(4), second = 5, ...rest] of [source])
                    console.log(first, second, rest.length, calls, closes);
                const captured = [];
                for await (const [value = await Promise.resolve(6), ...rest] of [[undefined, 8, 9], [7, 10]]) {
                    captured.push(() => value);
                    console.log(value, rest.join(','));
                }
                console.log(captured[0](), captured[1]());
                for await (const [first, ...rest] of ['a😀b']) console.log(first, rest.join(','));
            }
            main();
            """;
        Assert.Equal("4 5 0 1 0\n6 8,9\n7 10\n6 7\na 😀,b\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void NextAndValueFailuresDoNotCloseTheirCursor(ExecutionMode mode)
    {
        const string source = """
            const marker = {};
            async function main() {
                for (const kind of ['next', 'done', 'value']) {
                    let closes = 0;
                    const inner = {
                        [Symbol.iterator]() { return this; },
                        next() {
                            if (kind === 'next') throw marker;
                            return { get done() { if (kind === 'done') throw marker; return false; }, get value() { throw marker; } };
                        },
                        return() { closes++; return {}; }
                    };
                    try { for await (const [value] of [inner]) {} }
                    catch (error) { console.log(kind, error === marker, closes); }
                }
            }
            main();
            """;
        Assert.Equal("next true 0\ndone true 0\nvalue true 0\n", TestHarness.RunUnchecked(source, mode));
    }
}
