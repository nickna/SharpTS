using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2FunctionObjectPropertyKeysTests
{
    // Compiled Symbol-returning key conversion is independently covered by #2625.
    [Theory, InterpretedOnlyData]
    public void OrdinaryFunctionBracketReadsConvertObjectKeys(ExecutionMode mode)
    {
        const string source = """
            function C() {}
            const target: any = C, key = Symbol("own"), marker = {};
            let calls = 0;
            target.own = 3; target[key] = 7;
            const textKey: any = { [Symbol.toPrimitive](hint: string) { calls++; console.log(hint); return "own"; } };
            const symbolKey: any = { [Symbol.toPrimitive](hint: string) { calls++; console.log(hint); return key; } };
            console.log(target[textKey], calls);
            console.log(target[symbolKey], calls);
            const bad: any = { [Symbol.toPrimitive]() { throw marker; } };
            try { target[bad]; }
            catch (error) { console.log(error === marker); }
            """;
        Assert.Equal("string\n3 1\nstring\n7 2\ntrue\n", TestHarness.Run(source, mode));
    }
}
