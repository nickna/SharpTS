using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class NewSpreadTests
{
    [Fact]
    public void Function_expression_constructor_expands_spread_arguments()
    {
        const string source = """
            new function() {
                console.log(arguments.length);
                console.log(arguments[0]);
                console.log(arguments[1]);
                console.log(arguments[2]);
            }(1, ...[2, 3]);
            """;

        Assert.Equal("3\n1\n2\n3\n", TestHarness.RunCompiled(source));
    }
}
