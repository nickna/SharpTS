using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GuardQueryExecutionTests
{
    [Theory, ModeData]
    public void ReceiverAndArgumentEffectsExecuteOnceBeforeTheGuardBranch(ExecutionMode mode)
    {
        const string source = "const object={ok(value:any):boolean{return true;}};let value:string|number='s';let receivers=0;let argumentsSeen=0;if((()=>{receivers++;value=1;return object;})().ok((()=>{argumentsSeen++;value='ok';return value;})())){const correct:string=value;console.log(typeof value,receivers,argumentsSeen);}";
        Assert.Equal("string 1 1\n", TestHarness.Run(source, mode));
    }
}
