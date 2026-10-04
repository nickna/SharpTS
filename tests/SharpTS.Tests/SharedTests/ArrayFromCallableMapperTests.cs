using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ArrayFromCallableMapperTests
{
    [Theory, ModeData]
    public void OriginalBoundAndNumberMappersExecute(ExecutionMode mode)
    {
        const string source = """
            function twice(x: number) { return x * 2; }
            const bound: any = twice.bind(null);
            console.log(Array.from([1, 2], twice).join(","));
            console.log(Array.from([1, 2], bound).join(","));
            console.log(Array.from(["1", "2"], Number).join(","));
            """;
        Assert.Equal("2,4\n2,4\n1,2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void BoundMappersRetainReceiverAndEarlierArguments(ExecutionMode mode)
    {
        const string source = """
            function add(bias: number, value: number, index: number) {
                return this.base + bias + value + index;
            }
            const bound: any = add.bind({base: 10}, 100);
            console.log(Array.from([1, 2], bound, {base: 90}).join(','));
            const target: any = [];
            const push: any = target.push.bind(target);
            console.log(Array.from([3, 4], push).join(','));
            console.log(target.join(','));
            """;
        Assert.Equal("111,113\n2,4\n3,0,4,1\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void FunctionCallWrapperCanServeAsMapper(ExecutionMode mode)
    {
        const string source = """
            function twice(value: number) { return value * 2; }
            const call: any = twice.call;
            console.log(Array.from([null, null], call, twice).join(','));
            """;
        Assert.Equal("0,2\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void MapperValidationRunsForEmptyInputsAndUndefinedMeansOmitted(ExecutionMode mode)
    {
        const string source = """
            const from: any = Array.from;
            console.log(from([1, 2]).join(','), from([1, 2], undefined).join(','));
            for (const mapper of [null, {}, 0, 'x', Symbol('x')]) {
                try { from([], mapper); } catch (error) { console.log(error instanceof TypeError); }
            }
            """;
        Assert.Equal("1,2 1,2\ntrue\ntrue\ntrue\ntrue\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, CompiledOnlyData]
    public void ClassMapperIsAcceptedUntilItIsCalled(ExecutionMode mode)
    {
        // Classes have [[Call]], which throws when invoked. The separate
        // interpreter class-call defect is preserved in the fixture README.
        const string source = """
            const from: any = Array.from;
            class ConstructorOnly {}
            console.log(from([], ConstructorOnly).length);
            try { from([1], ConstructorOnly); console.log('accepted'); }
            catch (error) { console.log(error instanceof TypeError); }
            """;
        Assert.Equal("0\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void NumberValueUsesExplicitCoercionAndMissingArgumentDefault(ExecutionMode mode)
    {
        const string source = """
            const numeric: any = Number;
            console.log(numeric(), numeric(null), numeric('2'), numeric(3n));
            console.log(Number.isNaN(numeric(undefined)), Object.is(numeric(-0), -0));
            try { numeric(Symbol('x')); } catch (error) { console.log(error instanceof TypeError); }
            """;
        Assert.Equal("0 0 2 3\ntrue true\ntrue\n", TestHarness.Run(source, mode));
    }
}
