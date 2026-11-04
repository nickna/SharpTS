using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class GenericFunctionConstructionTests
{
    [Theory, ModeData]
    public void GenericFunctionConstructionUsesClosedInvocationCore(ExecutionMode mode)
    {
        Assert.Equal("7 true\n9 true\n11 true\n", TestHarness.Run("""
            function Target<T>(value: T): any { return { value, target: new.target }; }
            const Constructor: any = Target;
            const result = new Constructor(7);
            console.log(result.value, result.target === Target);
            const next = new Constructor(9);
            console.log(next.value, next.target === Target);
            const ordinary = Constructor(11);
            console.log(ordinary.value, ordinary.target === undefined);
            """, mode));
    }
}
