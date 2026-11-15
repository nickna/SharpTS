using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class OperandlessYieldTests
{
    public const string InitialValuesSource = """
        function* values(): Generator<any, any, any> {
            yield;
            yield undefined;
            yield null;
        }
        const iterator = values();
        const bare = iterator.next("ignored");
        console.log(bare.value === undefined, bare.value === null, bare.done);
        const explicitUndefined = iterator.next();
        console.log(explicitUndefined.value === undefined, explicitUndefined.value === null, explicitUndefined.done);
        const explicitNull = iterator.next();
        console.log(explicitNull.value === undefined, explicitNull.value === null, explicitNull.done);
        const completed = iterator.next();
        console.log(completed.value === undefined, completed.done);
        """;

    public const string ResumeSource = """
        function* receive(): Generator<any, any, any> {
            const sent = yield;
            return sent;
        }
        const marker = {};
        const objectIterator = receive();
        const first = objectIterator.next("ignored");
        const objectResult = objectIterator.next(marker);
        console.log(first.value === undefined, first.done, objectResult.value === marker, objectResult.done);
        const nullIterator = receive();
        nullIterator.next();
        const nullResult = nullIterator.next(null);
        console.log(nullResult.value === null, nullResult.done);
        const undefinedIterator = receive();
        undefinedIterator.next();
        const undefinedResult = undefinedIterator.next();
        console.log(undefinedResult.value === undefined, undefinedResult.done);
        """;

    [Theory, ModeData]
    public void OperandlessYield_ProducesUndefinedAndPreservesExplicitValues(ExecutionMode mode)
    {
        Assert.Equal("true false false\ntrue false false\nfalse true false\ntrue true\n",
            TestHarness.Run(InitialValuesSource, mode));
    }

    [Theory, ModeData]
    public void OperandlessYield_ReceivesTheResumingNextArgument(ExecutionMode mode)
    {
        Assert.Equal("true false true true\ntrue true\ntrue true\n",
            TestHarness.Run(ResumeSource, mode));
    }
}
