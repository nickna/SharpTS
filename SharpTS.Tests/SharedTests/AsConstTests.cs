using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for 'as const' assertion feature. Runs against both interpreter and compiler.
/// </summary>
public class AsConstTests
{
    #region Basic as const Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsConst_ArrayToTuple_TypeInferredCorrectly(ExecutionMode mode)
    {
        var source = """
            const arr = [1, 2, 3] as const;
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsConst_MixedTypeArray_Works(ExecutionMode mode)
    {
        var source = """
            const mixed = [1, "two", true] as const;
            console.log(mixed[0]);
            console.log(mixed[1]);
            console.log(mixed[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\ntwo\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsConst_ObjectLiteral_Works(ExecutionMode mode)
    {
        var source = """
            const obj = { x: 1, y: "hello" } as const;
            console.log(obj.x);
            console.log(obj.y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\nhello\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsConst_NestedStructure_Works(ExecutionMode mode)
    {
        var source = """
            const nested = { items: [1, 2], name: "test" } as const;
            console.log(nested.items[0]);
            console.log(nested.items[1]);
            console.log(nested.name);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\ntest\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsConst_RuntimePassThrough_Works(ExecutionMode mode)
    {
        var source = """
            const values = [10, 20, 30] as const;
            let sum = 0;
            for (const v of values) {
                sum = sum + v;
            }
            console.log(sum);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("60\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsConst_StringLiterals_Works(ExecutionMode mode)
    {
        var source = """
            const statuses = ["pending", "active", "done"] as const;
            console.log(statuses[0]);
            console.log(statuses[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("pending\ndone\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsConst_EmptyArray_Works(ExecutionMode mode)
    {
        var source = """
            const empty = [] as const;
            console.log(empty.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsConst_BooleanLiterals_Works(ExecutionMode mode)
    {
        var source = """
            const flags = [true, false, true] as const;
            console.log(flags[0]);
            console.log(flags[1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\nfalse\n", output);
    }

    #endregion

    #region Complex as const Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsConst_DeeplyNestedObject_Works(ExecutionMode mode)
    {
        var source = """
            const config = {
                server: {
                    host: "localhost",
                    port: 8080
                },
                features: ["auth", "logging"]
            } as const;
            console.log(config.server.host);
            console.log(config.server.port);
            console.log(config.features[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("localhost\n8080\nauth\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsConst_ArrayOfObjects_Works(ExecutionMode mode)
    {
        var source = """
            const points = [
                { x: 0, y: 0 },
                { x: 10, y: 20 }
            ] as const;
            console.log(points[0].x);
            console.log(points[1].y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n20\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsConst_NullValue_Works(ExecutionMode mode)
    {
        var source = """
            const nullable = [1, null, 3] as const;
            console.log(nullable[0]);
            console.log(nullable[1]);
            console.log(nullable[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\nnull\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsConst_InFunctionReturn_Works(ExecutionMode mode)
    {
        var source = """
            function getConfig(): { mode: string, fontSize: number } {
                return { mode: "dark", fontSize: 14 } as const;
            }
            const cfg = getConfig();
            console.log(cfg.mode);
            console.log(cfg.fontSize);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("dark\n14\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsConst_SpreadIntoArray_Works(ExecutionMode mode)
    {
        var source = """
            const tuple = [1, 2, 3] as const;
            const arr = [...tuple, 4, 5];
            console.log(arr.length);
            console.log(arr[0]);
            console.log(arr[4]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("5\n1\n5\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsConst_SpreadIntoObject_Works(ExecutionMode mode)
    {
        var source = """
            const base = { x: 1, y: 2 } as const;
            const extended = { ...base, z: 3 };
            console.log(extended.x);
            console.log(extended.y);
            console.log(extended.z);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void AsConst_WithMethodCalls_Works(ExecutionMode mode)
    {
        // Compiler does not support dynamic dispatch for map on as const arrays
        var source = """
            const nums = [3, 1, 2] as const;
            const doubled = nums.map(n => n * 2);
            console.log(doubled[0]);
            console.log(doubled[1]);
            console.log(doubled[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("6\n2\n4\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void AsConst_FilterOperation_Works(ExecutionMode mode)
    {
        // Compiler does not support dynamic dispatch for filter on as const arrays
        var source = """
            const values = [1, 2, 3, 4, 5] as const;
            const evens = values.filter(v => v % 2 === 0);
            console.log(evens.length);
            console.log(evens[0]);
            console.log(evens[1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("2\n2\n4\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.InterpretedOnly), MemberType = typeof(ExecutionModes))]
    public void AsConst_ReduceOperation_Works(ExecutionMode mode)
    {
        // Compiler does not support dynamic dispatch for reduce on as const arrays
        var source = """
            const nums = [1, 2, 3, 4] as const;
            const sum = nums.reduce((acc, n) => acc + n, 0);
            console.log(sum);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsConst_IndexAccess_Works(ExecutionMode mode)
    {
        var source = """
            const directions = ["north", "south", "east", "west"] as const;
            let idx = 2;
            console.log(directions[idx]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("east\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void AsConst_ObjectWithArrayProperty_Works(ExecutionMode mode)
    {
        var source = """
            const data = {
                name: "test",
                values: [100, 200, 300]
            } as const;
            console.log(data.name);
            console.log(data.values.length);
            console.log(data.values[1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("test\n3\n200\n", output);
    }

    #endregion
}
