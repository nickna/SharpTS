using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class FactoryGeneratorPrototypePresenceTests
{
    [Theory, ModeData]
    public void Issue2286Regression(ExecutionMode mode)
    {
        const string source = """
            function make(value: number): any { return function*() { yield value; }; }
            function makeAsync(value: number): any { return async function*() { yield value; }; }
            const first: any = make(1), second: any = make(2);
            console.log(typeof first, first !== second, Object.hasOwn(first, "prototype"), typeof first.prototype,
                Object.hasOwn(second, "prototype"), typeof second.prototype);
            const firstAsync: any = makeAsync(3), secondAsync: any = makeAsync(4);
            console.log(typeof firstAsync, firstAsync !== secondAsync, Object.hasOwn(firstAsync, "prototype"),
                typeof firstAsync.prototype, Object.hasOwn(secondAsync, "prototype"), typeof secondAsync.prototype);
            """;
        Assert.Equal("function true true object true object\nfunction true true object true object\n", TestHarness.Run(source, mode));
    }
}
