using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Pins ECMA-262 7.1.12.1 Number::toString output across every number→string path
/// and asserts interpreter/compiled parity. The interpreter routes all sites through
/// RuntimeTypes.FormatNumber; the compiled runtime emits a byte-identical
/// $Runtime.FormatNumber (RuntimeEmitter.NumberFormat.cs). Before this work both modes
/// were lossy in different ways (interp naive d.ToString -> "1E+21"; compiled 16-digit
/// "0.################" -> 0.1+0.2 = "0.3").
/// </summary>
public class NumberFormattingParityTests
{
    [Theory, ModeData]
    public void ConsoleLog_PrecisionAndThresholds(ExecutionMode mode)
    {
        var source = """
            console.log(0.1 + 0.2);
            console.log(1e21);
            console.log(1e20);
            console.log(1e15);
            console.log(1e-6);
            console.log(1e-7);
            console.log(123.456);
            console.log(-1.5);
            console.log(0.000123);
            """;
        Assert.Equal(
            "0.30000000000000004\n1e+21\n100000000000000000000\n1000000000000000\n0.000001\n1e-7\n123.456\n-1.5\n0.000123\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StringAndToString(ExecutionMode mode)
    {
        var source = """
            console.log(String(0.1 + 0.2));
            console.log(String(1e21));
            console.log((0.1 + 0.2).toString());
            console.log((1e21).toString());
            console.log((1e21).toString(10));
            """;
        Assert.Equal("0.30000000000000004\n1e+21\n0.30000000000000004\n1e+21\n1e+21\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ConcatTemplateJoinAndPropertyKey(ExecutionMode mode)
    {
        var source = """
            console.log("" + (0.1 + 0.2));
            console.log(`${0.1 + 0.2}|${1e21}`);
            console.log([0.1 + 0.2, 1e21, 1e20].join(","));
            const o: any = {};
            o[0.1 + 0.2] = 1;
            console.log(Object.keys(o)[0]);
            """;
        Assert.Equal(
            "0.30000000000000004\n0.30000000000000004|1e+21\n0.30000000000000004,1e+21,100000000000000000000\n0.30000000000000004\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void JsonStringify_PrecisionAndSpecials(ExecutionMode mode)
    {
        // NaN/Infinity serialize as null; everything else uses Number::toString.
        var source = """
            console.log(JSON.stringify({ a: 0.1 + 0.2, b: 1e21, c: 1e20, d: 1e-7 }));
            console.log(JSON.stringify([NaN, Infinity, -Infinity, 1.5]));
            """;
        Assert.Equal(
            "{\"a\":0.30000000000000004,\"b\":1e+21,\"c\":100000000000000000000,\"d\":1e-7}\n[null,null,null,1.5]\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void LargeIntegers_ShortestRoundTrip(ExecutionMode mode)
    {
        // At/above 2^53 the double loses integer precision; ECMA-262 uses the shortest
        // round-trip, not the exact integer value.
        var source = """
            console.log(1234567890123456789);
            console.log(9007199254740993);
            console.log(9007199254740992);
            """;
        Assert.Equal("1234567890123456800\n9007199254740992\n9007199254740992\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SpecialValuesAndRadix(ExecutionMode mode)
    {
        var source = """
            console.log(NaN, Infinity, -Infinity);
            console.log(0, -0, 1, -1);
            console.log((255).toString(16), (255).toString(2));
            console.log(1e300, 1e-300, 5e-324);
            """;
        Assert.Equal(
            "NaN Infinity -Infinity\n0 0 1 -1\nff 11111111\n1e+300 1e-300 5e-324\n",
            TestHarness.Run(source, mode));
    }
}
