using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class AsyncArrowFunctionIdentityTests
{
    [Theory, ModeData]
    public void AsyncArrowsReadTheCanonicalFunctionDeclaration(ExecutionMode mode)
    {
        const string source = """
            function CatchError() {}
            CatchError.tag = 7;
            const marker = new CatchError();
            const check = async (expected) => {
                console.log(CatchError === expected, CatchError === CatchError);
                await Promise.resolve();
                console.log(marker.constructor === CatchError, CatchError.tag);
                const read = () => CatchError;
                console.log(read() === CatchError);
            };
            async function main() {
                await check(CatchError);
                const read = async () => CatchError;
                console.log(await read() === CatchError);
            }
            main();
            """;
        Assert.Equal("true true\ntrue 7\ntrue\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }
}
