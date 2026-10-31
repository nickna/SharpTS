using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassConstructorIndexedWriteTests
{
    [Theory, ModeData]
    public void EvaluatedConstructorRetainsNamedAndNumericIndexedWrites(ExecutionMode mode)
    {
        const string source = """
            const C = class {};
            const dynamic: any = C;
            dynamic['slot'] = 'ok';
            dynamic[0] = 7;
            console.log(dynamic['slot'], dynamic[0], Object.hasOwn(dynamic, 'slot'));
            """;
        Assert.Equal("ok 7 true\n", TestHarness.Run(source, mode));
    }
}
