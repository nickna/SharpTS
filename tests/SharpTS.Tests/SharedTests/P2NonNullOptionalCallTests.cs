using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2NonNullOptionalCallTests
{
    [Theory, ModeData]
    public void NonNullAssertionsPreserveActiveOptionalCallChains(ExecutionMode mode)
    {
        const string source = """
            const absent: any = null;
            let effects = 0;
            function argument() { effects++; return 1; }
            console.log(absent?.method!(argument()) === undefined, effects);
            console.log(absent?.["method"]!(argument()) === undefined, effects);
            const owner = { value: 7, method(value: number) { return this.value + value; } };
            console.log(owner?.method!(argument()), effects);
            """;
        Assert.Equal("true 0\ntrue 0\n8 1\n", TestHarness.Run(source, mode));
    }
}
