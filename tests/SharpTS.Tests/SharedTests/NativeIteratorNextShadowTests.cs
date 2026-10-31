using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class NativeIteratorNextShadowTests
{
    // Native iterator descriptor overlays are currently a compiled carrier boundary.
    [Theory, CompiledOnlyData]
    public void ArrayFromHonorsUndefinedAndGetterlessNextClaims(ExecutionMode mode)
    {
        const string source = """
            for (const kind of ["value", "getter", "setter"]) {
                let reads = 0;
                const iterator: any = [1, 2].values();
                if (kind === "value") Object.defineProperty(iterator, "next", { value: undefined, configurable: true });
                if (kind === "getter") Object.defineProperty(iterator, "next", { get() { reads++; return undefined; }, configurable: true });
                if (kind === "setter") Object.defineProperty(iterator, "next", { set(value: any) {}, configurable: true });
                const source: any = { [Symbol.iterator]() { return iterator; } };
                try { Array.from(source); console.log("accepted"); }
                catch (error: any) { console.log(error instanceof TypeError, reads); }
            }
            """;
        Assert.Equal("true 0\ntrue 1\ntrue 0\n", TestHarness.Run(source, mode));
    }
}
