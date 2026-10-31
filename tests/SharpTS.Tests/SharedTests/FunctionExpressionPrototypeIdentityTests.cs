using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class FunctionExpressionPrototypeIdentityTests
{
    [Theory, ModeData]
    public void RepeatedEvaluationsOwnIndependentPrototypesAndProperties(ExecutionMode mode)
    {
        const string source = """
            function make() { return function named() {}; }
            const first: any = make(), second: any = make();
            console.log(first !== second, first.prototype !== second.prototype);
            first.prototype.tag = 'first'; first.extra = 7;
            console.log(first.prototype === first.prototype,
                second.prototype.tag === undefined, second.extra === undefined,
                first.prototype.constructor === first,
                second.prototype.constructor === second);
            """;
        Assert.Equal("true true\ntrue true true true true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void RepeatedDeclarationReadsRetainOnePrototype(ExecutionMode mode)
    {
        const string source = """
            function declared() {}
            const first: any = declared, second: any = declared;
            first.prototype.tag = 1;
            console.log(first === second, first.prototype === second.prototype, second.prototype.tag);
            """;
        Assert.Equal("true true 1\n", TestHarness.Run(source, mode));
    }
}
