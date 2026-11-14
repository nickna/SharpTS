using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class DelegatedIteratorResultIdentityTests
{
    public const string Source = """
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

    public const string Expected =
        "true 1 0\ntrue 2 0\ntrue 3 0\n7 true false 4 1\ntrue 2 0\nundefined true\nfalse true false\n";

    public const string ConsumerSource = """
        const marker = {};
        const step = { value: 4, done: false };
        function sequence(first) {
            return { [Symbol.iterator]() {
                let count = 0;
                return { next() { return ++count === 1 ? first : { value: 'end', done: true }; } };
            } };
        }
        function* mixed() {
            console.log('delegate completion', yield* sequence(step));
            yield 8;
            return 9;
        }
        const mixedGenerator = mixed();
        console.log('delegated', mixedGenerator.next() === step);
        let result = mixedGenerator.next();
        console.log('ordinary', result === step, result.value, result.done);
        result = mixedGenerator.next();
        console.log('completed', result === step, result.value, result.done);
        result = mixedGenerator.next();
        console.log('closed', result.value, result.done);

        let valueReads = 0;
        const poison = { done: false, get value() { valueReads++; throw marker; } };
        function* lazy() {
            try { return yield* sequence(poison); }
            catch (error) { console.log('unexpected generator catch'); }
            finally { console.log('finally'); }
        }
        const lazyGenerator = lazy();
        result = lazyGenerator.next();
        console.log('lazy', result === poison, valueReads);
        try { result.value; } catch (error) { console.log('consumer catch', error === marker, valueReads); }
        result = lazyGenerator.next();
        console.log('resumed', result.value, result.done);

        function readable(tag) {
            return sequence({ done: false, get value() { console.log(tag, 'read'); return 6; } });
        }
        function* values(source) { yield* source; }
        console.log('spread', [...values(readable('spread'))].join(','));
        for (const value of values(readable('loop'))) console.log('loop', value);

        function* recover() {
            let count = 0;
            try {
                yield* { [Symbol.iterator]() { return {
                    next() {
                        if (++count === 1) return step;
                        return { get done() { throw marker; } };
                    }
                }; } };
            } catch (error) { yield error === marker; }
            return 'recovered';
        }
        const recovering = recover();
        console.log('before error', recovering.next() === step);
        result = recovering.next();
        console.log('caught yield', result === step, result.value, result.done);
        result = recovering.next();
        console.log(result.value, result.done);
        """;

    public const string ConsumerExpected =
        "delegated true\ndelegate completion end\nordinary false 8 false\ncompleted false 9 true\nclosed undefined true\n" +
        "lazy true 0\nconsumer catch true 1\nfinally\nresumed end true\n" +
        "spread read\nspread 6\nloop read\nloop 6\n" +
        "before error true\ncaught yield false true false\nrecovered true\n";

    [Theory, ModeData]
    public void IncompleteDelegatedResultsKeepTheirIdentityAndLazyValue(ExecutionMode mode)
        => Assert.Equal(Expected, TestHarness.RunUnchecked(Source, mode));

    [Theory, ModeData]
    public void DelegatedResultsRemainLazyForConsumersAndDoNotLeakAcrossResumption(ExecutionMode mode)
        => Assert.Equal(ConsumerExpected, TestHarness.RunUnchecked(ConsumerSource, mode));
}
