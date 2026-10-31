using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class StringConstructorArgumentOrderTests
{
    [Theory, ModeData]
    public void SurplusArgumentsRunBeforeStringConversion(ExecutionMode mode)
        => Assert.Equal("\"\" 1\ntext argconvert\n", TestHarness.Run("""
            function count(String: any) {
                let calls = 0;
                const value: any = new String('', calls++);
                console.log(JSON.stringify(value.valueOf()), calls);
            }
            count(String);
            function order(String: any) {
                let trace = '';
                const input = { toString() { trace += 'convert'; return 'text'; } };
                function extra(): any { trace += 'arg'; return 1; }
                const value: any = new String(input, extra());
                console.log(value.valueOf(), trace);
            }
            order(String);
            """, mode));
}
