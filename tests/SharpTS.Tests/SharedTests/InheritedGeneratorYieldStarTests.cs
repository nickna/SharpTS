using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InheritedGeneratorYieldStarTests
{
    [Theory, ModeData]
    public void InheritedIterator_SelectsReplacementAndClosesIt(ExecutionMode mode)
    {
        const string source = """
            function* values() { yield 1; }
            const direct = values();
            function* outer() { return yield* direct; }
            const generator = outer();
            Object.defineProperty(Object.getPrototypeOf(direct), Symbol.iterator, {
                configurable: true, value: function () {
                    console.log('factory', this === direct);
                    return {
                        next() { return { value: 9, done: false }; },
                        return() { console.log('close', arguments.length); return { value: 7, done: true }; }
                    };
                }
            });
            console.log(generator.next().value);
            const result = generator.return(5);
            console.log(result.value, result.done);
            console.log(direct.next().value);
            """;
        Assert.Equal("factory true\n9\nclose 1\n7 true\n1\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void InheritedGetter_IsAcquiredOnceWithOriginalReceiver(ExecutionMode mode)
    {
        const string source = """
            function* values() { yield 1; }
            const direct = values();
            Object.defineProperty(Object.getPrototypeOf(direct), Symbol.iterator, { get() {
                console.log('get', this === direct);
                return function () {
                    console.log('call', this === direct, arguments.length);
                    let count = 0;
                    return { next() { return { value: 9 + count++, done: count > 2 }; } };
                };
            } });
            function* outer() { yield* direct; }
            console.log([...outer()].join(','));
            """;
        Assert.Equal("get true\ncall true 0\n9,10\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void InheritedInvalidIteratorAndThrowingGetter_DoNotFallBack(ExecutionMode mode)
    {
        const string source = """
            function* values() { yield 1; }
            function* outer(items) { yield* items; }
            for (const method of [undefined, null, 7]) {
                const direct = values();
                Object.defineProperty(Object.getPrototypeOf(direct), Symbol.iterator, {
                    configurable: true, value: method
                });
                try { outer(direct).next(); } catch (error) { console.log(error instanceof TypeError); }
            }
            const marker = {};
            const direct = values();
            Object.defineProperty(Object.getPrototypeOf(direct), Symbol.iterator, {
                configurable: true, get() { throw marker; }
            });
            try { outer(direct).next(); } catch (error) { console.log(error === marker); }
            """;
        Assert.Equal("true\ntrue\ntrue\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }
}
