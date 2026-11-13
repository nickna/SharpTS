using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class DelegatedIteratorResultIdentityTests
{
    // The emitted backend's independent result-object replacement is #3670.
    [Theory, InterpretedOnlyData]
    public void IncompleteDelegatedResultsKeepTheirIdentityAndLazyValue(ExecutionMode mode)
    {
        const string source = """
            let doneReads = 0, valueReads = 0, finished = false;
            const step = {
                get done() { doneReads++; return finished ? 'yes' : 0; },
                get value() { valueReads++; return 7; }
            };
            const iterable = { [Symbol.iterator]() { return {
                next() { return step; }, return() { return step; }, throw() { return step; }
            }; } };
            function* run() { return yield* iterable; }
            const generator = run();
            console.log(generator.next() === step, doneReads, valueReads);
            console.log(generator.throw('marker') === step, doneReads, valueReads);
            console.log(generator.return(9) === step, doneReads, valueReads);
            finished = true;
            const last = generator.next();
            console.log(last.value, last.done, last === step, doneReads, valueReads);
            finished = false; doneReads = 0; valueReads = 0;
            function* nested() { return yield* run(); }
            const outer = nested();
            console.log(outer.next() === step, doneReads, valueReads);
            finished = true;
            outer.next();
            const missingDone = { value: 1 };
            function* missing() { yield* { [Symbol.iterator]() { return {
                next() { return missingDone; }
            }; } }; }
            const missingGenerator = missing();
            const result = missingGenerator.next();
            console.log(result.done, result === missingDone);
            missingGenerator.return();
            function* plain() { yield step; }
            const plainGenerator = plain();
            const normal = plainGenerator.next();
            console.log(normal === step, normal.value === step, normal.done);
            plainGenerator.return();
            """;
        Assert.Equal("true 1 0\ntrue 2 0\ntrue 3 0\n7 true false 4 1\ntrue 2 0\nundefined true\nfalse true false\n",
            TestHarness.RunUnchecked(source, mode));
    }
}
