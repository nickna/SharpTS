using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2ArrayNoncallableIteratorClaimTests
{
    [Theory, ModeData]
    public void Issue2279Regression(ExecutionMode mode)
    {
        const string source = """
            function probe(descriptor: any) {
                const values: any = [1, 2];
                Object.defineProperty(values, Symbol.iterator, descriptor);
                let count = 0;
                try { for (const value of values) count++; console.log(false, count); }
                catch (error: any) { console.log(error instanceof TypeError, count); }
            }
            probe({ value: undefined, configurable: true });
            probe({ set: function(next: any) {}, configurable: true });

            """;
        Assert.Equal("true 0\ntrue 0\n", TestHarness.Run(source, mode));
    }
}