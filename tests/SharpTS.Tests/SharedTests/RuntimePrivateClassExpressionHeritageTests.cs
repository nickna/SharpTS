using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class RuntimePrivateClassExpressionHeritageTests
{
    [Theory]
    [InlineData("#value = 1;", ExecutionMode.Interpreted)]
    [InlineData("#value = 1;", ExecutionMode.Compiled)]
    [InlineData("#value() {}", ExecutionMode.Interpreted)]
    [InlineData("#value() {}", ExecutionMode.Compiled)]
    public void UncheckedRuntimeParentCanDeclarePrivateElements(string element, ExecutionMode mode)
    {
        var source = $$"""
            function make() { return class { {{element}} }; }
            const Base = make();
            class Child extends Base {}
            console.log(new Child() instanceof Base);
            """;

        Assert.Equal("true\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void RuntimeParentBrandsBelongToTheEvaluatedClassDefinition(ExecutionMode mode)
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

        Assert.Equal("true true true false 7\n", TestHarness.RunUnchecked(source, mode));
    }
}
