using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class GroupedOptionalCallEffectsTests
{
    [Theory, ModeData]
    public void ComputedNonNullOptionalCallRetainsItsReceiver(ExecutionMode mode)
        => Assert.Equal("8\n", TestHarness.Run("""
            const owner = { value: 7, method(value: number) { return this.value + value; } };
            console.log(owner?.['method']!(1));
            """, mode));

    [Theory, ModeData]
    public void GroupingEndsShortCircuitingBeforeArgumentEvaluation(ExecutionMode mode)
        => Assert.Equal("true 1\ntrue 2\ntrue 3\n3\n8 4\n", TestHarness.Run("""
            const absent: any = null;
            let effects = 0;
            function argument() { effects++; return 1; }
            try { (absent?.method)(argument()); }
            catch (error) { console.log(error instanceof TypeError, effects); }
            const missing: any = undefined;
            try { missing(argument()); }
            catch (error) { console.log(error instanceof TypeError, effects); }
            try { (absent?.['method'])(argument()); }
            catch (error) { console.log(error instanceof TypeError, effects); }
            absent?.method(argument());
            console.log(effects);
            const present: any = { value: 7, method(add: number) { return this.value + add; } };
            console.log((present?.['method'])(argument()), effects);
            """, mode));
}
