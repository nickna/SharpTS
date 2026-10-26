using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ReachableLoopBodyDomainExecutionTests
{
    private const string Source = """
        type State={kind:'text',text:string}|{kind:'count',count:number};
        let state:State={kind:'text',text:'start'};
        let turns=0;
        do {
            turns++;
            if(state.kind==='count'){
                const inside:number=state.count;
                console.log(inside,turns);
                break;
            }
            try { continue; }
            finally { state={kind:'count',count:4}; }
        } while(true);
        """;

    [Theory, ModeData]
    public void LaterGuardedBodyReadUsesTheActualBackedgeValue(ExecutionMode mode)
        => Assert.Equal("4 2\n", TestHarness.Run(Source, mode));

    [Fact]
    public void GuardedBodyReadHasVerifiedCompilerIl() => Assert.Empty(TestHarness.CompileAndVerifyOnly(Source));
}
