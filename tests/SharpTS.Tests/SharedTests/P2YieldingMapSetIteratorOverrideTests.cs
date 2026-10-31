using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2YieldingMapSetIteratorOverrideTests
{
    [Fact]
    public void YieldingForOfUsesKnownAndDynamicMapAndSetIteratorOverrides()
    {
        const string source = """
            const state = { calls: 0, receiver: false };
            const knownSet = new Set<number>([1]);
            const knownMap = new Map<string, number>([['a', 1]]);
            const dynamicSet: any = new Set<number>([1]);
            const dynamicMap: any = new Map<string, number>([['a', 1]]);
            let selected: any;
            function* custom(this: any) { state.calls++; state.receiver = this === selected; yield 42; }
            for (const value of [knownSet, knownMap, dynamicSet, dynamicMap])
                Object.defineProperty(value, Symbol.iterator, { value: custom, configurable: true });
            function* runSet() { for (const value of knownSet) yield value; }
            function* runMap() { for (const value of knownMap) yield value; }
            function* runDynamicSet() { for (const value of dynamicSet) yield value; }
            function* runDynamicMap() { for (const value of dynamicMap) yield value; }
            const sources: any[] = [knownSet, knownMap, dynamicSet, dynamicMap];
            const runs: any[] = [runSet, runMap, runDynamicSet, runDynamicMap];
            for (let index = 0; index < runs.length; index++) {
                selected = sources[index]; state.calls = 0; state.receiver = false;
                const iterator = runs[index]();
                const first = iterator.next(), second = iterator.next();
                console.log(first.value, second.done, state.calls, state.receiver);
            }

            """;
        Assert.Equal("42 true 1 true\n42 true 1 true\n42 true 1 true\n42 true 1 true\n",
            TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
