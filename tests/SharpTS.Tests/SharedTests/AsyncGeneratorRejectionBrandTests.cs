using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncGeneratorRejectionBrandTests
{
    [Theory, ModeData]
    public void BuiltInFailuresRejectWithGuestErrorsAndPreserveGuestValues(ExecutionMode mode)
    {
        const string source = """
            "use strict";
            const token = new TypeError("token");
            const stringToken = "TypeError: guest string";
            async function* builtIn(value) { yield delete value.length; }
            async function* guest(value) { throw value; }
            async function* rejected() { yield await Promise.reject(stringToken); }

            builtIn("x").next().then(
                result => console.log("fulfilled", result.value),
                error => console.log("rejected", error instanceof TypeError, error.name,
                    String(error).startsWith("TypeError: ")))
                .then(() => guest(token).next().catch(error => console.log(error === token)))
                .then(() => guest(stringToken).next().catch(error => console.log(error === stringToken)))
                .then(() => rejected().next().catch(error => console.log(error === stringToken)));
            """;

        Assert.Equal("rejected true TypeError true\ntrue\ntrue\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }
}
