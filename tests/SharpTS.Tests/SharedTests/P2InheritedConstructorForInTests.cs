using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2InheritedConstructorForInTests
{
    [Theory, ModeData]
    public void ConstructorForInIncludesAncestorsAndHonorsNonenumerableShadows(ExecutionMode mode)
    {
        const string source = """
            class Parent { static inherited = 2; static masked = 3; }
            class Child extends Parent { static own = 1; }
            Object.defineProperty(Child, "masked", { value: 4, enumerable: false });
            function* names() { for (const key in Child) yield key; }
            console.log([...names()].join(","));
            let keys = "";
            for (const key in Child) {
                keys += key + ",";
                if (key === "own") delete (Parent as any).inherited;
            }
            console.log(keys);
            """;
        Assert.Equal("own,inherited\nown,\n", TestHarness.Run(source, mode));
    }
}
