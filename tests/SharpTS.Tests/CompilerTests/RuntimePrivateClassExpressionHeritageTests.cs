using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class RuntimePrivateClassExpressionHeritageTests
{
    [Theory]
    [InlineData("#value = 1;")]
    [InlineData("#value() {}")]
    public void UncheckedRuntimeParentCanDeclarePrivateElements(string element)
    {
        var source = $$"""
            function make() { return class { {{element}} }; }
            const Base = make();
            class Child extends Base {}
            console.log(new Child() instanceof Base);
            """;

        Assert.Equal("true\n", TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }

    [Fact]
    public void RuntimeParentBrandsBelongToTheEvaluatedClassDefinition()
    {
        const string source = """
            function make(value) {
                return class {
                    #value = value;
                    #read() { return this.#value; }
                    read() { return this.#read(); }
                    static has(receiver) { return #value in receiver && #read in receiver; }
                };
            }
            const A = make(7), B = make(9);
            class Child extends A {}
            const value = new Child();
            console.log(value instanceof A, value instanceof Child, A.has(value), B.has(value), value.read());
            """;

        Assert.Equal("true true true false 7\n", TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }
}
