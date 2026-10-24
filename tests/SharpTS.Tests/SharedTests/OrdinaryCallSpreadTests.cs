using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class OrdinaryCallSpreadTests
{
    [Theory, ModeData]
    public void MultipleCompatibleSpreadsAndValuesAreAccepted(ExecutionMode mode)
        => Assert.Equal("1,2,3\n", TestHarness.Run("""
            function m(...args: number[]) { console.log(args.join(',')); }
            m(...[1], 2, ...[3]);
            """, mode));

    [Theory, ModeData]
    public void FixedTupleElementsConsumeFixedParametersBeforeRest(ExecutionMode mode)
    {
        const string source = """
            function m(first: string, ...rest: number[]) { console.log(first, rest.join(',')); }
            m(...['x', 1, 2] as const);
            m(...['x'] as const);
            m(...['x'] as const, ...[1, 2] as const);
            """;
        Assert.Equal("x 1,2\nx \nx 1,2\n", TestHarness.Run(source, mode));
    }
}
