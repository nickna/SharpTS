using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class SloppyGetterOnlyInstanceAssignmentTests
{
    [Theory, ModeData]
    public void NearestGetterOnlyAccessorBlocksSloppyOwnShadows(ExecutionMode mode)
    {
        const string source = """
            class Base {
                stored = 0;
                get value(): number { return this.stored; }
                set value(value: number) { this.stored = value; }
            }
            class Middle extends Base { override get value(): number { return 9; } }
            class Child extends Middle {}
            const target: any = new Child();
            console.log(target["value"] = 10, Object.hasOwn(target, "value"), target.value, target.stored);
            """;
        Assert.Equal("10 false 9 0\n", TestHarness.Run(source, mode));
    }
}
