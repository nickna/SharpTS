using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class LogicalConditionWriteGuardExecutionTests
{
    private const string Source = """
        let value:string|number='start';
        let writes=0;
        if(typeof value==='string'&&(writes++,value=3,true)){
            const checked:number=value;
            console.log(checked,writes);
        }
        if(typeof value==='string'&&(writes++,value=4,true))console.log('wrong');
        console.log(value,writes);
        """;

    [Theory, ModeData]
    public void TheTrueEdgeUsesTheExecutedRightHandWrite(ExecutionMode mode)
        => Assert.Equal("3 1\n3 1\n", TestHarness.Run(Source, mode));

    [Fact]
    public void OrderedConditionWritesHaveVerifiedCompilerIl() => Assert.Empty(TestHarness.CompileAndVerifyOnly(Source));
}
