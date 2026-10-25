using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class CommaConditionGuardExecutionTests
{
    private const string Source = """
        let calls=0;
        function touch():void{calls++;}
        function read(value:string|number):number {
            if((touch(),typeof value==='string'))return value.length;
            return value+1;
        }
        console.log(read('ok'),read(2),calls);
        """;

    [Theory, ModeData]
    public void PrefixEffectsExecuteOnceBeforeTheFinalGuard(ExecutionMode mode)
        => Assert.Equal("2 3 2\n", TestHarness.Run(Source, mode));

    [Fact]
    public void GuardedReadsHaveVerifiedCompilerIl() => TestHarness.CompileAndVerifyOnly(Source);
}
