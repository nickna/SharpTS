using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GeneratorLivePrototypeTests
{
    [Theory, ModeData]
    public void GeneratorPrototypeMutationHonorsExtensibilityAndCycles(ExecutionMode mode)
    {
        const string source = """
            function* values() {}
            async function* asyncValues() {}
            for (const factory of [values, asyncValues]) {
                const iterator = factory();
                const original = Object.getPrototypeOf(iterator);
                console.log(Object.isExtensible(iterator));
                console.log(Object.setPrototypeOf(iterator, null) === iterator);
                console.log(Object.getPrototypeOf(iterator) === null, iterator.next === undefined);
                console.log(Reflect.setPrototypeOf(iterator, original), typeof iterator.next);
                const child = Object.create(iterator);
                console.log(Reflect.setPrototypeOf(iterator, child));
                const other = factory();
                Object.setPrototypeOf(other, iterator);
                console.log(Reflect.setPrototypeOf(iterator, other));
                Object.preventExtensions(iterator);
                console.log(Reflect.setPrototypeOf(iterator, original));
                console.log(Reflect.setPrototypeOf(iterator, null));
                try { Object.setPrototypeOf(iterator, null); }
                catch (error) { console.log(error instanceof TypeError); }
            }
            """;
        const string expected = "true\ntrue\ntrue true\ntrue function\nfalse\nfalse\ntrue\nfalse\ntrue\n";
        Assert.Equal(expected + expected, TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void DetachedPrototypeDoesNotResurrectIterator(ExecutionMode mode)
    {
        const string source = """
            function* values() { yield 1; }
            const generator = values();
            Object.defineProperty(generator, 'next', { value: generator.next });
            const prototype = Object.getPrototypeOf(generator);
            const parent = Object.getPrototypeOf(prototype);
            const iterator = generator[Symbol.iterator];
            Object.setPrototypeOf(prototype, null);
            console.log(generator[Symbol.iterator] === undefined);
            function* outer() { yield* generator; }
            try { outer().next(); }
            catch (error) { console.log(error instanceof TypeError); }
            Object.setPrototypeOf(prototype, parent);
            console.log(generator[Symbol.iterator] === iterator);
            console.log(outer().next().value);
            """;
        Assert.Equal("true\ntrue\ntrue\n1\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void DeletedIntrinsicIteratorStaysMissing(ExecutionMode mode)
    {
        const string source = """
            function* values() { yield 1; }
            const generator = values();
            let prototype = Object.getPrototypeOf(generator);
            while (!Object.hasOwn(prototype, Symbol.iterator))
                prototype = Object.getPrototypeOf(prototype);
            console.log(delete prototype[Symbol.iterator]);
            console.log(generator[Symbol.iterator] === undefined);
            function* outer() { yield* generator; }
            try { outer().next(); }
            catch (error) { console.log(error instanceof TypeError); }
            """;
        Assert.Equal("true\ntrue\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }
}
