using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GeneratorExpressionEvaluationIdentityTests
{
    [Theory, ModeData]
    public void NoncapturingExpressionsAreFreshAcrossFactoryCalls(ExecutionMode mode)
    {
        const string source = """
            function make() { return function*() { yield 1; }; }
            function makeAsync() { return async function*() { yield 1; }; }
            const first: any = make(), second: any = make();
            const firstAsync: any = makeAsync(), secondAsync: any = makeAsync();
            console.log(first !== second, firstAsync !== secondAsync);
            console.log(first.prototype !== second.prototype, firstAsync.prototype !== secondAsync.prototype);
            first.extra = 1;
            console.log(second.extra === undefined, first().next().value);
            """;
        Assert.Equal("true true\ntrue true\ntrue 1\n", TestHarness.Run(source, mode));
    }
}
