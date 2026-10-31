using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2BoundArrayNewTargetTests
{
    [Theory, ModeData]
    public void BoundArrayConstructionUsesAlternateNewTargetPrototype(ExecutionMode mode)
    {
        const string source = """
            const Bound: any = Array.bind(null, 1);
            function Alternate() {}
            const created: any = Reflect.construct(Bound, [2], Alternate);
            console.log(Array.isArray(created), created.length, Object.getPrototypeOf(created) === Alternate.prototype);
            console.log(created[0], created[1]);
            const ordinary: any = new Bound(2);
            console.log(Array.isArray(ordinary), ordinary.length, Object.getPrototypeOf(ordinary) === Array.prototype);
            """;
        Assert.Equal("true 2 true\n1 2\ntrue 2 true\n", TestHarness.Run(source, mode));
    }
}
