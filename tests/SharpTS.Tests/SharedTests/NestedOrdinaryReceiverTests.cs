using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NestedOrdinaryReceiverTests
{
    [Theory, ModeData]
    public void NestedOrdinaryFunctionOwnsItsReceiver(ExecutionMode mode)
    {
        const string source = """
            const receiver = {};
            function outer() {
                "use strict";
                function inner(value = this) { return [this, value]; }
                const bare = inner();
                const bound = inner.call(receiver);
                const lexical = () => this;
                console.log(bare[0] === undefined, bare[1] === undefined,
                    bound[0] === receiver, bound[1] === receiver, lexical() === receiver);
            }
            outer.call(receiver);

            function sloppy() {
                function inner() { return this; }
                console.log(inner() === globalThis);
            }
            sloppy.call(receiver);
            """;

        Assert.Equal("true true true true true\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }
}
