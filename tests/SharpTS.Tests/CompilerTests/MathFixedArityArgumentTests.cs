using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

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

    // Compiler-specific until #3714 removes the interpreter's exact-arity
    // rejection of missing/surplus Math arguments. Supported canonical-arity
    // spread calls remain dual-mode in MathFixedAritySpreadTests.
    [Fact]
    public void ExpandedArgumentsRespectFixedArityAndEvaluationOrder() =>
        Assert.Equal(Expected, TestHarness.RunUnchecked(Source, ExecutionMode.Compiled));
}
