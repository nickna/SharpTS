using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ClassExoticPrimitiveCoercionTests
{
    [Theory, ModeData]
    public void DeclaredClassSymbolHooksPrecedeOrdinaryCoercion(ExecutionMode mode)
        => Assert.Equal("16 number\n5 default\n", TestHarness.Run("""
            let trace = '';
            class Value {
                [Symbol.toPrimitive](hint: string) { trace += hint; return 2; }
                valueOf(): any { throw 'unexpected valueOf'; }
            }
            let value: any = 8;
            const right: any = new Value();
            console.log(value *= right, trace);
            trace = '';
            const values: any[] = [new Value()];
            console.log(values[0] += 3, trace);
            """, mode));
}
