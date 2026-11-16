using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncGeneratorBareYieldTests
{
    [Theory, ModeData]
    public void BareYieldProducesUndefinedAndAcceptsSentValues(ExecutionMode mode)
    {
        const string source = """
            async function* values() { const sent = yield; console.log(sent); yield null; }
            async function run() {
                const iterator = values();
                const first = await iterator.next();
                console.log(first.value === undefined, first.done);
                const second = await iterator.next(7);
                console.log(second.value === null, second.done);
                console.log((await iterator.next()).done);
            }
            run();
            """;
        Assert.Equal("true false\n7\ntrue false\ntrue\n", TestHarness.RunUnchecked(source, mode));
    }
}
