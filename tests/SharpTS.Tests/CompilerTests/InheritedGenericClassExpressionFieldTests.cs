using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class InheritedGenericClassExpressionFieldTests
{
    [Fact]
    public void TransitiveFieldReadUsesTheClosedDeclaringOwner()
    {
        const string source = """
            class Base<T> { target = 7; }
            const Parent = class<T> extends Base<T> {};
            const Numeric = class extends Parent<number> {};
            const Text = class extends Parent<string> {};
            const first = new Numeric();
            const second = new Text();
            console.log(first.target, second.target);
            first.target = 9;
            console.log((first as any)['target'], (second as any)['target']);
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("7 7\n9 7\n", output);
    }
}
