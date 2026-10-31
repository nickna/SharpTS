using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2IndexedUpdateCoercionPhasesTests
{
    // Compiler object-key Symbol normalization is tracked separately by #2595.
    [Theory, InterpretedOnlyData]
    public void IndexedUpdatesConvertRawKeyForGetAndPut(ExecutionMode mode)
    {
        const string source = """
            function ordinary() {}
            for (const target of [Math.abs, ordinary] as any[]) {
                const first = Symbol(), second = Symbol(), events: string[] = [];
                let calls = 0, stored = 9;
                Object.defineProperty(target, first, {
                    get() { events.push("get"); return 5; },
                    set(value: number) { events.push("wrong-set"); }, configurable: true
                });
                Object.defineProperty(target, second, {
                    set(value: number) { events.push("set"); stored = value; }, configurable: true
                });
                const key: any = { [Symbol.toPrimitive](hint: string) {
                    calls++; events.push(hint); return calls === 1 ? first : second;
                } };
                console.log(target[key]++, stored, calls, events.join(","));
            }
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("5 6 2 string,get,string,set\n", 2)), TestHarness.Run(source, mode));
    }
}
