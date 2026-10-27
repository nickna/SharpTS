using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2ClassConstructorRestTests
{
    [Theory, ModeData]
    public void ClassConstructorRestCopiesOwnEnumerableProperties(ExecutionMode mode)
    {
        const string source = """
            class Owner { static value = 1; static excluded = 2; static method() {} }
            const target: any = Owner;
            const { excluded, ...rest } = target;
            console.log(Object.keys(target).join(","), rest.value, excluded);
            console.log(Object.keys(rest).join(","), rest.excluded === undefined, rest.method === undefined);
            """;
        Assert.Equal("value,excluded 1 2\nvalue true true\n", TestHarness.Run(source, mode));
    }
}
