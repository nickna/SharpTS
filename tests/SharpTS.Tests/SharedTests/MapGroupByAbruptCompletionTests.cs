using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class MapGroupByAbruptCompletionTests
{
    [Theory, ModeData]
    public void CallbackFailureClosesIteratorAndPreservesOriginalReason(ExecutionMode mode)
    {
        const string source = """
            let closed = 0;
            const original = {};
            function* values() { try { yield 1; yield 2; } finally { closed++; throw "cleanup"; } }
            const map: any = Map;
            let callbacks = 0;
            try { map.groupBy(values(), () => { callbacks++; throw original; }); }
            catch (error: any) { console.log(error === original, closed, callbacks); }
            """;
        Assert.Equal("true 1 1\n", TestHarness.Run(source, mode));
    }
}
