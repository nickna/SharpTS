using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class AsyncExpressionConstructorInitializationTests
{
    [Theory, ModeData]
    public void Issue2379Regression(ExecutionMode mode)
    {
        const string source = """
            class A {}
            const B: any = class extends A {
                label = 'async';
                async read() { await Promise.resolve(0); return this.label; }
            };
            Object.defineProperty(A.prototype, 'unused', { value: 1 });
            new B().read().then(value => console.log(value));
            """;
        Assert.Equal("async\n", TestHarness.Run(source, mode));
    }
}