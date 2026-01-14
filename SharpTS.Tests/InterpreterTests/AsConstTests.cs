using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.InterpreterTests;

/// <summary>
/// Tests for 'as const' assertion feature in interpreter mode.
/// </summary>
public class AsConstTests
{
    [Fact]
    public void AsConst_ArrayToTuple_TypeInferredCorrectly()
    {
        var source = """
            const arr = [1, 2, 3] as const;
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Fact]
    public void AsConst_MixedTypeArray_Works()
    {
        var source = """
            const mixed = [1, "two", true] as const;
            console.log(mixed[0]);
            console.log(mixed[1]);
            console.log(mixed[2]);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\ntwo\ntrue\n", output);
    }

    [Fact]
    public void AsConst_ObjectLiteral_Works()
    {
        var source = """
            const obj = { x: 1, y: "hello" } as const;
            console.log(obj.x);
            console.log(obj.y);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\nhello\n", output);
    }

    [Fact]
    public void AsConst_NestedStructure_Works()
    {
        var source = """
            const nested = { items: [1, 2], name: "test" } as const;
            console.log(nested.items[0]);
            console.log(nested.items[1]);
            console.log(nested.name);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("1\n2\ntest\n", output);
    }

    [Fact]
    public void AsConst_RuntimePassThrough_Works()
    {
        var source = """
            const values = [10, 20, 30] as const;
            let sum = 0;
            for (const v of values) {
                sum = sum + v;
            }
            console.log(sum);
            """;

        var output = TestHarness.RunInterpreted(source);
        Assert.Equal("60\n", output);
    }
}
