using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncGeneratorOperandExceptionTests
{
    [Theory, ModeData]
    public void ThrowingYieldOperandsRunGuestHandlers(ExecutionMode mode)
    {
        const string source = """
            function fail(token) { throw token; }
            async function* plain() {
                try { yield fail("plain"); }
                catch (error) { yield error; }
            }
            async function* delegated() {
                try { yield* fail("delegated"); }
                catch (error) { yield error; }
            }
            async function* awaited() {
                try { yield await fail("awaited"); }
                catch (error) { yield error; }
            }
            async function* handler() {
                try {
                    try { throw "trigger"; }
                    catch { yield fail("handler"); }
                    finally { console.log("cleanup"); }
                } catch (error) { yield error; }
            }
            Promise.all([plain().next(), delegated().next(), awaited().next(), handler().next()]).then(
                results => console.log(results[0].value, results[1].value, results[2].value, results[3].value),
                error => console.log("rejected", error));
            """;

        Assert.Equal("cleanup\nplain delegated awaited handler\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void AwaitRejectionInCatchRunsFinallyBeforeRejecting(ExecutionMode mode)
    {
        const string source = """
            async function* inspect() {
                try { throw "enter"; }
                catch { yield await Promise.resolve().then(() => { throw "awaited"; }); }
                finally { console.log("cleanup"); }
            }
            inspect().next().then(
                result => console.log("fulfilled", result.value),
                error => console.log("rejected", error));
            """;

        Assert.Equal("cleanup\nrejected awaited\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void SynchronousBinaryOperandsReachCatchOnEitherSideOfAwait(ExecutionMode mode)
    {
        const string source = """
            function fail(token) { throw token; }
            async function* inspect() {
                try { const value = fail("before") + await Promise.resolve(1); yield value; }
                catch (error) { yield error; }
                try { const value = await Promise.resolve(1) - fail("after"); yield value; }
                catch (error) { yield error; }
                try {
                    const operand = { valueOf() { throw "coercion"; } };
                    const value = operand + await 1;
                    yield value;
                } catch (error) { yield error; }
            }
            async function run() {
                const iterator = inspect();
                console.log((await iterator.next()).value);
                console.log((await iterator.next()).value);
                console.log((await iterator.next()).value);
            }
            run();
            """;

        Assert.Equal("before\nafter\ncoercion\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void SynchronousCallsReachCatchAfterAwaitedArguments(ExecutionMode mode)
    {
        const string source = """
            function fail(token) { throw token; }
            const alias = fail;
            async function* inspect() {
                try { const value = fail(await Promise.resolve("direct")); yield value; }
                catch (error) { yield error; }
                try { yield alias(await Promise.resolve("alias")); }
                catch (error) { yield error; }
                try { yield alias(fail("argument"), await 1); }
                catch (error) { yield error; }
            }
            async function run() {
                const iterator = inspect();
                console.log((await iterator.next()).value);
                console.log((await iterator.next()).value);
                console.log((await iterator.next()).value);
            }
            run();
            """;

        Assert.Equal("direct\nalias\nargument\n", TestHarness.RunUnchecked(source, mode));
    }
}
