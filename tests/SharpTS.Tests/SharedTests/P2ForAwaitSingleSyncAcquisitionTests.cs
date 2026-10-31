using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2ForAwaitSingleSyncAcquisitionTests
{
    [Theory, ModeData]
    public void MissingSynchronousConstructorIteratorIsReadOnce(ExecutionMode mode)
    {
        const string source = """
            let gets = 0;
            class C { static get [Symbol.iterator]() { gets++; return undefined; } }
            const input: any = C;
            async function run() {
                await Promise.resolve(0);
                try { for await (const value of input) {} }
                catch (error) { console.log(error instanceof TypeError, gets); }
            }
            run();
            """;
        Assert.Equal("true 1\n", TestHarness.Run(source, mode));
    }
}
