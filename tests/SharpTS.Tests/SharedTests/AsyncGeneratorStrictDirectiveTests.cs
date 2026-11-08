using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncGeneratorStrictDirectiveTests
{
    [Theory, ModeData]
    public void DeclarationUsesItsOwnStrictDirective(ExecutionMode mode)
    {
        const string source = """
            async function* strict(value) {
                "use strict";
                yield this === undefined;
                yield delete value.length;
            }
            async function* sloppy(value) { yield delete value.length; }
            const iterator = strict("x");
            iterator.next().then(result => console.log("receiver", result.value))
                .then(() => iterator.next().then(
                    result => console.log("fulfilled", result.value),
                    () => console.log("rejected")))
                .then(() => sloppy("x").next().then(result => console.log("sloppy", result.value)));
            """;

        Assert.Equal("receiver true\nrejected\nsloppy false\n", TestHarness.RunUnchecked(source, mode));
    }
}
