using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class GeneratorLoopCleanupTests
{
    [Theory, ModeData]
    public void BreakAndReturn_CloseGeneratorBeforeContinuing(ExecutionMode mode)
    {
        const string source = """
            let closed = 0;
            function* values() {
                try { yield 42; yield 43; }
                finally { closed++; }
            }
            for (const value of values()) break;
            console.log(closed);
            function read() {
                for (const value of values()) return value;
                return 0;
            }
            console.log(read(), closed);
            class C { static *[Symbol.iterator]() { yield* values(); } }
            const iterable: any = C;
            for (const value of iterable) break;
            console.log(closed);
            """;
        Assert.Equal("1\n42 2\n3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ContinueAndExhaustion_DoNotCloseEarly(ExecutionMode mode)
    {
        const string source = """
            let closed = false;
            function* values() {
                try { yield 1; yield 2; }
                finally { closed = true; }
            }
            for (const value of values()) {
                console.log(value, closed);
                continue;
            }
            console.log(closed);
            """;
        Assert.Equal("1 false\n2 false\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void CloseFailure_OverridesBreakButPreservesBodyThrow(ExecutionMode mode)
    {
        const string source = """
            function* values() {
                try { yield 1; }
                finally { throw "cleanup"; }
            }
            try { for (const value of values()) break; }
            catch (error) { console.log(error); }
            try { for (const value of values()) throw "body"; }
            catch (error) { console.log(error); }
            """;
        Assert.Equal("cleanup\nbody\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void FinallyYield_IsResumedOnceByClose(ExecutionMode mode)
    {
        const string source = """
            function* values() {
                try { yield 1; }
                finally { console.log("closing"); yield 2; console.log("resumed"); }
            }
            const iterator = values();
            for (const value of iterator) break;
            console.log(iterator.next().done);
            """;
        Assert.Equal("closing\nresumed\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ForAwaitSyncGenerator_ClosesOnceAndPreservesThrow(ExecutionMode mode)
    {
        const string source = """
            function* values() {
                try { yield 1; }
                finally { console.log("closing"); yield 2; console.log("resumed"); }
            }
            function* failing() {
                try { yield 1; }
                finally { throw "cleanup"; }
            }
            async function run() {
                const iterator = values();
                for await (const value of iterator) break;
                console.log(iterator.next().done);
                try { for await (const value of failing()) throw "body"; }
                catch (error) { console.log(error); }
            }
            run();
            """;
        Assert.Equal("closing\nresumed\ntrue\nbody\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ForAwaitRejectedStep_ClosesSyncGeneratorBeforeRejecting(ExecutionMode mode)
    {
        const string source = """
            let events = "";
            function* values() {
                try { yield Promise.reject("step"); }
                finally { events += "close"; }
            }
            async function run() {
                const iterator = values();
                try { for await (const value of iterator) {} }
                catch (error) { console.log(error, events === "close"); }
                console.log(iterator.next().done, events);
            }
            run();
            """;
        Assert.Equal("step true\ntrue close\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void CollectionCallbacks_CloseGeneratorAndPreserveTheirThrow(ExecutionMode mode)
    {
        const string source = """
            let closed = 0;
            const original = {};
            function* values() {
                try { yield 1; }
                finally { closed++; throw "cleanup"; }
            }
            try { Array.from(values(), () => { throw original; }); }
            catch (error) { console.log(error === original, closed); }
            try { Object.groupBy(values(), () => { throw original; }); }
            catch (error) { console.log(error === original, closed); }
            try { Object.fromEntries(values()); }
            catch (error) { console.log(error === "cleanup", closed); }
            """;
        Assert.Equal("true 1\ntrue 2\nfalse 3\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void PromiseCombinators_CloseGeneratorAndRejectWithOriginalError(ExecutionMode mode)
    {
        const string source = """
            let closed = 0;
            const original = {};
            function* values() {
                try { yield 1; }
                finally { closed++; throw "cleanup"; }
            }
            async function run() {
                const constructor: any = Promise;
                const resolve = constructor.resolve;
                for (const name of ["all", "race", "allSettled", "any"]) {
                    constructor.resolve = () => { throw original; };
                    let result: any;
                    try {
                        result = constructor[name](values());
                        console.log(name, "returned", closed);
                    } catch (error) { console.log(name, "unexpected throw", error); }
                    constructor.resolve = resolve;
                    try { await result; }
                    catch (error) { console.log(error === original); }
                }
                constructor.resolve = () => ({ then() { throw original; } });
                const result = constructor.all(values());
                constructor.resolve = resolve;
                try { await result; }
                catch (error) { console.log("then", error === original, closed); }
            }
            run();
            """;
        Assert.Equal("all returned 1\ntrue\nrace returned 2\ntrue\n"
            + "allSettled returned 3\ntrue\nany returned 4\ntrue\nthen true 5\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SumPrecise_CloseFailurePreservesNonNumberError(ExecutionMode mode)
    {
        // Compare the invalid-element error with the same error during failing close.
        const string source = """
            let closed = false;
            const math: any = Math;
            function* values() {
                try { yield "invalid"; }
                finally { closed = true; throw "cleanup"; }
            }
            let expected: any;
            try { math.sumPrecise(["invalid"]); }
            catch (error: any) { expected = error; }
            try { math.sumPrecise(values()); }
            catch (error: any) {
                console.log(error instanceof TypeError, error.message === expected.message, closed);
            }
            """;
        Assert.Equal("true true true\n", TestHarness.Run(source, mode));
    }
}
