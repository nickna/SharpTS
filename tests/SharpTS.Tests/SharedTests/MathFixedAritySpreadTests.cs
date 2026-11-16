using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class MathFixedAritySpreadTests
{
    [Theory, ModeData]
    public void SpreadAndFixedArgumentsExpandInOrder(ExecutionMode mode)
    {
        const string source = """
            console.log(Math.abs(...[-3]));
            console.log(Math.pow(...[2,3]), Math.pow(...[],2,...[3]), Math.pow(2,...[3]));
            const events=[];
            function arg(name,value) { events.push(name); return value; }
            console.log(Math.pow(...arg('spread',[2]),arg('fixed',3)));
            console.log(events.join(','));
            console.log(Math.pow(...[1,NaN]),Math.pow(...[1,Infinity]));
            console.log(Object.is(Math.log1p(...[-0]),-0),Object.is(Math.expm1(...[-0]),-0));
            """;
        Assert.Equal("3\n8 8 8\n8\nspread,fixed\nNaN NaN\ntrue true\n", TestHarness.RunUnchecked(source, mode));
    }
}
