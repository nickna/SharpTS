using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassExpressionSuperRestArgumentsTests
{
    [Theory, ModeData]
    public void Issue2333Regression(ExecutionMode mode)
    {
        const string source = """
            const Base = class {
                constructor(...values: any[]) {
                    console.log(Array.isArray(values), values.length, values[0], values[1], arguments.length);
                }
            };
            new Base(43, "tail");
            const Derived = class extends Base {
                constructor() { super(43, "tail"); }
            };
            new Derived();
            """;
        Assert.Equal("true 2 43 tail 2\ntrue 2 43 tail 2\n", TestHarness.Run(source, mode));
    }
}
