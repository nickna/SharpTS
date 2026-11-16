using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class EnumInitializerCallableTests
{
    [Theory, ModeData]
    public void EnumInitializerCallablesAndTheirRuntimeFeaturesAreDiscovered(ExecutionMode mode)
    {
        const string source = """
            const base = 6;
            enum E {
                A = (function(){ return base + 1; })(),
                B = (() => 8)(),
                C = (function(){ return /x/.test('x') ? 9 : 0; })(),
                D
            }
            console.log(E.A, E.B, E.C, E.D);
            console.log(E[7], E[8], E[9], E[10]);
            """;
        Assert.Equal("7 8 9 10\nA B C D\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void LocalEnumInitializersCaptureTheCurrentInvocation(ExecutionMode mode)
    {
        const string source = """
            function run(value) {
                enum E { A = (function(){ return value; })(), B = (() => value + 1)() }
                return E.A + E.B;
            }
            console.log(run(3), run(7));
            """;
        Assert.Equal("7 15\n", TestHarness.RunUnchecked(source, mode));
    }
}
