using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2GeneratorOwnReturnTests
{
    [Theory, ModeData]
    public void Issue2312Regression(ExecutionMode mode)
    {
        const string source = """
            function* values() { yield 1; }
            const source: any = values();
            const replacement: any = function() { return { done: true, value: 9 }; };
            Object.defineProperty(source, "return", { value: replacement, configurable: true });
            console.log(Object.hasOwn(source, "return"),
                Object.getOwnPropertyDescriptor(source, "return")!.value === replacement,
                source.return === replacement);
            const captured: any = source.return;
            console.log(captured.call(source).value);
            console.log(source.return().value);

            """;
        Assert.Equal("true true true\n9\n9\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void AbruptCollectionConsumerInvokesTheSelectedOwnReturn()
    {
        const string source = """
            const state = { closed: 0, calls: 0 };
            function* values() { try { yield 1; yield 2; } finally { state.closed++; } }
            const source: any = values();
            Object.defineProperty(source, 'return', { value: function(this: any) {
                state.calls++; console.log(this === source); return { done: true };
            }, configurable: true });
            const original = {};
            try { Array.from(source, () => { throw original; }); }
            catch (e) { console.log(e === original, state.closed, state.calls); }
            """;
        Assert.Equal("true\ntrue 0 1\n", TestHarness.Run(source, ExecutionMode.Compiled));
    }
}
