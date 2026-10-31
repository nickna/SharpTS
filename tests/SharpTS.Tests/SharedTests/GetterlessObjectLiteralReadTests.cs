using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class GetterlessObjectLiteralReadTests
{
    [Theory, ModeData]
    public void SetterOnlyObjectLiteralPropertiesReadAsUndefined(ExecutionMode mode)
        => Assert.Equal("true\nset NaN\nNaN\n", TestHarness.Run("""
            const holder: any = { set value(next: any) { console.log('set', next); } };
            console.log(holder.value === undefined);
            console.log(holder.value += 1);
            """, mode));
}
