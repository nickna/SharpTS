using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class YieldStarAcquisitionErrorTests
{
    [Theory, ModeData]
    public void PrimitiveIteratorResults_ReachLocalCatchAndFinally(ExecutionMode mode)
    {
        const string source = """
            function* delegate(value) {
                try { yield* { [Symbol.iterator]() { return value; } }; }
                catch (error) { console.log('caught', error instanceof TypeError); }
                finally { console.log('finally'); }
            }
            for (const value of [7, null, undefined, 'invalid', true]) {
                const generator = delegate(value);
                console.log('done', generator.next().done);
                console.log('closed', generator.next().done);
            }
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("caught true\nfinally\ndone true\nclosed true\n", 5)),
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void ThrowingNextGetter_EntersLocalHandlerAndCanSuspendDuringCleanup(ExecutionMode mode)
    {
        const string source = """
            const marker = {};
            const iterator = { get next() { console.log('next lookup', this === iterator); throw marker; } };
            const source = { [Symbol.iterator]() { console.log('factory', this === source); return iterator; } };
            function* delegate() {
                try { yield* source; }
                catch (error) { console.log('caught', error === marker); yield 'catch'; }
                finally { console.log('finally'); yield 'cleanup'; }
                return 'complete';
            }
            const generator = delegate();
            const first = generator.next();
            console.log(first.value, first.done);
            const second = generator.next();
            console.log(second.value, second.done);
            const third = generator.next();
            console.log(third.value, third.done);
            console.log(generator.next().done);
            """;
        Assert.Equal("factory true\nnext lookup true\ncaught true\ncatch false\nfinally\ncleanup false\ncomplete true\ntrue\n",
            TestHarness.RunUnchecked(source, mode));
    }
}