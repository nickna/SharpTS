using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P3CallableMetadataTests
{
    [Theory, ModeData]
    public void DeclaredFunctionLengthReadsObserveDescriptorChanges(ExecutionMode mode)
    {
        const string source = """
            function fn(first: any) { return first; }
            Object.defineProperty(fn, "length", { value: 4, configurable: true });
            console.log(fn.length, fn(17));
            Object.defineProperty(fn, "length", { get() { return 6; }, configurable: true });
            console.log(fn.length);
            console.log(delete fn.length, fn.length);
            """;
        Assert.Equal("4 17\n6\ntrue 0\n", TestHarness.Run(source, mode));
    }
}
