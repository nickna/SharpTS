using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncSynchronousIteratorErrorTests
{
    [Theory, ModeData]
    public void MissingClassIteratorThrowsTypeErrorAfterOneAcquisition(ExecutionMode mode)
    {
        const string source = """
            const state = { gets: 0 };
            class C { static get [Symbol.iterator]() { state.gets++; return undefined; } }
            const input: any = C;
            async function run() {
                await Promise.resolve(0);
                try { for (const value of input) { console.log("unexpected"); } }
                catch (error: any) { console.log(error instanceof TypeError, state.gets, error.name); }
                for (const value of [1, 2]) { console.log(value); }
            }
            run();
            """;
        Assert.Equal("true 1 TypeError\n1\n2\n", TestHarness.Run(source, mode));
    }
}
