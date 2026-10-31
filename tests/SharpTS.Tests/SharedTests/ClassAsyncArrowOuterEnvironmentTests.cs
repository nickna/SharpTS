using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassAsyncArrowOuterEnvironmentTests
{
    [Theory, ModeData]
    public void AsyncArrowRetainsEnclosingEnvironmentThroughClassMethod(ExecutionMode mode)
    {
        const string source = """
            function create(Promise: any) {
                const B: any = class {
                    label = 'outer';
                    make() { return async () => this.label + ':' + await Promise.resolve(7); }
                };
                return new B().make();
            }
            const read: any = create({ resolve(value: number) { return value + 1; } });
            read().then(value => console.log(value));
            """;
        Assert.Equal("outer:8\n", TestHarness.Run(source, mode));
    }
}
