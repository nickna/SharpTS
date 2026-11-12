using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class AcquiredGeneratorIteratorTests
{
    private const string FactorySource = """
        function wrap(tag, generatorFactory) {
            let lookups = 0;
            let calls = 0;
            let nextLookups = 0;
            const source = {
                get [Symbol.iterator]() {
                    if (++lookups !== 1) throw new Error("factory lookup repeated");
                    console.log("lookup", tag);
                    return function () {
                        if (++calls !== 1) throw new Error("factory call repeated");
                        console.log("acquire", tag, this === source);
                        const iterator = generatorFactory();
                        const next = iterator.next;
                        Object.defineProperty(iterator, "next", {
                            get() {
                                if (++nextLookups !== 1) throw new Error("next lookup repeated");
                                console.log("next lookup", tag, this === iterator);
                                return function (value) {
                                    if (this !== iterator) throw new Error("wrong next receiver");
                                    console.log("next", tag, this === iterator);
                                    return next.call(this, value);
                                };
                            }
                        });
                        return iterator;
                    };
                }
            };
            return source;
        }
        function show(result) { console.log(result.value, result.done); }
        """;

    [Theory, ModeData]
    public void YieldStar_RetainsAcquiredGeneratorAndForwardsSentAndCompletionValues(ExecutionMode mode)
    {
        var source = FactorySource + """
            function* values() {
                try { console.log("sent", yield 7); return 9; }
                finally { console.log("inner close"); }
            }
            function* syncOuter(items) {
                try { return yield* items; }
                finally { console.log("outer close"); }
            }
            async function* asyncOuter(items) {
                try { return yield* items; }
                finally { console.log("outer close"); }
            }
            const sync = syncOuter(wrap("sync", values));
            show(sync.next());
            show(sync.next(5));
            async function main() {
                const async = asyncOuter(wrap("async", values));
                show(await async.next());
                show(await async.next(5));
            }
            main();
            """;
        var expected = Acquisition("sync") + "7 false\nnext sync true\nsent 5\ninner close\nouter close\n9 true\n" +
            Acquisition("async") + "7 false\nnext async true\nsent 5\ninner close\nouter close\n9 true\n";
        Assert.Equal(expected, TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void AsyncYieldStar_UsesAcquiredGeneratorForReturnThrowAndCleanup(ExecutionMode mode)
    {
        var source = FactorySource + """
            const marker = {};
            function* values() {
                try {
                    try { yield 1; }
                    catch (error) { console.log("caught", error === marker); yield 2; }
                    return 3;
                } finally { console.log("inner close"); }
            }
            async function* outer(items) {
                try { return yield* items; }
                finally { console.log("outer close"); }
            }
            async function main() {
                let generator = outer(wrap("async-return", values));
                await generator.next();
                show(await generator.return(9));
                generator = outer(wrap("async-throw", values));
                await generator.next();
                show(await generator.throw(marker));
                show(await generator.next());
            }
            main();
            """;
        Assert.Equal(Acquisition("async-return") + "inner close\nouter close\n9 true\n" +
            Acquisition("async-throw") + "caught true\n2 false\nnext async-throw true\ninner close\nouter close\n3 true\n",
            TestHarness.RunUnchecked(source, mode));
    }

    // Compiled synchronous delegation skips abrupt requests to a generator
    // returned by a custom factory; the separate limitation is tracked in #3664.
    [Theory, InterpretedOnlyData]
    public void SyncYieldStar_UsesAcquiredGeneratorForReturnThrowAndCleanup(ExecutionMode mode)
    {
        var source = FactorySource + """
            const marker = {};
            function* values() {
                try {
                    try { yield 1; }
                    catch (error) { console.log("caught", error === marker); yield 2; }
                    return 3;
                } finally { console.log("inner close"); }
            }
            function* outer(items) {
                try { return yield* items; }
                finally { console.log("outer close"); }
            }
            let generator = outer(wrap("sync-return", values));
            generator.next();
            show(generator.return(9));
            generator = outer(wrap("sync-throw", values));
            generator.next();
            show(generator.throw(marker));
            show(generator.next());
            """;
        Assert.Equal(Acquisition("sync-return") + "inner close\nouter close\n9 true\n" +
            Acquisition("sync-throw") + "caught true\n2 false\nnext sync-throw true\ninner close\nouter close\n3 true\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void PromiseCombinators_RetainAcquiredGeneratorAndCaptureNextOnce(ExecutionMode mode)
    {
        var source = FactorySource + """
            function* values() {
                try { yield 7; yield 8; return 9; }
                finally { console.log("inner close"); }
            }
            async function main() {
                console.log((await Promise.all(wrap("all", values))).join(","));
                console.log(await Promise.race(wrap("race", values)));
                console.log(await Promise.any(wrap("any", values)));
                const settled = await Promise.allSettled(wrap("settled", values));
                console.log(settled[0].value, settled[1].value);
            }
            main();
            """;
        var expected = Exhaustion("all") + "7,8\n" + Exhaustion("race") + "7\n" +
            Exhaustion("any") + "7\n" + Exhaustion("settled") + "7 8\n";
        Assert.Equal(expected, TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void PromiseAll_ClosesAcquiredGeneratorWhenResolveThrows(ExecutionMode mode)
    {
        var source = FactorySource + """
            const marker = {};
            function* values() {
                try { yield 7; yield 8; }
                finally { console.log("inner close"); }
            }
            async function main() {
                const resolve = Promise.resolve;
                Promise.resolve = function () { throw marker; };
                try { await Promise.all(wrap("close", values)); }
                catch (error) { console.log("rejected", error === marker); }
                finally { Promise.resolve = resolve; }
            }
            main();
            """;
        Assert.Equal(Acquisition("close") + "inner close\nrejected true\n", TestHarness.RunUnchecked(source, mode));
    }

    private static string Acquisition(string tag)
        => $"lookup {tag}\nacquire {tag} true\nnext lookup {tag} true\nnext {tag} true\n";

    private static string Exhaustion(string tag)
        => Acquisition(tag) + $"next {tag} true\nnext {tag} true\ninner close\n";
}
