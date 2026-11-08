using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class HoistedParameterFunctionTests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public void FunctionDeclarationInitializesTheSameNamedParameter(ExecutionMode mode)
    {
        const string source = """
            function basic(value) {
                console.log(typeof value, later());
                function later() { return value(); }
                function value() { return 7; }
            }
            basic(1);

            function mapped(value) {
                console.log(arguments[0] === value, value());
                function value() { return 8; }
                const read = () => value;
                arguments[0] = 9;
                console.log(value, read());
            }
            mapped(1);

            function defaultClosure(value = 4, read = () => value) {
                console.log(typeof value, read());
                function value() {}
            }
            defaultClosure();

            function block(value) {
                "use strict";
                { function value() {} }
                console.log(typeof value);
            }
            block(1);
            """;

        Assert.Equal("function 7\ntrue 8\n9 9\nfunction 4\nnumber\n",
            TestHarness.RunUnchecked(source, mode));
    }
}
