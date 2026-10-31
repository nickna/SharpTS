using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncOwnerFunctionArgumentsTests
{
    [Theory, ModeData]
    public void NestedFunctionRetainsCaptureAndOwnArgumentsAfterAwait(ExecutionMode mode)
    {
        const string source = """
            async function run() {
                const captured = 47;
                function answer() { return captured + arguments.length; }
                await Promise.resolve(0);
                console.log(answer());
            }
            run().catch((error: any) => console.log('unexpected', error.name, error.message));
            """;
        Assert.Equal("47\n", TestHarness.Run(source, mode));
    }
}
