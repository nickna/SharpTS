using System.Text;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class AsyncFromSyncRejectionCloseTests
{
    // Nested helper capture identity is tracked independently in #3668.
    [Theory, InterpretedOnlyData]
    public void RejectedNextAndThrowValuesCloseOnceAndPreserveTheOriginalReason(ExecutionMode mode)
    {
        const string source = """
            async function check(request, failure, closing) {
                console.log(request, failure, closing);
                const marker = {}, closeMarker = {};
                let first = true;
                function step() {
                    let value;
                    if (failure === "constructor") {
                        value = Promise.resolve(1);
                        Object.defineProperty(value, "constructor", { get() { throw marker; } });
                    } else if (failure === "later") {
                        value = { then(resolve, reject) { setTimeout(() => reject(marker), 0); } };
                    } else value = Promise.reject(marker);
                    return { done: false, value };
                }
                const iterator = {
                    next() {
                        if (request === "throw" && first) { first = false; return { value: 1, done: false }; }
                        return step();
                    },
                    throw() { return step(); }
                };
                Object.defineProperty(iterator, "return", {
                    get() {
                        console.log("lookup");
                        if (closing === "getter") throw closeMarker;
                        if (closing === "noncallable") return 1;
                        return function () {
                            console.log("close", arguments.length, this === iterator);
                            if (closing === "call") throw closeMarker;
                            if (closing === "primitive") return 1;
                            return {
                                get done() { console.log("unexpected done"); return true; },
                                get value() { console.log("unexpected value"); return 0; }
                            };
                        };
                    }
                });
                async function* run() { yield* { [Symbol.iterator]() { return iterator; } }; }
                const generator = run();
                if (request === "throw") await generator.next();
                try { await (request === "throw" ? generator.throw("injected") : generator.next()); }
                catch (error) { console.log("original", error === marker); }
                console.log((await generator.next()).done);
            }
            async function main() {
                for (const request of ["next", "throw"])
                for (const failure of ["reject", "later", "constructor"])
                for (const closing of ["ok", "getter", "call", "primitive", "noncallable"])
                    await check(request, failure, closing);
            }
            main();
            """;
        var expected = new StringBuilder();
        foreach (string request in new[] { "next", "throw" })
        foreach (string failure in new[] { "reject", "later", "constructor" })
        foreach (string closing in new[] { "ok", "getter", "call", "primitive", "noncallable" })
        {
            expected.Append($"{request} {failure} {closing}\nlookup\n");
            if (closing is not ("getter" or "noncallable")) expected.Append("close 0 true\n");
            expected.Append("original true\ntrue\n");
        }
        Assert.Equal(expected.ToString(), TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void FinalValuesReturnRequestsAndResultGetterFailuresDoNotClose(ExecutionMode mode)
    {
        const string source = """
            const marker = {};
            let request = "", final = false, getter = "", first = true, returns = 0;
            function step() {
                return {
                    get done() { if (getter === "done") throw marker; return final; },
                    get value() { if (getter === "value") throw marker; return Promise.reject(marker); }
                };
            }
            const iterator = {
                next() {
                    if (request !== "next" && first) { first = false; return { value: 1, done: false }; }
                    return step();
                },
                throw() { return step(); },
                return() { console.log("return", ++returns, arguments.length); return step(); }
            };
            async function* run() { yield* { [Symbol.iterator]() { return iterator; } }; }
            async function check(operation, done, getterFailure) {
                request = operation; final = done; getter = getterFailure; first = true; returns = 0;
                const generator = run();
                if (request !== "next") await generator.next();
                try {
                    if (request === "return") await generator.return(9);
                    else if (request === "throw") await generator.throw("injected");
                    else await generator.next();
                } catch (error) { console.log(error === marker, returns); }
            }
            async function main() {
                await check("next", true, "none");
                await check("throw", true, "none");
                await check("return", false, "none");
                await check("return", true, "none");
                await check("next", false, "done");
                await check("next", false, "value");
                await check("throw", false, "done");
                await check("throw", false, "value");
            }
            main();
            """;
        Assert.Equal("true 0\ntrue 0\nreturn 1 1\ntrue 1\nreturn 1 1\ntrue 1\n" +
            "true 0\ntrue 0\ntrue 0\ntrue 0\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void PoisonedPromiseConstructorClosesBeforeNextReturns(ExecutionMode mode)
    {
        const string source = """
            const marker = {};
            const value = Promise.resolve(1);
            Object.defineProperty(value, "constructor", {
                get() { console.log("constructor"); throw marker; }
            });
            const iterable = { [Symbol.iterator]() { return {
                next() { return { value, done: false }; },
                return() { console.log("close"); return {}; }
            }; } };
            async function* run() { yield* iterable; }
            const result = run().next();
            console.log("issued");
            result.catch(error => console.log(error === marker));
            """;
        Assert.Equal("constructor\nclose\nissued\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void RejectedAcquiredGeneratorValueRunsItsFinally(ExecutionMode mode)
    {
        const string source = """
            const marker = {};
            function* values() {
                try { yield Promise.reject(marker); }
                finally { console.log("inner finally"); }
            }
            const iterable = { [Symbol.iterator]() { return values(); } };
            async function* run() { yield* iterable; }
            run().next().catch(error => console.log(error === marker));
            """;
        Assert.Equal("inner finally\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }
}
