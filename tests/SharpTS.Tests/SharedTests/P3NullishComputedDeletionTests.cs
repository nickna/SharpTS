using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P3NullishComputedDeletionTests
{
    [Theory, ModeData]
    public void NullishDeletionRejectsBeforePropertyKeyCoercion(ExecutionMode mode)
    {
        const string source = """
            let coercions = 0, evaluations = 0;
            const key:any = { [Symbol.toPrimitive]() { coercions++; return 'value'; } };
            function getKey() { evaluations++; return key; }
            for (const receiver of [null, undefined] as any[]) {
                try { delete receiver[getKey()]; console.log('accepted'); }
                catch (error:any) { console.log(error instanceof TypeError, coercions, evaluations); }
            }
            console.log(delete ({} as any)[key], coercions);
            """;
        Assert.Equal("true 0 1\ntrue 0 2\ntrue 1\n", TestHarness.Run(source, mode));
    }
}
