using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncArgumentsCalleeTests
{
    [Theory, ModeData]
    public void StrictAsyncFunctionExpressionProtectsArgumentsCallee(ExecutionMode mode)
    {
        const string source = """
            const run = async function () {
                "use strict";
                try { console.log("read", arguments.callee); }
                catch (error) { console.log("caught", error instanceof TypeError); }
            };
            run();
            """;

        Assert.Equal("caught true\n", TestHarness.RunUnchecked(source, mode));
    }
}
