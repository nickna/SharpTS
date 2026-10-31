using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2NativeCollectionDescriptorsTests
{
    [Theory, ModeData]
    public void MapAndSetSupportOrdinaryDescriptorsAndIteratorOverrides(ExecutionMode mode)
    {
        const string source = """
            const set: any = new Set<number>([1]);
            const map: any = new Map<string, number>([['a', 1]]);
            function* replacement() { yield 42; }
            for (const value of [set, map]) {
                Object.defineProperty(value, Symbol.iterator, { value: replacement, configurable: true });
                Object.defineProperty(value, 'marker', { get() { return this === value; }, configurable: true });
                const descriptor: any = Object.getOwnPropertyDescriptor(value, Symbol.iterator);
                console.log(descriptor.value === replacement, Object.hasOwn(value, Symbol.iterator), value.marker);
                console.log([...value].join(','));
            }
            console.log(set.has(1), map.get('a'), set.size, map.size);
            """;
        Assert.Equal("true true true\n42\ntrue true true\n42\ntrue 1 1 1\n", TestHarness.Run(source, mode));
    }
}
