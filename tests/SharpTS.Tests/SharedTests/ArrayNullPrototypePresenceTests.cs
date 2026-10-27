using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class ArrayNullPrototypePresenceTests
{
    [Theory, ModeData]
    public void ArrayPresenceStopsAtExplicitNullPrototypes(ExecutionMode mode)
    {
        const string source = """
            class Values extends Array {}
            const targets: any[] = [[], new Values()];
            for (const target of targets) {
                console.log("hasOwnProperty" in target, Reflect.has(target, "valueOf"));
                Object.setPrototypeOf(target, null);
                console.log("hasOwnProperty" in target, "valueOf" in target, Reflect.has(target, "hasOwnProperty"), typeof Reflect.get(target, "hasOwnProperty"));
                console.log("length" in target);
            }
            """;
        Assert.Equal("true true\nfalse false false undefined\ntrue\ntrue true\nfalse false false undefined\ntrue\n", TestHarness.Run(source, mode));
    }
}
