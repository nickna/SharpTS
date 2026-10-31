using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class LocalAbstractClassFactoryTests
{
    [Theory, ModeData]
    public void ConcreteChildOfLocalAbstractBaseRemainsVerifiable(ExecutionMode mode)
    {
        const string source = """
            function make() {
                abstract class Base { value = 3; abstract run(): number; }
                class Derived extends Base { run(): number { return 7 + this.value; } }
                return new Derived().run();
            }
            console.log(make(), make());
            """;
        Assert.Equal("10 10\n", TestHarness.Run(source, mode));
    }
}
