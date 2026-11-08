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
}
