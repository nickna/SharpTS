using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2ShadowedNumberConstructorTests
{
    [Theory, ModeData]
    public void NewNumberUsesTheLexicalConstructorBinding(ExecutionMode mode)
    {
        const string source = """
            class Custom {
                value: any;
                constructor(value: any) { this.value = value + 100; }
            }
            function probe(Number: any) {
                const direct: any = new Number(7);
                const Alias: any = Number;
                const aliased: any = new Alias(7);
                console.log(Number === Custom, direct instanceof Custom, direct.value, aliased.value);
            }
            probe(Custom);
            console.log(new Number(8).valueOf());
            """;
        Assert.Equal("true true 107 107\n8\n", TestHarness.Run(source, mode));
    }
}
