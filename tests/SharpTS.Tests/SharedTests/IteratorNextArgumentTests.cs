using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class IteratorNextArgumentTests
{
    [Theory, ModeData]
    public void NextRetainsZeroLength(ExecutionMode mode)
        => Assert.Equal("0\n", TestHarness.RunUnchecked(
            "console.log([9][Symbol.iterator]().next.length);", mode));

    [Theory, ModeData]
    public void ArrayIteratorNextCanBeReadAndCalledAsAValue(ExecutionMode mode)
    {
        const string source = """
            const iterator = [9, 10][Symbol.iterator]();
            const next = iterator.next;
            console.log(typeof next, next.name, next.length);
            console.log(next.call(iterator, 123).value);
            console.log(next.apply(iterator, [456]).value);
            const final = next.call(iterator);
            console.log(final.done, final.value === undefined);
            """;
        Assert.Equal("function next 0\n9\n10\ntrue true\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void ArrayIteratorNextValuesRemainReachableThroughIndirectReads(ExecutionMode mode)
    {
        // Compile each access independently: Reflect must not incidentally
        // retain the helper for the destructuring or indexed-read cases.
        foreach (string read in new[]
        {
            "const { next } = iterator; console.log(next.call(iterator).value);",
            "console.log(iterator['next'].call(iterator).value);",
            "const key = 'ne' + 'xt'; console.log(iterator[key].call(iterator).value);",
            "console.log(Reflect.get(iterator, 'next').call(iterator).value);"
        })
            Assert.Equal("1\n", TestHarness.RunUnchecked("const iterator = [1].values();" + read, mode));
    }

    [Theory, ModeData]
    public void ArrayIteratorNextSharesIdentityAndValidatesTheCallReceiver(ExecutionMode mode)
    {
        const string source = """
            const a = [9].values(), b = [20].values();
            const next = a.next;
            console.log(next === a.next, next === b.next, next === [].keys().next);
            console.log(next.call(b).value, next.call(a).value);
            try { next.call({}); } catch (error) { console.log(error instanceof TypeError); }
            try { new next(); } catch (error) { console.log(error instanceof TypeError); }
            Object.defineProperty(a, 'next', { value: undefined });
            console.log(a.next === undefined);
            """;
        Assert.Equal("true true true\n20 9\ntrue\ntrue\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }

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
    public void ArrayIteratorNextRespectsReplacedPrototype(ExecutionMode mode)
    {
        const string source = """
            const iterator = [9].values();
            const original = iterator.next;
            let reads = 0;
            Object.setPrototypeOf(iterator, { get next() {
                reads++;
                return function () { return this === iterator; };
            } });
            const replacement = iterator.next;
            console.log(reads, replacement.call(iterator), original.call(iterator).value);
            Object.setPrototypeOf(iterator, { next: undefined });
            console.log(iterator.next === undefined);
            Object.setPrototypeOf(iterator, null);
            console.log(iterator.next === undefined);
            """;
        Assert.Equal("1 true 9\ntrue\ntrue\n", TestHarness.RunUnchecked(source, mode));
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
