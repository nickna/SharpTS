using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NestedGeneratorReceiverTests
{
    [Theory, ModeData]
    public void DeclarationsUseTheirOwnReceiverInStrictAndSloppyScopes(ExecutionMode mode)
    {
        const string source = """
            const receiver = {};
            const root = globalThis;
            function strict() {
                "use strict";
                function* sync() { yield this; }
                async function* asynchronous() { yield this; }
                console.log("strict", sync().next().value === undefined, sync.call(receiver).next().value === receiver);
                return asynchronous;
            }
            function sloppy() {
                function* sync() { yield this; }
                async function* asynchronous() { yield this; }
                console.log("sloppy", sync().next().value === root);
                return asynchronous;
            }
            async function run() {
                const strictAsync = strict.call(receiver);
                const sloppyAsync = sloppy.call(receiver);
                console.log("async", (await strictAsync().next()).value === undefined,
                    (await strictAsync.call(receiver).next()).value === receiver,
                    (await sloppyAsync().next()).value === root);
            }
            run();
            """;

        Assert.Equal("strict true true\nsloppy true\nasync true true true\n", TestHarness.RunUnchecked(source, mode));
    }
}
