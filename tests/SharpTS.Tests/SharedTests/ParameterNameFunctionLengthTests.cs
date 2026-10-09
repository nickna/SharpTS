using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ParameterNameFunctionLengthTests
{
    [Theory, ModeData]
    public void DirectFunctionLengthCountsSourceParametersWithInternalLookingNames(ExecutionMode mode)
    {
        var source = """
            function ordinary(value: number, other: number) {}
            function prefixed(__value: number, other: number) {}
            function receiverNamed(__this: number, __value: number) {}
            console.log(ordinary.length);
            console.log(prefixed.length);
            console.log(receiverNamed.length);
            """;

        Assert.Equal("2\n2\n2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DirectFunctionLengthUsesDefaultAndRestParameterRules(ExecutionMode mode)
    {
        var source = """
            function defaulted(__value: number, __this: number = 2, other: number = 3) {}
            function defaultFirst(__this: number = 1, other: number = 2) {}
            function rest(__value: number, ...__this: number[]) {}
            function onlyRest(...__this: number[]) {}
            console.log(defaulted.length);
            console.log(defaultFirst.length);
            console.log(rest.length);
            console.log(onlyRest.length);
            """;

        Assert.Equal("1\n0\n1\n0\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void CompiledFunctionLengthCountsErasedOptionalParameterSyntax()
    {
        var source = """
            function optional(__value: number, __this?: number) {}
            console.log(optional.length);
            """;

        Assert.Equal("2\n", TestHarness.Run(source, ExecutionMode.Compiled));
    }

    [Theory, ModeData]
    public void FunctionValuesRetainTheSameLengthAsDirectReferences(ExecutionMode mode)
    {
        var source = """
            function named(__this: number, __value: number) {}
            function defaulted(__value: number, __this: number = 2) {}
            function rest(__value: number, ...__this: number[]) {}
            const values: any[] = [named, defaulted, rest];
            console.log(values[0].length);
            console.log(values[1].length);
            console.log(values[2].length);
            """;

        Assert.Equal("2\n1\n1\n", TestHarness.Run(source, mode));
    }
}
