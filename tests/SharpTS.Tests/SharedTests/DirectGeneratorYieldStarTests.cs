using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class DirectGeneratorYieldStarTests
{
    [Theory, ModeData]
    public void OwnIterator_ReplacesNativeGeneratorAndClosesSelectedIterator(ExecutionMode mode)
    {
        const string source = """
            function* values() { yield 1; }
            const direct = values();
            Object.defineProperty(direct, Symbol.iterator, { get() {
                console.log('lookup', this === direct);
                return function () {
                    console.log('factory', this === direct, arguments.length);
                    return {
                        next(value) { console.log('next', arguments.length, value === undefined); return { value: 9, done: false }; },
                        return(value) { console.log('close', arguments.length, value); return { value: 7, done: true }; }
                    };
                };
            } });
            function* outer() { return yield* direct; }
            const generator = outer();
            console.log(generator.next().value);
            const result = generator.return(5);
            console.log(result.value, result.done);
            console.log(direct.next().value);
            """;
        Assert.Equal("lookup true\nfactory true 0\nnext 1 true\n9\nclose 1 5\n7 true\n1\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void NativeIterator_CapturesNextAndReadsAbruptMethodsOnEachRequest(ExecutionMode mode)
    {
        const string source = """
            function* values() { yield 1; }
            const direct = values();
            Object.defineProperty(direct, 'next', { get() {
                console.log('lookup next', this === direct);
                return function (value) {
                    console.log('next', this === direct, arguments.length, value);
                    return { value: 9, done: false };
                };
            } });
            Object.defineProperty(direct, 'throw', { get() {
                console.log('lookup throw', this === direct);
                return function (value) {
                    console.log('throw', this === direct, arguments.length, value);
                    return { value: 8, done: false };
                };
            } });
            Object.defineProperty(direct, 'return', { get() {
                console.log('lookup return', this === direct);
                return function (value) {
                    console.log('return', this === direct, arguments.length, value);
                    return { value: 7, done: true };
                };
            } });
            function* outer() { return yield* direct; }
            const generator = outer();
            console.log(generator.next().value);
            console.log(generator.next(4).value);
            console.log(generator.throw(5).value);
            const result = generator.return(6);
            console.log(result.value, result.done);
            """;
        Assert.Equal("lookup next true\nnext true 1 undefined\n9\nnext true 1 4\n9\nlookup throw true\nthrow true 1 5\n8\nlookup return true\nreturn true 1 6\n7 true\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void NonCallableOwnIterator_DoesNotUseNativeGenerator(ExecutionMode mode)
    {
        const string source = """
            function* values() { yield 1; }
            function* outer(items) { try { yield* items; } catch (error) { console.log(error instanceof TypeError); } }
            for (const method of [undefined, null, 7]) {
                const direct = values();
                Object.defineProperty(direct, Symbol.iterator, { value: method });
                console.log(outer(direct).next().done);
                console.log(direct.next().value);
            }
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("true\ntrue\n1\n", 3)), TestHarness.RunUnchecked(source, mode));
    }
}