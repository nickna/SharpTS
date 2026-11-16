using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class IteratorNextArgumentTests
{
    // Compiled Array iterator method-value lookup is separately tracked by #3717.
    [Theory, InterpretedOnlyData]
    public void NextRetainsZeroLength(ExecutionMode mode)
        => Assert.Equal("0\n", TestHarness.RunUnchecked(
            "console.log([9][Symbol.iterator]().next.length);", mode));

    [Theory, ModeData]
    public void ArrayIteratorNextEvaluatesButDoesNotCoerceSurplusArguments(ExecutionMode mode)
    {
        const string source = """
            const iterator = [9, 10][Symbol.iterator]();
            const argument = { valueOf() { throw new Error('coerced'); } };
            let effects = 0;
            console.log(iterator.next(argument, effects++).value, effects);
            console.log(iterator.next(...[argument, effects++]).value, effects);
            console.log(iterator.next(argument).done);
            """;
        Assert.Equal("9 1\n10 2\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void YieldStarCanSendArgumentsToFactoryReturnedArrayIterator(ExecutionMode mode)
    {
        const string source = """
            const iterable = { [Symbol.iterator]() { return [9, 10][Symbol.iterator](); } };
            function* values() { yield* iterable; }
            const iterator = values();
            console.log(iterator.next().value);
            console.log(iterator.next(123).value);
            console.log(iterator.next(456).done);
            console.log([...values()].join(','));
            """;
        Assert.Equal("9\n10\ntrue\n9,10\n", TestHarness.RunUnchecked(source, mode));
    }
}
