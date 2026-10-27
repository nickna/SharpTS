using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class AsyncSingleParameterArrowTests
{
    [Theory, ModeData]
    public void ExpressionAndBlockBodiesAwaitTheirParameter(ExecutionMode mode)
    {
        const string source = """
            const expression: any = async value => await value;
            const block: any = async value => { return await value; };
            async function run() {
                console.log(await expression(Promise.resolve(7)));
                console.log(await block(Promise.resolve(9)));
                console.log(expression.name);
            }
            run();
            """;
        Assert.Equal("7\n9\nexpression\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AsyncRemainsAContextualIdentifierAcrossANewline(ExecutionMode mode)
    {
        const string source = """
            const async = 4;
            const value = async
            const arrow = value => value + 1;
            console.log(value, arrow(async));
            """;
        Assert.Equal("4 5\n", TestHarness.Run(source, mode));
    }
}
