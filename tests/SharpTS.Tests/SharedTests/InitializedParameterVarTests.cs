using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InitializedParameterVarTests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void InitializedVarWritesToTheExistingParameterBinding(ExecutionMode mode)
    {
        const string source = """
            function basic(value) {
                var value = 7;
                var value;
                console.log(value);
            }
            basic(1);

            function capture(value) {
                const read = () => value;
                { var value = 8; }
                console.log(value, read());
            }
            capture(1);

            function defaultClosure(value = 4, read = () => value) {
                var value = 9;
                console.log(value, read());
            }
            defaultClosure();

            const arrow = value => { var value = 10; console.log(value); };
            arrow(1);
            """;

        Assert.Equal("7\n8 8\n9 4\n10\n", TestHarness.RunUnchecked(source, mode));
    }
}
