using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class AsyncGeneratorCustomSyncRequestTests
{
    public const string RequestSource = """
        function make(returnDone) {
            let first = true;
            const iterator = {
                next(value) {
                    if (!first) return { value, done: true };
                    first = false;
                    return { value: Promise.resolve(7), done: false };
                },
                get return() {
                    console.log("lookup return");
                    return function (value) {
                        console.log("return", arguments.length, value, this === iterator);
                        return { value: Promise.resolve(value + 1), done: returnDone };
                    };
                },
                get throw() {
                    console.log("lookup throw");
                    return function (value) {
                        console.log("throw", arguments.length, value, this === iterator);
                        return { value: Promise.resolve("handled:" + value), done: returnDone };
                    };
                }
            };
            return { [Symbol.iterator]() { return iterator; } };
        }
        async function* run(iterable) {
            try { return yield* iterable; }
            finally { console.log("outer"); }
        }
        async function show(result) { console.log(result.value, result.done); }
        async function main() {
            let generator = run(make(true));
            await show(await generator.next());
            await show(await generator.return(9));
            generator = run(make(false));
            await generator.next();
            await show(await generator.return(9));
            await show(await generator.next(12));
            generator = run(make(false));
            await generator.next();
            await show(await generator.throw("boom"));
            await show(await generator.next(12));
            generator = run(make(true));
            await generator.next();
            await show(await generator.throw("done"));
        }
        main();
        """;

    [Theory, ModeData]
    public void YieldStar_ForwardsSyncReturnAndThrowRequestsAndAdoptsTheirValues(ExecutionMode mode)
    {
        Assert.Equal("7 false\nlookup return\nreturn 1 9 true\nouter\n10 true\n" +
            "lookup return\nreturn 1 9 true\n10 false\nouter\n12 true\n" +
            "lookup throw\nthrow 1 boom true\nhandled:boom false\nouter\n12 true\n" +
            "lookup throw\nthrow 1 done true\nouter\nhandled:done true\n",
            TestHarness.RunUnchecked(RequestSource, mode));
    }

    // The emitted adapter's primitive-result rejection loses its TypeError
    // identity; that independent compiler defect is tracked in #3661.
    [Theory, InterpretedOnlyData]
    public void YieldStar_RejectsPrimitiveSyncReturnAndThrowResults(ExecutionMode mode)
    {
        const string source = """
            const iterable = {
                [Symbol.iterator]() {
                    return {
                        next() { return { value: 1, done: false }; },
                        return() { return 1; },
                        throw() { return 1; }
                    };
                }
            };
            async function* run() {
                try { yield* iterable; }
                catch (error) { yield error.name; }
            }
            async function main() {
                const returning = run();
                await returning.next();
                console.log((await returning.return(9)).value);
                console.log((await returning.next()).done);
                const throwing = run();
                await throwing.next();
                console.log((await throwing.throw("boom")).value);
                console.log((await throwing.next()).done);
            }
            main();
            """;
        Assert.Equal("TypeError\ntrue\nTypeError\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }
}
