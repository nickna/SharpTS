using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class CapturedParameterRepresentationTests
{
    [Theory, ModeData]
    public void OwnerReadsActualValueAfterChildAnyWrite(ExecutionMode mode)
    {
        const string source = """
            function run(n: number) {
                const write = () => { n = ("s" as any); };
                const read = () => n;
                write();
                console.log(typeof n, n, read());
            }
            run(1);
            """;
        Assert.Equal("string s s\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NumericOwnerReadsStillSupportTypedArithmetic(ExecutionMode mode)
    {
        const string source = """
            function run(n: number) {
                const write = () => { n = n + 3; };
                write();
                console.log(n + 1, n * 2, typeof n);
            }
            run(1);
            """;
        Assert.Equal("5 8 number\n", TestHarness.Run(source, mode));
    }
}
