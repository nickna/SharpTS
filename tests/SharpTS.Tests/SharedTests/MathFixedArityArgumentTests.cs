using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class MathFixedArityArgumentTests
{
    public const string Source = """
        console.log(Math.abs(...[]), Math.abs(...[-3, 9]));
        console.log(Math.pow(...[2, 3]), Math.pow(...[2]), Math.pow(...[]));
        console.log(Math.pow(...[], 2, ...[3]), Math.pow(2, ...[3, 4]));
        console.log(Math.abs(), Math.pow(2), Math.abs(-4, 7), Math.pow(2, 3, 4));
        const events = [];
        function argument(name, value) { events.push(name); return value; }
        const extra = { valueOf() { throw 'extra was coerced'; } };
        const first = { valueOf() { events.push('coerce'); return -5; } };
        console.log(Math.abs(argument('first', first), ...argument('spread', [extra]), argument('last', extra)));
        console.log(events.join(','));
        console.log(Math.pow(...[2,3,extra]), Math.atan2(...[0,1,extra]), Math.imul(...[3,4,extra]));
        console.log(Math.fround(...[]), Math.f16round(...[]), Math.clz32(...[]), Math.imul(...[]));
        console.log(Math.fround(...[2,extra]), Math.f16round(...[2,extra]), Math.clz32(...[1,extra]));
        console.log(Math.pow(...[1,NaN]), Math.pow(...[1,Infinity]));
        console.log(Object.is(Math.log1p(...[-0]), -0), Object.is(Math.expm1(...[-0]), -0));
        events.length = 0;
        Math.random(...argument('random', [extra]));
        console.log(events.join(','));
        console.log(Math.sumPrecise(...[[1,2,3]], extra));
        try { Math.sumPrecise(...[]); } catch(e) { console.log(e.name); }
        """;

    public const string Expected = "NaN 3\n8 NaN NaN\n8 8\nNaN NaN 4 8\n5\nfirst,spread,last,coerce\n"
        + "8 0 12\nNaN NaN 32 0\n2 2 31\nNaN NaN\ntrue true\nrandom\n6\nTypeError\n";

    [Theory, ModeData]
    public void ExpandedArgumentsRespectFixedArityAndEvaluationOrder(ExecutionMode mode) =>
        Assert.Equal(Expected, TestHarness.RunUnchecked(Source, mode));

    [Theory, ModeData]
    public void FixedMethodsKeepTheirSpecifiedLengthsAndPadMissingValues(ExecutionMode mode)
    {
        const string source = """
            console.log(Math.abs.length, Math.pow.length, Math.atan2.length, Math.imul.length, Math.random.length);
            console.log(Math.sin(), Math.log(), Math.sqrt(), Math.sign(), Math.trunc(), Math.atan2(1));
            const extra = { valueOf() { throw 'surplus coercion'; } };
            console.log(Math.sin(0, extra), Math.log(1, extra), Math.sqrt(4, extra), Math.sign(-2, extra));
            """;
        Assert.Equal("1 2 2 2 0\nNaN NaN NaN NaN NaN NaN\n0 0 2 -1\n",
            TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void AliasedMethodsUseTheSameArgumentNormalization(ExecutionMode mode) =>
        Assert.Equal("NaN 3\n", TestHarness.RunUnchecked(
            "const abs = Math.abs; console.log(abs(), abs(-3, { valueOf(){ throw 'extra'; } }));", mode));

    [Theory, ModeData]
    public void MathFunctionValuesDistinguishOmissionFromNullAcrossInvocationForms(ExecutionMode mode)
    {
        const string source = """
            const abs = Math.abs, pow = Math.pow, atan2 = Math.atan2;
            console.log(abs(), abs(undefined), abs(null), abs.length);
            console.log(abs.call(null), abs.apply(null, []), abs.bind(null)());
            console.log(pow(2), pow.apply(null, [2]), pow.bind(null, 2)(), pow(2, null), pow.length);
            console.log(atan2(1), atan2(1, null));
            const m = Math;
            console.log(m.sin(), m.sqrt(), m.fround(), m.clz32(), m.imul());
            const min = Math.min, max = Math.max, hypot = Math.hypot;
            console.log(min(), max(), hypot(), min(null), max(undefined));
            """;
        Assert.Equal("NaN NaN 0 1\nNaN NaN NaN\nNaN NaN NaN 1 2\nNaN 1.5707963267948966\n"
            + "NaN NaN NaN 32 0\nInfinity -Infinity 0 0 NaN\n", TestHarness.RunUnchecked(source, mode));
    }
}
