using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class NonCallableCallMemberTests
{
    [Theory, ModeData]
    public void PublicCallMemberDoesNotGrantCallCapability(ExecutionMode mode)
    {
        Assert.Equal("true 0\n42\n", TestHarness.Run("""
            let calls = 0;
            class C {
                Call(interpreter: any, args: any): number { calls++; return 42; }
            }
            const value: any = new C();
            try { value(1, 2); } catch (error) { console.log(error instanceof TypeError, calls); }
            console.log(value.Call(null, []));
            """, mode));
    }
}
