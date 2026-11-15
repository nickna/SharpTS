using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class OperandlessYieldBoundaryTests
{
    public const string ArraySource = """
        function* values() {
            const array = [yield];
            console.log(array.length, array[0]);
            return array[0];
        }
        const generator = values();
        const first = generator.next();
        console.log(first.value === undefined, first.done);
        const last = generator.next(2);
        console.log(last.value, last.done);
        """;

    public const string TemplateSource = """
        function* values() {
            const value = `1${yield}3${yield}5`;
            console.log(value);
            return value;
        }
        const generator = values();
        const first = generator.next();
        console.log(first.value === undefined, first.done);
        const second = generator.next(2);
        console.log(second.value === undefined, second.done);
        const last = generator.next(4);
        console.log(last.value, last.done);
        """;

    [Theory, ModeData]
    public void OperandlessYield_BeforeArrayClose_PreservesResumeValue(ExecutionMode mode)
    {
        Assert.Equal("true false\n1 2\n2 true\n", TestHarness.RunUnchecked(ArraySource, mode));
    }

    [Theory, ModeData]
    public void OperandlessYield_BeforeTemplateMiddleAndTail_PreservesResumeValues(ExecutionMode mode)
    {
        Assert.Equal("true false\ntrue false\n12345\n12345 true\n",
            TestHarness.RunUnchecked(TemplateSource, mode));
    }

    [Theory]
    [InlineData("function* values() { [yield*]; }")]
    [InlineData("function* values() { `${yield*}`; }")]
    [InlineData("function* values() { `${yield*}middle${1}`; }")]
    [InlineData("function* values() { true ? yield* : 0; }")]
    public void DelegatingYield_StillRequiresOperandAtNewOmissionBoundaries(string source)
    {
        Assert.ThrowsAny<Exception>(() => TestHarness.ParseOrThrow(source));
    }

    [Theory, ModeData]
    public void OperandlessYield_BeforeConditionalColon_PreservesSelectedBranchAndResumeValue(ExecutionMode mode)
    {
        const string source = """
            function* values(flag) { return flag ? yield : yield; }
            for (const flag of [true, false]) {
                const generator = values(flag);
                const first = generator.next();
                console.log(first.value === undefined, first.done);
                const last = generator.next(7);
                console.log(last.value, last.done);
            }
            function* nested() { return (yield) ? yield : yield; }
            const generator = nested();
            console.log(generator.next().value === undefined);
            console.log(generator.next(true).value === undefined);
            const last = generator.next(9);
            console.log(last.value, last.done);
            """;
        Assert.Equal("true false\n7 true\ntrue false\n7 true\ntrue\ntrue\n9 true\n",
            TestHarness.RunUnchecked(source, mode));
    }
}
