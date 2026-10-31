using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2GeneratorOwnIteratorForOfTests
{
    [Theory, ModeData]
    public void Issue2311Regression(ExecutionMode mode)
    {
        const string source = """
            function* values() { yield 1; }
            const source: any = values();
            let calls = 0;
            const replacement: any = function(this: any) {
                calls++;
                console.log(this === source);
                return { next() { return { done: true, value: undefined }; } };
            };
            source[Symbol.iterator] = replacement;
            console.log(source[Symbol.iterator] === replacement);
            let visits = 0;
            for (const value of source) visits++;
            console.log(visits, calls);

            """;
        Assert.Equal("true\ntrue\n0 1\n", TestHarness.Run(source, mode));
    }
}