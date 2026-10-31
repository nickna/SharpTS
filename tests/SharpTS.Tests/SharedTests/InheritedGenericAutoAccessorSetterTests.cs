using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InheritedGenericAutoAccessorSetterTests
{
    [Theory, ModeData]
    public void Issue2520Regression(ExecutionMode mode)
    {
        const string source = """
            class Box<T> {
                accessor value: T;
                constructor(value: T) { this.value = value; }
            }
            class NumberBox extends Box<number> {
                write(value: number) { return this.value = value; }
            }
            const box = new NumberBox(1);
            console.log(box.write(2));
            console.log(box.value);
            """;
        Assert.Equal("2\n2\n", TestHarness.Run(source, mode));
    }
}
