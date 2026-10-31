using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class FunctionSymbolPropertyKeyCoercionTests
{
    [Theory, ModeData]
    public void FunctionObjectKeysCoerceOnceAndRetainSymbols(ExecutionMode mode)
    {
        const string source = """
            function C() {}
            const target: any = C, key = Symbol("own"), state = { calls: 0, hint: "" };
            target[key] = 7;
            const property: any = { [Symbol.toPrimitive](hint: string) { state.calls++; state.hint = hint; return key; } };
            console.log(target[property], state.calls, state.hint);
            """;
        Assert.Equal("7 1 string\n", TestHarness.Run(source, mode));
    }
}
