using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Array static methods (isArray, from, of). Runs against both interpreter and compiler.
/// </summary>
public class ArrayStaticTests
{
    #region Array.isArray Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_IsArray_ReturnsTrue_ForArray(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [1, 2, 3];
            console.log(Array.isArray(arr));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_IsArray_ReturnsTrue_ForEmptyArray(ExecutionMode mode)
    {
        var source = """
            let arr: number[] = [];
            console.log(Array.isArray(arr));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("true\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_IsArray_ReturnsFalse_ForNumber(ExecutionMode mode)
    {
        var source = """
            let num: number = 42;
            console.log(Array.isArray(num));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_IsArray_ReturnsFalse_ForString(ExecutionMode mode)
    {
        var source = """
            let str: string = "hello";
            console.log(Array.isArray(str));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_IsArray_ReturnsFalse_ForObject(ExecutionMode mode)
    {
        var source = """
            let obj: { x: number } = { x: 1 };
            console.log(Array.isArray(obj));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_IsArray_ReturnsFalse_ForNull(ExecutionMode mode)
    {
        var source = """
            let val: any = null;
            console.log(Array.isArray(val));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("false\n", output);
    }

    #endregion

    #region Array.from Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_From_CreatesArrayFromArray(ExecutionMode mode)
    {
        var source = """
            let arr = Array.from([1, 2, 3]);
            console.log(arr.length);
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n1\n2\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_From_CreatesArrayFromString(ExecutionMode mode)
    {
        var source = """
            let arr = Array.from("abc");
            console.log(arr.length);
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\na\nb\nc\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_From_WithMapFunction(ExecutionMode mode)
    {
        var source = """
            let arr = Array.from([1, 2, 3], (x) => x * 2);
            console.log(arr.length);
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n2\n4\n6\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_From_MapFunctionWithIndex(ExecutionMode mode)
    {
        var source = """
            let arr = Array.from([10, 20, 30], (val, idx) => val + idx);
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n21\n32\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_From_EmptyArray(ExecutionMode mode)
    {
        var source = """
            let arr = Array.from([]);
            console.log(arr.length);
            console.log(Array.isArray(arr));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_From_FromSet(ExecutionMode mode)
    {
        var source = """
            let s = new Set<number>();
            s.add(1);
            s.add(2);
            s.add(3);
            let arr = Array.from(s);
            console.log(arr.length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_From_WithGenerator(ExecutionMode mode)
    {
        var source = """
            function* gen(): Generator<number> {
                yield 1;
                yield 2;
                yield 3;
            }
            let arr = Array.from(gen());
            console.log(arr.length);
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n1\n2\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_From_CustomIterator(ExecutionMode mode)
    {
        var source = """
            let obj = {
                [Symbol.iterator](): Iterator<number> {
                    let i = 0;
                    return {
                        next(): IteratorResult<number> {
                            if (i < 3) {
                                return { value: ++i, done: false };
                            }
                            return { value: 0, done: true };
                        }
                    };
                }
            };
            let arr = Array.from(obj);
            console.log(arr.length);
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n1\n2\n3\n", output);
    }

    #endregion

    #region Array.of Tests

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Of_CreatesArrayFromArguments(ExecutionMode mode)
    {
        var source = """
            let arr = Array.of(1, 2, 3);
            console.log(arr.length);
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n1\n2\n3\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Of_SingleNumber(ExecutionMode mode)
    {
        var source = """
            let arr = Array.of(7);
            console.log(arr.length);
            console.log(arr[0]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n7\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Of_NoArguments(ExecutionMode mode)
    {
        var source = """
            let arr = Array.of();
            console.log(arr.length);
            console.log(Array.isArray(arr));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\ntrue\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Of_MixedTypes(ExecutionMode mode)
    {
        var source = """
            let arr = Array.of(1, "two", true, null);
            console.log(arr.length);
            console.log(arr[0]);
            console.log(arr[1]);
            console.log(arr[2]);
            console.log(arr[3]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("4\n1\ntwo\ntrue\nnull\n", output);
    }

    [Theory]
    [MemberData(nameof(ExecutionModes.All), MemberType = typeof(ExecutionModes))]
    public void Array_Of_WithStrings(ExecutionMode mode)
    {
        var source = """
            let arr = Array.of("a", "b", "c");
            console.log(arr.join("-"));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("a-b-c\n", output);
    }

    #endregion
}
