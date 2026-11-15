using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PrimitiveForOfOperandTests
{
    [Theory, ModeData]
    public void PrimitiveLiteralAndComputedOperands_VerifyAndUsePrototypeIterators(ExecutionMode mode)
    {
        const string source = """
            Boolean.prototype[Symbol.iterator] = function* () { yield this.valueOf(); };
            Number.prototype[Symbol.iterator] = function* () { yield this.valueOf(); };
            for (const value of true) console.log(value);
            for (const value of 7) console.log(value);
            for (const value of (1 < 0)) console.log(value);
            for (const value of (2 + 3)) console.log(value);
            let calls = 0;
            function operand() { calls++; return 11; }
            for (const value of operand()) console.log(value);
            console.log(calls);
            """;
        Assert.Equal("true\n7\nfalse\n5\n11\n1\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void NonIterablePrimitiveOperands_VerifyAndThrowGuestTypeErrors(ExecutionMode mode)
    {
        const string source = """
            try { for (const value of true) {} } catch (error) { console.log(error instanceof TypeError); }
            try { for (const value of 7) {} } catch (error) { console.log(error instanceof TypeError); }
            try { for (const value of (1 < 0)) {} } catch (error) { console.log(error instanceof TypeError); }
            try { for (const value of (2 + 3)) {} } catch (error) { console.log(error instanceof TypeError); }
            """;
        Assert.Equal("true\ntrue\ntrue\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }
}
