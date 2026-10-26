using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PureCallableProtocolExecutionTests
{
    internal const string Source = "type Unary={(value:number):number};const next:Unary=value=>value+1;const identity:{<T>(value:T):T}=<T>(value:T):T=>value;console.log(next(2));console.log(identity('ok'));";

    [Theory, ModeData]
    public void PureCallableProtocolsRetainTheirRuntimeFunctionRepresentation(ExecutionMode mode)
        => Assert.Equal("3\nok\n", TestHarness.Run(Source, mode));

    [Fact]
    public void PureCallableProtocolsProduceValidIl()
        => Assert.Empty(TestHarness.CompileAndVerifyOnly(Source));
}
