using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class FactoryNativeIteratorSpreadTests
{
    [Theory, ModeData]
    public void CustomIteratorFactoriesCanReturnNativeArrayIterators(ExecutionMode mode)
    {
        const string source = """
            const values: any = { [Symbol.iterator]() { return [2, 1].values(); } };
            console.log([...values].join(","));
            function collect(...values: any[]) { console.log(values.join(":")); }
            collect(...values);
            const [first, second] = values;
            console.log(first, second);
            """;
        Assert.Equal("2,1\n2:1\n2 1\n", TestHarness.Run(source, mode));
    }
}
